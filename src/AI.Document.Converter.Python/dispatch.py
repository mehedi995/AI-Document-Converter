"""JSON stdin/stdout entry point for the bundled Python engine (ADR-001).

Reads exactly one JSON request line from stdin, dispatches it by "operation", and
writes exactly one JSON response line to stdout. This process is started fresh by
PythonEngineClient for every single request - it is not a long-running server.

Wire format is plain camelCase JSON (see PythonEngineClient.cs), independent of
either language's native naming convention:

  Request:  {"operation": "...", "payload": ...}
  Response: {"success": true, "result": ..., "errorCategory": null, "errorMessage": null}
         or {"success": false, "result": null, "errorCategory": "...", "errorMessage": "..."}
"""

import contextlib
import io
import json
import os
import sys

import tokenizer
from extractors.common import ExtractionError
from extractors import docx_extractor, pdf_extractor, pptx_extractor, xlsx_extractor

EXTRACTORS_BY_FORMAT = {
    "pdf": pdf_extractor.extract,
    "docx": docx_extractor.extract,
    "xlsx": xlsx_extractor.extract,
    "pptx": pptx_extractor.extract,
}


def handle_health_check(_payload):
    return None


def handle_extract(payload):
    file_format = payload.get("format")
    file_path = payload.get("filePath")

    extractor = EXTRACTORS_BY_FORMAT.get(file_format)
    if extractor is None:
        raise ExtractionError("unsupportedFile", f"No extractor registered for format: {file_format}")

    if not os.path.exists(file_path):
        raise ExtractionError("fileNotFound", f"File not found: {file_path}")

    return extractor(file_path)


def handle_tokenize(payload):
    return tokenizer.estimate(payload)


def handle_count_tokens(payload):
    return tokenizer.count_batch(payload)


OPERATION_HANDLERS = {
    "health_check": handle_health_check,
    "extract": handle_extract,
    "tokenize": handle_tokenize,
    "count_tokens": handle_count_tokens,
}


def success_response(result):
    return {
        "success": True,
        "result": result,
        "errorCategory": None,
        "errorMessage": None,
    }


def error_response(error_category, error_message):
    return {
        "success": False,
        "result": None,
        "errorCategory": error_category,
        "errorMessage": error_message,
    }


def main():
    raw_request = sys.stdin.readline()
    # Some libraries print informational messages directly to stdout (observed:
    # pymupdf's find_tables() prints a "consider using pymupdf_layout" notice) -
    # that would corrupt this process's one and only JSON response line. stdout
    # is redirected to a throwaway buffer for the entire duration of the
    # request; real_stdout is the only stream write_response ever uses.
    real_stdout = sys.stdout

    try:
        request = json.loads(raw_request)
    except json.JSONDecodeError as ex:
        write_response(real_stdout, error_response("pythonEngineFailure", f"Invalid request JSON: {ex}"))
        return

    operation = request.get("operation")
    handler = OPERATION_HANDLERS.get(operation)

    if handler is None:
        write_response(real_stdout, error_response("pythonEngineFailure", f"Unknown operation: {operation}"))
        return

    try:
        with contextlib.redirect_stdout(io.StringIO()):
            result = handler(request.get("payload") or {})
    except ExtractionError as ex:
        # A known, categorizable failure (FR-029) - e.g., a missing or
        # password-protected file.
        write_response(real_stdout, error_response(ex.category, str(ex)))
        return
    except Exception as ex:  # noqa: BLE001 - this IS the boundary: any bug in an
        # extractor must become a categorized error response, never a stack
        # trace on stderr that the .NET side has to guess at (FR-029, FR-031).
        write_response(real_stdout, error_response("unexpectedException", str(ex)))
        return

    write_response(real_stdout, success_response(result))


def write_response(stream, response):
    stream.write(json.dumps(response))
    stream.flush()


if __name__ == "__main__":
    main()
