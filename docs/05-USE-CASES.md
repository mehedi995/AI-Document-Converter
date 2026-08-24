# 05 — Use Cases

**Project:** AI Document Converter
**Status:** Draft — Phase 5
**Date:** 2026-08-23

---

## Actors

- **Business User** — primary actor who imports and converts documents.
- **Administrator** — configures application settings (may be the same person as the
  Business User in practice).
- **Python Processing Engine** — internal supporting actor (not a human), invoked by
  the system to perform extraction.
- **File System** — supporting actor providing source files and receiving output.

---

## UC-001 — Import and Convert a Single Document

**Actor:** Business User
**Related:** FR-001, FR-005, FR-006–FR-013, US-001, US-006–US-011

**Preconditions:** Application is running. User has at least one supported file
accessible on disk.

**Main Flow:**
1. User selects "Import File" and chooses one file via the file picker.
2. System validates the file extension is supported.
3. System adds the file to the conversion list showing name, type, and size.
4. User selects "Convert."
5. System sends the file to the appropriate document processor (via the Python
   engine where applicable).
6. System builds the normalized document model from the extracted content.
7. System generates structured Markdown with front-matter metadata.
8. System estimates original and converted token counts and displays the reduction.
9. System writes the Markdown output to the configured output directory.
10. System displays a success result with output location.

**Alternative Flow — Unsupported File (FR-005):**
2a. File extension is not supported → system shows a clear rejection message and does
    not add the file to the conversion list. Flow ends.

**Exception Flow — Extraction Failure:**
5a. Python engine reports an extraction failure (corrupted file, password-protected,
    engine crash) → system categorizes the error (FR-029), logs technical detail, and
    shows a user-friendly message with a retry option where applicable. Flow ends for
    this file.

**Exception Flow — Partially Extractable PDF (FR-043):**
5b. The PDF contains one or more pages with no extractable text (e.g., a scanned page)
    → system marks those pages with a placeholder note in the Markdown output and
    continues; the file is still reported as an overall success.

**Postconditions:** A Markdown file exists in the output directory (success path), or
the user has a clear explanation of why conversion did not occur (failure path).

---

## UC-002 — Batch Convert Multiple Documents

**Actor:** Business User
**Related:** FR-002–FR-004, FR-022–FR-025, FR-036, FR-037, FR-045, NFR-013,
US-002–US-004, US-016–US-018, US-025

**Preconditions:** Application is running.

**Main Flow:**
1. User imports multiple files via multi-select, drag-and-drop, or folder selection.
2. System validates each file; unsupported files are flagged individually without
   blocking the rest.
3. User selects "Convert All."
4. System processes files asynchronously with bounded parallelism (NFR-011),
   updating: total files, processed count, success count, failure count, current
   file, and progress percentage in real time.
5. UI remains responsive throughout (NFR-003, PERF-001/002).
6. On completion, system displays a batch summary (successes, failures, total token
   reduction).

**Alternative Flow — Partial Failure:**
4a. One or more files fail during batch processing → system records each failure with
    its category and continues processing remaining files (BR-006).

**Exception Flow — Cancellation (FR-037, US-025):**
4b. User cancels the batch → system stops issuing new file-processing tasks, allows
    in-flight files to complete or cancel cleanly, and reports the partial summary.

**Exception Flow — Batch Size Ceiling Exceeded (NFR-013):**
1a. The imported selection exceeds the configured maximum batch size or total data
    volume → system accepts files up to the ceiling and clearly reports how many
    files/how much data were excluded.

**Postconditions:** All successfully processed files have Markdown output; a summary
report reflects the outcome of every file in the batch.

---

## UC-003 — Generate AI-Ready Chunks

**Actor:** Business User
**Related:** FR-018–FR-021, US-014–US-015

**Preconditions:** A document has been successfully converted to Markdown.

**Main Flow:**
1. User opens Chunk Settings and sets chunk size and overlap (or accepts defaults).
2. User selects "Generate Chunks."
3. System splits the converted Markdown into chunk files according to the configured
   size/overlap, avoiding splitting tables (BR-004) and avoiding separating headings
   from their immediate content where possible.
4. System writes numbered chunk files (e.g., `chunk_001.md`) with source/sequence
   metadata to the output directory.

**Exception Flow:**
3a. Chunk size is smaller than an atomic structure (e.g., a very large table) → system
    keeps the atomic structure intact even if it exceeds the configured chunk size, and
    logs a warning.

**Postconditions:** Chunk files exist in the output directory, ready for downstream
RAG/embedding use.

---

## UC-004 — Export Converted Output

**Actor:** Business User
**Related:** FR-027–FR-028, US-021

**Preconditions:** One or more documents have been converted (and optionally chunked).

**Main Flow:**
1. User selects "Export."
2. User chooses export type: individual Markdown files, or a ZIP package.
3. If ZIP: system packages `markdown/`, `chunks/`, and `metadata/` into a single ZIP
   file at a user-chosen location.
4. System confirms export success and shows the output location.

**Exception Flow — Output Path Invalid/Unsafe:**
3a. Chosen output path fails validation (NFR-005, SEC-002) → system rejects the path
    with a clear message and prompts the user to choose another location.

**Postconditions:** Output files/ZIP exist at the confirmed export location.

---

## UC-005 — Configure Application Settings

**Actor:** Administrator (or Business User acting in an admin capacity)
**Related:** FR-032–FR-033, SEC-005, US-022

**Preconditions:** Application is running.

**Main Flow:**
1. User opens Settings.
2. User modifies output directory, chunk defaults, Python engine path, log directory,
   maximum parallelism, and/or tokenizer provider selection.
3. User saves settings.
4. System validates each setting (e.g., path exists/writable) and persists valid
   settings for future sessions.

**Exception Flow — Invalid Setting:**
3a. A provided path is invalid or unsafe → system rejects the change with a clear
    message and retains the previous valid value.

**Exception Flow — Invalid Python Engine Path (SEC-005):**
3b. The configured Python executable path does not resolve to a genuine, expected
    Python interpreter → system rejects the change with a clear error and retains the
    previous valid path.

**Postconditions:** Updated settings take effect for subsequent conversions and
persist across application restarts.

---

## UC-006 — Handle a Conversion Error and Retry

**Actor:** Business User
**Related:** FR-029–FR-031, US-019–US-020

**Preconditions:** A file has failed during conversion.

**Main Flow:**
1. System displays the failed file with a categorized, user-friendly error message.
2. User resolves the underlying issue if possible (e.g., closes the file if it was
   locked by another application).
3. User selects "Retry" for the failed file.
4. System re-attempts conversion for that file only, following UC-001 from step 5.

**Postconditions:** The file either converts successfully or fails again with an
updated error state; the rest of the batch/session is unaffected.

---

## UC-007 — Detect an Unavailable Processing Engine at Startup

**Actor:** Administrator / Business User
**Related:** FR-038, US-026

**Preconditions:** The application is launching.

**Main Flow:**
1. System checks that the configured document-processing engine is available and
   correctly configured.
2. Check succeeds → application proceeds to the normal Dashboard/main screen.

**Exception Flow — Engine Unavailable:**
2a. Check fails → system displays a single, clear setup message explaining the
    processing engine could not be initialized, before the user can attempt any
    import, rather than allowing every subsequent file to fail individually.

**Postconditions:** The user knows immediately whether the application is ready to
convert documents.

---

*Revision Note (2026-08-23): Added Exception Flow for partially extractable PDFs to
UC-001; added batch-size-ceiling and cancellation references to UC-002; added
Python-path validation Exception Flow to UC-005; added UC-007 (startup engine health
check) — all following the Requirements Quality Review.*

---

*Next document: `docs/06-ACCEPTANCE-CRITERIA.md`*
