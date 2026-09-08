"""Normalized document model v2 helpers (SaaS audit B-04/B-05/B-06).

v1 returned only {"metadata", "sections"}. It had no way to say "this output is
incomplete", which is why a 1000-row sheet could come back as 5 rows reported as
a success (audit C-01). v2 adds three things:

  modelVersion / engineVersion  - so an artifact can be traced to its contract
  warnings[]                    - what was NOT recovered, machine-readable
  blockId on every block        - a stable anchor for chunks and warnings

Wire format is camelCase JSON, matching the rest of the engine contract.
"""

MODEL_VERSION = "2.0"

# Bump when extraction behavior changes in a way that can alter output for the
# same input bytes. Independent of the .NET assembly version on purpose - the
# engine and the host ship on their own cadences.
ENGINE_VERSION = "1.1.0"

# Severities. Error means supported content was NOT recovered; the host turns
# that into "completed with warnings", never a plain success.
INFO = "info"
WARNING = "warning"
ERROR = "error"

# Warning codes - must match AI.Document.Converter.Domain.Enums.WarningCode.
# Never rename or repurpose a released value; add a new one instead.
SHEET_TRUNCATED = "sheetTruncated"
NO_EXTRACTABLE_TEXT = "noExtractableText"
IMAGE_OMITTED = "imageOmitted"
FORMULA_VALUE_UNAVAILABLE = "formulaValueUnavailable"
TABLE_EXCEEDS_CHUNK_SIZE = "tableExceedsChunkSize"


def warning(code, severity, message, location=None, block_id=None, details=None):
    """Builds one warning. `details` values are stringified so the contract
    never depends on how a given JSON parser types numbers.
    """
    return {
        "code": code,
        "severity": severity,
        "message": message,
        "location": location,
        "blockId": block_id,
        "details": {k: str(v) for k, v in (details or {}).items()} or None,
    }


def location(page_number=None, slide_number=None, sheet_name=None):
    return {"pageNumber": page_number, "slideNumber": slide_number, "sheetName": sheet_name}


def block_id(section_index, block_index):
    """Positional and therefore deterministic: the same bytes extracted twice by
    the same engine version produce the same IDs. Deliberately not a UUID -
    a random ID would be useless as a stable anchor.

    Both indexes are 1-based to match how page/slide numbers are reported
    everywhere else in this contract.
    """
    return f"s{section_index}-b{block_index}"


def assign_block_ids(sections):
    """Stamps blockId onto every block, in document order. Called once per
    extraction, after all sections are built, so an extractor never has to
    thread an index counter through its own logic.
    """
    for section_index, section in enumerate(sections, start=1):
        for block_index, block in enumerate(section.get("blocks") or [], start=1):
            block["blockId"] = block_id(section_index, block_index)
    return sections


def document(metadata, sections, warnings=None):
    """The single place a v2 extraction result is shaped, so no extractor can
    accidentally omit the version fields or the warnings array.
    """
    return {
        "modelVersion": MODEL_VERSION,
        "engineVersion": ENGINE_VERSION,
        "metadata": metadata,
        "sections": assign_block_ids(sections),
        "warnings": warnings or [],
    }
