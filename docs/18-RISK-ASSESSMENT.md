# 18 — Risk Assessment

**Project:** AI Document Converter
**Status:** Draft — Phase 18 (Risk Assessment) — Gate 4
**Date:** 2026-08-23

---

Risk Level = Probability × Impact (Low / Medium / High, qualitative). Supersedes and
expands the business-level summary in `docs/01-BRD.md` Section 10.

| ID | Risk | Probability | Impact | Level | Mitigation | Contingency |
|---|---|---|---|---|---|---|
| R-01 | Large file processing (100 MB) exhausts memory or freezes the UI | Medium | High | High | Async pipeline throughout (NFR-003); processing happens inside the Python subprocess, keeping .NET memory footprint low; benchmark matrix validated in Phase 11 | If a real-world file exceeds targets, add a documented maximum file size with a clear rejection message rather than a silent hang |
| R-02 | Corrupted/malformed source files crash extraction | Medium | Medium | Medium | Each processor call wrapped in categorized exception handling (FR-029); Python-side extraction wrapped in try/except mapped to the JSON error contract (ADR-001) | Failing file is skipped (BR-006); user can retry (FR-030) after fixing the file elsewhere |
| R-03 | Python distribution/version drift breaks extraction on a user's machine | Low | High | Medium | Bundled, version-pinned PyInstaller executable (ADR-001) removes dependency on the user's own Python entirely | Settings override to a system Python is validated (SEC-005); if that also fails, FR-038's startup check surfaces one clear error instead of per-file failures |
| R-04 | Third-party library (pymupdf, python-docx, etc.) version incompatibility during future maintenance | Medium | Medium | Medium | Pin exact library versions in `requirements.txt`; rebuild and re-test the bundled executable on any version bump, never float versions in production builds | Roll back to the last known-good pinned version; document the incompatibility in `docs/22-BUG-TRACKER.md` |
| R-05 | Memory usage during large batch runs | Medium | Medium | Medium | Bounded parallelism (NFR-011); batch size ceiling (NFR-013); each file's Python subprocess exits after completion, releasing its memory | Reduce default parallelism/ceiling if benchmark testing (Phase 11) shows pressure on typical hardware |
| R-06 | Tokenizer estimates diverge meaningfully from a provider's real token count | High | Low | Medium | Estimates always labeled as estimates (FR-017); ADR-003 documents exactly which encoding backs each label, with the Claude-style estimate explicitly flagged as approximated | If user feedback shows the approximation is materially misleading, revisit ADR-003's encoding choice or add a wider disclaimer |
| R-07 | Table extraction quality (merged cells, nested tables, irregular layouts) is imperfect | High | Low | Medium | FR-042 defines a concrete default (computed values, repeated merged-cell values); known limitations documented rather than silently producing malformed tables | Track user-reported extraction-quality issues in `docs/22-BUG-TRACKER.md`; address the highest-frequency cases first |
| R-08 | Complex/multi-column PDF layouts extract with scrambled reading order | High | Low | Medium | Documented as a known limitation (this row) rather than an implicit promise of perfect fidelity — avoids over-promising during UAT | No fix planned for MVP; revisit if it blocks a real customer document set |
| R-09 | Password-protected/encrypted documents | Medium | Low | Low | Explicitly rejected with a clear "not supported" message (BR-003), not treated as a generic failure | Out of scope for MVP; revisit if demand emerges (`docs/20-FUTURE-ROADMAP.md`) |
| R-10 | File permission/lock issues (file open elsewhere, no write permission to output) | Medium | Low | Low | Categorized as `FileLocked`/`PermissionDenied` (FR-029) with retry support (FR-030) | User closes the file/grants permission and retries; no data loss since the source file is never modified |
| R-11 | WPF UI freezing during extraction/batch/export | Medium | High | High | Async/await end-to-end (NFR-003, PERF-001/002); UI thread never performs file I/O or waits on the Python subprocess synchronously | If freezing is observed in testing, profile for a blocking call and fix before release — this is a release blocker, not a known limitation |
| R-12 | Future OCR integration requires a breaking change to the document model | Low | Medium | Low | `ExtractionMethod` enum reserves an unused `OcrText` value now (`docs/09-DATA-MODEL.md` Section 3) | If the reservation proves insufficient when Phase 2 OCR work begins, a model versioning ADR is written at that time |
| R-13 | A telemetry/analytics dependency is pulled in unintentionally via a NuGet/pip package, silently violating the offline guarantee | Low | High | Medium | Dependency audit task (TASK-012) at the end of Phase 1 and re-checked before release (NFR-012) | If found post-release, treated as a Critical bug (`docs/22-BUG-TRACKER.md`) with an immediate patch release |
| R-14 | User-configured Python executable path points to an untrusted/malicious binary | Low | High | Medium | SEC-005 validation (exists, executable, expected interpreter) before any invocation | Residual risk accepted for IT-configured environments per `docs/07-TECHNICAL-ARCHITECTURE.md` Section 9 — documented, not silently ignored |
| R-15 | Partially scanned PDFs (mixed digital + scanned pages) produce silently incomplete Markdown | Medium | Medium | Medium | FR-043: unextractable pages are explicitly marked, never silently dropped | Full OCR remains a Phase 2 item; MVP's marking behavior sets correct user expectations in the meantime |
| R-16 | Enterprise IT blocks running a bundled, unsigned executable (the Python engine or the app itself) | Medium | High | High | Code-sign the installer and the bundled Python executable as part of the release build (`docs/19-DEPLOYMENT-PLAN.md`) | If signing is not yet in place for an early pilot, provide IT with a documented hash/checksum for manual allow-listing |

---

*Next document: `docs/19-DEPLOYMENT-PLAN.md`*
