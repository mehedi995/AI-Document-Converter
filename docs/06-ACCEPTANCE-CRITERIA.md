# 06 — Acceptance Criteria

**Project:** AI Document Converter
**Status:** Draft — Phase 6
**Date:** 2026-08-23

Each criterion is written in Given/When/Then form and is testable. IDs map to the
User Stories in `docs/04-USER-STORIES.md`.

---

**AC-001** (US-001)
Given the user selects "Import File" and chooses one supported file,
When the file picker closes,
Then the file appears in the conversion list with correct name, type, and size.

**AC-002** (US-002)
Given the user selects multiple supported files in the file picker,
When the dialog closes,
Then all selected files appear in the conversion list.

**AC-003** (US-003)
Given the user drags one or more supported files from Windows Explorer onto the
application window,
When the files are dropped,
Then the files are added to the conversion list as if imported via the file picker.

**AC-004** (US-004)
Given the user selects a folder containing supported and unsupported files,
When the folder is imported,
Then all supported files in the folder are added to the conversion list and
unsupported files are excluded with a visible notice of how many were skipped.

**AC-005** (US-005)
Given the user attempts to import a file with an unsupported extension,
When the import is processed,
Then the system displays a clear message identifying the file and stating the file
type is not supported, and the file is not added to the conversion list.

**AC-006** (US-006)
Given a valid, non-corrupted, non-password-protected PDF,
When the user starts conversion,
Then the system generates a Markdown file preserving headings, tables, and page
references present in the source, and the operation is reported as successful.

**AC-007** (US-007)
Given a valid DOCX file containing headings, paragraphs, a table, a list, and a
hyperlink,
When the user starts conversion,
Then the generated Markdown preserves each of those five elements in a recognizable
form.

**AC-008** (US-008)
Given a valid XLSX file with multiple sheets containing tabular data,
When the user starts conversion,
Then each sheet is represented as a distinct Markdown table (or clearly delimited
section) in the output.

**AC-009** (US-009)
Given a valid PPTX file with slide titles, body content, and speaker notes,
When the user starts conversion,
Then the generated Markdown includes each slide's title, content, and notes in a
clearly delimited, slide-referenced structure.

**AC-010** (US-010)
Given a valid TXT file,
When the user starts conversion,
Then the system produces a Markdown file containing the original text and front-matter
metadata, without data loss.

**AC-011** (US-011)
Given any successfully converted document,
When the Markdown output is generated,
Then the output begins with a YAML front-matter block containing at minimum `source`,
`file_type`, `created_date`, `converted_date`, and `pages` (where applicable).

**AC-012** (US-012)
Given a successfully converted document,
When the conversion completes,
Then the UI displays the original token count, converted token count, and the
percentage reduction.

**AC-013** (US-013)
Given any screen displaying a token count,
When the user views it,
Then a visible label or tooltip states the count is an estimate and may vary by AI
provider.

**AC-014** (US-014)
Given a converted Markdown document and a configured chunk size/overlap,
When the user generates chunks,
Then the system produces sequentially numbered chunk files such that, accounting for
the configured overlap between adjacent chunks, no content from the source document is
lost (the non-overlapping portions of all chunks, concatenated in order, reproduce the
source content).

**AC-015** (US-015)
Given a converted document containing a Markdown table larger than the configured
chunk size,
When chunks are generated,
Then the table appears intact within a single chunk, even if that chunk exceeds the
configured size.

**AC-016** (US-016)
Given a batch of at least 100 supported files,
When the user starts batch conversion,
Then every file is processed (successfully or with a categorized failure) and a final
summary accounts for all files.

**AC-017** (US-017)
Given a batch conversion in progress,
When the user views the main screen,
Then total files, processed count, success count, failure count, current file, and
progress percentage are visible and update in near real time.

**AC-018** (US-018)
Given a batch conversion or a single 100 MB file is being processed,
When the user interacts with the UI (e.g., clicks Cancel or navigates a menu),
Then the UI responds without freezing or becoming unresponsive.

**AC-019** (US-019)
Given a file fails to convert,
When the failure is reported to the user,
Then a plain-language message describing the error category is shown, without a raw
stack trace, alongside a technical log entry.

**AC-020** (US-020)
Given a previously failed file whose underlying issue has been resolved,
When the user selects "Retry" for that file,
Then the system reattempts conversion for only that file and updates its status
accordingly.

**AC-021** (US-021)
Given one or more converted documents (with or without chunks),
When the user exports as a ZIP package,
Then a single ZIP file is created containing the expected `markdown/`, `chunks/`, and
`metadata/` contents.

**AC-022** (US-022)
Given the user changes a setting (e.g., output directory) to a valid value and saves,
When the application is restarted,
Then the previously saved setting is still in effect.

**AC-023** (US-023)
Given the application is performing any supported operation (import, conversion,
chunking, export),
When network traffic is monitored during that operation,
Then no outbound network call containing document content is observed.

**AC-024** (US-024)
Given conversion activity and at least one error has occurred,
When the user or administrator reviews the log output,
Then log entries exist for the relevant lifecycle events and errors, and none contain
raw document content.

**AC-025** (US-025)
Given a batch conversion in progress,
When the user selects "Cancel,"
Then no new files begin processing, any in-flight file completes or aborts cleanly,
and a partial summary is shown reflecting the files completed before cancellation.

**AC-026** (US-026)
Given the configured document-processing engine is missing or misconfigured,
When the application starts,
Then a single clear setup message is shown before any file import is attempted,
rather than the failure surfacing separately for every imported file.

**AC-027** (FR-039)
Given a DOCX, PDF, or PPTX file containing an embedded image,
When the document is converted,
Then the Markdown output contains a placeholder marker at the image's location instead
of the image itself.

**AC-028** (FR-041)
Given an XLSX sheet exceeding the configured row/column threshold,
When the document is converted,
Then the output contains a summarized representation (header row, total row count, and
a sample of rows) with a note indicating summarization occurred, instead of the full
sheet.

**AC-029** (FR-043)
Given a PDF containing at least one page with no extractable text,
When the document is converted,
Then each such page is marked with a visible placeholder note in the Markdown output,
and the file is still reported as an overall success.

**AC-030** (SEC-005)
Given the user sets the Python executable path in Settings to a file that is not a
valid Python interpreter,
When the user saves the setting,
Then the system rejects the change with a clear error and retains the previous valid
path.

**AC-031** (NFR-013)
Given a folder or multi-file selection exceeding the configured batch size ceiling,
When the user attempts to import it,
Then the system accepts files up to the ceiling and clearly reports how many
files/how much data were excluded.

**AC-032** (FR-044 / BR-002)
Given a file that was previously converted to a given output location,
When the user converts the same file to the same output location again,
Then the new output replaces the previous output without a warning, consistent with
the documented re-conversion behavior.

**AC-033** (FR-036)
Given a batch import where two different source files (e.g., from different
subfolders) would otherwise produce the same output Markdown file name,
When both files are converted,
Then the system avoids the collision by mirroring the relative source folder
structure under the output directory, or by appending a numeric suffix, so that
neither file's output silently overwrites the other's.

---

*Revision Note (2026-08-23): Added AC-025 through AC-032 and corrected AC-014's
reconstruction wording, following the Requirements Quality Review. Added AC-033
(FR-036 output collision handling) during the Requirement Traceability pass —
FR-036 had no owning acceptance criterion until this addition.*

---

*This completes the Phase 1–6 Business Analysis and Requirements documentation set.
Architecture and design phases (07+) require explicit approval before proceeding.*
