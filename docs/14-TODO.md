# 14 — TODO / Task Tracker

**Project:** AI Document Converter
**Status:** In Progress — Gate 5 (Production Coding) underway
**Date:** 2026-08-24 (Phase 1 and Phase 2 completed)

All tasks start as **TODO**. Status values: `TODO`, `IN PROGRESS`, `BLOCKED`, `DONE`.
Priority: High / Medium / Low. Phase references `docs/12-DEVELOPMENT-ROADMAP.md`.

---

## Phase 1 — Project Foundation

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-001 | Initialize solution and five projects per `docs/10-FOLDER-STRUCTURE.md` | High | none | DONE |
| TASK-002 | Configure DI container in `App.xaml.cs` (`Microsoft.Extensions.DependencyInjection`) | High | TASK-001 | DONE |
| TASK-003 | Configure Serilog file sink and log levels (FR-034/035) | High | TASK-001 | DONE |
| TASK-004 | Implement Options-pattern configuration bound to `settings.json` (FR-032/033) | High | TASK-001 | DONE |
| TASK-005 | Implement SEC-005 Python-path validator | High | TASK-004 | DONE |
| TASK-006 | Implement Settings screen (View + ViewModel) for all FR-032 fields | Medium | TASK-004, TASK-005 | DONE |
| TASK-007 | Scaffold Domain model classes per `docs/09-DATA-MODEL.md` | High | TASK-001 | DONE |
| TASK-008 | Create Python project skeleton (`extractors/`, `tokenizer.py`, `dispatch.py`, `requirements.txt`) | High | TASK-001 | DONE |
| TASK-009 | Implement PyInstaller build script (`scripts/build-python-engine.ps1`) | High | TASK-008 | DONE |
| TASK-010 | Implement `PythonEngineClient` (subprocess, JSON I/O, timeout, cancellation) per ADR-001 | High | TASK-009 | DONE |
| TASK-011 | Implement FR-038 startup engine health check + setup-error screen | High | TASK-010 | DONE |
| TASK-012 | Dependency audit confirming no telemetry/analytics package is referenced (NFR-012) | Medium | TASK-001 | DONE |
| TASK-013 | Unit tests: DI resolution, settings round-trip, Python-path validator | High | TASK-005, TASK-006 | DONE |

## Phase 2 — File Import

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-014 | Implement single-file import via file picker (FR-001) | High | TASK-002 | DONE |
| TASK-015 | Implement multi-file import (FR-002) | High | TASK-014 | DONE |
| TASK-016 | Implement drag-and-drop import (FR-003) | Medium | TASK-014 | DONE |
| TASK-017 | Implement top-level folder import (FR-004) | Medium | TASK-014 | DONE |
| TASK-018 | Implement extension validation and per-file rejection messaging (FR-005) | High | TASK-014 | DONE |
| TASK-019 | Implement batch size ceiling enforcement at import time (NFR-013) | Medium | TASK-006 | DONE |
| TASK-020 | Build conversion list UI (name/type/size/status) | High | TASK-014 | DONE |
| TASK-021 | Unit/UI tests for import validation and ceiling enforcement | High | TASK-018, TASK-019 | DONE |

## Phase 3 — Document Extraction

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-022 | Implement `TextDocumentProcessor` with encoding detection (FR-010, FR-040) | Medium | TASK-020 | TODO |
| TASK-023 | Implement `docx_extractor.py` + `DocxDocumentProcessor` (FR-007) | High | TASK-010, TASK-020 | TODO |
| TASK-024 | Implement `pdf_extractor.py` + `PdfDocumentProcessor` incl. partial-page marking (FR-006, FR-043) | High | TASK-010, TASK-020 | TODO |
| TASK-025 | Implement `xlsx_extractor.py` + `ExcelDocumentProcessor` incl. row-cap summarization, formulas/merged cells (FR-008, FR-041, FR-042) | High | TASK-010, TASK-020 | TODO |
| TASK-026 | Implement `pptx_extractor.py` + `PowerPointDocumentProcessor` (FR-009) | Medium | TASK-010, TASK-020 | TODO |
| TASK-027 | Implement shared image-placeholder handling across the four Python-backed processors (FR-039) | Medium | TASK-023, TASK-024, TASK-026 | TODO |
| TASK-028 | Implement `IDocumentProcessor` strategy resolution in the Application layer (ADR-002) | High | TASK-022–TASK-026 | TODO |
| TASK-029 | Integration tests: each processor against `samples/` | High | TASK-028 | TODO |

## Phase 4 — Markdown Conversion

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-030 | Implement `IMarkdownGenerator` (headings/lists/tables/links/source-location) (FR-012) | High | TASK-029 | TODO |
| TASK-031 | Implement YAML front-matter generation, omitting absent `author` (FR-013) | High | TASK-030 | TODO |
| TASK-032 | Implement output filename collision handling (FR-036) | Medium | TASK-030 | TODO |
| TASK-033 | Implement re-conversion overwrite behavior (FR-044, BR-002) | Medium | TASK-030 | TODO |
| TASK-034 | Unit tests for Markdown generator against `DocumentModel` fixtures | High | TASK-030–TASK-033 | TODO |

## Phase 6 — Token Estimation

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-035 | Implement `tokenizer.py` using tiktoken `o200k_base`/`cl100k_base` (ADR-003) | High | TASK-010 | TODO |
| TASK-036 | Implement `ITokenEstimator` client calling the Python tokenizer (FR-014, FR-016) | High | TASK-035 | TODO |
| TASK-037 | Implement Conversion Result UI: both estimates, reduction %, estimate disclaimer (FR-015, FR-017) | High | TASK-036, TASK-034 | TODO |
| TASK-038 | Unit tests for `ITokenEstimator` (mocked) and disclaimer rendering | Medium | TASK-037 | TODO |

## Phase 7 — Chunk Generation

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-039 | Implement `IChunkGenerator` operating on the structured `DocumentModel` (FR-019, FR-020) | High | TASK-037 | TODO |
| TASK-040 | Implement configurable chunk size/overlap in tokens, default 512/50 (FR-018) | High | TASK-039 | TODO |
| TASK-041 | Implement chunk file writer with sequence + source metadata (FR-021) | Medium | TASK-040 | TODO |
| TASK-042 | Build Chunk Settings screen | Medium | TASK-040 | TODO |
| TASK-043 | Unit tests: table-safe chunking, overlap correctness, minimum-chunk-size edge case | High | TASK-039–TASK-041 | TODO |

## Phase 8 — Batch Processing

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-044 | Implement `BatchService` with bounded parallelism (`SemaphoreSlim`) (FR-022, NFR-011) | High | TASK-028, TASK-030, TASK-039 | TODO |
| TASK-045 | Implement `IProgress`-based progress reporting (FR-023) | High | TASK-044 | TODO |
| TASK-046 | Implement Cancel command wired to per-file `CancellationToken`s (FR-037) | High | TASK-044 | TODO |
| TASK-047 | Implement per-file multi-stage status tracking (Converted/Chunked/Exported) (FR-045) | Medium | TASK-044 | TODO |
| TASK-048 | Verify UI responsiveness under batch load (FR-024, NFR-003) | High | TASK-045 | TODO |
| TASK-049 | Integration tests: 100+ file batch, cancellation mid-batch | High | TASK-044–TASK-047 | TODO |

## Phase 9 — Error Handling

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-050 | Implement FR-029 error category mapping at every pipeline failure point | High | TASK-049 | TODO |
| TASK-051 | Implement retry command for failed files (FR-030) | High | TASK-050 | TODO |
| TASK-052 | Ensure no stack traces surface in the UI (FR-031) | High | TASK-050 | TODO |
| TASK-053 | Implement SEC-006 injection-safe subprocess argument handling | High | TASK-010 | TODO |
| TASK-054 | Implement SEC-004 temp-file cleanup incl. startup orphan cleanup | Medium | TASK-010 | TODO |
| TASK-055 | Unit/integration tests simulating each error category and the retry flow | High | TASK-050–TASK-052 | TODO |

## Phase 10 — Export

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-056 | Implement `ExportService`: individual Markdown file export (FR-027) | High | TASK-041 | TODO |
| TASK-057 | Implement ZIP export, one ZIP per batch, per-document subfolders (FR-028, FR-046) | High | TASK-056 | TODO |
| TASK-058 | Implement export path validation (SEC-002) | High | TASK-056 | TODO |
| TASK-059 | Unit/integration tests for export incl. path validation rejection | High | TASK-057, TASK-058 | TODO |

## Phase 11 — Testing

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-060 | Execute full unit test suite per `docs/17-UNIT-TEST-PLAN.md` (Gate 4) | High | all prior test tasks | TODO |
| TASK-061 | Execute performance benchmark matrix (`docs/03-SRS.md` Section 8) and record results | High | TASK-049 | TODO |
| TASK-062 | Execute offline-compliance verification via network monitoring (US-023, AC-023) | High | TASK-059 | TODO |
| TASK-063 | Triage and fix defects logged in `docs/22-BUG-TRACKER.md` (Gate 4) | High | TASK-060–TASK-062 | TODO |

## Phase 12 — Release

| Task ID | Description | Priority | Dependency | Status |
|---|---|---|---|---|
| TASK-064 | Finalize `docs/19-DEPLOYMENT-PLAN.md` and installer packaging | High | TASK-063 | TODO |
| TASK-065 | Run full Release Checklist (CLAUDE.md Section 56) | High | TASK-064 | TODO |
| TASK-066 | Update `CHANGELOG.md` and set version to 1.0.0 | Medium | TASK-065 | TODO |
| TASK-067 | Tag release build | Medium | TASK-066 | TODO |

---

*Gates 1–4 approved 2026-08-23; Gate 5 (Production Coding) approved 2026-08-24.
Phase 1 (TASK-001–TASK-013) and Phase 2 (TASK-014–TASK-021) are complete and
verified — see `CHANGELOG.md`. Phase 3 (Document Extraction) is next.*
