# Changelog

All notable changes to this project are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); versioning follows
[Semantic Versioning](https://semver.org/) (CLAUDE.md Section 55).

## [1.0.0] — 2026-08-24

First release. All 12 `docs/12-DEVELOPMENT-ROADMAP.md` phases complete; every
Gate 1–5 approval satisfied.

### Added
- 2026-08-24 (Gate 5, Phase 12 — Release): self-contained `win-x64` publish
  (`dotnet publish`, `<Version>1.0.0</Version>` set solution-wide via
  `Directory.Build.props`), `scripts/package-release.ps1` (publish → separate
  debug symbols into `publish/symbols/` → portable ZIP → Inno Setup installer
  when available), and `scripts/installer.iss` (Start Menu shortcuts, optional
  desktop shortcut, uninstaller that never touches
  `%LOCALAPPDATA%\AIDocumentConverter\`, per `docs/19-DEPLOYMENT-PLAN.md`
  Sections 6/8). Verified the published self-contained build launches and
  passes its startup health check against its *own* bundled Python engine
  copy specifically - re-tested with the dev build's copy temporarily removed
  from disk, since a stale local Settings override had silently been pointing
  at the dev copy instead. Code signing and a genuinely clean-VM install
  pass are called out explicitly as remaining, environment-dependent manual
  steps (`docs/19-DEPLOYMENT-PLAN.md` Section 9) rather than skipped
  silently. Rewrote `README.md`, which had been left describing the
  pre-implementation state through eleven phases of actual development.
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
- 2026-08-24 (Gate 5, Phase 7 — Chunk Generation): `ChunkGenerator` (FR-018-021)
  operating on `DocumentModel` - a section's heading is glued to its first block
  as one atomic unit (FR-020), a table exceeding the chunk size gets its own
  dedicated chunk rather than ever being split (FR-019/AC-015), and overlap is
  applied at whole-unit boundaries. Real per-unit token counts come from one new
  batched `count_tokens` Python operation per document (not one subprocess call
  per chunk-boundary decision, which would have reintroduced the R-17 startup-cost
  problem). Extracted `MarkdownBlockRenderer` so `MarkdownGenerator` and
  `ChunkGenerator` share identical formatting rules instead of risking drift
  between two renderers. Added `IChunkFileWriter` (writes `chunk_NNN.md` with
  source/sequence front matter) and a new `ConversionService.GenerateChunksAsync`
  entry point plus a "Generate Chunks" Dashboard action - chunk size/overlap
  reuse the existing Settings screen fields rather than a duplicate screen.
  103 tests passing (90 unit + 13 integration, including a real multi-chunk
  generation test against the bundled engine).
- 2026-08-24 (Gate 5, Phase 8 — Batch Processing): `BatchService` (FR-022,
  NFR-011) running the same single-file `ConvertAsync`/`GenerateChunksAsync`
  pipeline over many files with a `SemaphoreSlim`-bounded degree of
  parallelism, per-file `IProgress<BatchProgressUpdate>` reporting
  (Started/Completed/Cancelled, FR-023), and a Dashboard Cancel command
  wired to a per-batch `CancellationToken` (FR-037). `DashboardViewModel`
  now tracks each file's pipeline stage independently rather than one
  collapsed status — Queued/Converting/Converted for Convert All and
  Chunking/Chunked for Generate Chunks, each able to fail on its own stage
  (FR-045). 113 tests passing (98 unit + 15 integration, including a real
  120-file batch and a real mid-batch cancellation against the bundled
  Python engine).

  Two concurrency bugs found and fixed while making parallelism real rather
  than assumed safe:
  - `OutputPathResolver` is intentionally shared across a whole batch to
    resolve filename collisions across all its files, but its collision map
    was not synchronized — concurrent files could resolve to the same output
    path. Added a lock around every read/write of that map.
  - `tiktoken`'s own cache loader deletes and rewrites a cache file on a hash
    mismatch; under NFR-011's real concurrent subprocess launches, one
    process's rewrite could race another's read of the same bundled
    vocabulary file, corrupting that read. Since every vocabulary file this
    app needs is bundled ahead of time and never fetched at runtime
    (NFR-001/002), replaced tiktoken's read-verify-rewrite loader with a
    plain read-only load, removing the only path that could race. Also
    switched `dispatch.py`'s extractor imports to on-demand (`importlib`)
    rather than eager module-load imports, so a batch's concurrent
    tokenize-only subprocesses no longer each pay to import all four
    extraction libraries (~86MB) they don't use.

  Verified by running the built app (Dashboard renders correctly, Convert
  All/Generate Chunks/Cancel show the correct enabled/disabled state) and by
  confirming no blocking (`.Wait()`/`Task.Result`/`GetAwaiter().GetResult()`)
  calls exist anywhere in the WPF layer. Driving the native file-import
  dialog end-to-end was not automated in this pass — no UI-automation
  harness exists yet in this repo; see `docs/18-RISK-ASSESSMENT.md` if one
  is added later.
- 2026-08-24 (Gate 5, Phase 9 — Error Handling): most of FR-029's error
  categorization already existed by construction from earlier phases
  (`DocumentConversionException`, `ConversionService`'s catch-all
  `ExecuteAsync`, and SEC-006's stdin/stdout-only subprocess design in
  `PythonEngineClient`, which never gave file paths or content a
  command-line injection surface to begin with). This phase closed the real
  remaining gaps: none of the four Python extractors distinguished a
  locked/permission-denied file from a generic crash, and DOCX/XLSX/PPTX
  password-protection fell through as a misleading `corruptedDocument`
  error — a real BR-003 violation, since only PDF had ever checked for it.
  Added `check_file_accessible`/`check_not_encrypted_ooxml` to
  `extractors/common.py`, wired into all four extractors. Added a Retry
  command (FR-030/AC-020) on the Dashboard — `FileConversionViewModel` now
  tracks `ConversionSummary`/`ChunkSummary` as two independent fields
  (rather than one accumulating string) plus `LastAttemptedOperation`, so
  Retry re-runs only the stage that actually failed for that row without
  duplicating text from a prior attempt. Added a last-resort
  `DispatcherUnhandledException`/`AppDomain.UnhandledException`/
  `TaskScheduler.UnobservedTaskException` safety net in `App.xaml.cs`
  (FR-031) — full detail to the log, always the same fixed generic sentence
  to the user, never `ex.Message`/`ex.ToString()`. 123 tests passing (98
  unit + 25 integration, 10 of them new: a real OS-level file lock via
  `FileShare.None`, real garbage bytes, and a real OLE-compound-file header,
  each driven through the actual bundled Python engine, plus a real
  retry-after-lock-released flow).

  SEC-004 (temp-file cleanup) turned out to have nothing to clean up:
  confirmed by inspection that this app creates zero temporary files
  anywhere — ADR-001's stdin/stdout design and the `--onedir` PyInstaller
  layout (R-17) together mean no intermediate file is ever written by
  either side of the integration. Documented as a deliberate resolution
  (`docs/adr/ADR-001-python-integration.md` Addendum,
  `docs/18-RISK-ASSESSMENT.md` R-19) rather than building a
  `TempFileCleanupService` with nothing to actually clean up.

  One design assumption caught and corrected during verification, before it
  ever reached a test or shipped: Windows' `PermissionError` for a locked
  file was assumed to carry a distinguishing `winerror` (32) separate from a
  real ACL denial (5) — empirically, CPython's `open()` collapses both to
  the same `PermissionError` with `winerror=None` on this platform. Fixed by
  probing accessibility with `CreateFileW` directly via `ctypes` and reading
  the real Win32 error code, verified against an actual
  `FileShare.None`-held file before relying on it in any test.
- 2026-08-24 (Gate 5, Phase 10 — Export): closed a real documentation gap
  found while starting this phase — `FR-046` had been referenced from the
  roadmap, sprint plan, and traceability matrix since Gate 1, but was never
  actually written into `docs/03-SRS.md`, and the ZIP's internal layout was
  never actually specified in `docs/10-FOLDER-STRUCTURE.md` despite three
  other documents citing it as "finalized there." What `metadata/` should
  contain was a genuine open design question (front matter already covers
  source/dates/pages inline) — confirmed with the user rather than assumed:
  one `metadata/<name>.json` per successfully converted file, carrying the
  token-estimate/success/chunk-count data that today exists only
  transiently in the UI. Added both to close the gap before implementing
  against it. TASK-056 (individual Markdown export, FR-027) needed no new
  code — Phase 4/7's `IMarkdownFileWriter`/`IChunkFileWriter` already
  produce exactly that; this phase's only new deliverable was the
  single-ZIP packaging (FR-028/046).

  Added `IExportService`/`ExportService` (Application) and
  `ZipPackageBuilder` (a plain class, no interface — pure file-copying
  logic over already-materialized output, nothing worth mocking) producing
  `markdown/<name>.md`, `chunks/<name>/chunk_NNN.md` (only for files that
  were chunked), and `metadata/<name>.json` per file. `ConversionResult`
  now carries `DocumentMetadata` (set once, in `ConversionService`, so
  Export never has to re-parse it back out of already-written front
  matter). `IPathValidator` gained `IsValidFilePath` (SEC-002) alongside
  the existing `IsValidDirectoryPath` — same traversal-rejection rule,
  named for the caller's intent. Added an "Export as ZIP..." action on the
  Dashboard; `FileConversionViewModel` now retains each row's actual
  `ConversionResult`/chunk `ConversionResult` (not just their display
  strings) so Export has real data to package rather than re-deriving it
  from UI text.

  A failed export leaves no half-written ZIP behind — the destination file
  is deleted on any write failure rather than left as a corrupt file
  masquerading as a successful export.

  130 tests passing (103 unit + 27 integration, 6 new: export-path
  validation rejection, no-successful-files rejection, and a real ZIP built
  and re-opened to assert its actual internal layout and metadata JSON
  content).
- 2026-08-24 (Gate 5, Phase 11 — Testing): a full verification pass against
  the SRS and Acceptance Criteria that found and closed several real gaps,
  not just a pre-existing-test run:
  - `docs/16-TEST-STRATEGY.md` had promised a dedicated End-to-End suite
    (`tests/.../EndToEnd/FullPipelineTests.cs`, Import → Convert → Chunk →
    Export across all five formats through the real bundled engine) since
    Gate 4 - it never actually existed until this phase.
  - **AC-027/FR-039 (embedded image → placeholder) was only ever correctly
    implemented for PPTX** - PDF and DOCX extraction silently dropped
    embedded images entirely, with no test ever having caught it since no
    fixture with a real embedded image existed for any format. Added real
    image detection to `pdf_extractor.py` (pymupdf's image dict-blocks) and
    `docx_extractor.py` (`w:drawing`/`w:pict` inside a paragraph's runs),
    plus `image-sample.{pdf,docx,pptx}` fixtures and tests for all three.
  - Added the password-protected-PDF test `docs/17-UNIT-TEST-PLAN.md`
    required but never had a fixture for, and a real `permissionDenied`
    category test (a directory-as-file `CreateFileW` probe - reliably
    triggers `ERROR_ACCESS_DENIED` with no ACL manipulation needed).
  - **AC-022 (settings survive a restart)** had only ever been verified
    against a mocked `IOptionsMonitor` - added a test building the real
    `AddJsonFile`+`Configure<AppSettings>`+`IOptionsMonitor` pipeline in a
    fresh DI container over a file a prior "session" wrote to.
  - **AC-023 (no outbound network calls)** verified two ways: a full static
    audit of every `_logger.Log*`/`Log.*` call site and every Python module
    for a network API (none found), and a live network-connection check
    during a real five-format Import→Convert→Chunk→Export run - the bundled
    Python engine subprocess made zero TCP/UDP connections of any kind.
  - **AC-024 (no document content in logs)**: every log call site logs only
    fixed text, file names/paths, categories, or an exception's own message;
    the one call that echoes a Python subprocess's stderr is Debug-level,
    filtered out entirely by the configured `MinimumLevel.Information()`.
  - Measured the full `docs/03-SRS.md` Section 8 performance benchmark
    matrix for real - every target holds with a wide margin (100 MB in
    ~26-27s against a 3-minute target; a 100-file batch in ~76s against an
    8-minute target). Investigating an intermittent failure on the 100 MB
    case led to a real finding: chaining six large-allocation benchmark
    cases in one .NET process fragments the Large Object Heap, which plain
    `GC.Collect()` does not compact by default - fixed in the benchmark
    harness with an explicit `GCSettings.LargeObjectHeapCompactionMode =
    CompactOnce`. Confirmed this is a test-harness characteristic, not a
    product defect: the bundled engine, the `tiktoken` cache integrity, and
    the conversion pipeline were each individually verified correct, and the
    100 MB case is completely reliable run in isolation. See
    `docs/18-RISK-ASSESSMENT.md` R-20.
  - Hardened `tokenizer.py`'s bundled-cache reader to verify the full
    `expected_hash` (not just a byte count) before accepting a read, with a
    brief retry - a stronger, more defensible check than what Phase 8 shipped,
    even though it was not the root cause of the R-20 investigation above.

  154 tests passing (104 unit + 41 integration in the routine suite, plus a
  separately-run 6-case performance suite).
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

---

Tagged as `v1.0.0` — see the `master` branch commit history for the full
per-phase commit sequence from the solution skeleton (`e3b48d4`) onward.
