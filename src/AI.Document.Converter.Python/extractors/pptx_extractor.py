"""PPTX extraction (FR-009, FR-039) via python-pptx."""

import os

from pptx import Presentation
from pptx.enum.shapes import MSO_SHAPE_TYPE
from pptx.exc import PackageNotFoundError

from extractors.common import (
    ExtractionError,
    check_file_accessible,
    check_not_encrypted_ooxml,
    converted_date_iso,
    file_created_date_iso,
    image_placeholder_block,
    normalize_optional_text,
    unextractable_text_block,
)


def _is_title_shape(shape, slide):
    return slide.shapes.title is not None and shape.shape_id == slide.shapes.title.shape_id


def _slide_body_blocks(slide):
    blocks = []

    for shape in slide.shapes:
        if shape.shape_type == MSO_SHAPE_TYPE.PICTURE:
            blocks.append(image_placeholder_block())
            continue

        if not shape.has_text_frame or _is_title_shape(shape, slide):
            continue

        items = [p.text.strip() for p in shape.text_frame.paragraphs if p.text.strip()]
        if items:
            blocks.append({"type": "list", "isOrdered": False, "items": items})

    if slide.has_notes_slide:
        notes_text = slide.notes_slide.notes_text_frame.text.strip()
        if notes_text:
            # No dedicated "speaker notes" block type in the schema (FR-009 only
            # requires the content be preserved) - a plainly labeled paragraph
            # keeps notes textually distinguishable from slide body content.
            blocks.append({"type": "paragraph", "text": f"Speaker notes: {notes_text}"})

    if not blocks:
        blocks.append(unextractable_text_block("Slide has no extractable text content."))

    return blocks


def extract(file_path):
    check_file_accessible(file_path)
    check_not_encrypted_ooxml(file_path)

    try:
        presentation = Presentation(file_path)
    except PackageNotFoundError as ex:
        raise ExtractionError(
            "corruptedDocument", f"'{os.path.basename(file_path)}' could not be opened: {ex}"
        )

    sections = []
    for index, slide in enumerate(presentation.slides):
        slide_number = index + 1
        title_shape = slide.shapes.title
        title = title_shape.text.strip() if title_shape and title_shape.has_text_frame else ""

        sections.append(
            {
                "heading": title or None,
                "headingLevel": 1 if title else None,
                "blocks": _slide_body_blocks(slide),
                "location": {"pageNumber": None, "slideNumber": slide_number, "sheetName": None},
            }
        )

    return {
        "metadata": {
            "sourceFilePath": file_path,
            "fileType": "pptx",
            "createdDate": file_created_date_iso(file_path),
            "convertedDate": converted_date_iso(),
            "pageCount": None,
            "slideCount": len(presentation.slides),
            "sheetCount": None,
            "author": normalize_optional_text(presentation.core_properties.author),
        },
        "sections": sections,
    }
