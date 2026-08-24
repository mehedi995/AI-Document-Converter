# ADR-001 — .NET / Python Integration Mechanism

**Status:** Accepted
**Date:** 2026-08-23

## Context

Document extraction (PDF, DOCX, XLSX, PPTX) relies on Python libraries (`pymupdf`,
`python-docx`, `openpyxl`, `pandas`, `python-pptx`, `markdownify`, `tiktoken`) that
have no comparably mature .NET equivalent. CLAUDE.md Section 7 requires evaluating:

1. Python executable integration (bundle a standalone Python executable)
2. Local Python environment (require Python pre-installed on the target machine)
3. Embedded Python (in-process CPython, e.g., via `pythonnet`)
4. Python subprocess (invoke Python as a child process)
5. JSON input/output communication (a specific IPC data format)

Constraints that drive this decision (see `docs/01-BRD.md`, `docs/03-SRS.md`):

- Must work on locked-down enterprise Windows desktops, often without local admin
  rights or internet access (NFR-010).
- Must be deployable with minimal manual dependency installation.
- Must be maintainable by a **mid-level developer** — simplicity beats performance
  headroom the product doesn't need (CLAUDE.md Section 5).
- Must support cancellation of an in-progress file (FR-037).
- Must not open any network listener or make outbound calls (NFR-001, SEC-001).

## Options Considered

| Option | Pros | Cons |
|---|---|---|
| **A. Require a system-installed Python** | Smallest installer; no bundling effort | Version/library drift is exactly the "Python distribution complexity" risk flagged in the BRD; adoption friction on locked-down IT-managed machines; support burden falls on IT. |
| **B. Embedded Python (`pythonnet` / CPython embedding API)** | No process-start overhead; in-process calls feel "native" | Significant added complexity (GIL/threading interaction, embedding-version pinning, harder to debug for a mid-level developer); packaging is more fragile; performance gain is irrelevant here since parsing time dominates over process-start time for the document sizes in scope (up to 100 MB). |
| **C. Long-running local Python server (REST/gRPC over loopback)** | Avoids repeated process-start cost for large batches | Adds process lifecycle management (start/stop/health/port conflicts) and a local listening socket — an unnecessary attack-surface question for a banking-sector desktop app; batch conversion is not a high-throughput streaming workload, so the added complexity buys little. |
| **D. Bundled standalone Python executable, invoked as a short-lived subprocess per file, JSON over stdin/stdout** | Zero separate Python install required (solves NFR-010 outright); subprocess isolation means one file's crash can't corrupt shared state or take down other files; trivial to cancel (kill the process, satisfies FR-037); simple, debuggable, mid-level-developer-friendly mental model: "run a program, read its JSON output." | Slight per-file process-start overhead (milliseconds — negligible next to actual parsing time); requires a packaging step (PyInstaller or equivalent) as part of the build. |

## Decision

**Option D**, with a fallback override:

- The Python processing engine ships as a **self-contained executable** (built with
  PyInstaller or equivalent) bundled inside the WPF application's installation
  folder. No separate Python installation is required for the default path.
- The .NET Infrastructure layer invokes this executable as a **short-lived
  subprocess, one invocation per file** (not a long-running shared process).
- Communication uses **JSON over stdin/stdout**: the .NET side writes a JSON request
  (file path, operation, options) to the process's stdin and reads a JSON response
  (extracted document model or a structured error) from stdout. This keeps the
  contract simple, versionable, and testable independently on each side.
- `docs/03-SRS.md` FR-032 already allows the Python executable/engine path to be
  overridden in Settings, for enterprise environments that require a centrally
  managed Python distribution instead of the bundled one. Any overridden path is
  validated per SEC-005 before use.

## Consequences

- Deployment (`docs/19-DEPLOYMENT-PLAN.md`, future) must include a build step that
  produces the bundled Python executable and packages it alongside the WPF
  installer output.
- The Infrastructure layer needs one adapter responsible for: building the JSON
  request, starting the process, enforcing a timeout, reading/parsing the JSON
  response, and mapping subprocess failures to the FR-029 error categories.
- Cancellation (FR-037) is implemented by killing the child process; this is
  the reason a per-file short-lived process was chosen over a shared long-running
  one, where killing the server would cancel every in-flight file.
- Requirement wording elsewhere that says "Python engine" (FR-029, FR-034, FR-038)
  now maps concretely to "the bundled Python subprocess" for architecture and
  implementation purposes.

## Addendum (2026-08-24) — PyInstaller "onedir", not "onefile"

Phase 3 (Document Extraction) added `pymupdf`, `python-docx`, `openpyxl`, and
`python-pptx` to the bundled engine. Measured against the real build:

- A PyInstaller **`--onefile`** build (Phase 1's original choice, when the engine
  was stdlib-only) re-extracts its entire payload to a temp directory on *every*
  launch. Once the heavier libraries were bundled in, this cost **~5 seconds per
  invocation** — and since the Decision above starts one process per file, that
  cost would be paid on every single file in every batch, making the
  `docs/03-SRS.md` Section 8 performance benchmarks (e.g., 100 files in ≤ 8
  minutes) unachievable on process-start overhead alone.
- Switching to PyInstaller **`--onedir`** (unpacked once at build time, not on
  every run) brought per-invocation startup down to **~1.1 seconds**, measured
  identically. `scripts/build-python-engine.ps1`,
  `AI.Document.Converter.Wpf.csproj`'s copy step, and the integration tests'
  `RepoPaths.BundledPythonEnginePath()` were all updated accordingly — the
  bundled engine is now a **folder** (`AIDocumentConverter.PythonEngine.exe` plus
  an `_internal/` folder of dependencies), not a single file.
- This does not change the Decision above: it is still one self-contained,
  bundled artifact requiring no separate Python install, still invoked as a
  short-lived subprocess per file, still communicating over JSON stdin/stdout.
  `docs/19-DEPLOYMENT-PLAN.md`'s wording is updated to say "application folder"
  rather than "single executable" where that distinction matters (e.g., an
  installer must package the whole folder, not one file).

## Addendum (2026-08-24) — SEC-004 has nothing to clean up

Phase 9 (Error Handling) revisited SEC-004's requirement to clean up
temporary files, including orphaned ones from a prior abnormally terminated
session. This Decision's stdin/stdout JSON transport means the .NET side
never writes an intermediate file for the Python engine to read, and the
`--onedir` switch above means the engine itself no longer self-extracts to a
temp directory either (that was specifically the `--onefile` behavior being
replaced). Confirmed by inspection that neither side of this integration
creates a temp file anywhere. No `TempFileCleanupService` was built as a
result — see `docs/18-RISK-ASSESSMENT.md` R-19.
