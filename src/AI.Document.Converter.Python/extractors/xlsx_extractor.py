"""XLSX extraction (FR-008, FR-042) via openpyxl.

Extraction modes (SaaS SR-INT-1, replacing the old FR-041 behavior)
-------------------------------------------------------------------
"faithful" (default)
    Every non-empty cell within the supported limit is exported. A workbook
    larger than the supported limit FAILS with a clear message. It is never
    silently reduced.

"summary" (must be explicitly requested)
    The legacy header-plus-sample behavior, kept because a quick preview of a
    huge sheet is genuinely useful - but it now emits a sheetTruncated warning
    at ERROR severity, so the host reports "completed with warnings" and can
    never present it as a complete extraction.

The old default did the summary silently at 200 rows and still reported plain
success: a 1000-row sheet came back as 5 rows with no warning of any kind
(SaaS audit C-01). That is undisclosed data loss, and it is why faithful mode
is now the default and the sampling path has to announce itself.
"""

import os
from zipfile import BadZipFile

import openpyxl
from openpyxl.utils.exceptions import InvalidFileException

from extractors import model
from extractors.common import (
    ExtractionError,
    check_file_accessible,
    check_not_encrypted_ooxml,
    converted_date_iso,
    file_created_date_iso,
    normalize_optional_text,
)

FAITHFUL_MODE = "faithful"
SUMMARY_MODE = "summary"

# Above this, faithful extraction is refused outright rather than attempted and
# silently degraded. Chosen so an ordinary business workbook always fits while a
# pathological one cannot exhaust the worker (SaaS SR-SEC-5). A refusal here is
# an honest "not supported at this size", not a partial result.
MAX_SUPPORTED_CELLS = 2_000_000

# Summary-mode thresholds - unchanged from the legacy behavior they reproduce.
SUMMARY_ROW_THRESHOLD = 200
SUMMARY_COLUMN_THRESHOLD = 50
SUMMARY_SAMPLE_ROWS = 5


def _merged_value_map(worksheet):
    """Maps every cell coordinate covered by a merge to the merge's anchor
    value, so FR-042's "repeat the value across the merged span" rule is a
    simple lookup per cell rather than special-casing merge boundaries.

    Also returns the spans themselves, so the fact of the merge survives as
    provenance instead of being flattened away (SaaS audit C-02).
    """
    value_map = {}
    spans = []
    for merged_range in worksheet.merged_cells.ranges:
        anchor_value = worksheet.cell(row=merged_range.min_row, column=merged_range.min_col).value
        spans.append(str(merged_range))
        for row in range(merged_range.min_row, merged_range.max_row + 1):
            for col in range(merged_range.min_col, merged_range.max_col + 1):
                value_map[(row, col)] = anchor_value
    return value_map, spans


def _cell_text(worksheet, row, col, merged_values):
    # data_only=True (see extract()) means a formula cell's value here is
    # Excel's last-cached computed result, not the formula text (FR-042) - or
    # None if the file was never opened/saved in Excel, in which case an empty
    # cell is the only correct output. We do not compute, guess, or execute
    # anything to fill it; we warn instead (SR-INT-2).
    value = merged_values.get((row, col), worksheet.cell(row=row, column=col).value)
    return "" if value is None else str(value)


def _sheet_dimensions(worksheet):
    return worksheet.max_row or 0, worksheet.max_column or 0


def _read_rows(worksheet, row_limit, col_limit, merged_values):
    return [
        [_cell_text(worksheet, row, col, merged_values) for col in range(1, col_limit + 1)]
        for row in range(1, row_limit + 1)
    ]


def _sheets_with_uncached_formulas(file_path):
    """Sheet titles that contain at least one formula cell Excel never cached a
    value for. Needs a second load because data_only=True and data_only=False
    are mutually exclusive views of the same file - openpyxl cannot show both
    at once. read_only keeps the extra pass cheap.

    Detecting this matters because the correct output for such a cell is an
    empty string, which is indistinguishable from a genuinely blank cell unless
    we say so (SaaS audit C-04).
    """
    titles = set()
    try:
        formula_workbook = openpyxl.load_workbook(file_path, data_only=False, read_only=True)
    except Exception:  # noqa: BLE001 - a best-effort diagnostic must never be
        # the reason an otherwise successful extraction fails.
        return titles

    try:
        for worksheet in formula_workbook.worksheets:
            for row in worksheet.iter_rows():
                if any(isinstance(c.value, str) and c.value.startswith("=") for c in row):
                    titles.add(worksheet.title)
                    break
    finally:
        formula_workbook.close()

    return titles


def _faithful_sheet(worksheet, merged_values):
    max_row, max_col = _sheet_dimensions(worksheet)
    rows = _read_rows(worksheet, max_row, max_col, merged_values)
    return [{"type": "table", "headers": rows[0], "rows": rows[1:]}] if rows else []


def _summary_sheet(worksheet, merged_values, warnings):
    """The legacy sample, now loudly labelled."""
    max_row, max_col = _sheet_dimensions(worksheet)
    is_oversized = max_row > SUMMARY_ROW_THRESHOLD or max_col > SUMMARY_COLUMN_THRESHOLD

    row_limit = min(max_row, 1 + SUMMARY_SAMPLE_ROWS) if is_oversized else max_row
    col_limit = min(max_col, SUMMARY_COLUMN_THRESHOLD) if is_oversized else max_col
    rows = _read_rows(worksheet, row_limit, col_limit, merged_values)

    blocks = []
    if is_oversized:
        returned_data_rows = max(len(rows) - 1, 0)
        blocks.append(
            {
                "type": "paragraph",
                "text": (
                    f"INCOMPLETE EXTRACTION - summary mode. Sheet '{worksheet.title}' has "
                    f"{max_row} rows and {max_col} columns; only the header row and the first "
                    f"{returned_data_rows} data rows are included. This is a preview, not a "
                    f"complete extraction of this sheet."
                ),
            }
        )
        warnings.append(
            model.warning(
                model.SHEET_TRUNCATED,
                model.ERROR,
                (
                    f"Sheet '{worksheet.title}' was truncated: {returned_data_rows} of "
                    f"{max(max_row - 1, 0)} data rows and {min(max_col, SUMMARY_COLUMN_THRESHOLD)} "
                    f"of {max_col} columns were extracted. Re-run in faithful mode for the "
                    f"complete sheet."
                ),
                location=model.location(sheet_name=worksheet.title),
                details={
                    "sheetName": worksheet.title,
                    "totalRows": max_row,
                    "totalColumns": max_col,
                    "extractedDataRows": returned_data_rows,
                    "extractedColumns": min(max_col, SUMMARY_COLUMN_THRESHOLD),
                },
            )
        )

    if rows:
        blocks.append({"type": "table", "headers": rows[0], "rows": rows[1:]})

    return blocks


def _guard_supported_size(workbook, file_name):
    """Faithful mode promises completeness, so a workbook it cannot complete has
    to fail rather than quietly return part of itself.
    """
    total_cells = sum(
        (ws.max_row or 0) * (ws.max_column or 0) for ws in workbook.worksheets
    )

    if total_cells > MAX_SUPPORTED_CELLS:
        raise ExtractionError(
            "unsupportedFile",
            (
                f"'{file_name}' contains about {total_cells:,} cells, which exceeds the "
                f"{MAX_SUPPORTED_CELLS:,}-cell limit for complete extraction. Split the "
                f"workbook, or request summary mode for a preview."
            ),
        )


def _count_billable_source_cells(workbook):
    """Non-empty cells as they exist in the SOURCE file (SaaS SR-BIL-4).

    Two rules the requirement is explicit about, and both change the number:

    - Counted BEFORE merged-cell expansion. A merge spanning 10 columns is one
      authored value; billing it as 10 would charge for formatting.
    - A formula cell counts ONCE, not once for the formula and once for its
      cached result.

    Counted from the same data_only view the extraction uses, so the number
    billed and the content delivered come from one reading of the file.
    """
    total = 0

    for worksheet in workbook.worksheets:
        merged_non_anchor = set()
        for merged_range in worksheet.merged_cells.ranges:
            for row in range(merged_range.min_row, merged_range.max_row + 1):
                for col in range(merged_range.min_col, merged_range.max_col + 1):
                    if (row, col) != (merged_range.min_row, merged_range.min_col):
                        merged_non_anchor.add((row, col))

        for row in worksheet.iter_rows():
            for cell in row:
                if cell.value is None:
                    continue
                if (cell.row, cell.column) in merged_non_anchor:
                    continue
                total += 1

    return total


def extract(file_path, mode=FAITHFUL_MODE):
    check_file_accessible(file_path)
    check_not_encrypted_ooxml(file_path)

    if mode not in (FAITHFUL_MODE, SUMMARY_MODE):
        raise ExtractionError(
            "unsupportedFile",
            f"Unknown extraction mode '{mode}'. Supported: {FAITHFUL_MODE}, {SUMMARY_MODE}.",
        )

    try:
        workbook = openpyxl.load_workbook(file_path, data_only=True)
    except (InvalidFileException, BadZipFile, KeyError) as ex:
        raise ExtractionError(
            "corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}"
        )

    if mode == FAITHFUL_MODE:
        _guard_supported_size(workbook, os.path.basename(file_path))

    warnings = []
    uncached_formula_sheets = _sheets_with_uncached_formulas(file_path)

    sections = []
    for worksheet in workbook.worksheets:
        max_row, max_col = _sheet_dimensions(worksheet)
        if max_row == 0 or max_col == 0:
            continue

        merged_values, merge_spans = _merged_value_map(worksheet)

        blocks = (
            _faithful_sheet(worksheet, merged_values)
            if mode == FAITHFUL_MODE
            else _summary_sheet(worksheet, merged_values, warnings)
        )
        if not blocks:
            continue

        if worksheet.title in uncached_formula_sheets:
            warnings.append(
                model.warning(
                    model.FORMULA_VALUE_UNAVAILABLE,
                    model.WARNING,
                    (
                        f"Sheet '{worksheet.title}' contains formulas with no value cached by "
                        f"Excel. Those cells are exported empty; the formula is neither "
                        f"evaluated nor shown as text. Open and save the workbook in Excel to "
                        f"cache the values."
                    ),
                    location=model.location(sheet_name=worksheet.title),
                    details={"sheetName": worksheet.title},
                )
            )

        section = {
            "heading": worksheet.title,
            "headingLevel": 1,
            "blocks": blocks,
            "location": model.location(sheet_name=worksheet.title),
        }
        if merge_spans:
            # Provenance for FR-042's value repetition: without this, a repeated
            # value is indistinguishable from a genuine duplicate (audit C-02).
            section["mergedCellRanges"] = merge_spans

        sections.append(section)

    metadata = {
        "sourceFilePath": file_path,
        "fileType": "xlsx",
        "createdDate": file_created_date_iso(file_path),
        "convertedDate": converted_date_iso(),
        "pageCount": None,
        "slideCount": None,
        "sheetCount": len(workbook.worksheets),
        "author": normalize_optional_text(workbook.properties.creator),
        "extractionMode": mode,
        # Metering input (SR-BIL-4). Reported even in summary mode, because the
        # customer is billed for the document they supplied, not for how much
        # of it a preview happened to show.
        "billableSourceCells": _count_billable_source_cells(workbook),
    }

    return model.document(metadata, sections, warnings)
