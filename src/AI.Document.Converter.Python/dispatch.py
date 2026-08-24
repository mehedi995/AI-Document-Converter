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

import json
import sys


def handle_health_check(_payload):
    return success_response(result=None)


# New operations are added here as later phases introduce them:
# Phase 3 adds "extract", Phase 6 adds "tokenize".
OPERATION_HANDLERS = {
    "health_check": handle_health_check,
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

    try:
        request = json.loads(raw_request)
    except json.JSONDecodeError as ex:
        write_response(error_response("pythonEngineFailure", f"Invalid request JSON: {ex}"))
        return

    operation = request.get("operation")
    handler = OPERATION_HANDLERS.get(operation)

    if handler is None:
        write_response(error_response("pythonEngineFailure", f"Unknown operation: {operation}"))
        return

    try:
        response = handler(request.get("payload"))
    except Exception as ex:  # noqa: BLE001 - a broad catch here IS the boundary: any
        # extractor bug must become a categorized error response, never a stack
        # trace on stderr that the .NET side has to guess at (FR-029, FR-031).
        write_response(error_response("unexpectedException", str(ex)))
        return

    write_response(response)


def write_response(response):
    sys.stdout.write(json.dumps(response))
    sys.stdout.flush()


if __name__ == "__main__":
    main()
