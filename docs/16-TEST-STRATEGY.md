# 16 — Test Strategy

**Project:** AI Document Converter
**Status:** Draft — Phase 16 (Test Strategy) — Gate 4
**Date:** 2026-08-23

---

## 1. Testing Levels

| Level | Scope | Technology |
|---|---|---|
| **Unit** | Pure logic with no file system, process, or Python dependency: Markdown generation, front-matter building, chunking algorithm, error mapping, path validation, settings serialization. Infrastructure classes tested with their Python/file-system dependency mocked. | xUnit + a mocking library (Moq or NSubstitute — final choice at implementation time; either is fine for the small number of interfaces involved) |
| **Integration** | Real Python engine against real sample files (`samples/`); real file system writes for export/ZIP; real settings file round-trip. | xUnit, `tests/AI.Document.Converter.IntegrationTests` |
| **UI** | WPF views are kept intentionally logic-free (MVVM, CLAUDE.md Section 50), so most "UI testing" is really ViewModel unit testing. Genuine UI behavior (drag-and-drop, dialog interaction) is verified manually against a documented exploratory test script, not automated — full WPF UI automation (e.g., a driver like WinAppDriver) is judged disproportionate to this project's size and is explicitly out of scope for MVP. | Manual, scripted |
| **End-to-End** | Full pipeline: import → extract → convert → estimate → chunk → export, across all five formats, run as one integration test suite. | xUnit, `tests/AI.Document.Converter.IntegrationTests/EndToEnd` |
| **Performance** | The benchmark matrix in `docs/03-SRS.md` Section 8, measured with a simple stopwatch-based harness around real conversions (a dedicated `BenchmarkDotNet` suite is not warranted at this scale — the targets are wall-clock pass/fail thresholds, not micro-benchmarks). | Custom harness in `IntegrationTests`, run manually per release (Sprint 11/Phase 11) |
| **Regression** | Full unit + integration suite re-run before every release; a short manual smoke-test checklist (one file per format, one batch, one cancellation, one error case) before tagging a release. | CI-equivalent local run (no CI server is in scope for MVP — see `docs/19-DEPLOYMENT-PLAN.md`) |

## 2. What Is Deliberately Not Automated

- **WPF UI automation** — the cost of a UI automation framework outweighs its value
  for a single-window, MVVM-disciplined desktop app with five screens. Manual
  exploratory testing against `docs/06-ACCEPTANCE-CRITERIA.md` covers this.
- **Load/stress testing beyond the SRS benchmark matrix** — the application is
  single-user; there is no concurrent-user load profile to test.
- **Security penetration testing** — out of scope for MVP; the SEC-00x requirements
  are verified by code review and targeted unit tests (e.g., path-traversal
  rejection, injection-safe subprocess arguments), not a formal pentest.

## 3. Test Data

`samples/` (`docs/34` per CLAUDE.md numbering is folded into this strategy, not a
separate document) holds one representative, non-confidential file per format:
`sample.pdf`, `sample.docx`, `sample.xlsx`, `sample.pptx`, `sample.txt`, each
containing headings, a table, a list, and a hyperlink where the format supports it,
so a single fixture set exercises most FR-006–FR-010 requirements. Additional
purpose-built fixtures are added per edge case as needed (a corrupted file, a
password-protected file, an oversized XLSX sheet, a PDF with a scanned page) —
tracked in `docs/17-UNIT-TEST-PLAN.md`.

## 4. Definition of Done Linkage

A feature is not "Done" (CLAUDE.md Section 40) until its unit tests pass, its
relevant integration tests pass, and its Acceptance Criteria (from
`docs/06-ACCEPTANCE-CRITERIA.md`) are demonstrably satisfied — this strategy is the
mechanism that makes that demonstrable rather than assumed.

## 5. Offline-Compliance Verification (US-023)

A dedicated test pass, not just code review: run a full
import → convert → chunk → export cycle across all five formats while capturing
network traffic (e.g., via a local proxy or OS-level connection monitor) and assert
zero outbound connections were made. This is a Sprint 10 / Phase 11 activity
(`docs/13-SPRINT-PLAN.md`).

**Verified 2026-08-24 (Phase 11, TASK-062/AC-023).** Two complementary checks:

- **Static**: `grep`-audited the entire codebase (`src/`, both .NET and Python)
  for any network API (`HttpClient`, `WebRequest`, sockets, Python `requests`/
  `urllib`) outside of `tiktoken_cache`/test fixtures. None found.
- **Live**: ran `FullPipelineTests` (Import → Convert → Chunk → Export, all five
  formats, real bundled Python engine) while polling `Get-NetTCPConnection`/
  `Get-NetUDPEndpoint` for every relevant process. `AIDocumentConverter.
  PythonEngine.exe` (the actual document-processing subprocess) never appeared
  in any sample - zero TCP or UDP connections of any kind, for any of the five
  conversions. The test-runner process itself (`testhost`) showed only
  loopback (127.0.0.1) traffic to its own parent `dotnet` process - the
  standard VSTest IPC channel, not application network activity.

  One unrelated `dotnet.exe` process on the machine (a different PID, present
  before the test started and using a different port pattern with no loopback
  pairing to the test's own IPC) held an established connection to an Azure IP
  over HTTPS throughout - identified as background IDE/tooling (VS Code's C#
  language server or similar), not the application under test, since it: (a)
  was a distinct process from both `testhost` and its parent, (b) was already
  connected before the monitored operation began, and (c) has no code path in
  this repository that could produce it. Flagged and investigated explicitly
  rather than assumed, per the "always verify" pattern established in earlier
  phases - this is exactly the kind of finding that must not be waved away
  without checking.

---

*Next document: `docs/17-UNIT-TEST-PLAN.md`*
