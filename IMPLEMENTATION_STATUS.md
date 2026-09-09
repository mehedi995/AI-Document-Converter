# IMPLEMENTATION_STATUS — Cloud SaaS Edition

**Last updated:** 2026-09-08
**Baseline commit:** `d27e8a9` (desktop v1.0.0)
**Current phase:** Phase 0 complete. **Phase 1 started 2026-09-09** — tooling installed, host
scaffolded, schema migrated. No customer journey yet.

> Scope note: the cloud SaaS edition is authorized and supersedes the desktop-only /
> no-server / no-auth / no-database constraints **for the cloud edition only**. The desktop
> application in `src/AI.Document.Converter.Wpf` is unchanged and stays on `net8.0-windows`.

---

## 1. Completed, with evidence

### Phase 0 — Repository audit ✅

| Deliverable | Location |
|---|---|
| Evidence table (46 audited rows across 6 areas) | `docs/saas/00-PHASE0-AUDIT.md` |
| SaaS requirements change matrix (46 FR, 13 NFR, 7 BR, 6 SEC + new SR-* requirements) | `docs/saas/01-REQUIREMENTS-CHANGE-MATRIX.md` |

**Executed baseline (actual results):**

| Command | Result |
|---|---|
| `dotnet build AI.Document.Converter.sln -c Release` | Build succeeded. **0 Warning(s), 0 Error(s).** |
| `dotnet test --filter "Category!=Performance"` | **145 passed, 0 failed** (104 unit + 41 integration) |
| `dotnet test` (all, incl. Performance) | 150 passed, **1 failed** — `PerformanceBenchmarkTests(100 MB)`. Pre-existing, documented in that test's own header (LOH fragmentation, risk R-20); `docs/16-TEST-STRATEGY.md` prescribes excluding `Category=Performance` from routine runs. **Not a regression.** |

**Headline audit findings** (full detail in the audit document):

| ID | Finding | Severity |
|---|---|---|
| **A-03** | Python engine **could not start on Linux at all** — module-scope `ctypes.WinDLL("kernel32")`. **FIXED, see §2.** | was blocking |
| **C-01** | XLSX **silently samples**: proved 1001-row sheet → 5 rows with `success: true` and no warning field. | blocks XLSX sale |
| **B-05** | Normalized model has **no warnings channel** at all — root cause of C-01 and C-03. | high |
| **B-04** | No stable block IDs. | high |
| **D-04** | Jobs are in-memory `Task.WhenAll` — no persistence, queue, lease or retry. | high |
| **F-01** | **PyMuPDF is AGPL-3.0**; AGPL §13 network clause applies to a PDF SaaS. Zero licence discussion exists in the repo. | blocks launch |
| **E-05** | No tenancy, auth, or database of any kind (expected — single-user desktop app). | build from zero |

**Verified-good and reused as-is:** clean layering (`Application` → `Domain` only); no shell-injection
surface (A-02); chunking already operates on the structured model and never splits a table (C-06/C-07);
token baseline is a proper extracted-text baseline, not source bytes (C-10); the Claude estimate is
honestly labelled a `cl100k_base` proxy (C-13); DOCX page count correctly omitted rather than
fabricated (C-05); no document content in logs (E-06); password-protected files properly rejected (E-01).

### A-03 Linux portability fix ✅ (the one code change so far)

**File changed:** `src/AI.Document.Converter.Python/extractors/common.py`

**Why it was needed:** `dispatch.py:22` imports `extractors.common` unconditionally, and that module
executed `from ctypes import wintypes` and `ctypes.WinDLL("kernel32")` at import scope. On Linux
CPython, `ctypes.WinDLL` does not exist and `ctypes.wintypes` raises `ValueError`. **Every operation —
including `health_check` — would have failed to start in a Linux container.** No containerised worker
was possible until this was fixed, so it is decision-independent and was done first.

**What changed:** added an `IS_WINDOWS` guard; split the probe into
`_check_file_accessible_windows` (the original CreateFileW logic, unchanged) and a new
`_check_file_accessible_posix`. The POSIX branch deliberately does **not** report `fileLocked` —
POSIX has no mandatory locking, so that category is genuinely unreachable there and claiming it
would be a lie.

**Verification actually executed:**

| Check | Result |
|---|---|
| Real XLSX extract on Windows | `success: True`, sheet `Ledger` — unchanged |
| `fileNotFound` categorization | `errorCategory: "fileNotFound"` — unchanged |
| `health_check` | `success: true` — unchanged |
| Simulated Linux interpreter (`sys.platform="linux"`, `ctypes.WinDLL` deleted, `ctypes.wintypes` blocked) | **`extractors.common` and `dispatch` both import cleanly; `IS_WINDOWS=False`; `fileNotFound` still categorized; POSIX probe accepts a readable file; `docx_extractor` and `pptx_extractor` import** |
| `dotnet test --filter "Category!=Performance"` after the change | **145 passed, 0 failed** |

**Honest limitations of that verification:**
1. The Linux simulation could not complete `xlsx_extractor` import — numpy (via openpyxl) took its
   Linux branch and called `os.uname()`, which does not exist on Windows. That is a limitation of
   simulating Linux on Windows, and is in fact evidence the simulation was faithful; it is not a
   defect in the change. **Real Linux verification is still outstanding** and is deferred to the
   container work.
2. **The .NET integration tests do not exercise this change.** `RepoPaths.BundledPythonEnginePath()`
   points at the *PyInstaller build artifact* in `dist/` (built 2026-08-24), not at the source I
   edited (2026-09-08). Those 145 passes prove no .NET build regression; the Python change itself is
   proved by the direct `python dispatch.py` runs above. **The bundled exe is now stale relative to
   source** — a real drift hazard worth fixing in the container work.

---

### Conversion-integrity remediation ✅ (increment 2)

Stack decisions taken by the owner on 2026-09-08: **.NET 10 LTS**, **PostgreSQL local install**,
**replace PyMuPDF with a permissive library**. Neither the .NET 10 SDK nor PostgreSQL is installed
yet, so this increment covered the work that needs neither.

| Audit finding | Fix | Files |
|---|---|---|
| **B-05** no warnings channel | `ExtractionWarning` + `WarningCode` + `WarningSeverity`; `DocumentModel.Warnings`; `HasUnrecoveredContent` centralizes the "any Error ⇒ completed_with_warnings" rule | `Domain/Entities/ExtractionWarning.cs`, `Domain/Enums/WarningCode.cs`, `Domain/Enums/WarningSeverity.cs`, `Domain/Entities/DocumentModel.cs` |
| **B-04** no block IDs | `ContentBlock.BlockId`, stamped positionally (`s3-b1`) so re-extracting the same bytes yields the same IDs | `Domain/Entities/ContentBlock.cs`, `Python/extractors/model.py` |
| **B-06** unversioned model | `modelVersion` (2.0) + `engineVersion` (1.1.0) on every result | `Python/extractors/model.py`, `Domain/Entities/DocumentModel.cs` |
| **C-01** XLSX silent sampling | **Faithful mode is now the default** — all rows, or an explicit `unsupportedFile` failure above a 2,000,000-cell ceiling. Summary mode is opt-in and emits an **Error-severity** `sheetTruncated` warning | `Python/extractors/xlsx_extractor.py`, `Python/dispatch.py` |
| **C-02** merge provenance lost | `mergedCellRanges` recorded per section | `Python/extractors/xlsx_extractor.py` |
| **C-03** scanned PDF reports success | `noExtractableText` at Error severity, with page numbers | `Python/extractors/pdf_extractor.py`, `pptx_extractor.py`, `docx_extractor.py` |
| **C-04** silent empty formula cells | `formulaValueUnavailable` warning via a cheap read-only second pass | `Python/extractors/xlsx_extractor.py` |
| **C-09** chunk options unvalidated | `ChunkOptions.TryValidate`; `ChunkGenerator` rejects degenerate configs | `Domain/ValueObjects/ChunkOptions.cs`, `Application/Services/ChunkGenerator.cs` |
| **C-11** "0% reduction" on empty baseline | `ReductionPercentGpt4oStyle` is now `double?`; null renders **N/A** | `Domain/ValueObjects/TokenEstimate.cs`, `Wpf/ViewModels/DashboardViewModel.cs` |
| SR-INT-4 | image omissions surface as Info warnings | all three OOXML/PDF extractors |

**Measured evidence (executed):**

| Check | Before | After |
|---|---|---|
| 1001-row sheet, default mode | 5 data rows, `success: true`, **0 warnings** | **1000 data rows**, 0 warnings, `mode=faithful` |
| 1001-row sheet, summary mode | *(was the default)* | 5 rows **+ Error-severity `sheetTruncated`** naming 5 of 1000 rows |
| `sample.pdf` (page 3 has 0 chars — verified against pymupdf directly) | plain success | `noExtractableText` / **error**, `pagesWithoutTextCount=1`, `totalPages=3` |
| `image-sample.{pdf,docx,pptx}` | placeholder only | `imageOmitted` / info, and `HasUnrecoveredContent` stays **false** (no text lost) |
| `dotnet build` (incl. WPF) | 0 warnings, 0 errors | **0 warnings, 0 errors** |
| `dotnet test --filter "Category!=Performance"` | 145 passed | **160 passed, 0 failed** (116 unit + 44 integration) |

**Intentional behaviour change:** `ExcelDocumentProcessor_ExtractsSample_HandlesFormulasMergesAndSummarization`
asserted the old sampling and was rewritten as
`..._ReturnsEveryRowAndPreservesFormulasAndMerges`, now asserting the full 250 data rows. Four tests
were added for the v2 contract, PDF/image warnings, chunk validation and token honesty.

**The stale-bundle drift from increment 1 is resolved:** `scripts/build-python-engine.ps1` could not
be run as-is (PowerShell 5.1 treats pip's stderr as a fatal `NativeCommandError`), so PyInstaller was
invoked directly with the script's own arguments plus `--hidden-import extractors.model`. The
integration tests now exercise the v2 engine, which is what proves the Python→JSON→C# round trip of
warnings, block IDs and enums actually works.

**Known gap in this increment:** summary mode is reachable from the Python contract but **not yet
from .NET** — `ExtractRequestPayload` has no `mode` field. That is the safe default (the .NET host
can currently only obtain faithful extraction) but it means the summary-mode warning path is proven
at the Python level only, not through an integration test.

---

### PyMuPDF replacement ✅ (increment 3) — audit F-01 resolved for the runtime

**Replaced PyMuPDF 1.28.2 (AGPL-3.0 / Artifex) with pdfplumber 0.11.10 (MIT).** Full chain verified
permissive: pdfplumber MIT, pdfminer.six MIT, Pillow MIT-CMU, pypdfium2 BSD-3/Apache-2.0.
pypdfium2 was evaluated and **rejected — it has no table API at all.**

Full report and its limitations: `docs/saas/02-PDF-ENGINE-BENCHMARK.md`.

| Measure | PyMuPDF | pdfplumber |
|---|---|---|
| Cell-exact fidelity (6-fixture corpus) | 54/72 · **75%** · 4/6 shapes | 54/72 · **75%** · 4/6 shapes |
| Text strategy (both worse) | 14/72 · 19% | 14/72 · 19% |
| 60-page extract, median of 3 | 0.77 s | **0.98 s (~27% slower)**, identical 240 rows |
| Unruled tables (t2, t3) | 0/9 each | 0/9 each — pre-existing limit, not a regression |

**Files:** `Python/extractors/pdf_extractor.py` (ported), `Python/requirements.txt` (pymupdf removed,
licence policy documented), `Python/requirements-dev.txt` (new — pymupdf retained dev-only as
benchmark baseline and fixture generator), `scripts/generate-table-benchmark.py` (new),
`scripts/benchmark-pdf-tables.py` (new).

**Verified:** bundle rebuilt with `--exclude-module pymupdf --exclude-module fitz`;
`find -iname "*mupdf*" -o -iname "*fitz*" -o -iname "*pymupdf*"` → **0 matches**. Built exe exercised
directly (sample.pdf correct; password-protected → `unsupportedFile`; health_check OK).
**160 passed, 0 failed — no test changes required.**

**One real gap solved:** pdfplumber collapses "encrypted" and "corrupt" into one exception, but
BR-003 needs them distinguished. Resolved by probing with pdfminer before pdfplumber opens the file
(`PDFPasswordIncorrect` / `document.encryption` vs `PDFSyntaxError`), verified against both fixtures.

**Benchmark bug found and fixed mid-run:** the first Bengali fixture used PyMuPDF's `china-s` font,
which has no Bengali coverage and silently substituted CJK — the PDF contained zero Bengali
codepoints, making *both* engines appear to lose 3 cells. The generator now embeds a real Bengali
font and refuses to run without one. After the fix both engines score 9/9.

**Do not over-read the result.** The corpus is synthetic and simple; identical scores mean **it does
not discriminate between the engines**, not that they are equivalent on real documents. Scanner
output, multi-column layouts, spanning cells and generator quirks are unrepresented. No
table-fidelity claim should be published until real customer documents are measured.

---

### LICENSE and third-party notices ✅ (increment 4) — audit F-03

| Added | Purpose |
|---|---|
| `LICENSE` | Proprietary, all rights reserved. Includes an explicit no-warranty clause on **extraction completeness** — the absence of a warning is stated not to be a guarantee of completeness. |
| `THIRD-PARTY-NOTICES.md` | Every distributed component with verified version, licence and attribution. |
| `scripts/check-licences.py` | CI/release gate. |

**Licences were read from installed metadata (`importlib.metadata`, NuGet `.nuspec`), not from
memory.** 23 Python components and 12 .NET packages catalogued, with dev-only tools listed
separately as non-distributed.

**Two findings the inventory surfaced:**

1. **certifi is MPL-2.0** — the only weak-copyleft component that ships. Permitted in a proprietary
   product (obligations attach per-file, and we do not modify it), but it must be disclosed and its
   source location given. It now is.
2. **numpy and pandas ship although nothing in this project imports them.** PyInstaller pulls them
   in via an optional openpyxl import chain. Both BSD-3, so not a licence defect, but ~40 MB of
   unnecessary distribution surface. Listed in the notices because they *are* distributed; cleanup
   tracked below.

`scripts/check-licences.py` enforces three rules and **was verified to fail, not merely to pass**:

| Rule | Negative test |
|---|---|
| No AGPL in `requirements.txt` | Re-added `pymupdf` → exit 1, named the licence and the file |
| No `mupdf`/`fitz`/`pymupdf` artifact in the built bundle | Bundle scanned; 0 matches |
| `LICENSE` exists and has no unfilled placeholder | Placeholder present → exit 1; filled → exit 0 |

It also reports **"NOT BUILT - bundle not verified"** rather than passing silently when `dist/` is
absent, so "nothing found because nothing was built" can never read as a clean result.

**Distribution:** `LICENSE` and `THIRD-PARTY-NOTICES.md` are copied to the WPF build output (verified
present in `bin/Release/net8.0-windows/`), so they flow into the publish folder the installer
packages. `installer.iss` now shows the licence via `LicenseFile`.

**160 passed, 0 failed.** Build clean.

#### Two items needing the owner

1. ~~`LICENSE` placeholder~~ — **resolved 2026-09-08: `Copyright (c) 2026 Mehedi Hasan. All rights
   reserved.`** `check-licences.py` now passes (exit 0), and the filled copyright line is present in
   the shipped build output.
2. ~~`installer.iss` AppPublisher mismatch~~ — **resolved 2026-09-08.** Owner directed
   Publisher/Company to be **Mehedi Hasan only**, dropping Grameen Bank from product metadata.
   Three ownership surfaces now agree, verified against the built artifact:

   | Surface | Value |
   |---|---|
   | `LICENSE` line 2 | `Copyright (c) 2026 Mehedi Hasan. All rights reserved.` |
   | `scripts/installer.iss` `AppPublisher` | `Mehedi Hasan` |
   | Built assembly `CompanyName` / `LegalCopyright` | `Mehedi Hasan` / `Copyright (c) 2026 Mehedi Hasan. All rights reserved.` |

   A third surface was found while checking, which had not been raised before: `Company` and
   `Copyright` were never set in MSBuild, so every built assembly reported its `CompanyName` as the
   fallback `"AI.Document.Converter.Wpf"` with an **empty** `LegalCopyright`. Both are now set once
   in `Directory.Build.props` for the whole solution rather than per-project, so they cannot drift.

   **Correction to an earlier claim.** I previously said the installer would show Publisher and
   Copyright "side by side". That was wrong and was asserted without verification. Inno Setup's
   `LicenseFile` page shows only the licence text; `AppPublisher` never appears in the wizard at all
   — it goes to the uninstall registry entry and the Publisher column of Windows' Apps & features,
   seen only after install. The inconsistency was real, the described location was not. Inno Setup
   is not installed on this machine, so the installer could not be built and observed directly.

   **Still referring to Grameen Bank, deliberately unchanged:** `README.md`, `docs/01-BRD.md` and
   `CLAUDE.md` describe Grameen Bank as a target user and sponsoring organization. That is a
   statement about who the product is *for*, not about who owns it, and it was outside the
   "Publisher/Company only" scope of the instruction. `docs/01-BRD.md` calls it the "sponsoring
   organization", which may warrant a separate look now that copyright is personal.

---

### Warnings surfaced end to end, bundle slimmed ✅ (increment 5)

**The gap this closed.** Increment 2 added the warnings channel to the engine and the model, but
`ConversionResult` had nowhere to put it. The warnings were being produced, deserialized into
`DocumentModel`, and then **dropped before any caller saw them** - so a run with missing content
still looked like a plain success to the batch service and the UI. The channel existed but was not
connected at the far end.

| Item | Change |
|---|---|
| **Warnings reach the caller** | `ConversionResult.Warnings` + `HasUnrecoveredContent`; `ConversionService` populates both paths, merging extraction and chunking warnings |
| **C-08** oversized table | `WarningCode.TableExceedsChunkSize` was a **dead enum value** - defined but emitted by nothing. `ChunkGenerator` now returns `ChunkGenerationResult { Chunks, Warnings }` and raises it, anchored to the table's `BlockId` (the first real payoff of B-04) |
| **D-03** batch summary | `BatchSummary`/`BatchProgressUpdate` gain `WarningCount` and `CancelledCount`. The four buckets are mutually exclusive and sum to `ProcessedFiles`, asserted in a test |
| Desktop UI | Batch status message reports warnings and cancellations instead of folding them into the success count |
| Bundle size | **131 MB → 78 MB (40%)** by excluding numpy and pandas |
| Build script | `scripts/build-python-engine.ps1` **could not be run at all** under PowerShell 5.1; now fixed and verified end to end |

**Bundle slimming.** numpy and pandas were bundled although nothing in the project imports them
(flagged in increment 4's notices). `openpyxl` guards its numpy import in `compat/numbers.py` and
simply sets `NUMPY = False`, so dropping it is safe for the plain str/int/float cell values this
engine reads - verified after the change against all five formats plus tokenize, not assumed.

**Build script.** It failed on pip's first stderr line: under PowerShell 5.1 with
`$ErrorActionPreference = "Stop"`, a native command's stderr becomes a terminating
`NativeCommandError` even when the process exits 0. My first fix used `2>&1`, which **made it
worse** - redirecting a native command's stderr in 5.1 is itself what wraps each line in an
ErrorRecord. The working fix is an `Invoke-Native` helper that relaxes the preference around the
call and checks `$LASTEXITCODE`, which is the only reliable success signal for a native process.
Both pip and PyInstaller go through it. The exclusion flags are now in the script, so the slim,
AGPL-free bundle is reproducible rather than depending on my ad-hoc command line.

**Verified:** `dotnet build` clean; **166 passed, 0 failed** (121 unit + 45 integration, up from
160); licence gate green; script-built bundle contains 0 pymupdf/fitz/mupdf and 0 numpy/pandas
artifacts.

Six tests added: three for the oversized-table warning (including that an oversized *paragraph*
must not raise it, and that a within-size table raises nothing), two for the batch buckets
(including that an Info-severity note must **not** demote a file out of the success count), and one
integration test proving warnings survive Python → JSON → `DocumentModel` → `ConversionResult`
against the real bundled engine.

---

### Phase 1 — host scaffold and data model ✅ (increment 6)

Tooling blockers cleared by the owner on 2026-09-09: **.NET SDK 10.0.400** and **PostgreSQL 18.6**
(running, port 5432, scram-sha-256).

| Added | Purpose |
|---|---|
| `global.json` | Pins SDK **10.0.400** so the repo cannot silently build on 9.0.315 |
| `src/AI.Document.Converter.Contracts` | net10.0 — versioned worker request/result contract (empty so far) |
| `src/AI.Document.Converter.Persistence` | net10.0 — EF Core entities, `ConverterDbContext`, migrations |
| `src/AI.Document.Converter.Web` | net10.0 — ASP.NET Core host, Identity, cookie/CSRF config |
| `src/AI.Document.Converter.Worker` | net10.0 — background processor (template + DB reference) |
| `docs/saas/03-DEVELOPMENT-SETUP.md` | Setup, run, migrate, and the data-model rationale |

**Verified:** pinning the SDK does **not** break the desktop app — the whole solution builds clean
with net8.0/net8.0-windows and net10.0 side by side (0 warnings, 0 errors). Migration
`InitialSchema` generates 14 tables, both `xmin` row versions, and every intended index including
the tenant-scoped `IX_SourceDocuments_WorkspaceId_Sha256`.

**Design decisions worth knowing:**

- **`Persistence` is its own project** because both Web and Worker need the DbContext. Folding it
  into `Infrastructure` (net8.0, ships inside the desktop install) would drag EF Core and Npgsql
  into a desktop application.
- **No EF global query filters for tenancy.** A global filter is bypassable
  (`IgnoreQueryFilters`, raw SQL, `Find()` by key) and hides the security decision from the reader.
  Every tenant-owned row carries `WorkspaceId` and ownership is enforced at each call site, so it
  is visible in review and testable (SR-SEC-2). `WorkspaceId` is denormalized onto job items,
  artifacts and warnings so the worker can authorize without a join.
- **Leases, not locks** (`LeaseOwner`, `LeaseExpiresAtUtc`): an expired lease means the worker
  died and the item is claimable again. At-least-once delivery; exactly-once is not claimed.
- **`SourceDocument` deletes are `Restrict`** — removing a document must not erase the record that
  work was done on it. Bytes go by retention; the row stays.
- **UTC enforced by a model-wide value converter**, not by every call site remembering `UtcNow`.
- **No connection string in the repo.** `appsettings.Development.json` carries the shape only;
  the real value lives in .NET user secrets. `Program.cs` throws on startup rather than falling
  back to a default, because a silent fallback is how someone writes to the wrong database.

**Blocked on one owner action:** the database and application role do not exist yet, and creating
them needs a password I must not handle. See §4.

---

## 2. Not started

Phases 1–4 in full: web host, identity/workspaces, upload, persisted jobs and durable queue, results
workbench, history, export, plans/metering/billing, operator console, pilot benchmarks.

**No SaaS code exists yet beyond the engine/model work above.** No web project, no database, no
migrations, no authentication, no tenancy, no billing. Nothing in this repository should be
described as a working SaaS.

---

## 3. Corrections to the inventory's claims

Verified against source and executed runs, these documented claims do **not** hold as stated:

| Claim | Reality |
|---|---|
| "Configurable token-based chunks and overlap" | Chunking is correct, but **overlap/size are unvalidated** (C-09); `overlap >= size` degenerates. |
| "Intact tables" | True, but **no oversized-table warning** and no provider hard-limit check (C-08). |
| "Honest zero or negative reduction" | Negative is honest (C-12); **zero baseline renders "0%" rather than N/A** (C-11). |
| "Isolated file failures" | True in-batch (D-01), but the batch summary **cannot report cancelled or warning counts** (D-03). |
| "Large XLSX sheets summarized" | Presented as a feature; in practice it is **undisclosed data loss reported as success** (C-01). |
| "OCR is deferred; images become placeholders" | Placeholders exist (verified), but a fully scanned PDF still reports **plain success**, not `completed_with_warnings` (C-03). |

---

## 4. Blockers — awaiting decision

| # | Blocker | Blocks | Notes |
|---|---|---|---|
| **1** | **`adc_dev` database and `adc_app` role do not exist**, and the connection string is not set | Applying migrations; running the host | Requires choosing a password, which must not pass through this conversation or the repo. Two commands, in `docs/saas/03-DEVELOPMENT-SETUP.md` §2. |
| 2 | **PyMuPDF AGPL** | — | **RESOLVED** — replaced with pdfplumber (MIT). |
| 3 | Billing provider eligibility (Bangladesh seller) | Phase 3 only | Deferred by design; build a provider-neutral boundary first. Stripe merchant eligibility is not assumed. |
| 4 | No container runtime (Docker not installed) | Worker sandboxing (SR-SEC-5); real Linux verification of A-03 | Not blocking Phase 1 on Windows, but the engine's Linux support is still proven only by simulation. |

## 5. Next concrete task

**Once the database exists** (blocker 1): apply `InitialSchema`, then build the first vertical
slice of the customer journey — registration with email verification (local capture adapter,
clearly labelled), automatic personal-workspace creation, login, and a dashboard that lists the
signed-in user's own workspace and nothing else. That slice is what the first cross-tenant
authorization test will target.

Then: private upload with content-based validation (SR-SEC-1), persisted job before scheduling
(SR-JOB-1), worker picks it up under a lease, real extraction through the existing engine, result
preview, authorized download.

**Decision-independent work that can proceed in parallel** (in priority order):

1. **Plumb extraction mode through .NET** — add `mode` to `ExtractRequestPayload` so XLSX summary
   mode is reachable from the host and its Error-severity `sheetTruncated` warning is covered by an
   integration test. Today .NET can only obtain faithful extraction, which is the safe default but
   leaves that warning path proven at the Python level only.
2. **D-05** extract once and fan out. `GenerateChunksAsync` still re-extracts the document from
   scratch, spawning a second engine subprocess for a file `ConvertAsync` already parsed — 2x
   metered compute and latency per chunked conversion. Deferred until the SaaS pipeline exists,
   because the desktop flow deliberately calls the two operations independently.
3. **B-08** `createdDate` uses `os.path.getctime`, which on a server is upload time, not authorship
   time. Prefer embedded document metadata, else omit.
4. Real Linux verification of the engine once a container runtime exists (A-03 follow-up). The
   platform guard is proven only by simulation on Windows so far.

### Notes for whoever picks this up

- Run the routine suite as `dotnet test --filter "Category!=Performance"`. The Performance category
  contains a known-flaky 100 MB benchmark (see §1).
- After **any** Python change, run `scripts/build-python-engine.ps1` or the integration tests will
  silently test the previously built engine — they run the bundled exe, not your source. This bit
  once already (increment 1).
- The build script carries load-bearing `--exclude-module` flags (pymupdf/fitz for licensing,
  numpy/pandas for size). Do not drop them when editing it; `scripts/check-licences.py` catches the
  licensing half, nothing catches the size half automatically.
