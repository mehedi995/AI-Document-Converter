# 03 — Software Requirements Specification (SRS)

**Project:** AI Document Converter
**Status:** Draft — Phase 3 (SRS)
**Date:** 2026-08-23

---

## 1. Functional Requirements

### File Import

| ID | Requirement |
|---|---|
| FR-001 | The system shall allow the user to select a single file for conversion via a file picker dialog. |
| FR-002 | The system shall allow the user to select multiple files at once via a file picker dialog. |
| FR-003 | The system shall allow the user to drag and drop one or more files onto the application window. |
| FR-004 | The system shall allow the user to select a folder; all supported files at the **top level** of that folder shall be added. Subfolders are not scanned in the MVP (recursive folder import is a Phase 2 candidate). |
| FR-005 | The system shall validate each selected file's extension against the supported list (PDF, DOCX, XLSX, PPTX, TXT) and reject unsupported files with a clear, non-technical message, without blocking processing of the remaining valid files. |
| FR-036 | If converting a file would overwrite a different source file's existing output in the configured output directory, the system shall avoid a silent collision by mirroring the relative source folder structure under the output directory, or by appending a numeric suffix to the output file name if the collision persists. |
| FR-037 | The system shall allow the user to cancel an in-progress batch conversion. No new files shall begin processing after cancellation; any file already in progress may complete or abort; a partial summary shall be shown reflecting files completed before cancellation. |
| FR-038 | The system shall verify, at application startup (or on first use), that the configured document-processing engine is available and correctly configured, and shall present a single clear setup message if it is not — rather than allowing every subsequent per-file conversion to fail individually. |

### Document Extraction

| ID | Requirement |
|---|---|
| FR-006 | The system shall extract text, tables, headings, and page references from PDF files. |
| FR-007 | The system shall extract headings, paragraphs, tables, lists, and hyperlinks from DOCX files. |
| FR-008 | The system shall extract workbook metadata, sheet names, and tabular data from XLSX files, converting each sheet into one or more Markdown tables. |
| FR-009 | The system shall extract slide titles, slide content, and speaker notes from PPTX files. |
| FR-010 | The system shall read TXT files as plain text. |
| FR-011 | The system shall map every extracted document into a common, normalized internal document model prior to Markdown generation (see `docs/09-DATA-MODEL.md`, to be produced in the Architecture phase). |
| FR-039 | The system shall not extract embedded images from PDF, DOCX, or PPTX files in the MVP. It shall insert a placeholder marker (e.g., `[image omitted]`) in the Markdown output at the image's location so document structure is not silently broken. |
| FR-040 | For TXT files, the system shall detect a byte-order mark where present, otherwise assume UTF-8 encoding. Content that cannot be decoded shall be treated as a corrupted-document error (see FR-029). |
| FR-041 | For XLSX files, a sheet exceeding a configurable row/column threshold shall be summarized (header row, total row count, and a sample of rows) rather than fully converted, with a note in the output indicating that summarization occurred. |
| FR-042 | For XLSX files, the system shall extract computed cell values, not formula text. A merged cell's value shall be repeated across each Markdown table cell spanned by the merge. |
| FR-043 | For PDF files, a page from which no extractable text is obtained (e.g., a scanned page) shall be marked with a visible placeholder note in the Markdown output rather than silently omitted; the file is still reported as an overall success. |

### Markdown Generation & Metadata

| ID | Requirement |
|---|---|
| FR-012 | The system shall generate structured Markdown output from the normalized document model, preserving headings, lists, tables, links, and page/slide/sheet references where present in the source. |
| FR-013 | The system shall prepend YAML front-matter metadata to every generated Markdown file, including at minimum: `source`, `file_type`, `created_date`, `converted_date`, `pages` (where applicable), and `author` (where available from source metadata). |

### Token Estimation

| ID | Requirement |
|---|---|
| FR-014 | The system shall estimate and display the original document's token count and the converted Markdown's token count. |
| FR-015 | The system shall display the token reduction amount and percentage. This value may be zero or negative for documents that were already minimally structured; the UI shall display it without assuming reduction is guaranteed. |
| FR-016 | The system shall estimate tokens **simultaneously** for at least two tokenizer profiles — a Claude-style estimate and a GPT-4o/Azure-OpenAI-style estimate (via `tiktoken`) — displayed side by side, using a documented tokenizer strategy (exact encodings to be finalized in `docs/07-TECHNICAL-ARCHITECTURE.md`). |
| FR-017 | The system shall clearly label all displayed token counts as **estimates** and shall not claim exact equivalence across AI providers. |

### Chunk Generation

| ID | Requirement |
|---|---|
| FR-018 | The system shall generate AI-ready Markdown chunk files from a converted document, with user-configurable chunk size and overlap, both measured in **tokens** (default size: 512 tokens; default overlap: 50 tokens), using the same estimator as FR-016. |
| FR-019 | The chunking algorithm shall avoid splitting a Markdown table across two chunks. |
| FR-020 | The chunking algorithm shall avoid separating a heading from its immediately following content where reasonably possible. |
| FR-021 | Each generated chunk file shall include metadata identifying its source document and chunk sequence number. |

### Batch Processing

| ID | Requirement |
|---|---|
| FR-022 | The system shall process multiple files in a single batch operation. |
| FR-023 | The system shall display, during batch processing: total files, processed count, success count, failure count, current file being processed, and overall progress percentage. |
| FR-024 | The system shall remain responsive (UI thread not blocked) throughout batch processing. |
| FR-025 | The system shall allow the user to control or the system to bound the degree of parallel processing (see NFR-003, configuration in FR-030). |
| FR-045 | The system shall track each file's pipeline status independently across stages (Converted / Chunked / Exported, as applicable to the requested operation). A file shall be counted as a batch "success" only if every pipeline stage requested for that run completed successfully. |

### Conversion Results & Export

| ID | Requirement |
|---|---|
| FR-026 | The system shall display a per-file and/or batch-level conversion result summary including original tokens, converted tokens, reduction percentage, and output location. |
| FR-027 | The system shall export converted output as individual Markdown files. |
| FR-028 | For a batch, the system shall export all converted output as a **single ZIP package for the whole batch**, containing per-document subfolders under `markdown/`, `chunks/`, and `metadata/` (exact layout finalized in System Design). |
| FR-044 | On re-conversion of the same source file to the same output location, the system shall overwrite the previously generated output. This is documented, expected behavior (see BR-002), not an error condition. |
| FR-046 | The ZIP package's `metadata/` folder (FR-028) shall contain one JSON file per successfully converted document (named after the source file), recording the conversion-result data that is otherwise shown only transiently in the UI: source file name, file type, created/converted dates, page/slide/sheet counts, author (when present), token estimates and reduction percentage, and chunk count (when chunking was requested). This is distinct from the front matter already embedded in the `markdown/`/`chunks/` files (FR-013), which a `metadata/` file does not duplicate beyond the fields above needed to make it self-contained. |

### Error Handling

| ID | Requirement |
|---|---|
| FR-029 | The system shall detect and categorize errors (unsupported file, file not found, file locked, permission denied, corrupted document, Python engine failure, conversion failure, output failure, unexpected exception) and present a user-friendly message for each, while logging technical detail separately. |
| FR-030 | The system shall offer a retry option for transient/recoverable errors (e.g., file temporarily locked). |
| FR-031 | The system shall not expose raw stack traces to the business user in the UI. |

### Configuration

| ID | Requirement |
|---|---|
| FR-032 | The system shall provide a Settings screen allowing configuration of: output directory, chunk size, chunk overlap, Python executable/engine path, log directory, maximum parallel processing, maximum batch size, and which tokenizer providers appear in the token-estimation summary (see FR-016). |
| FR-033 | The system shall persist user configuration between sessions. |

### Logging

| ID | Requirement |
|---|---|
| FR-034 | The system shall log, via Serilog, application startup/shutdown, file import, conversion start/success/failure, Python process failures, file read/write failures, batch status, retry attempts, and unexpected exceptions, at appropriate log levels (Information, Warning, Error, Debug). |
| FR-035 | The system shall never log full document content; log entries referencing documents shall use file names/paths and non-sensitive metadata only. |

## 2. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-001 | The system shall operate fully offline; it shall not make outbound network calls during normal document conversion operations. |
| NFR-002 | The system shall not transmit document content to any third-party or cloud AI API in the initial release. |
| NFR-003 | The system shall process a 100 MB document without freezing the UI, using asynchronous processing and progress reporting. |
| NFR-004 | The system's architecture shall allow future extension to OCR, additional file formats, additional AI providers, embedding generation, and vector database integration without redesigning the core pipeline. |
| NFR-005 | The system shall validate all file paths, prevent path traversal, and prevent writing output to unsafe or unintended locations. |
| NFR-006 | The system shall not log sensitive document content. |
| NFR-007 | The system shall follow SOLID principles, Clean/Layered Architecture, and MVVM, with clear separation of concerns between Presentation, Application, Domain, and Infrastructure layers. |
| NFR-008 | The system's UI shall be operable by a non-technical business user without prior training, using clear labels, workflow guidance, and meaningful error messages. |
| NFR-009 | The system shall handle corrupted, locked, or otherwise problematic files gracefully, without crashing the application or aborting an entire batch due to a single file failure. |
| NFR-010 | The system shall be deployable on an enterprise Windows environment with minimal manual dependency installation (exact strategy to be defined in `docs/19-DEPLOYMENT-PLAN.md`). |
| NFR-011 | The system shall use bounded, configurable parallelism for batch processing to avoid uncontrolled resource (CPU/memory) consumption. |
| NFR-012 | The system shall not include any telemetry, analytics, or crash-reporting library that transmits data externally. This shall be verified in code review and dependency audit, since such libraries can be pulled in unintentionally via a package default. |
| NFR-013 | The system shall enforce a configurable maximum batch size (default: 500 files or 5 GB total input size, whichever is reached first) and shall reject additional files beyond the limit with a clear message stating how many files/how much data were excluded. |

## 3. System Constraints

- Desktop-only, Windows 10/11, .NET 8 / WPF.
- Document extraction relies on a Python processing engine; integration mechanism to
  be selected and documented in `docs/07-TECHNICAL-ARCHITECTURE.md`.
- No database required for MVP (file system + local configuration only), pending
  confirmation in `docs/09-DATA-MODEL.md`.

## 4. Business Rules

| ID | Rule |
|---|---|
| BR-001 | A file with an unsupported extension shall never be sent to a document processor. |
| BR-002 | Output shall never silently overwrite the original **source** file. Output from a prior conversion of the same source file to the same output location IS expected to be overwritten on re-conversion (see FR-044) — this is documented behavior, not a defect. |
| BR-003 | Password-protected or encrypted documents are rejected in the MVP with a clear "not supported" message (not treated as a generic failure). |
| BR-007 | Documents are assumed to be in English for the MVP; non-English content is processed on a best-effort basis with no accuracy or tokenizer-fidelity guarantee. |
| BR-004 | A Markdown table shall never be split across two chunk files. |
| BR-005 | Token counts displayed to the user must always be accompanied by an indication that they are estimates, not exact provider counts. |
| BR-006 | A single failed file in a batch shall not stop processing of the remaining files in that batch. |

## 5. Input/Output Requirements

**Inputs:** PDF, DOCX, XLSX, PPTX, TXT files, selected individually, in multiples, via
drag-and-drop, or via folder selection.

**Outputs:** Markdown files (`.md`) with YAML front-matter; optional chunk files
(`chunk_001.md`, `chunk_002.md`, ...); optional ZIP package containing
`markdown/`, `chunks/`, and `metadata/` subfolders (exact layout finalized in System
Design).

## 6. Error Scenarios

See FR-029 categories. Each category must map to a distinct user-facing message
template and a distinct log event. Full mapping table to be included in
`docs/15-IMPLEMENTATION-PLAN.md`.

## 7. Security Requirements

| ID | Requirement |
|---|---|
| SEC-001 | No document content may be sent to any network endpoint. |
| SEC-002 | All file paths (input and output) must be validated before use; path traversal sequences must be rejected. |
| SEC-003 | No secrets or credentials shall be hard-coded in source code or configuration. |
| SEC-004 | Temporary files created during processing (e.g., intermediate Python I/O) must be stored in a controlled, non-public location and cleaned up after use, including cleanup of any orphaned temporary files remaining from a previous, abnormally terminated session, performed at next application startup. |
| SEC-005 | The system shall validate that the configured Python executable path resolves to a genuine, expected Python interpreter (exists, is executable, has expected modules available) before invoking it. An invalid configuration shall be rejected with a clear Settings-level error rather than being invoked. |
| SEC-006 | Inter-process communication between the .NET application and the document-processing engine shall not be vulnerable to argument or command injection via file names, paths, or document content. |

## 8. Performance Requirements

| ID | Requirement |
|---|---|
| PERF-001 | UI must remain interactive (able to accept input, e.g., a Cancel action) while processing any single file up to 100 MB. |
| PERF-002 | Batch processing of at least 100 typical office documents must complete without UI freeze, within the proposed benchmark matrix below. |

**Performance benchmark matrix — measured 2026-08-24 (Phase 11, TASK-061)**, via
`tests/AI.Document.Converter.IntegrationTests/Performance/PerformanceBenchmarkTests.cs`
against the real bundled Python engine on the reference dev machine. Every target
holds with a wide margin; original targets kept as-is (no adjustment needed):

| Profile | Target | Measured |
|---|---|---|
| Single file, 1 MB | ≤ 5 seconds | ~1.5 seconds |
| Single file, 10 MB | ≤ 20 seconds | ~4 seconds |
| Single file, 50 MB | ≤ 90 seconds | ~14–16 seconds |
| Single file, 100 MB | ≤ 3 minutes; UI never freezes | ~26–27 seconds (run in isolation — see note) |
| Batch of 10 files (avg 2 MB each) | ≤ 1 minute total | ~8 seconds |
| Batch of 100 files (avg 2 MB each) | ≤ 8 minutes total | ~76 seconds |

**Test-harness note on the 100 MB case:** run by itself, this case is completely
reliable at ~26–27 seconds. Run chained immediately after the other five
benchmark cases in the same test process (xUnit does not process-isolate
`[Theory]` cases), it can intermittently fail fast (~1 second) with an
`OutOfMemoryException` or a generic conversion failure — cumulative Large
Object Heap fragmentation from several large (50 MB+10 MB+1 MB+~200 MB of
batch-file content) sequential allocations in one process, confirmed by
direct investigation: the bundled engine, `tiktoken` cache integrity, and the
conversion pipeline itself were all individually verified correct, and an
explicit `GCSettings.LargeObjectHeapCompactionMode = CompactOnce` between
benchmark cases measurably reduced (but did not eliminate) the failure rate.
This is a characteristic of chaining six large-allocation benchmark cases in
one .NET process on constrained hardware, not a product defect a real user
would encounter converting one 100 MB file in a fresh application session —
see `docs/18-RISK-ASSESSMENT.md` R-20. Run the 100 MB case in isolation
(`dotnet test --filter FullyQualifiedName~ConvertAsync_SingleFileAtTargetSize`)
for a reliable measurement.

## 9. Open Questions Affecting Requirements — Status

Resolved by stakeholder decision (2026-08-23), following the Requirements Quality
Review:

- Documents are **English only** for MVP (BR-007).
- Folder import is **top-level only** for MVP (FR-004); recursive import deferred to
  Phase 2.
- Chunk size/overlap are measured in **tokens**, default 512/50 (FR-018).
- Multi-provider token estimation is shown **simultaneously**, not via a single
  selectable provider (FR-016, FR-032).

Still open, to be resolved during Phase 07 (Technical Architecture), not blocking
further Phase 1 documentation:

- NFR-010: Python distribution strategy (embedded vs. externally installed).
- FR-016: exact tokenizer encodings (e.g., `o200k_base` vs. `cl100k_base`) and the
  proxy method used for the Claude-style estimate.
- Whether the normalized document model (FR-011, `docs/09-DATA-MODEL.md`) should
  reserve an explicit extensibility point now for future OCR-derived page content, to
  avoid a breaking change when OCR is added in Phase 2.

## 10. Architectural Guidance Carried Forward (non-binding, for Phase 07/08)

- Chunking (FR-019/020) must operate on the **structured document model**, not a
  post-hoc string split of the finished Markdown file — otherwise table/heading-safe
  chunking cannot be reliably guaranteed.
- Requirement wording that references a "Python engine" (e.g., FR-029, FR-034,
  FR-038) is provisional terminology; it must be reinterpreted for whichever
  integration mechanism Phase 07 selects (subprocess, embedded, or other).

## 11. Revision Note (2026-08-23)

Following a Requirements Quality Review, this document was updated with 10 new
functional requirements (FR-036–FR-045), 2 new NFRs (NFR-012, NFR-013), 2 new security
requirements (SEC-005, SEC-006), 1 new business rule (BR-007), an explicit performance
benchmark matrix, and clarifying edits to FR-004, FR-014–FR-018, FR-028, FR-032,
SEC-004, and BR-002. See `docs/22-BUG-TRACKER.md` (future) / this note for traceability
until `docs/21-REQUIREMENT-TRACEABILITY.md` is produced.

## 12. Revision Note (2026-08-24)

`docs/21-REQUIREMENT-TRACEABILITY.md` referenced FR-046 (→ US-021 → AC-021 →
TASK-057) from the moment it was written, but the requirement itself was never
actually added to this document - discovered as a genuine documentation gap
while starting Phase 10 (Export). Added above under Section "Conversion
Results & Export," resolving what the `metadata/` folder in FR-028's ZIP
layout is supposed to contain (a per-document JSON of conversion-result data,
confirmed with the user rather than assumed, since it shapes a concrete
exported file format).

---

*Next document: `docs/04-USER-STORIES.md`*
