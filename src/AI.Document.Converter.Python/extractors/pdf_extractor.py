"""PDF extraction (FR-006, FR-043) via pdfplumber.

Licensing (SaaS audit F-01)
---------------------------
This replaced PyMuPDF, which is dual-licensed AGPL-3.0 / Artifex commercial.
AGPL section 13's network clause is triggered by serving PDF conversion over a
network, and subprocess or container separation does not discharge it. The
replacement chain is entirely permissive:

    pdfplumber   MIT
    pdfminer.six MIT          (text, layout, encryption)
    Pillow       MIT-CMU      (image objects)
    pypdfium2    BSD-3/Apache-2.0

Measured table fidelity against scripts/generate-table-benchmark.py was
identical to PyMuPDF (54/72 cells, 4/6 exact shapes) - see
docs/saas/02-PDF-ENGINE-BENCHMARK.md, including that benchmark's limitations.

Model per page (a deliberately simple heuristic - see docs/18-RISK-ASSESSMENT.md
R-07/R-08 for the accepted limitations of automatic PDF structure detection):
- at most one heading, taken from the first text line if its font size clears
  HEADING_FONT_SIZE_THRESHOLD;
- the rest of the page's non-table text as a single paragraph block;
- one table block per table pdfplumber detects;
- if none of the above produced anything, a single unextractableText block
  (FR-043) instead of silently omitting the page.
"""

import os

import pdfplumber
from pdfminer.pdfdocument import PDFDocument, PDFPasswordIncorrect, PDFSyntaxError
from pdfminer.pdfparser import PDFParser

from extractors import model
from extractors.common import (
    ExtractionError,
    check_file_accessible,
    converted_date_iso,
    file_created_date_iso,
    image_placeholder_block,
    normalize_optional_text,
    unextractable_text_block,
)

HEADING_FONT_SIZE_THRESHOLD = 14

# A text line is treated as part of a table (and therefore not repeated as body
# prose) when its vertical midpoint falls inside a detected table's bounds.
# Midpoint rather than full containment: a line's reported bbox often overhangs
# the ruling line by a fraction of a point, which a strict containment test
# would reject, duplicating every table row into the paragraph text.
def _line_is_inside_any_table(line, table_bboxes):
    mid_y = (line["top"] + line["bottom"]) / 2
    mid_x = (line["x0"] + line["x1"]) / 2
    for x0, top, x1, bottom in table_bboxes:
        if x0 <= mid_x <= x1 and top <= mid_y <= bottom:
            return True
    return False


def _line_max_font_size(line):
    sizes = [char["size"] for char in line.get("chars", []) if "size" in char]
    return max(sizes) if sizes else 0


def _cell_text(value):
    # pdfplumber returns None for an empty cell and may keep the newline from a
    # wrapped cell; both are normalized so a table cell is always a flat string,
    # matching what the Markdown renderer and the C# TableBlock expect.
    if value is None:
        return ""
    return " ".join(str(value).split())


def _extract_page(page, page_number):
    tables = page.find_tables()
    table_bboxes = [table.bbox for table in tables]

    # extract_text_lines gives per-line bboxes plus the underlying chars, which
    # is what the heading heuristic needs (PyMuPDF's get_text("dict") supplied
    # the equivalent as pre-grouped blocks with spans).
    try:
        lines = page.extract_text_lines()
    except Exception:  # noqa: BLE001 - a page whose text layer cannot be read
        # must degrade to the FR-043 placeholder, not fail the whole document.
        lines = []

    image_block_count = len(page.images)

    heading = None
    body_lines = []

    for index, line in enumerate(lines):
        if _line_is_inside_any_table(line, table_bboxes):
            continue

        text = (line.get("text") or "").strip()
        if not text:
            continue

        if (
            heading is None
            and index == 0
            and _line_max_font_size(line) >= HEADING_FONT_SIZE_THRESHOLD
        ):
            heading = text
            continue

        body_lines.append(text)

    blocks = []
    if body_lines:
        blocks.append({"type": "paragraph", "text": "\n".join(body_lines)})

    for _ in range(image_block_count):
        blocks.append(image_placeholder_block())

    for table in tables:
        rows = table.extract()
        if not rows:
            continue
        blocks.append(
            {
                "type": "table",
                "headers": [_cell_text(cell) for cell in rows[0]],
                "rows": [[_cell_text(cell) for cell in row] for row in rows[1:]],
            }
        )

    has_no_text = not blocks
    if has_no_text:
        blocks.append(
            unextractable_text_block(
                f"Page {page_number} has no extractable text (e.g., a scanned page)."
            )
        )

    return has_no_text, image_block_count, {
        "heading": heading,
        "headingLevel": 1 if heading else None,
        "blocks": blocks,
        "location": {"pageNumber": page_number, "slideNumber": None, "sheetName": None},
    }


def _guard_openable(file_path):
    """BR-003 needs "password-protected" and "corrupted" to be different
    messages, but pdfplumber collapses both into one PdfminerException. Asking
    pdfminer directly, before pdfplumber opens the file, recovers the
    distinction - PyMuPDF gave it to us for free via its needs_pass flag.
    """
    file_name = os.path.basename(file_path)
    try:
        with open(file_path, "rb") as handle:
            document = PDFDocument(PDFParser(handle))
    except PDFPasswordIncorrect:
        raise ExtractionError(
            "unsupportedFile", "Password-protected PDFs are not supported in this release."
        )
    except PDFSyntaxError as ex:
        raise ExtractionError(
            "corruptedDocument", f"'{file_name}' could not be opened: {ex}"
        )
    except Exception as ex:  # noqa: BLE001 - anything else at parse time is a
        # broken file as far as the caller is concerned.
        raise ExtractionError(
            "corruptedDocument", f"'{file_name}' could not be opened: {ex}"
        )

    # An encrypted-but-blank-password file parses without raising; it still
    # cannot be extracted meaningfully.
    if getattr(document, "encryption", None) is not None:
        raise ExtractionError(
            "unsupportedFile", "Password-protected PDFs are not supported in this release."
        )


def _author(pdf):
    author = pdf.metadata.get("Author") if pdf.metadata else None
    if isinstance(author, bytes):
        author = author.decode("utf-8", errors="replace")
    return normalize_optional_text(author)


def extract(file_path):
    check_file_accessible(file_path)
    _guard_openable(file_path)

    try:
        pdf = pdfplumber.open(file_path)
    except Exception as ex:  # noqa: BLE001
        raise ExtractionError(
            "corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}"
        )

    try:
        sections = []
        warnings = []
        pages_without_text = []
        total_images_omitted = 0

        for index, page in enumerate(pdf.pages):
            page_number = index + 1
            has_no_text, image_count, section = _extract_page(page, page_number)
            sections.append(section)

            if has_no_text:
                pages_without_text.append(page_number)
            total_images_omitted += image_count

        page_count = len(pdf.pages)
        author = _author(pdf)
    finally:
        pdf.close()

    # SaaS audit C-03: a placeholder block on its own let a fully scanned PDF
    # report plain success. An Error-severity warning is what forces the host to
    # report "completed with warnings" instead.
    if pages_without_text:
        all_pages = len(pages_without_text) == page_count
        scope = (
            f"any of the {page_count} pages"
            if all_pages
            else f"{len(pages_without_text)} of {page_count} pages"
        )
        warnings.append(
            model.warning(
                model.NO_EXTRACTABLE_TEXT,
                model.ERROR,
                (
                    f"No text could be extracted from {scope} (typically a scanned document). "
                    f"OCR is not enabled, so this content was not recovered."
                ),
                location=model.location(page_number=pages_without_text[0]),
                details={
                    "pagesWithoutText": ",".join(str(p) for p in pages_without_text[:50]),
                    "pagesWithoutTextCount": len(pages_without_text),
                    "totalPages": page_count,
                },
            )
        )

    # SaaS SR-INT-4: image omissions must be visible in the results panel and
    # the export manifest, not only as an inline placeholder.
    if total_images_omitted:
        warnings.append(
            model.warning(
                model.IMAGE_OMITTED,
                model.INFO,
                (
                    f"{total_images_omitted} embedded image(s) were not extracted. A placeholder "
                    f"marks each position in the output."
                ),
                details={"imageCount": total_images_omitted},
            )
        )

    metadata = {
        "sourceFilePath": file_path,
        "fileType": "pdf",
        "createdDate": file_created_date_iso(file_path),
        "convertedDate": converted_date_iso(),
        "pageCount": page_count,
        "slideCount": None,
        "sheetCount": None,
        "author": author,
    }

    return model.document(metadata, sections, warnings)
