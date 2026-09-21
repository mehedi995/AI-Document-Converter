"""Shared helpers for all four Python-backed extractors (ADR-002) - kept here so
metadata-date handling and error categorization are implemented exactly once.
"""

import ctypes
import os
import sys
from datetime import datetime, timedelta, timezone

# The Win32 file-accessibility probe below is Windows-only, and importing
# ctypes.wintypes on Linux raises ValueError outright. This module is imported
# unconditionally by dispatch.py, so an unguarded import made the whole engine -
# every operation, including health_check - fail to start on a Linux container
# (SaaS audit A-03). The guard keeps the desktop behavior byte-for-byte while
# letting the same source run on a Linux worker.
IS_WINDOWS = sys.platform == "win32"

if IS_WINDOWS:
    from ctypes import wintypes


class ExtractionError(Exception):
    """Raised by an extractor for an error that maps to a specific FR-029
    category (e.g., a password-protected file). Anything else an extractor
    raises is caught by dispatch.py and reported as unexpectedException.
    """

    def __init__(self, category, message):
        super().__init__(message)
        self.category = category


def embedded_created_date_iso(value):
    """The SOURCE document's own creation date, from the file's embedded
    metadata, or None when the format does not record one (audit B-08).

    Deliberately NOT the filesystem timestamp, which this used to be. On a
    server the file is created by the upload, so os.path.getctime reports when
    we received it - a plausible-looking wrong answer in the front matter of
    every converted document. It is not reliable on the desktop either: copying
    a file resets it. Omitting the field is the honest alternative, and the
    author field already works that way (FR-013).

    OOXML records dcterms:created in UTC, but the libraries disagree about
    saying so: python-docx returns it tz-aware while openpyxl and python-pptx
    return a naive datetime for the same value. A naive value is therefore read
    as UTC, not as local time.
    """
    if not isinstance(value, datetime):
        return None

    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)

    return value.astimezone(timezone.utc).isoformat()


def pdf_created_date_iso(raw):
    """Parses a PDF /CreationDate into the same ISO-8601 UTC string the other
    formats produce, or None when it is absent or unparseable.

    PDF 32000-1 section 7.9.4 spells the date "D:YYYYMMDDHHmmSSOHH'mm'", where
    everything after the year is optional and O is +, - or Z. Real files do
    truncate it, so a short value is completed from defaults rather than
    treated as corrupt; a value that is the wrong shape yields None, because a
    wrong date is worse here than no date.
    """
    if not isinstance(raw, str):
        return None

    text = raw.strip()
    if text.startswith("D:"):
        text = text[2:]

    parts = [text[0:4], text[4:6], text[6:8], text[8:10], text[10:12], text[12:14]]
    # The year has no default: a value that does not even start with one is not
    # a date. Everything after it falls back to the start of the period.
    defaults = [None, 1, 1, 0, 0, 0]

    values = []
    for part, default, width in zip(parts, defaults, [4, 2, 2, 2, 2, 2]):
        if not part and default is not None:
            values.append(default)
            continue
        if len(part) != width or not part.isdigit():
            return None
        values.append(int(part))

    try:
        stamp = datetime(*values, tzinfo=_pdf_utc_offset(text[14:]))
    except ValueError:
        # Out-of-range components, e.g. month 19 or day 31 in February.
        return None

    return stamp.astimezone(timezone.utc).isoformat()


def _pdf_utc_offset(suffix):
    """The trailing O HH ' mm ' of a PDF date. Absent, "Z" or malformed all
    mean UTC - the date itself is still worth keeping when only its offset is
    unreadable, and UTC is what an offsetless PDF date means anyway.
    """
    text = suffix.strip().replace("'", "")

    if not text or text[0] not in "+-":
        return timezone.utc

    if not text[1:3].isdigit():
        return timezone.utc

    hours = int(text[1:3])
    minutes = int(text[3:5]) if text[3:5].isdigit() else 0

    if hours > 23 or minutes > 59:
        return timezone.utc

    offset = timedelta(hours=hours, minutes=minutes)
    return timezone(-offset if text[0] == "-" else offset)


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


if IS_WINDOWS:
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


def _check_file_accessible_windows(file_path):
    """Uses CreateFileW directly rather than Python's open(): CPython's own I/O
    layer maps BOTH a real ACL denial (Win32 ERROR_ACCESS_DENIED) and another
    process's exclusive lock (Win32 ERROR_SHARING_VIOLATION) to the same
    PermissionError with no usable winerror attribute on this Python/Windows
    combination - verified empirically, not assumed. Asking Win32 directly
    (with a maximally permissive share mode on our own side, so we only ever
    fail because of the OTHER handle's conflicting lock or ACLs) recovers the
    real error code.
    """
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


def _check_file_accessible_posix(file_path):
    """POSIX has no mandatory file locking, so there is no "another process
    holds it exclusively" condition to detect - fileLocked is genuinely not
    reachable here, and reporting it would be a lie. A plain read attempt is
    therefore sufficient and correct: EACCES/EPERM is the only OS-level access
    failure this platform distinguishes.
    """
    try:
        with open(file_path, "rb"):
            pass
    except PermissionError:
        raise ExtractionError(
            "permissionDenied", f"Cannot access '{os.path.basename(file_path)}' (permission denied).")
    except OSError as ex:
        raise ExtractionError(
            "permissionDenied", f"Cannot access '{os.path.basename(file_path)}' ({ex.strerror}).")


def check_file_accessible(file_path):
    """FR-029: fileNotFound/fileLocked/permissionDenied categorization shared
    by all four extractors, run before any format-specific open call - so an
    OS-level access failure is never mistaken for a corrupted document by a
    library's own open() catch block.

    The existence check is platform-independent; the access probe is not (see
    the two helpers above).
    """
    if not os.path.exists(file_path):
        raise ExtractionError("fileNotFound", f"File not found: {file_path}")

    if IS_WINDOWS:
        _check_file_accessible_windows(file_path)
    else:
        _check_file_accessible_posix(file_path)


_OLE_COMPOUND_FILE_SIGNATURE = b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1"


def check_not_encrypted_ooxml(file_path):
    """BR-003: a password-protected OOXML file (docx/xlsx/pptx) is stored as
    an OLE Compound File wrapper around an "EncryptedPackage" stream, not a
    plain ZIP - python-docx/openpyxl/python-pptx can't open it at all, and
    without this check it would surface as a generic corruptedDocument
    failure instead of BR-003's required clear "not supported" message.
    (PDF doesn't need this - pdf_extractor._guard_openable covers it there, by
    asking pdfminer directly. It has to: unlike PyMuPDF's old needs_pass flag,
    pdfplumber reports an encrypted file and a corrupt file as the same
    exception type, so the two are separated before pdfplumber opens the file.)
    """
    with open(file_path, "rb") as f:
        header = f.read(len(_OLE_COMPOUND_FILE_SIGNATURE))

    if header == _OLE_COMPOUND_FILE_SIGNATURE:
        raise ExtractionError(
            "unsupportedFile",
            f"'{os.path.basename(file_path)}' appears to be password-protected, "
            "which is not supported in this release.",
        )
