"""Shared helpers for all four Python-backed extractors (ADR-002) - kept here so
metadata-date handling and error categorization are implemented exactly once.
"""

import os
from datetime import datetime, timezone


class ExtractionError(Exception):
    """Raised by an extractor for an error that maps to a specific FR-029
    category (e.g., a password-protected file). Anything else an extractor
    raises is caught by dispatch.py and reported as unexpectedException.
    """

    def __init__(self, category, message):
        super().__init__(message)
        self.category = category


def file_created_date_iso(file_path):
    # A consistent source across all four formats (filesystem creation time)
    # rather than parsing each format's own embedded-metadata date field, which
    # varies in availability and format (e.g., PDF's "D:YYYYMMDDHHmmSS" string).
    return datetime.fromtimestamp(os.path.getctime(file_path), tz=timezone.utc).isoformat()


def converted_date_iso():
    return datetime.now(timezone.utc).isoformat()


def normalize_optional_text(value):
    """Turns an empty/whitespace-only string (common for absent metadata in
    several of these libraries) into None, so FR-013's "omit when absent" rule
    has consistent input to work with.
    """
    if value is None:
        return None
    stripped = str(value).strip()
    return stripped if stripped else None


def unextractable_text_block(note):
    return {
        "type": "unextractableText",
        "reason": "placeholderNoText",
        "note": note,
    }


def image_placeholder_block(alt_text=None):
    return {"type": "imagePlaceholder", "altText": alt_text}
