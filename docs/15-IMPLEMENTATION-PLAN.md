# 15 — Implementation Plan

**Project:** AI Document Converter
**Status:** Draft — Phase 15 (Implementation Plan) — Gate 4
**Date:** 2026-08-23

---

Organized by feature group, matching `docs/12-DEVELOPMENT-ROADMAP.md` Phases 1–10.
Each feature lists: what will be built, which project owns it, required classes,
required interfaces, dependencies, testing approach, and acceptance criteria
(CLAUDE.md Section 31).

---

## 1. Application Foundation (Roadmap Phase 1)

**What will be built:** Solution/project skeleton, DI composition, logging,
configuration, the Domain model, and the Python engine bootstrap.

| | |
|---|---|
| **Owning project(s)** | All five projects (skeleton); `Wpf` (composition root, Settings screen); `Infrastructure` (config, logging, Python client); `Domain` (models); `AI.Document.Converter.Python` (engine skeleton) |
| **Classes** | `App` (composition root), `SettingsOptions`, `PythonEngineOptions`, `LoggingOptions`, `SettingsService`, `PythonPathValidator`, `PythonEngineClient`, `DocumentModel`/`Section`/`ContentBlock` hierarchy, `SettingsViewModel`, `SettingsView` |
| **Interfaces** | `ISettingsStore`, `IPythonEngineClient` |
| **Dependencies** | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Serilog` (+ file sink) |
| **Testing approach** | Unit tests: settings round-trip (save/load), `PythonPathValidator` against valid/invalid/malicious paths (SEC-005). Manual: subprocess round-trip smoke test against the stub Python dispatch. |
| **Acceptance criteria** | Phase 1 exit criteria (`docs/12-DEVELOPMENT-ROADMAP.md`); no formal AC ID (infrastructure, not user-facing behavior) except AC-030 (Python path rejection), which is implemented here even though verified fully once Settings UI exists. |

## 2. File Import (Roadmap Phase 2)

**What will be built:** Single/multi/drag-drop/folder import with validation and a
batch ceiling.

| | |
|---|---|
| **Owning project(s)** | `Wpf` (Views/ViewModels/Commands), `Application` (import orchestration) |
| **Classes** | `ImportService`, `FileImportItem` (DTO), `DashboardViewModel`, `ImportCommand`, `DragDropBehavior` (WPF attached behavior) |
| **Interfaces** | `IImportService` |
| **Dependencies** | Foundation (Phase 1) for Settings (batch ceiling value) |
| **Testing approach** | Unit tests for extension validation and ceiling enforcement against synthetic file lists (no real I/O needed for these). Manual test for drag-and-drop (WPF drag/drop is impractical to unit test). |
| **Acceptance criteria** | AC-001, AC-002, AC-003, AC-004, AC-005, AC-031 |

## 3. Document Extraction (Roadmap Phase 3)

**What will be built:** Five `IDocumentProcessor` implementations, the Python-side
extractors, and the strategy resolution that picks the right processor per file.

| | |
|---|---|
| **Owning project(s)** | `Infrastructure/DocumentProcessing/*`, `AI.Document.Converter.Python/extractors/*`, `Application` (resolution) |
| **Classes** | `TextDocumentProcessor`, `DocxDocumentProcessor`, `PdfDocumentProcessor`, `ExcelDocumentProcessor`, `PowerPointDocumentProcessor`, `DocumentProcessorResolver`; Python: `docx_extractor.py`, `pdf_extractor.py`, `xlsx_extractor.py`, `pptx_extractor.py`, `dispatch.py` |
| **Interfaces** | `IDocumentProcessor` (ADR-002) |
| **Dependencies** | Phase 1 (`IPythonEngineClient`), `pdfplumber`, `python-docx`, `openpyxl`, `python-pptx` |
| **Testing approach** | Integration tests per processor against `samples/*` (real Python engine, real files) — the one area where genuine integration tests (not mocks) are required, since the extraction logic's correctness *is* the Python library's behavior. |
| **Acceptance criteria** | AC-006, AC-007, AC-008, AC-009, AC-010, AC-027, AC-028, AC-029 |

## 4. Markdown Conversion (Roadmap Phase 4)

**What will be built:** `DocumentModel` → Markdown text, with collision-safe output
naming.

| | |
|---|---|
| **Owning project(s)** | `Application`/`Domain` (`IMarkdownGenerator` is pure logic, no I/O) |
| **Classes** | `MarkdownGenerator`, `OutputPathResolver` (collision handling, FR-036) |
| **Interfaces** | `IMarkdownGenerator` |
| **Dependencies** | Phase 3 (`DocumentModel` input) |
| **Testing approach** | Unit tests against hand-built `DocumentModel` fixtures — no Python or file I/O needed, since this layer is pure transformation. |
| **Acceptance criteria** | AC-006–AC-011 (structural preservation), AC-032 (re-conversion overwrite) |

## 5. Metadata (Roadmap Phase 5)

**What will be built:** YAML front-matter block prepended to every Markdown output.

| | |
|---|---|
| **Owning project(s)** | `Application`/`Domain` |
| **Classes** | `FrontMatterBuilder` (used by `MarkdownGenerator`) |
| **Interfaces** | none new — a collaborator of `IMarkdownGenerator` |
| **Dependencies** | Phase 4 |
| **Testing approach** | Unit tests asserting exact front-matter field presence/absence per format, including the omit-if-null `author` rule. |
| **Acceptance criteria** | AC-011 |

## 6. Token Estimation (Roadmap Phase 6)

**What will be built:** Dual tokenizer-style estimation and its UI display.

| | |
|---|---|
| **Owning project(s)** | `Infrastructure` (`TokenEstimator`, Python `tokenizer.py`), `Wpf` (Conversion Result screen) |
| **Classes** | `TokenEstimator`, Python `tokenizer.py`; `ConversionResultViewModel` |
| **Interfaces** | `ITokenEstimator` |
| **Dependencies** | Phase 1 (`IPythonEngineClient`), `tiktoken`; ADR-003 |
| **Testing approach** | Unit tests for `TokenEstimator` with a mocked `IPythonEngineClient` response; a manual UI check that both estimates and the disclaimer render. |
| **Acceptance criteria** | AC-012, AC-013 |

## 7. Chunk Generation (Roadmap Phase 7)

**What will be built:** Table/heading-safe chunking of a converted document.

| | |
|---|---|
| **Owning project(s)** | `Application`/`Domain` (pure logic, operates on `DocumentModel`, not the rendered Markdown string — per the architectural guidance in `docs/03-SRS.md` Section 10), `Wpf` (Chunk Settings screen) |
| **Classes** | `ChunkGenerator`, `ChunkWriter`; `ChunkSettingsViewModel` |
| **Interfaces** | `IChunkGenerator` |
| **Dependencies** | Phase 6 (`ITokenEstimator`, since chunk sizing is token-based) |
| **Testing approach** | Unit tests: a table larger than the configured chunk size stays intact in one chunk; a heading is not separated from its immediately following content where avoidable; overlap produces no content loss (corrected AC-014 semantics). |
| **Acceptance criteria** | AC-014, AC-015 |

## 8. Batch Processing (Roadmap Phase 8)

**What will be built:** Orchestration of the single-file pipeline (Phases 2–7) across
many files, with bounded parallelism, progress, and cancellation.

| | |
|---|---|
| **Owning project(s)** | `Application` (`BatchService`), `Wpf` (progress UI, Cancel command) |
| **Classes** | `BatchService`, `BatchProgressDto`, `BatchItemStatus` (per-file, `[Flags] PipelineStage`) |
| **Interfaces** | `IBatchService` |
| **Dependencies** | Phases 2–7 (batches the full pipeline) |
| **Testing approach** | Integration test running 100+ sample-derived files through the real pipeline with parallelism capped low (e.g., 2) to keep test runtime reasonable; a dedicated cancellation test asserting no new files start after Cancel. |
| **Acceptance criteria** | AC-016, AC-017, AC-018, AC-025 |

## 9. Error Handling (Roadmap Phase 9)

**What will be built:** Categorized, safe, recoverable failure handling threaded
through every pipeline stage.

| | |
|---|---|
| **Owning project(s)** | `Domain` (`ErrorCategory`, `DocumentConversionException`), `Infrastructure` (mapping at each failure point), `Wpf` (error display, retry command) |
| **Classes** | `DocumentConversionException`, `RetryCommand`; updates to `PythonEngineClient` (SEC-006 argument handling), `TempFileCleanupService` (SEC-004) |
| **Interfaces** | none new — cross-cutting |
| **Dependencies** | Phase 8 (most failure surface area is exercised there) |
| **Testing approach** | Unit/integration tests simulating each FR-029 category: a locked file (open a `FileStream` with an exclusive lock in the test, then attempt conversion), a corrupted file (truncated sample), a missing file, an artificially slow Python response (timeout path). |
| **Acceptance criteria** | AC-019, AC-020; BR-006 re-verified under real error conditions |

## 10. Export (Roadmap Phase 10)

**What will be built:** Individual Markdown export and single-ZIP-per-batch export.

| | |
|---|---|
| **Owning project(s)** | `Application` (`ExportService`), `Wpf` (export UI) |
| **Classes** | `ExportService`, `ZipPackageBuilder` |
| **Interfaces** | `IExportService` |
| **Dependencies** | Phases 4–7 (needs Markdown/chunks to export); `System.IO.Compression` |
| **Testing approach** | Unit test for export-path validation rejection; integration test producing a real ZIP and asserting its internal folder layout matches `docs/10-FOLDER-STRUCTURE.md`/FR-046. |
| **Acceptance criteria** | AC-021 |

---

## 11. Cross-Feature Note: Settings & Logging Hardening (Sprint 10)

Not a new feature — a verification pass confirming AC-022 (settings persist across
restart), AC-023 (no outbound network calls across a full cycle), and AC-024 (logs
contain no document content), using the features built in Phases 1–10. No new
classes are anticipated; if the verification pass finds a gap, it is logged in
`docs/22-BUG-TRACKER.md` and fixed as a normal defect.

**Verified 2026-08-24 (Phase 11).**
- **AC-022**: found a real gap — every existing settings test exercised either
  the write side or a read side backed by a mocked/static `IOptionsMonitor`;
  none proved a value survives an actual restart. Added
  `JsonSettingsStoreTests.SaveAsync_ThenFreshOptionsMonitorOverSameFile_
  SeesTheSavedValue_SimulatingRestart`, which builds the exact same
  `AddJsonFile` + `Configure<AppSettings>` + `IOptionsMonitor` pipeline
  `InfrastructureServiceCollectionExtensions` wires up in the real app, as a
  brand-new DI container reading the file a prior "session" wrote to.
- **AC-023**: see `docs/16-TEST-STRATEGY.md` Section 5 for the full verification
  (static code audit + a live network-connection check during a real five-format
  Import→Convert→Chunk→Export run).
- **AC-024**: every `_logger.Log*`/`Log.*` call site in `src/` was audited by
  hand - each one logs only a fixed message, a file name/path, a category, or
  an exception's own message, never extracted document text (paragraph/cell/
  slide content is never passed to a logging call anywhere). The one call
  that echoes a Python subprocess's raw stderr
  (`PythonEngineClient.SendAsync`) is `LogDebug`-level, and
  `SerilogConfigurator` sets `MinimumLevel.Information()`, so it is filtered
  out of the log file entirely by default - it would only ever carry a
  library's own diagnostic text in any case (the observed instance was
  pymupdf's `pymupdf_layout` notice, before the 2026-09 move to pdfplumber;
  the protection stays because any parsing library may write diagnostics),
  never document content, since `dispatch.py` isolates stdout but never
  routes extracted content to stderr.

---

*Next document: `docs/16-TEST-STRATEGY.md`*
