"""DOCX extraction (FR-007) via python-docx.

python-docx exposes document.paragraphs and document.tables as two separate,
unordered collections, which would lose the original reading order of a document
that interleaves paragraphs and tables. _iter_block_items below is the standard
recipe for walking the document body in its actual XML order instead.
"""

import os
import re
from zipfile import BadZipFile

from docx import Document
from docx.opc.exceptions import PackageNotFoundError
from docx.oxml.ns import qn
from docx.oxml.table import CT_Tbl
from docx.oxml.text.paragraph import CT_P
from docx.table import Table
from docx.text.paragraph import Paragraph

from extractors.common import (
    ExtractionError,
    check_file_accessible,
    check_not_encrypted_ooxml,
    converted_date_iso,
    file_created_date_iso,
    normalize_optional_text,
    unextractable_text_block,
)


def _iter_block_items(document):
    for child in document.element.body.iterchildren():
        if isinstance(child, CT_P):
            yield Paragraph(child, document)
        elif isinstance(child, CT_Tbl):
            yield Table(child, document)


def _heading_level(style_name):
    match = re.search(r"(\d+)", style_name or "")
    return int(match.group(1)) if match else 1


def _paragraph_text_with_inline_links(paragraph):
    # paragraph.text (python-docx's own property) already walks into
    # w:hyperlink elements for their visible text, so a naive "plain text plus
    # a separate LinkBlock per hyperlink" would duplicate the link text in the
    # output. Markdown represents an inline link as `[text](url)` inline in the
    # running prose anyway, so building that directly here - rather than a
    # standalone LinkBlock - is both correct and the more natural fit; LinkBlock
    # remains available for contexts where a link isn't embedded in prose.
    parts = []
    for child in paragraph._p:
        if child.tag == qn("w:r"):
            parts.append("".join(node.text or "" for node in child.iter(qn("w:t"))))
        elif child.tag == qn("w:hyperlink"):
            text = "".join(node.text or "" for node in child.iter(qn("w:t"))).strip()
            r_id = child.get(qn("r:id"))
            url = None
            if r_id:
                try:
                    url = paragraph.part.rels[r_id].target_ref
                except KeyError:
                    url = None
            parts.append(f"[{text}]({url})" if text and url else text)
    return "".join(parts).strip()


def extract(file_path):
    check_file_accessible(file_path)
    check_not_encrypted_ooxml(file_path)

    try:
        document = Document(file_path)
    except (PackageNotFoundError, BadZipFile) as ex:
        raise ExtractionError(
            "corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}"
        )

    sections = []
    current_section = {"heading": None, "headingLevel": None, "blocks": [], "location": None}
    pending_list = None

    def flush_list():
        nonlocal pending_list
        if pending_list is not None and pending_list["items"]:
            current_section["blocks"].append(pending_list)
        pending_list = None

    def start_new_section(heading_text, level):
        nonlocal current_section
        flush_list()
        if current_section["heading"] or current_section["blocks"]:
            sections.append(current_section)
        current_section = {"heading": heading_text, "headingLevel": level, "blocks": [], "location": None}

    for item in _iter_block_items(document):
        if isinstance(item, Paragraph):
            style_name = item.style.name if item.style else ""
            text = _paragraph_text_with_inline_links(item)

            if style_name.startswith("Heading") or style_name == "Title":
                start_new_section(text, _heading_level(style_name))
                continue

            if "List" in style_name:
                is_ordered = "Number" in style_name
                if pending_list is None or pending_list["isOrdered"] != is_ordered:
                    flush_list()
                    pending_list = {"type": "list", "isOrdered": is_ordered, "items": []}
                if text:
                    pending_list["items"].append(text)
                continue

            flush_list()

            if text:
                current_section["blocks"].append({"type": "paragraph", "text": text})

        elif isinstance(item, Table):
            flush_list()
            rows = [[cell.text for cell in row.cells] for row in item.rows]
            if rows:
                current_section["blocks"].append(
                    {"type": "table", "headers": rows[0], "rows": rows[1:]}
                )

    flush_list()
    if current_section["heading"] or current_section["blocks"]:
        sections.append(current_section)

    if not sections:
        sections.append(
            {
                "heading": None,
                "headingLevel": None,
                "blocks": [unextractable_text_block("Document has no extractable content.")],
                "location": None,
            }
        )

    core_properties = document.core_properties

    return {
        "metadata": {
            "sourceFilePath": file_path,
            "fileType": "docx",
            "createdDate": file_created_date_iso(file_path),
            "convertedDate": converted_date_iso(),
            "pageCount": None,
            "slideCount": None,
            "sheetCount": None,
            "author": normalize_optional_text(core_properties.author),
        },
        "sections": sections,
    }
