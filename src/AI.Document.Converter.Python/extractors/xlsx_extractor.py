"""XLSX extraction (FR-008, FR-041, FR-042) via openpyxl."""

import os
from zipfile import BadZipFile

import openpyxl
from openpyxl.utils.exceptions import InvalidFileException

from extractors.common import (
    ExtractionError,
    check_file_accessible,
    check_not_encrypted_ooxml,
    converted_date_iso,
    file_created_date_iso,
    normalize_optional_text,
)

MAX_TABLE_ROWS = 200
MAX_TABLE_COLUMNS = 50
SUMMARY_SAMPLE_ROWS = 5


def _merged_value_map(worksheet):
    """Maps every cell coordinate covered by a merge to the merge's anchor
    value, so FR-042's "repeat the value across the merged span" rule is a
    simple lookup per cell rather than special-casing merge boundaries.
    """
    value_map = {}
    for merged_range in worksheet.merged_cells.ranges:
        anchor_value = worksheet.cell(row=merged_range.min_row, column=merged_range.min_col).value
        for row in range(merged_range.min_row, merged_range.max_row + 1):
            for col in range(merged_range.min_col, merged_range.max_col + 1):
                value_map[(row, col)] = anchor_value
    return value_map


def _cell_text(worksheet, row, col, merged_values):
    # data_only=True (see extract()) means a formula cell's value here is
    # Excel's last-cached computed result, not the formula text (FR-042) - or
    # None if the file was never opened/saved in Excel, in which case an empty
    # cell is the only correct output (showing the formula text would violate
    # FR-042's "computed values, not formula text" rule).
    value = merged_values.get((row, col), worksheet.cell(row=row, column=col).value)
    return "" if value is None else str(value)


def _sheet_to_blocks(worksheet):
    max_row = worksheet.max_row or 0
    max_col = worksheet.max_column or 0

    if max_row == 0 or max_col == 0:
        return []

    merged_values = _merged_value_map(worksheet)
    is_oversized = max_row > MAX_TABLE_ROWS or max_col > MAX_TABLE_COLUMNS

    row_limit = min(max_row, 1 + SUMMARY_SAMPLE_ROWS) if is_oversized else max_row
    col_limit = min(max_col, MAX_TABLE_COLUMNS) if is_oversized else max_col

    rows = [
        [_cell_text(worksheet, row, col, merged_values) for col in range(1, col_limit + 1)]
        for row in range(1, row_limit + 1)
    ]

    blocks = []
    if is_oversized:
        blocks.append(
            {
                "type": "paragraph",
                "text": (
                    f"Sheet '{worksheet.title}' has {max_row} rows and {max_col} columns, "
                    f"exceeding the {MAX_TABLE_ROWS}-row/{MAX_TABLE_COLUMNS}-column "
                    f"summarization threshold. Showing the header row and the first "
                    f"{SUMMARY_SAMPLE_ROWS} data rows only."
                ),
            }
        )

    if rows:
        blocks.append({"type": "table", "headers": rows[0], "rows": rows[1:]})

    return blocks


def extract(file_path):
    check_file_accessible(file_path)
    check_not_encrypted_ooxml(file_path)

    try:
        workbook = openpyxl.load_workbook(file_path, data_only=True)
    except (InvalidFileException, BadZipFile, KeyError) as ex:
        raise ExtractionError(
            "corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}"
        )

    sections = []
    for worksheet in workbook.worksheets:
        blocks = _sheet_to_blocks(worksheet)
        if not blocks:
            continue
        sections.append(
            {
                "heading": worksheet.title,
                "headingLevel": 1,
                "blocks": blocks,
                "location": {"pageNumber": None, "slideNumber": None, "sheetName": worksheet.title},
            }
        )

    return {
        "metadata": {
            "sourceFilePath": file_path,
            "fileType": "xlsx",
            "createdDate": file_created_date_iso(file_path),
            "convertedDate": converted_date_iso(),
            "pageCount": None,
            "slideCount": None,
            "sheetCount": len(workbook.worksheets),
            "author": normalize_optional_text(workbook.properties.creator),
        },
        "sections": sections,
    }
