"""PDF extraction (FR-006, FR-043) via pymupdf.

Model per page (a deliberately simple heuristic - see docs/18-RISK-ASSESSMENT.md
R-07/R-08 for the accepted limitations of automatic PDF structure detection):
- at most one heading, taken from the first text block if its font size clears
  HEADING_FONT_SIZE_THRESHOLD;
- the rest of the page's non-table text as a single paragraph block;
- one table block per table pymupdf's find_tables() detects;
- if none of the above produced anything, a single unextractableText block
  (FR-043) instead of silently omitting the page.
"""

import os

import pymupdf

from extractors.common import (
    ExtractionError,
    check_file_accessible,
    converted_date_iso,
    file_created_date_iso,
    normalize_optional_text,
    unextractable_text_block,
)

HEADING_FONT_SIZE_THRESHOLD = 14


def _table_regions(page):
    return [pymupdf.Rect(table.bbox) for table in page.find_tables().tables]


def _overlaps_any(bbox, regions):
    rect = pymupdf.Rect(bbox)
    return any(rect.intersects(region) for region in regions)


def _block_text(block):
    return "".join(
        span["text"] for line in block.get("lines", []) for span in line.get("spans", [])
    ).strip()


def _max_font_size(block):
    sizes = [span["size"] for line in block.get("lines", []) for span in line.get("spans", [])]
    return max(sizes) if sizes else 0


def _extract_page(page, page_number):
    tables = page.find_tables().tables
    table_regions = [pymupdf.Rect(t.bbox) for t in tables]

    text_blocks = [b for b in page.get_text("dict")["blocks"] if b.get("type") == 0]
    text_blocks.sort(key=lambda b: (b["bbox"][1], b["bbox"][0]))

    heading = None
    body_paragraphs = []

    for index, block in enumerate(text_blocks):
        if _overlaps_any(block["bbox"], table_regions):
            continue

        text = _block_text(block)
        if not text:
            continue

        if heading is None and index == 0 and _max_font_size(block) >= HEADING_FONT_SIZE_THRESHOLD:
            heading = text
            continue

        body_paragraphs.append(text)

    blocks = []
    if body_paragraphs:
        blocks.append({"type": "paragraph", "text": "\n".join(body_paragraphs)})

    for table in tables:
        rows = table.extract()
        if not rows:
            continue
        blocks.append(
            {
                "type": "table",
                "headers": [str(cell) if cell is not None else "" for cell in rows[0]],
                "rows": [
                    [str(cell) if cell is not None else "" for cell in row] for row in rows[1:]
                ],
            }
        )

    if not blocks:
        blocks.append(
            unextractable_text_block(
                f"Page {page_number} has no extractable text (e.g., a scanned page)."
            )
        )

    return {
        "heading": heading,
        "headingLevel": 1 if heading else None,
        "blocks": blocks,
        "location": {"pageNumber": page_number, "slideNumber": None, "sheetName": None},
    }


def extract(file_path):
    check_file_accessible(file_path)

    try:
        document = pymupdf.open(file_path)
    except pymupdf.FileDataError as ex:
        raise ExtractionError("corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}")

    if document.needs_pass:
        raise ExtractionError(
            "unsupportedFile", "Password-protected PDFs are not supported in this release."
        )

    sections = [_extract_page(document[i], i + 1) for i in range(len(document))]

    return {
        "metadata": {
            "sourceFilePath": file_path,
            "fileType": "pdf",
            "createdDate": file_created_date_iso(file_path),
            "convertedDate": converted_date_iso(),
            "pageCount": len(document),
            "slideCount": None,
            "sheetCount": None,
            "author": normalize_optional_text(document.metadata.get("author")),
        },
        "sections": sections,
    }
