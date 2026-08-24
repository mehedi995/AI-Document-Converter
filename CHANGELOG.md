# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); versioning follows
[Semantic Versioning](https://semver.org/) (CLAUDE.md Section 55).

## [Unreleased]

### Added
- 2026-08-24 (Gate 5, Phase 1 — Project Foundation): solution skeleton
  (`AI.Document.Converter.sln`, five `src/` projects, two `tests/` projects) per
  `docs/10-FOLDER-STRUCTURE.md`; DI composition root, Serilog file logging,
  JSON-file-backed settings with an Options-pattern read side (`ISettingsStore`,
  `JsonSettingsStore`), SEC-002/SEC-005 path validators, the Domain model
  (`docs/09-DATA-MODEL.md`), the bundled Python engine skeleton (`dispatch.py`,
  PyInstaller build script) and `PythonEngineClient` (ADR-001), the FR-038 startup
  health check, and a working Settings screen. 22 unit/integration tests passing;
  verified end-to-end by running the built app (settings bootstrap, Python engine
  round trip, clean shutdown all confirmed via the log output).
- 2026-08-24 (Gate 5, Phase 2 — File Import): `ImportService` (single/multiple/
  drag-drop/folder import all reduce to one validated path list), extension
  validation (FR-005) via `SupportedFileTypeExtensions`, cumulative batch
  file-count/byte-size ceiling enforcement (NFR-013), a `DashboardView` with an
  Import Files/Import Folder UI, a drag-and-drop attached behavior, and a
  file-size display converter. `MainWindow` now hosts Convert and Settings as
  tabs. 40 tests passing (39 unit + 1 integration); verified by running the built
  app.
- 2026-08-24 (Gate 5, Phase 3 — Document Extraction): all five
  `IDocumentProcessor` implementations (`TextDocumentProcessor` pure .NET;
  `Pdf`/`Docx`/`Excel`/`PowerPoint` via a shared `PythonBackedDocumentProcessor`
  base class and the four new Python extractors) plus `DocumentProcessorResolver`
  (ADR-002). `ContentBlock` refactored to use `System.Text.Json`'s built-in
  polymorphic discriminator instead of a redundant `Type` property (would have
  collided with it on the wire). Generated non-confidential fixtures in
  `samples/` (`scripts/generate-samples.py`). 47 tests passing (39 unit + 8
  integration, the latter against the real bundled engine and real fixtures).

  Two real bugs found and fixed while wiring this up, not just written and
  assumed correct:
  - `pymupdf.find_tables()` prints an informational line straight to **stdout**,
    which would have corrupted every PDF extraction's JSON response in
    production; `dispatch.py` now isolates the JSON channel from any library's
    stray prints.
  - A PyInstaller `--onefile` build re-extracts its whole payload on every
    launch (~5s once Phase 3's libraries were bundled in) — unaffordable with
    one process per file. Switched to `--onedir` (~1.1s); see
    `docs/adr/ADR-001-python-integration.md` Addendum and `docs/18-RISK-ASSESSMENT.md`
    R-17.
- 2026-08-24 (Gate 5, Phase 4 — Markdown Conversion): `MarkdownGenerator`
  (headings, lists, tables with pipe/newline escaping, inline links,
  image-placeholder and unextractable-text rendering, page/slide reference
  lines), `FrontMatterBuilder` (FR-013, extended with `slides`/`sheets` fields
  alongside `pages` since the data model tracks them separately), and
  `OutputPathResolver` (FR-036 numeric-suffix collision handling, FR-044
  deterministic re-conversion path — both pure, no disk I/O; actual file
  writing is Phase 10). 79 tests passing (69 unit + 10 integration, the latter
  including a real Extract→Generate pipeline test against the bundled engine).
- 2026-08-24 (Gate 5, Phase 6 — Token Estimation): `tokenizer.py` (tiktoken
  `o200k_base`/`cl100k_base`, ADR-003), `ITokenEstimator`, and a new
  `ConversionService` (single-file UC-001 orchestration: extract → generate
  Markdown → estimate tokens → write file — sequential, no batching yet, that's
  Phase 8) wired into a real "Convert All" action on the Dashboard, showing both
  tokenizer estimates, the reduction percentage, and a persistent "these are
  estimates" disclaimer (FR-017/AC-013). 94 tests passing (82 unit + 12
  integration, including a full real-pipeline conversion test).

  Two more real, offline-breaking bugs found and fixed, both now permanent
  risk-register entries (R-18) so a routine `tiktoken` upgrade doesn't quietly
  reintroduce them:
  - `tiktoken` downloads its vocabulary files over HTTPS on first use by
    default — would fail/hang on a genuinely offline machine. Fixed by bundling
    pre-fetched vocab files (`tiktoken_cache/`) and pointing
    `TIKTOKEN_CACHE_DIR` at them before any `tiktoken` call.
  - PyInstaller's static analysis can't see `tiktoken`'s plugin-discovered
    encoding registration, causing `Unknown encoding o200k_base` at runtime.
    Fixed with an explicit `--hidden-import tiktoken_ext.openai_public`.
  Both verified by hiding the OS-level tiktoken cache entirely and confirming
  tokenization still succeeds against the bundled files alone.

Documentation progress ahead of the first release:
- Gate 1 (2026-08-23): Business Analysis & Requirements —
  `docs/01-BRD.md` through `docs/06-ACCEPTANCE-CRITERIA.md`, including a
  Requirements Quality Review and the English-only MVP scope decision.
- Gate 2 (2026-08-23): Architecture —
  `docs/07-TECHNICAL-ARCHITECTURE.md` through `docs/10-FOLDER-STRUCTURE.md`, plus
  `docs/adr/ADR-001` (Python integration), `ADR-002` (document processing
  architecture), `ADR-003` (token estimation strategy).
- Gate 3 (2026-08-23): Planning —
  `docs/12-DEVELOPMENT-ROADMAP.md`, `docs/13-SPRINT-PLAN.md`, `docs/14-TODO.md`.
- Gate 4 (2026-08-23): Implementation Readiness —
  `docs/11-CODING-STANDARDS.md`, `docs/15-IMPLEMENTATION-PLAN.md`,
  `docs/16-TEST-STRATEGY.md`, `docs/17-UNIT-TEST-PLAN.md`,
  `docs/18-RISK-ASSESSMENT.md`, `docs/19-DEPLOYMENT-PLAN.md`,
  `docs/20-FUTURE-ROADMAP.md`, `docs/21-REQUIREMENT-TRACEABILITY.md`,
  `docs/22-BUG-TRACKER.md` (template).

## [0.0.0] — Planned first tag

Reserved for the first buildable commit of the solution skeleton
(`docs/12-DEVELOPMENT-ROADMAP.md` Phase 1). Git was initialized 2026-08-24; no
version tag has been cut yet — see the `master` branch commit history for
per-phase commits in the meantime.

## [1.0.0] — Planned MVP release

Reserved for the first release satisfying all Gate 1–4 documentation and the
Release Checklist (CLAUDE.md Section 56). Not yet created.
