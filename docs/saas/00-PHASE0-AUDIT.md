# Phase 0 — Repository Audit and Evidence Table

**Audit date:** 2026-09-08
**Audited commit:** `d27e8a9` (`release: complete Phase 12 (v1.0.0) - packaging, versioning, documentation`)
**Working tree:** clean, branch `master`, remote `https://github.com/mehedi995/AI-Document-Converter.git`
**Method:** direct source reading plus *executed* builds, *executed* test runs, and *executed* engine
probes. Every row marked VERIFIED was checked against running code, not against a document.

---

## 1. Executed baseline (actual results, not claims)

| Command | Actual result |
|---|---|
| `dotnet build AI.Document.Converter.sln -c Release` | **Build succeeded. 0 Warning(s), 0 Error(s).** 23.1s |
| `dotnet test ... --filter "Category!=Performance"` | **145 passed, 0 failed** (104 unit + 41 integration) |
| `dotnet test ...` (everything, incl. Performance) | **150 passed, 1 failed** (see below) |

The single failure is `PerformanceBenchmarkTests.ConvertAsync_SingleFileAtTargetSize(100 MB)`.
It is **not** a timeout — it failed in 1.3s against a 180s target with
`An unexpected error occurred during conversion for this file`. That test file's own header comment
documents this as known behaviour (Large Object Heap fragmentation when the 100 MB case runs chained
after the other heavy cases in one process; risk R-20), and `docs/16-TEST-STRATEGY.md` prescribes
excluding `Category=Performance` from routine runs.

**Assessment: a pre-existing, documented, environment-sensitive benchmark flake — not a functional
regression.** It is nonetheless a real unresolved memory-pressure signal that matters considerably
more for a long-lived server worker than for a desktop session, and is carried forward as risk SR-11.

### Toolchain actually present on this machine

| Tool | Present | Consequence |
|---|---|---|
| .NET SDK | **9.0.315 only** | **No .NET 10 SDK.** Blocks the prescribed ASP.NET Core 10 LTS host. BLOCKER-1. |
| .NET runtimes | 6.0, 7.0, 8.0, 9.0 (+ WindowsDesktop, AspNetCore) | Desktop app runs; no 10.x runtime. |
| Python | 3.13.14, all 5 engine deps importable | Engine runnable from source for probing. |
| Node / npm | 24.19.0 / 11.17.0 | Tailwind + TypeScript build chain is viable. |
| Docker | **not installed** | No container isolation for the worker; no throwaway Postgres. BLOCKER-2. |
| PostgreSQL | **not installed** | No local DB for EF Core migrations. BLOCKER-2. |

---

## 2. What the codebase actually is

A genuinely well-built desktop application — not a skeleton. 116 C# files across 4 projects plus an
8-file Python engine, 145 passing tests, 22 numbered design documents and 3 ADRs.

Code quality is high and, importantly, **the existing comments are honest**: they document known
limitations rather than overselling them. Several findings below were located *because* a source
comment admitted the gap.

```
src/AI.Document.Converter.Domain          18 cs   entities, enums, value objects (zero dependencies)
src/AI.Document.Converter.Application     47 cs   services + interfaces (pure, no I/O implementations)
src/AI.Document.Converter.Infrastructure  19 cs   Python client, file system, config, Serilog
src/AI.Document.Converter.Wpf             16 cs + 5 xaml   MVVM desktop shell
src/AI.Document.Converter.Python           8 py   dispatch.py + 4 extractors + tokenizer
```

The layering is real and clean: `Application` references only `Domain` plus MS.Extensions
abstractions, and holds no I/O implementation. **This is the single most valuable asset for the SaaS
migration** — the conversion pipeline is already separated from the desktop shell.

---

## 3. Evidence table

Status: **VERIFIED** (checked against running code) · **PARTIAL** · **MISSING** · **NOT-VERIFIABLE**

### 3.1 Engine portability and hosting

| ID | Documented behaviour | Source location | Observed verification | Status | Migration action | Residual risk |
|---|---|---|---|---|---|---|
| A-01 | Engine independent of WPF | `dispatch.py`; `PythonBackedDocumentProcessor.cs` | No WPF/UI reference in Domain/Application/Python. Application csproj references only Domain + MS.Ext abstractions. | **VERIFIED** | Reuse as-is | none |
| A-02 | No shell-injection surface | `PythonEngineClient.cs:63-71` | `UseShellExecute = false` and **no process arguments at all** — the request travels as one JSON line over stdin, so it can never be interpolated into a command line. | **VERIFIED** | Reuse | none |
| A-03 | Engine can run on Linux | `extractors/common.py:7,56` | **FAILS.** `from ctypes import wintypes` (line 7) and `ctypes.WinDLL("kernel32")` (line 56) execute at **module import scope**, and `dispatch.py:22` imports `extractors.common` unconditionally. On Linux CPython `ctypes.WinDLL` does not exist and `ctypes.wintypes` raises `ValueError`. **The engine cannot start on Linux at all — not for PDF, not even for `health_check`.** | **MISSING** | **Platform-guard `check_file_accessible`.** Highest-priority migration task. | Blocks every containerised/Linux deployment |
| A-04 | Engine path not user-controlled | `AppSettings.PythonExecutablePath`; `PythonEngineClient.ResolveExecutablePath()` | A user-supplied setting overrides the bundled path. Correct for desktop; **arbitrary-executable RCE if ever exposed to a tenant.** | **PARTIAL** | Remove from server config surface; fix path server-side | high if missed |
| A-05 | Locally installed Python required at runtime | `scripts/build-python-engine.ps1`, Wpf csproj | Not required — a PyInstaller onedir build is bundled. Python is a build-time dependency only. | **VERIFIED** | Container image replaces PyInstaller server-side | none |
| A-06 | Subprocess is resource-limited | `PythonEngineClient.cs` | **Only a wall-clock timeout** (default 120s). No CPU, memory, disk, file-descriptor or network limit. | **MISSING** | OS-level sandbox for the worker | parser-exhaustion DoS |
| A-07 | Engine makes no network calls | `tokenizer.py:1-40` | `TIKTOKEN_CACHE_DIR` is pinned to a bundled vocab folder *before* `import tiktoken`, specifically to prevent tiktoken's HTTPS vocab download. Genuinely offline. | **VERIFIED** | Reuse; keep vocab in image | none |

### 3.2 Normalized document model

| ID | Documented behaviour | Source location | Observed verification | Status | Migration action | Residual risk |
|---|---|---|---|---|---|---|
| B-01 | Represents paragraphs/headings/lists/tables/links | `Domain/Entities/ContentBlock.cs` | All six block types present, with a `System.Text.Json` polymorphic discriminator. | **VERIFIED** | Reuse | none |
| B-02 | Represents source locations | `Domain/ValueObjects/SourceLocation.cs` | `PageNumber` / `SlideNumber` / `SheetName`, populated by the PDF, PPTX and XLSX extractors. | **VERIFIED** | Reuse | none |
| B-03 | Represents missing content | `ContentBlock.cs` | `UnextractableTextBlock` and `ImagePlaceholderBlock` both exist and are actually emitted. | **VERIFIED** | Reuse | none |
| B-04 | Carries **stable block IDs** | — | **No ID field on any block, section or document.** Nothing to anchor a chunk, a warning, or a source highlight to. | **MISSING** | Add `BlockId` in model v2 | blocks traceability and warning anchoring |
| B-05 | Carries **extraction warnings** | — | **No warnings collection anywhere.** Verified empirically: the engine's JSON result has exactly two top-level keys, `metadata` and `sections`. | **MISSING** | Add `warnings[]` in model v2 | **root cause of C-01 and C-03** |
| B-06 | Is **versioned** | — | No schema or model version field, on the wire or in C#. | **MISSING** | Add `modelVersion` + `engineVersion` | silent contract drift |
| B-07 | Metadata records engine/preset/tokenizer/source hash | `Domain/Entities/DocumentMetadata.cs` | Has source path, file type, dates, page/slide/sheet counts, author. **No** source hash, engine version, preset, tokenizer version, or OCR/language setting. | **PARTIAL** | Extend for artifact metadata | non-reproducible artifacts |
| B-08 | `createdDate` is meaningful | `extractors/common.py:24` | Uses `os.path.getctime` — filesystem creation time. On a server that is **upload time**, not authorship time, and would be silently wrong. | **PARTIAL** | Prefer embedded document metadata, else omit | misleading metadata |

### 3.3 Conversion integrity — the material findings

| ID | Documented behaviour | Source location | Observed verification | Status | Migration action | Residual risk |
|---|---|---|---|---|---|---|
| **C-01** | Large XLSX sheets are not silently sampled | `xlsx_extractor.py:22-24,60-78` | **CONFIRMED SILENT DATA LOSS.** Ran the real engine against a generated 1001-row × 3-col sheet: returned `success: true`; returned **5 data rows out of 1000** (99.5% dropped); the only disclosure is an **in-band `paragraph` block of English prose inside the content itself**; the result carries **no warning field**. Thresholds hard-coded: `MAX_TABLE_ROWS=200`, `MAX_TABLE_COLUMNS=50`, `SUMMARY_SAMPLE_ROWS=5`. | **MISSING** (violates SaaS §6) | **Faithful mode must export all rows or fail explicitly.** Sampling survives only as an explicitly-labelled derivative. | **Blocks sale of XLSX conversion.** A customer would ship a 5-row "complete" extraction into a RAG index. |
| C-02 | Merged-cell provenance preserved | `xlsx_extractor.py:28-39` | The merge anchor value is **repeated into every covered cell** per FR-042. Faithful for reading, but the fact of the merge and its span is **not recorded** — provenance is lost, not preserved. | **PARTIAL** | Record merge span in block metadata | cannot distinguish genuine repetition from merge expansion |
| C-03 | A scanned PDF is not reported as complete success | `pdf_extractor.py:103-108` | A no-text page **does** get an `unextractableText` placeholder — good, not silently dropped. But with no warning channel, `ConversionResult.Success = true` and there is no `completed_with_warnings` state. A fully scanned PDF converts to a file of placeholders and **reports plain success**. | **PARTIAL** | Warning + `completed_with_warnings` status | user ships an empty document believing it converted |
| C-04 | Formula values are not fabricated | `xlsx_extractor.py:41-50` | `data_only=True`; if Excel never cached a value the cell is emitted **empty** rather than showing formula text or a guess. Correct and honest — but **uncommunicated** to the user. | **PARTIAL** | Warn when cached values are absent | silent blanks |
| C-05 | DOCX page numbers are not invented | `docx_extractor.py` metadata | `pageCount: None` for DOCX — **correctly omitted rather than fabricated**, exactly the SaaS §6 rule. | **VERIFIED** | Reuse | none |
| C-06 | Chunking operates on the structured model | `Application/Services/ChunkGenerator.cs` | Flattens the `DocumentModel` into atomic units — not a regex split of rendered Markdown. Headings glued to first block; overlap takes **whole trailing units only**. | **VERIFIED** | Reuse | none |
| C-07 | A table is never split across chunks | `ChunkGenerator.cs:96-104` | An oversized unit gets its own dedicated chunk (`isOversized` branch) rather than being split. | **VERIFIED** | Reuse | none |
| C-08 | Oversized tables raise a visible warning | `ChunkGenerator.cs` | The intact artifact is preserved, but **no warning is produced** and there is no provider hard-limit check. | **MISSING** | Emit warning; flag exports exceeding a provider limit | chunk silently unusable downstream |
| C-09 | Chunk options validated (size > 0, overlap < size) | `Domain/ValueObjects/ChunkOptions.cs` | **No validation on the type.** With `overlap >= size` the greedy packer degenerates — the carried-over overlap units alone fill each new chunk. | **MISSING** | Validate at the API boundary | degenerate output / runaway chunk count |
| C-10 | Token baseline is a documented extracted-text baseline, not source bytes | `Application/Services/PlainTextRenderer.cs` | The baseline is the **same extracted model re-rendered without Markdown syntax**. Exactly the correct comparison, and documented in the class comment. | **VERIFIED** | Reuse | none |
| C-11 | Zero baseline does not divide by zero | `Domain/ValueObjects/TokenEstimate.cs` | Guarded — returns `0` when `OriginalGpt4oStyle == 0`. No crash. But `0` displays as "0% reduction" rather than **N/A**, which is a different and wrong claim. | **PARTIAL** | Nullable; render "N/A" | mildly misleading |
| C-12 | Negative reduction is permitted | `TokenEstimate.cs` | Formula is unclamped, and the comment explicitly warns callers not to assume a positive value. Honest. | **VERIFIED** | Reuse | none |
| C-13 | The Claude estimate is not presented as exact | `tokenizer.py:1-14`, ADR-003 | Uses `cl100k_base` as an **acknowledged proxy**; the docstring states that Anthropic publishes no offline tokenizer and that the .NET side must label it. Correct and honest at the engine layer. | **VERIFIED** | Carry the label into the web UI — a UI obligation, not an engine one | mislabelling in the new UI |

### 3.4 Jobs, batching and reliability

| ID | Documented behaviour | Source location | Observed verification | Status | Migration action | Residual risk |
|---|---|---|---|---|---|---|
| D-01 | One failed file does not stop the batch | `Application/Services/BatchService.cs` | Per-file try/catch; failures counted, batch proceeds. | **VERIFIED** | Port the semantics | none |
| D-02 | Cancellation prevents unstarted work | `BatchService.cs:56` | `await semaphore.WaitAsync(cancellationToken)` throws before an unstarted file begins. Clean and correct. | **VERIFIED** | Port | none |
| D-03 | Batch summary distinguishes outcome classes | `Application/Models/BatchSummary.cs` | Has Total/Processed/Success/Failure. **No cancelled count and no warning count** — SaaS §7 requires all four. | **PARTIAL** | Extend summary | inaccurate batch reporting |
| D-04 | Jobs are durable / survive restart | `BatchService.cs` | **In-memory `Task.WhenAll` only.** No persistence, queue, lease, heartbeat, retry or outbox. Correct for desktop; SaaS §7 explicitly rules it insufficient. | **MISSING** | Persisted job + durable queue | total job loss on restart |
| D-05 | Extraction performed once per job | `ConversionService.cs:63-88` | **`GenerateChunksAsync` re-extracts from scratch**, spawning a second Python subprocess for a file `ConvertAsync` already extracted. Harmless on a desktop; on metered server compute it doubles the cost of every chunked conversion. | **PARTIAL** | Extract once, fan out to markdown/chunks/export | 2× compute cost and latency |
| D-06 | Output names are collision-safe | `Application/Services/OutputPathResolver.cs` | Numeric-suffix strategy, correctly locked for concurrent batch use and stable per source file. **But it consults only its own in-memory dictionary — never the filesystem** — so it can overwrite a file left by an earlier run. | **PARTIAL** | Run-scoped object-storage prefixes make this moot | overwrite across runs |
| D-07 | Errors are machine-readable | `Domain/Enums/ErrorCategory.cs` | 9 categories mapped end-to-end from the Python `errorCategory` string through to `ConversionResult`. **No retryability flag, no correlation ID.** | **PARTIAL** | Add retryable + correlation ID | poor operability |

### 3.5 Security and multi-tenancy

| ID | Documented behaviour | Source location | Observed verification | Status | Migration action | Residual risk |
|---|---|---|---|---|---|---|
| E-01 | Password-protected files handled, not crashed | `common.py:112-131`, `pdf_extractor.py:127` | OOXML: detects the OLE compound-file wrapper by signature. PDF: `document.needs_pass`. Both raise a clear `unsupportedFile`. Genuinely good work; `samples/password-protected-sample.pdf` exists as a fixture. | **VERIFIED** | Reuse | none |
| E-02 | File type validated by **content**, not just extension | `SupportedFileTypeExtensions.cs`; `ImportService` | **Extension-only.** No magic-byte or container check at the boundary; a mislabelled file is only caught later by the parser. | **MISSING** | Signature validation at upload | spoofed content type |
| E-03 | Archive / zip-bomb defences | — | **None.** OOXML files are ZIP containers handed straight to python-docx/openpyxl/python-pptx with no expansion-ratio or entry-count limit. | **MISSING** | Decompression limits in the worker | decompression-bomb DoS |
| E-04 | Path traversal rejected | `Infrastructure/FileSystem/PathValidator.cs` | Rejects non-rooted paths and any path whose `GetFullPath` normalization differs from the input. Sound for a desktop file picker. | **VERIFIED** (desktop scope) | Irrelevant server-side — replaced by tenant-scoped object keys | none |
| E-05 | Tenant isolation | — | **Not applicable — single-user desktop app.** No user, tenant, workspace, authentication, authorization or database of any kind. The entire SaaS §8 surface is to be built from zero. | **MISSING** | Build | — |
| E-06 | Document text kept out of logs | Serilog usage across services | Log statements carry **file names and error categories only** — no document content. Spot-checked `ConversionService`, `PythonEngineClient`, `BatchService`. | **VERIFIED** | Keep the discipline; file **names** still need review as PII server-side | filename leakage |
| E-07 | Markdown/HTML preview sanitized | WPF views | No HTML rendering exists in the desktop app, so nothing to sanitize. **A browser preview is a brand-new XSS surface with no existing defence to inherit.** | **MISSING** | Sanitize + CSP | stored XSS |

### 3.6 Licensing — unaddressed and commercially material

| ID | Finding | Evidence | Status | Residual risk |
|---|---|---|---|---|
| **F-01** | **PyMuPDF is AGPL-3.0.** Read directly from installed package metadata: `pymupdf 1.28.2 → License = "Dual Licensed - GNU AFFERO GPL 3.0 or Artifex commercial license"`. AGPL **§13 is the network clause**: making the combined work available to users over a network triggers the obligation to offer the complete corresponding source of that work. A paid SaaS converting PDFs with PyMuPDF is precisely that case. Subprocess or container separation does **not** by itself discharge the obligation. | `importlib.metadata` on the pinned version | **MISSING — no decision recorded** | **Blocks commercial launch of PDF conversion.** Not a development blocker. |
| F-02 | All other engine dependencies are permissive | `python-docx 1.2.0` MIT · `openpyxl 3.1.5` MIT · `python-pptx 1.0.2` MIT · `tiktoken 0.14.0` MIT | **VERIFIED** | none |
| F-03 | No licence documentation exists | Searched all 22 `docs/*.md`, 3 ADRs and `README.md` for `agpl|licen[cs]e|commercial` — **zero matches**. No `LICENSE` file, no attribution file, no third-party notices. | **MISSING** | attribution obligations unmet |

---

## 4. Reuse / refactor / replace plan

### REUSE unchanged — the migration's real head start
- `AI.Document.Converter.Domain` — entity/enum/value-object model, to be extended, not rewritten.
- `Application/Services/ChunkGenerator.cs` — already satisfies the SaaS chunking rules (C-06, C-07).
- `MarkdownGenerator.cs`, `MarkdownBlockRenderer.cs`, `FrontMatterBuilder.cs`, `PlainTextRenderer.cs`.
- `Python/tokenizer.py`, including its concurrency-hardened cache reader.
- `Python/extractors/docx_extractor.py`, `pptx_extractor.py` — sound, MIT-licensed, portable.
- All 145 passing tests, as the regression net for everything above.

### REFACTOR
| Item | Change |
|---|---|
| `extractors/common.py` | **Platform-guard the Win32 block (A-03) — top-priority task.** Nothing runs on Linux until this is done. |
| Normalized model | v2: block IDs, `warnings[]`, `modelVersion`, `engineVersion` (B-04/05/06). |
| `xlsx_extractor.py` | Faithful-mode full export or explicit failure; sampling demoted to a labelled derivative (**C-01**). |
| `ConversionService` | Path-in/path-out → stream/blob boundary; extract once and fan out (D-05). |
| `PythonEngineClient` | Fixed server-side engine path; resource limits; correlation ID (A-04, A-06). |
| `ChunkOptions` | Validate size > 0 and overlap < size (C-09). |
| `TokenEstimate` | Nullable reduction so an empty baseline renders **N/A**, not 0% (C-11). |
| `BatchSummary` | Add cancelled and warning counts (D-03). |

### REPLACE
| Item | Replaced by | Why |
|---|---|---|
| `BatchService` in-memory orchestration | Persisted job + durable queue + lease/heartbeat | D-04 |
| `JsonSettingsStore` / `AppSettings` | Server config + versioned plan/preset config | multi-tenant |
| `PathValidator`, `OutputPathResolver`, file writers | Tenant-scoped object storage keys | E-04, D-06 |
| PyInstaller onedir bundle | Container image | A-03, A-06 |
| WPF shell | ASP.NET Core web host | — |
| **PyMuPDF** *(pending decision)* | Artifex commercial licence **or** pypdfium2 / pdfminer.six | **F-01** |

### PRESERVE UNTOUCHED
`src/AI.Document.Converter.Wpf` and its `net8.0-windows` target. No SaaS work requires changing it.

---

## 5. Blockers requiring a decision

| # | Blocker | Blocks | Why it cannot be defaulted |
|---|---|---|---|
| **1** | **No .NET 10 SDK** (9.0.315 only) | Phase 1 project creation | The prescribed default is ASP.NET Core 10 LTS. .NET 9 is STS and **already out of support as of May 2026** — a poor foundation for a new commercial product. Installing an SDK is a machine-level change requiring a download. |
| **2** | **No Docker, no PostgreSQL** | EF Core migrations; worker isolation | The prescribed default is EF Core + PostgreSQL with a durable queue and a sandboxed worker. Neither the database nor a container runtime exists on this machine. |
| **3** | **PyMuPDF AGPL (F-01)** | Commercial launch of PDF conversion | Requires a paid Artifex licence, a library replacement, or open-sourcing the service. A legal and commercial call, not an engineering default. **Does not block development.** |
| 4 | Billing provider eligibility (Bangladesh) | Phase 3 only | Deferred by design; build a provider-neutral boundary first. |
