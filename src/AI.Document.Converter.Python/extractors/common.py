"""Shared helpers for all four Python-backed extractors (ADR-002) - kept here so
metadata-date handling and error categorization are implemented exactly once.
"""

import ctypes
import os
from ctypes import wintypes
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


_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_kernel32.CreateFileW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
    wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE,
]
_kernel32.CreateFileW.restype = wintypes.HANDLE
_kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
_kernel32.CloseHandle.restype = wintypes.BOOL

_GENERIC_READ = 0x80000000
_FILE_SHARE_READ_WRITE_DELETE = 0x00000001 | 0x00000002 | 0x00000004
_OPEN_EXISTING = 3
_INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value
_ERROR_SHARING_VIOLATION = 32


def check_file_accessible(file_path):
    """FR-029: fileNotFound/fileLocked/permissionDenied categorization shared
    by all four extractors, run before any format-specific open call - so an
    OS-level access failure is never mistaken for a corrupted document by a
    library's own open() catch block.

    Uses CreateFileW directly rather than Python's open(): CPython's own I/O
    layer maps BOTH a real ACL denial (Win32 ERROR_ACCESS_DENIED) and another
    process's exclusive lock (Win32 ERROR_SHARING_VIOLATION) to the same
    PermissionError with no usable winerror attribute on this Python/Windows
    combination - verified empirically, not assumed. Asking Win32 directly
    (with a maximally permissive share mode on our own side, so we only ever
    fail because of the OTHER handle's conflicting lock or ACLs) recovers the
    real error code.
    """
    if not os.path.exists(file_path):
        raise ExtractionError("fileNotFound", f"File not found: {file_path}")

    handle = _kernel32.CreateFileW(
        file_path,
        _GENERIC_READ,
        _FILE_SHARE_READ_WRITE_DELETE,
        None,
        _OPEN_EXISTING,
        0,
        None,
    )

    if handle == _INVALID_HANDLE_VALUE:
        error_code = ctypes.get_last_error()
        if error_code == _ERROR_SHARING_VIOLATION:
            raise ExtractionError(
                "fileLocked", f"'{os.path.basename(file_path)}' is in use by another process.")
        raise ExtractionError(
            "permissionDenied",
            f"Cannot access '{os.path.basename(file_path)}' (Windows error {error_code}).")

    _kernel32.CloseHandle(handle)


_OLE_COMPOUND_FILE_SIGNATURE = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"


def check_not_encrypted_ooxml(file_path):
    """BR-003: a password-protected OOXML file (docx/xlsx/pptx) is stored as
    an OLE Compound File wrapper around an "EncryptedPackage" stream, not a
    plain ZIP - python-docx/openpyxl/python-pptx can't open it at all, and
    without this check it would surface as a generic corruptedDocument
    failure instead of BR-003's required clear "not supported" message.
    (PDF doesn't need this - pymupdf's own needs_pass flag already covers it.)
    """
    with open(file_path, "rb") as f:
        header = f.read(len(_OLE_COMPOUND_FILE_SIGNATURE))

    if header == _OLE_COMPOUND_FILE_SIGNATURE:
        raise ExtractionError(
            "unsupportedFile",
            f"'{os.path.basename(file_path)}' appears to be password-protected, "
            "which is not supported in this release.",
        )
