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
(`docs/12-DEVELOPMENT-ROADMAP.md` Phase 1). The code now exists (see Unreleased
above); no git repository has been initialized or tagged yet.

## [1.0.0] — Planned MVP release

Reserved for the first release satisfying all Gate 1–4 documentation and the
Release Checklist (CLAUDE.md Section 56). Not yet created.
