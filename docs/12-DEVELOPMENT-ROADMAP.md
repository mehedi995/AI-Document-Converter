# 12 — Development Roadmap

**Project:** AI Document Converter
**Status:** Draft — Phase 12 (Development Roadmap) — Gate 3
**Date:** 2026-08-23

---

Twelve phases, per CLAUDE.md Section 28. Each phase lists its objective, the
requirements it satisfies (traceable to `docs/03-SRS.md`), its dependencies, and its
exit criteria. Phases are built in order — later phases assume earlier ones are done,
since e.g. chunking (Phase 7) needs Markdown generation (Phase 4) working first.

---

## Phase 1 — Project Foundation

**Objective:** Stand up the solution skeleton, cross-cutting infrastructure, and the
Python engine bootstrap — nothing user-facing beyond a running shell and a working
Settings screen.

**Covers:** NFR-007, NFR-012, FR-032, FR-033, FR-034, FR-035, FR-038, SEC-005,
ADR-001 (folder/build skeleton).

**Deliverables:**
- Solution and five projects per `docs/10-FOLDER-STRUCTURE.md`.
- DI composition root (`Microsoft.Extensions.DependencyInjection`) in `App.xaml.cs`.
- Serilog configured (file sink, rolling, log levels).
- Options-pattern configuration bound to a local `settings.json`.
- Settings screen (View + ViewModel) for all FR-032 fields, with SEC-005 Python-path
  validation wired in.
- Domain model classes scaffolded per `docs/09-DATA-MODEL.md`.
- Python project skeleton (`extractors/`, `tokenizer.py`, `dispatch.py`,
  `requirements.txt`) with a stub dispatch that echoes a request back as JSON.
- PyInstaller build script producing the bundled executable.
- `PythonEngineClient` (subprocess start, JSON I/O, timeout, cancellation) working
  end-to-end against the stub dispatch.
- Startup engine health check (FR-038) with a clear setup-error screen.
- A one-time dependency audit confirming no telemetry/analytics package is
  referenced (NFR-012).

**Depends on:** Gate 2 approval (this document).

**Exit criteria:** Solution builds; app launches to Settings; a settings round-trip
(save, restart, reload) works; the bundled Python executable starts, receives a JSON
request over stdin, and returns a JSON response over stdout; a log file is created on
startup.

---

## Phase 2 — File Import

**Objective:** Get files into the app, validated, without yet converting them.

**Covers:** FR-001, FR-002, FR-003, FR-004, FR-005, NFR-013.

**Deliverables:** Single/multiple file picker import; drag-and-drop; top-level folder
import; extension validation with per-file rejection messaging; batch size ceiling
enforcement at import time; the conversion list UI (name, type, size, status).

**Depends on:** Phase 1 (Settings screen provides the batch ceiling value).

**Exit criteria:** AC-001 through AC-005 and AC-031 pass.

---

## Phase 3 — Document Extraction

**Objective:** Turn each supported file into the normalized `DocumentModel`.

**Covers:** FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-039, FR-040, FR-041,
FR-042, FR-043, ADR-002.

**Deliverables:** `TextDocumentProcessor` (pure .NET, encoding detection);
`docx_extractor.py`/`DocxDocumentProcessor`; `pdf_extractor.py`/`PdfDocumentProcessor`
(including partially-extractable-page marking); `xlsx_extractor.py`/
`ExcelDocumentProcessor` (including large-sheet summarization and
formula/merged-cell handling); `pptx_extractor.py`/`PowerPointDocumentProcessor`;
shared image-placeholder handling across the four Python-backed processors; the
`IDocumentProcessor` strategy resolution in the Application layer.

**Depends on:** Phase 1 (Python engine plumbing), Phase 2 (files to extract from).

**Exit criteria:** AC-006 through AC-010, AC-027, AC-028, AC-029 pass against
`samples/`.

---

## Phase 4 — Markdown Conversion

**Objective:** Generate structured Markdown from the normalized model.

**Covers:** FR-012, FR-036, FR-044, BR-002, BR-004, BR-005 (partially — full display
lands in Phase 6).

**Deliverables:** `IMarkdownGenerator` (headings, lists, tables, links,
page/slide/sheet references); output filename collision handling; documented
re-conversion overwrite behavior.

**Depends on:** Phase 3 (needs a `DocumentModel` to render).

**Exit criteria:** AC-006 through AC-010 (structural preservation) and AC-032
(re-conversion overwrite) pass.

---

## Phase 5 — Metadata

**Objective:** Attach YAML front matter to every generated Markdown file.

**Covers:** FR-013.

**Deliverables:** Front-matter generation (`source`, `file_type`, `created_date`,
`converted_date`, `pages`, `author`), with `author` omitted (not blank) when
unavailable.

**Depends on:** Phase 4.

**Exit criteria:** AC-011 passes for every supported format.

---

## Phase 6 — Token Estimation

**Objective:** Show the user the value of the conversion.

**Covers:** FR-014, FR-015, FR-016, FR-017, ADR-003.

**Deliverables:** `tokenizer.py` (tiktoken `o200k_base` and `cl100k_base`);
`ITokenEstimator` client; Conversion Result UI displaying both estimates side by side
with the "estimate, not exact" disclaimer, tolerating a zero/negative reduction.

**Depends on:** Phase 4/5 (needs finished Markdown to estimate against), Phase 1
(Python engine).

**Exit criteria:** AC-012 and AC-013 pass.

---

## Phase 7 — Chunk Generation

**Objective:** Produce AI-ready chunk files.

**Covers:** FR-018, FR-019, FR-020, FR-021, BR-004.

**Deliverables:** `IChunkGenerator` operating on the structured `DocumentModel` (per
the architectural guidance in `docs/03-SRS.md` Section 10, not a post-hoc string
split); configurable size/overlap in tokens (default 512/50); numbered chunk files
with source/sequence metadata; Chunk Settings screen.

**Depends on:** Phase 6 (chunk sizing uses the same token estimator).

**Exit criteria:** AC-014 and AC-015 pass.

---

## Phase 8 — Batch Processing

**Objective:** Make the single-file pipeline (Phases 2–7) work reliably across many
files at once, without freezing the UI.

**Covers:** FR-022, FR-023, FR-024, FR-025, FR-037, FR-045, NFR-003, NFR-011.

**Deliverables:** `BatchService` with bounded parallelism; `IProgress`-based progress
reporting; Cancel command wired to per-file `CancellationToken`s; per-file
multi-stage status tracking (Converted/Chunked/Exported).

**Depends on:** Phases 2–7 (batches the single-file pipeline).

**Exit criteria:** AC-016, AC-017, AC-018, and AC-025 pass against a 100+ file batch.

---

## Phase 9 — Error Handling

**Objective:** Make failures graceful, categorized, and recoverable.

**Covers:** FR-029, FR-030, FR-031, SEC-004, SEC-006, BR-006.

**Deliverables:** Error-category mapping at every failure point in the pipeline;
retry command; no stack traces in the UI; injection-safe subprocess argument
handling; temp-file cleanup including startup orphan cleanup.

**Depends on:** Phases 3, 8 (most failure points live in extraction and batch
orchestration).

**Exit criteria:** AC-019 and AC-020 pass; a single failed file never stops a batch
(BR-006, already exercised in Phase 8's exit test).

---

## Phase 10 — Export

**Objective:** Get converted output out of the app.

**Covers:** FR-026, FR-027, FR-028, FR-046, SEC-002.

**Deliverables:** `ExportService` for individual Markdown files and for a single
batch-wide ZIP (per-document subfolders under `markdown/`, `chunks/`, `metadata/`);
export path validation.

**Depends on:** Phases 4–7 (needs converted/chunked output to export).

**Exit criteria:** AC-021 passes.

---

## Phase 11 — Testing

**Objective:** Verify the whole system against the SRS and the Acceptance Criteria.

**Covers:** all FR/NFR/SEC/BR via `docs/16-TEST-STRATEGY.md` and
`docs/17-UNIT-TEST-PLAN.md` (produced under Gate 4).

**Deliverables:** Full unit/integration test suite executed; performance benchmark
matrix (`docs/03-SRS.md` Section 8) measured and recorded; offline-compliance
verification via network monitoring; defects tracked and fixed in
`docs/22-BUG-TRACKER.md`.

**Depends on:** Phases 1–10 complete.

**Exit criteria:** Definition of Done (CLAUDE.md Section 40) met for every feature; no
known blocking defects.

---

## Phase 12 — Release

**Objective:** Ship v1.0.0.

**Covers:** `docs/19-DEPLOYMENT-PLAN.md` (produced under Gate 4).

**Deliverables:** Installer packaging (WPF app + bundled Python executable); Release
Checklist (CLAUDE.md Section 56) executed; `CHANGELOG.md` and version number
finalized.

**Depends on:** Phase 11 sign-off.

**Exit criteria:** Release Checklist fully checked off; sample documents convert
successfully on a clean machine with no manual Python setup.

---

*Next document: `docs/13-SPRINT-PLAN.md`*
