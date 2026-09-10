# IMPLEMENTATION_STATUS — Cloud SaaS Edition

**Last updated:** 2026-09-08
**Baseline commit:** `d27e8a9` (desktop v1.0.0)
**Current phase:** **Phase 2 complete.** All five formats, history, search, reconvert, retention
and orphan reconciliation. Phase 3 (commercial) is blocked on a business decision, not code.

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

### Phase 1 - account and workspace journey (increment 7)

Database `adc_dev` created, `InitialSchema` applied, first vertical slice working end to end.

| Added | Purpose |
|---|---|
| `IEmailSender` + `DevFileEmailSender` | Dev capture writes mail to a file stamped "THIS EMAIL WAS NOT SENT". Registered **only** in Development; any other environment refuses to start rather than dropping verification mail on the floor |
| `WorkspaceProvisioner` | Personal workspace + Owner membership in the **same transaction** as the user |
| `WorkspaceAccessService` | The single place workspace access is decided |
| `AccountController` | Register, confirm, login, logout |
| `DashboardController` | Workspace-scoped recent jobs |
| `tests/AI.Document.Converter.Web.Tests` | Cross-tenant isolation against **real PostgreSQL** |

**Verified over HTTP against the running app, not only by unit test:**

| Step | Result |
|---|---|
| `POST /account/register` | 302 to pending; user + workspace + Owner membership in one transaction |
| Login before confirming | refused, with an explanatory message |
| Confirmation link from captured dev email | "Email confirmed" |
| `GET /dashboard` signed out | 302 to `/account/login?ReturnUrl=%2Fdashboard` |
| Login after confirming | 302 to `/dashboard`, showing only that user's workspace |
| `POST` with no antiforgery token | **HTTP 400** |

**Cross-tenant tests (SaaS 13.2) - 6/6 against real PostgreSQL** with two real users and two real
workspaces. Deliberately not the in-memory provider: tenancy is enforced by queries, and a query
only behaves as expected once a real database has translated it. The fixture creates a throwaway
database per run and **throws rather than skips** when no connection is configured - a silently
skipped isolation test is worse than none, because the suite still reports green.

Covered: resolving another tenant's workspace by id; a foreign workspace being indistinguishable
from a nonexistent one (otherwise any id is an existence oracle); listing returning only your own;
job queries excluding the other tenant; **artifact lookup by id alone finding the wrong tenant's
row**, which is exactly why `WorkspaceId` is denormalized onto `Artifact`; and membership
uniqueness.

**Two mistakes of mine, corrected:**

1. The password policy contradicted its own comment - it claimed length over character classes but
   left Identity's digit and uppercase rules on, so registration failed. Config now matches the
   stated reasoning.
2. The membership-uniqueness test went through EF's change tracker, which catches the duplicate
   before PostgreSQL sees it; that version would have passed with no constraint on the table at
   all. It now inserts with raw SQL and asserts SQLSTATE `23505`.

**A pre-existing flaky test was found and fixed.** `RunAsync_Cancelled_StopsStartingNewFiles` used
`CancelAfter(120)` racing a 20-file batch; it failed once under load, then passed on five
consecutive re-runs. Nothing in this branch touches that path. Cancellation is now triggered
deterministically by the work itself; verified over six consecutive runs.

**172 passed, 0 failed** (121 unit + 6 web + 45 integration). Build clean.

`adc_app` was granted `CREATEDB` so the fixture can create its throwaway database. That grants no
access to other owners' databases, no file reads and no role creation - verified `rolsuper` and
`rolcreaterole` are both false.

---

### Phase 1 - storage and validated private upload (increment 8)

| Added | Purpose |
|---|---|
| `IObjectStorage` + `LocalFileSystemObjectStorage` | Opaque, tenant-prefixed keys; atomic publish via write-to-temp-then-move |
| `StorageKeys` | The only place key layout is decided, so a caller cannot drop the tenant prefix |
| `UploadValidator` | Content-based validation (SR-SEC-1, closing audit E-02 and E-03) |
| `ConversionIntakeService` | Validate, store, persist job **before** anything is scheduled (SR-JOB-1) |
| `UploadController` + views | Multi-file upload with retention disclosed before processing |

**Validation now checks what the desktop app never did.** The desktop checked the file extension
and nothing else, which is fine for a file the user picked off their own disk and useless on a
public endpoint. Added: magic-byte signatures; OOXML container structure (a marker entry, so a
plain ZIP renamed `.docx` is refused); declared-versus-actual size; archive expansion ratio and
entry count, read from ZIP entry headers so a bomb is refused **without inflating it**; and
filename sanitization.

**Verified against the running application** by uploading two genuine samples plus a spoofed
`.pdf` (an MZ executable header) in a single request:

| Check | Result |
|---|---|
| `POST /upload` | 302 to dashboard |
| Job persisted | `Status=Queued`, 2 items - committed before anything could schedule it |
| Spoofed `.pdf` | **rejected**; the two genuine files still went through (BR-006) |
| Bytes on disk | tenant-prefixed keys under a root outside the webroot |
| Stored sizes | 3173 and 37040 bytes, matching the database rows |

**The database is the queue, deliberately.** The lease columns already on `ConversionJobItem` do
what a broker's visibility timeout would. This avoids the dual-write problem outright: the job
existing and the job being claimable are the *same commit*, so there is no window where work is
accepted but unscheduled. A broker can be introduced later without changing that ordering.

**20 validator tests**, each pinning a specific attack: executable renamed to `.pdf`, plain ZIP
renamed to `.docx`, password-protected OOXML (its own reason, not "corrupted"), a 60 MB
decompression bomb, entry-count flooding, a lying declared size, and path traversal in filenames.

**192 passed, 0 failed** (121 unit + 26 web + 45 integration). Build clean.

---

### Phase 1 - the worker: real conversion end to end (increment 9)

Uploaded documents are now genuinely converted by the **real Python engine** - the same one the
desktop app uses, not placeholder output.

| Added | Purpose |
|---|---|
| `JobClaimer` | Atomic lease claim via `FOR UPDATE SKIP LOCKED` |
| `JobProcessor` | Stage, extract, publish, roll up job status |
| `ConversionWorkerService` | Bounded-concurrency poll loop |
| `Worker/Program.cs` | DI, with the engine path as **server** configuration |

**Verified end to end against the running system:**

| Check | Result |
|---|---|
| `sample.docx` | `Completed`, 0 warnings |
| `sample.pdf` | **`CompletedWithWarnings`**, 1 warning - `noExtractableText`/`error`, page 3, with details |
| Job roll-up | `CompletedWithWarnings` - one item's warnings correctly prevent a clean-success claim |
| Markdown output | real front matter, real `# Sample PDF Document`, real `\| Item \| Value \|` table |
| Artifact rows vs files | 2 and 2 |
| Staging temp files | directory empty after the run (SEC-004) |

**Engine path is server configuration, never a customer setting** (audit A-04). The desktop's
`PythonEngineClient` is reused for its tested behaviour - no shell, no arguments, deadlock-safe
concurrent stream reads - but fed a fixed options value rather than anything a tenant influences.

#### Three real bugs found by running it

1. **Lease renewal invalidated its own row version.** `TryRenewLeaseAsync` updates via raw SQL,
   bumping `xmin`, so the next `SaveChanges` on the tracked entity failed a concurrency check
   against *this same worker's* change. Fixed by reloading after renewal.
2. **Job roll-up had an unhandled concurrency race.** Two items finishing at once both write the
   job row; one lost and threw. That is exactly what `ConversionJob.Version` is for - but catching
   a conflict is only useful if it is then handled. Now reloads and re-evaluates, and treats
   "another worker already wrote this exact status" as success rather than an error.
3. **Front matter leaked an internal identifier.** The engine records the path it is handed, so
   staging as `<itemId>.pdf` put a GUID in the customer's downloadable metadata instead of their
   filename. Now staged in a per-item *directory* under the document's own (already sanitized)
   name: the directory keeps the path unique, the filename keeps the metadata truthful.

**192 passed, 0 failed.** Build clean.

#### Known gap, found while testing

There is **no reconciliation between stored objects and database rows**. When rows were deleted
directly during testing, four artifact objects were left orphaned on disk with nothing referencing
them. Retention will walk rows, so orphans created by any future failure path would never be swept.
Not a defect in the code paths above - the intake failure path does clean up after itself - but a
real gap for production and not yet covered.

---

### Phase 1 - results view and authorized download (increment 10)

The journey now closes: a customer can see what was produced, what was not recovered, and take the
output away.

| Added | Purpose |
|---|---|
| `ResultsController` | Job detail, artifact preview, authorized download |
| Results views | Warning panel, per-file status, bounded preview |
| Security headers | CSP, `X-Content-Type-Options`, `Referrer-Policy` |
| `ArtifactAuthorizationTests` | 4 tests pinning the download query |

**Cross-tenant isolation verified on the live download path,** with two real accounts:

| Eve requesting Alice's URLs | Alice requesting her own |
|---|---|
| results page → **404** | results page → **200** |
| artifact preview → **404** | artifact download → **200** |
| artifact download → **404** | |

404 rather than 403, deliberately: a job that exists but belongs to someone else must be
indistinguishable from one that does not exist, or the URL becomes an existence oracle.

**Verified on the live response:** `Content-Disposition: attachment`, `Content-Type:
text/markdown`, `X-Content-Type-Options: nosniff`, and a CSP with `object-src 'none'` and
`frame-ancestors 'none'`. The downloaded bytes are the real conversion - front matter naming
`sample.pdf`, the heading, and the table.

**The results page surfaces what was lost**, not just that something happened: the
`CompletedWithWarnings` banner states plainly that this is *not* a complete extraction, and the
per-file panel shows "No text could be extracted from 1 of 3 pages... OCR is not enabled, so this
content was not recovered."

#### Deliberately not done yet

**The preview shows escaped Markdown source, not rendered HTML.** Rendering converted customer
documents as HTML would execute whatever markup a crafted source file carried through the pipeline
(SR-SEC-3). A rendered view needs a real sanitizer, and it is not claimed until it has one. The CSP
is defence in depth behind the encoding, not a substitute for it.

**196 passed, 0 failed** (121 unit + 30 web + 45 integration). Build clean, zero warnings - a
`CA2024` warning (`reader.EndOfStream` blocking inside an async method) was fixed rather than
suppressed, by reading one character past the preview limit to detect truncation.

---

### Phase 1 - retention and deletion (increment 11)

The upload page told customers source files are kept 24 hours and output 7 days. Nothing enforced
that. Now something does.

| Added | Purpose |
|---|---|
| `RetentionPolicy` | The disclosed windows, as configuration |
| `RetentionService` | Sweep + customer-initiated deletion |
| `RetentionSweepService` | Timed sweep in the worker |
| Tombstone (`ConversionJob.DeletedAtUtc`) | Stops deleted content being recreated |
| `POST /results/{id}/delete` | Customer deletion |
| `RetentionTests` | 8 tests against real PostgreSQL and real storage |

**The upload page and the sweep read the same `RetentionPolicy` object**, so the sentence shown to
the customer cannot drift away from what the sweep actually does.

**Ordering is deliberate throughout: bytes are deleted BEFORE the row is marked.** Marking first
would mean a crash in between leaves a row claiming the content is gone while it still sits in
storage - the one outcome that turns a deletion promise into a false statement. The reverse leaves
at worst a repeated delete, which is harmless because `DeleteAsync` is idempotent. Customer
deletion inverts this for the *tombstone* specifically, which is written first so a crash cannot
leave the job deletable-but-republishable.

**Verified against the live system:**

| Check | Result |
|---|---|
| Customer deletes a job | 4 stored objects → **0**; status `Expired`; tombstone set |
| **Queue replay after deletion** | items forced back to `Queued`, worker run → **"Skipping item: its job was deleted, so no content will be recreated"** |
| Content after replay | **0 files, 0 artifact rows** - nothing resurrected |
| Item outcome | `Cancelled`, not silently re-run |

That replay test is the SR-SEC-6 requirement that deletion must prevent queued or retried jobs from
recreating artifacts. The worker checks the tombstone twice - once before starting, and again
immediately before publishing, because extraction takes real time and a customer can delete midway.

**A wording bug the tests caught:** the policy description rendered a 24-hour window as "1 day".
Arithmetically identical, but vaguer than "24 hours" in a notice about when someone's files
disappear. Fixed the formatter rather than the assertion.

**204 passed, 0 failed** (121 unit + 38 web + 45 integration). Build clean.

#### Still open

The **orphaned-object gap from increment 9 is not closed.** The sweep walks rows, so an object with
no row pointing at it is invisible to it. That needs a reconciliation pass over storage keys, and
it is not written.

---

### Phase 1 - cancel and retry (increment 12)

| Added | Purpose |
|---|---|
| `JobLifecycleService` | Cancel and retry, both workspace-scoped |
| Worker `StoppedReasonFor` | One guard covering deleted *and* cancelled |
| Cancel/retry actions + UI | Only shown when the server would honour them |
| `JobLifecycleTests` | 9 tests against real PostgreSQL |

**Cancel (FR-037).** Queued items are flipped to `Cancelled` immediately, which is what actually
stops them - the worker's claim query only takes `Queued` rows, so a cancelled one is never picked
up. Items already extracting are **deliberately left to the worker**, which discards its own result
before publishing. Reaching in from outside would race the worker's write, and killing a running
extraction mid-write is how a half-published artifact happens (SR-JOB-3).

The message says what actually happened rather than a bare "cancelled": a file already being
converted may still finish extracting, and the user should not be surprised by that.

**Retry (FR-030).** Only failed items are requeued; anything that already succeeded is untouched -
re-running it would waste the customer's allowance and could replace a good artifact with a worse
one if the engine changed in between. `AttemptCount` is deliberately **not** reset, because it is
what bounds the retries. Refused for non-retryable failures (a corrupt document does not become
readable on a second look), past the attempt limit, and for deleted jobs.

**Verified against the live system:** cancelling a queued job returned 302, set the job to
`Cancelled` with a tombstone, and flipped both items to `Cancelled`. Running the worker afterwards
claimed **nothing** - attempt counts stayed at 0, no files appeared on disk, and the log shows no
items processed.

One thing worth recording about that check: a naive `count(*) from "Artifacts"` reported 2 and
looked like a failure. Those were **stale rows from the earlier deletion test** (`BytesDeletedAtUtc`
already set, 0 files on disk). The real signals - attempt counts and files on disk - were both
zero. Worth remembering that artifact *rows* outlive their bytes by design, so a row count is not a
measure of what exists.

**213 passed, 0 failed** (121 unit + 47 web + 45 integration). Build clean.

---

### Phase 1 - ZIP export with a manifest (increment 13)

| Added | Purpose |
|---|---|
| `ExportPackageBuilder` | Builds `markdown/`, `metadata/`, `manifest.json`, `README.txt` |
| `GET /results/{id}/export` | Streams the package |
| `ExportPackageTests` | 9 tests, manifest-focused |

**The manifest is the point, not the zipping.** SaaS §6 requires omissions and extraction gaps to
be visible in the export manifest - not only on a web page the customer may never revisit. A
package containing only Markdown would let an incomplete extraction travel onward looking complete.

Verified on a real download: the manifest carries per-file status, source SHA-256, the immutable
run id, and the full warning with its page number and details. It also states in prose that a
warned file is **"NOT a complete extraction"**, and - always, even on a clean run - that the
absence of warnings **"is not a guarantee that the parser recovered every fact from the source."**

**Two bugs found by running it:**

1. **The package was truncated to 48 bytes.** `ZipArchive` finalises its central directory with
   *synchronous* writes, and ASP.NET Core rejects synchronous IO on the response body. My comment
   claiming it "streamed as it is built" was simply wrong. It now builds into a temp file opened
   with `DeleteOnClose` and streams that back - bounded memory, and the file disappears when the
   response ends, including on a client disconnect. Allowing synchronous IO would have worked too,
   but lets a slow client hold a thread-pool thread for a whole download.
2. **Colliding names were opaque.** `sample.pdf` and `sample.docx` both reduce to `sample`, giving
   `sample.md` and `sample (2).md` - collision-safe but useless to someone who has just unzipped
   it. Now disambiguated by source extension: `sample.md` and `sample-pdf.md`. The counter remains
   for genuinely identical filenames.

**229 passed, 0 failed** (121 unit + 56 web + 45 integration). Build clean.

---

### Phase 1 - chunks in the SaaS pipeline (increment 14)

The last piece of the documented output contract. `ChunkGenerator` was already correct and tested
(structure-aware, never splits a table, warns on oversized ones) but nothing called it, so no
chunks reached the export.

| Added | Purpose |
|---|---|
| `ConversionPresets` | Named, server-side presets |
| Worker chunk generation | From the same extraction as the Markdown |
| `ChunkSet` artifact | One JSON object per document |
| Export expansion | `chunks/<name>/chunk_NNN.md` |
| Preset + chunk export tests | 8 tests |

**Chunk sizes never come from the client.** The form submits a preset *name*; the server decides
the numbers. A client-supplied chunk size would let anyone request a one-token size and turn a
single document into a hundred thousand chunks. An unknown name falls back to the default rather
than failing, because a stale bookmark should not cost someone their upload.

**Chunked from the same `DocumentModel` as the Markdown, in one pass.** The desktop re-extracts for
chunking (audit D-05), doubling the engine cost per document - unacceptable when that compute is
metered. This closes D-05 for the SaaS path.

**Stored as one JSON object, exported as individual files.** A 200-page document can produce
hundreds of chunks; a row and a stored object each would swamp both the artifact table and the
object store. The JSON also carries FR-021's per-chunk metadata (sequence, token count, overlap),
which a folder of `.md` files cannot. The export expands it into the `chunks/<name>/chunk_NNN.md`
layout an ingestion script expects, zero-padded so alphabetical order is reading order.

**Severity handling was subtle enough to get wrong:** chunk warnings join extraction warnings in
one list, but only **Error** severity demotes an item to `CompletedWithWarnings`. An oversized
table is a `Warning` - the table is intact, the chunk is merely large - so it must not demote the
item, or the distinction stops meaning anything.

**Verified end to end:** worker produced Markdown + ChunkSet artifacts for both samples; the export
contains `chunks/sample/chunk_001.md` and `chunks/sample-pdf/chunk_001.md`; the chunk content shows
the heading kept with its content and the table intact (FR-019/FR-020).

**230 passed, 0 failed** (121 unit + 64 web + 45 integration). Build clean.

---

### Phase 1 - dashboard with real batch progress (increment 15)

| Added | Purpose |
|---|---|
| Per-status counts in the dashboard query | Computed in SQL, not by loading every item |
| `GET /dashboard/status` | JSON for the poll |
| `wwwroot/js/dashboard.js` | Polls only while work is in flight |
| Real empty state and outcome badges | Every outcome named separately |
| `DashboardProgressTests` | 6 tests on the honesty rules |

**Progress is a count of finished files, never a percentage.** SaaS §10 forbids fabricated
percentages, and the reason is concrete: nothing knows how far through the *current* document the
engine is - extraction time depends on the document - so a bar creeping forward on a timer tells
the user something nobody actually knows, and they will believe it. The page says
`2 of 5 files finished`, which is a fact.

**Every outcome is a separate badge.** `1 complete` and `1 with warnings` are never folded into
`2 complete`; that would be the exact misreport the warnings channel exists to prevent.

**Queued is in flight but not "converting".** A queued item has not started, and showing it as
converting would display activity that is not happening.

**The poll stops when work stops.** A page left open overnight should not keep hitting the server.
Nothing is animated or estimated client-side; every number comes from the server, and a client with
scripting disabled sees identical information, just less often.

**Verified on real data:** with both items queued the endpoint reported `anyInFlight: true` and
`0 of 2 files finished`; after the worker ran, `anyInFlight: false` with `1 complete` and
`1 with warnings` as separate counts.

**236 passed, 0 failed** (121 unit + 70 web + 45 integration). Build clean.

---

### Phase 2 - five formats verified, history, search, reconvert (increment 16)

#### All five baseline formats, end to end (acceptance criterion §13.1)

Previously only PDF and DOCX had been proven through the SaaS pipeline. All five uploaded in one
request, converted, and exported:

| Format | Outcome | Warnings |
|---|---|---|
| `sample.docx` | Completed | none |
| `sample.pdf` | **CompletedWithWarnings** | `noExtractableText`/error |
| `sample.pptx` | Completed | none |
| `sample.txt` | Completed | none |
| `sample.xlsx` | Completed | `tableExceedsChunkSize`/warning, `formulaValueUnavailable`/warning |

**C-01's fix confirmed through the SaaS path**, which had only ever been proven at the Python level:
the large sheet produced **258 markdown table rows ending at row 250**, with no sampling notice. The
audit's original finding was 5 rows out of 1000 reported as success.

Three warning types fired in one run - **C-01, C-04 and C-08 all demonstrated in the real
pipeline**. `tableExceedsChunkSize` is Warning severity, so XLSX correctly stayed `Completed`: the
table is intact, the chunk is merely large.

Export contained 5 markdown files, 6 chunk files, and a manifest whose summary read
`completed: 4, completedWithWarnings: 1`.

#### History, search and filter

Filename search via parameterised `EF.Functions.Like` - wildcards are added by us, never taken from
the user, so a term containing `%` cannot widen the search. The workspace filter is applied first
and is not optional; search only narrows within it. Empty states distinguish "you have nothing"
from "your search matched nothing", because those need different next actions.

#### Reconvert (FR-044 superseded)

A reconvert creates a **new run**; it never overwrites the earlier one. Cloud runs are immutable, so
history stays intact and two results can be compared.

Verified live: the new job queued while the original stayed at `CompletedWithWarnings`, and **5
source documents were shared across 10 job items with only 5 stored objects** - no byte
duplication. Refused for deleted jobs, and for sources past their retention window with an
explanation rather than a silent failure.

#### One naming fix

With five files all stemmed `sample`, only the alphabetically-first got the bare name and the rest
were suffixed - which reads as arbitrary. Collisions are now detected up front so **every** member
of a colliding group is suffixed uniformly.

**241 passed, 0 failed** (121 unit + 75 web + 45 integration). Build clean.

---

### Phase 2 - orphan reconciliation (increment 17)

Closes the gap recorded in increment 9, and the last open item in Phase 2's "cleanup".

**Why it was needed.** Everything else reasons from rows outward: retention walks `SourceDocument`
and `Artifact` rows and deletes the bytes they name. That can never find an object whose row was
lost - a failed commit, a manual database edit, a restored backup older than the object store.
Those objects are invisible, retained forever, and paid for.

Reconciliation is the only process that reasons **from storage inward**, which makes it the only
one that can delete something nothing knows about. A false positive here is unrecoverable customer
data loss, so most of the design is refusals:

| Safeguard | Why |
|---|---|
| **2-hour minimum age** | Uploads write bytes *before* committing the row, so a file being accepted right now legitimately has no row. Without this, the sweep would race ordinary uploads. |
| **Per-run cap (500)**, abort not truncate | A bug making everything look orphaned would otherwise empty the store in one pass. Over the limit it deletes **nothing** and logs an error. |
| **Report-only mode** | The intended way to introduce it: run, read the logs, then let it delete. |
| **`.partial` files skipped** | A write in progress has no key and no row; deleting it would destroy an upload mid-flight. |
| **Rows with deleted bytes still count as references** | Such a row still *names* its key. If the byte deletion silently failed, that is retention's job to retry - claiming it here would hide the failure. |
| **6-hour interval, no run at startup** | An orphan is wasted storage, not an emergency. No reason to scan while a restarting worker is busiest. |

Logs carry counts and bytes only, never keys - a key embeds a workspace id (FR-035).

**8 tests, weighted towards what it must refuse:** a just-written object surviving, a `.partial`
file surviving, a row with deleted bytes protecting its object, the per-run limit deleting *nothing*
rather than a subset, and report-only sizing the reclaim without acting.

**Checked against the live store:** 15 rows referencing a key, 15 objects on disk, **0 orphans and
0 dangling references** - the system is currently consistent, which is the result you want from a
tool like this on a healthy system.

**249 passed, 0 failed** (121 unit + 83 web + 45 integration). Build clean.

---

### Increment: metering, credits and the provider-neutral billing boundary (Phase 3, provider-independent half)

**What works.** A conversion cannot be started without an allowance behind it, cannot be charged
twice, and cannot quietly consume credits it did not use. Verified end to end against a running
instance, not only in tests.

**Conversion credits (SR-BIL-4).** A deterministic unit, versioned as `credits-v1`, defined
separately from AI token estimates because a bill must be reproducible from the input alone while a
token count depends on a tokenizer we do not control. One credit per PDF page, per PPTX slide, per
3,000 characters of DOCX/TXT text, per 1,000 non-empty spreadsheet cells; minimum one per non-empty
file. Characters are counted as **Unicode scalar values** - the Bengali greeting used in the tests is
9 scalar values, 10 UTF-16 code units and 26 UTF-8 bytes, and billing on bytes would charge a Bengali
user roughly three times what an ASCII user pays for the same writing. All three counts are pinned in
a test so a "simplification" to `string.Length` is caught.

The Python engine now reports `billableSourceCells` for XLSX - counted in the SOURCE before merged
cells are expanded, formulas once - because the extracted model has already expanded merges and
would over-count. Measured 513 for `samples/sample.xlsx`.

**Pre-extraction estimate.** `CreditEstimator` prices a file from its CONTAINER, before any
extraction runs and before any Python subprocess starts - which is also what makes it safe to expose
as a quote, since pricing a file must not cost us the processing it describes.

It is deliberately **not** size-based. Measuring the sample corpus showed bytes-per-unit varying by
more than six times *within* one format - `sample.docx` is 182 bytes per character,
`image-sample.docx` is 1,196, because one embeds a picture. A size heuristic would refuse
small-but-heavy files and wave through large-but-empty ones. Instead: PPTX slide parts (exact), XLSX
declared used range (an upper bound, which is the safe direction), DOCX body text, TXT scalar values
(exact), PDF page-tree scan (best effort, never advertised as exact). Verified against ground truth
measured independently with pdfplumber/openpyxl/python-docx: 3 and 1 pages, 2 and 1 slides, and a
spreadsheet bound at or above its true 515 cells.

**Atomic reservation (SR-BIL-5).** `TryReserveAsync` applies the hold with a **single conditional
UPDATE** whose WHERE clause re-checks the balance, so there is no application-side window in which
two uploads both read the same remaining credits. Proven with ten concurrent 20-credit jobs against
an allowance of 100 on real PostgreSQL, each on its own connection: **exactly five granted**, balance
never negative.

Intake commits the job, its items and the hold in **one transaction**. Splitting them fails in both
directions - reserving first leaks a hold against a job that never existed, committing first leaves a
claimable job a worker can start before the allowance was checked.

**Settlement.** Charged only when output is durably published, for what the engine actually reported,
priced from the document's own text rather than from the generated Markdown - billing for our heading
hashes and table pipes would charge customers for our formatting. Idempotent by a unique index on the
ledger's `IdempotencyKey`, because at-least-once delivery makes a duplicate certain rather than
hypothetical. The unused portion of the hold is returned when the job finishes.

**Refusal, not overage.** A job beyond the allowance is refused with the shortfall named, and nothing
is left behind: the transaction rolls back and the already-written bytes are deleted. Automatic
monetary overages stay disabled.

**Every path that creates or re-runs work is metered** - upload, reconvert (which re-reads stored
bytes to price them), and retry (which takes a *new* hold covering only the items being re-run,
because the original was closed when the job first finished). Cancel returns the hold.

**Provider-neutral boundary (SR-BIL-1).** `IBillingProvider` names no provider. The registered
implementation is `NoBillingProvider`, which **refuses** rather than pretends - there is no
configuration flag that turns it into a working integration and no half-written provider code behind
an `if`. A stub returning fake success would grant entitlements nobody paid for, which is the worst
failure available to a billing system. Adopting a provider is one line in `Program.cs`.

Entitlements change **only** through verified provider evidence; there is no method that grants a
plan from a request body. Applied events are recorded under a unique `(provider, eventId)` index, so
a redelivered webhook cannot extend a subscription twice. A scheduled cancellation keeps access to
the end of the paid period and a failed payment does not remove it (SR-BIL-3) - both pinned by tests
using a fake provider.

**No prices anywhere.** `PlanCatalog` holds allowances and limits, deliberately no monetary figures,
and the billing page states "Pricing has not been set."

**Two defects found and fixed while building this**, both by tests rather than by inspection:

- A unique index on `UsageReservation.JobId` made a retry impossible. The invariant is *one open hold
  per job*, not one ever, so the index is now filtered on `Status = Held` - which also makes the
  `SingleOrDefault(Held)` lookups safe by schema rather than by hope.
- A ledger entry read `0 non-empty cells` for a document whose count was never reported. Stating a
  count we do not have, on the one record a disputing customer is shown, is wrong; an unreported
  count now reads `minimum charge - non-empty cells not reported by the extractor`.

The compiler caught a third: a controller field added without its assignment, which would have thrown
on the dashboard.

**Commands actually run.**

```
dotnet test AI.Document.Converter.sln -c Debug --filter "Category!=Performance"
  -> 121 unit + 144 web + 45 integration = 310 passed, 0 failed
dotnet build AI.Document.Converter.sln -c Release      -> Build succeeded, 0 warnings, 0 errors
python scripts/check-licences.py                       -> PASSED (exit 0)
dotnet ef database update                              -> 3 migrations applied to adc_dev
```

**Verified against a running instance** (registered a fresh account, confirmed it via the dev mail
file, signed in, uploaded `samples/sample.pdf`, ran the worker):

| Stage | Observed |
|---|---|
| After upload | `trial@v1`, included 50, **reserved 3**, settled 0; job item `EstimatedCredits = 3` |
| After worker | reserved **0**, settled **3**; reservation status `Settled` |
| Ledger | `3` credits, basis **`3 pages`**, policy `credits-v1`, plan `trial@v1` |

Pages render for a signed-in user: `/dashboard` 200, `/billing` 200, `/upload` 200; all three
redirect to login when anonymous.

**Limitations, stated plainly.**

- **There is no per-file quote before upload.** The true cost of a document is not knowable until its
  bytes arrive, so the upload page shows the balance and the counting rule instead of a figure it
  would have to guess at. Calling this a "preflight quote UI" would overstate it.
- The credit ratios are a documented deterministic unit, **not validated pricing**. They must be
  cost-tested before anything is sold.
- Settlement is authoritative and can exceed its hold, so a period balance can go negative if an
  estimate was low. That is not a monetary overage - the customer is never billed past their plan -
  but it means the estimate leaning high is load-bearing.
- The PDF page scan cannot see a page tree inside a compressed object stream. It is never reported as
  exact, and a hold that is too small is corrected at settlement.
- `SubscriptionService` is exercised only against a **test double**. No real provider integration
  exists or has been tested, because none is approved.
- Worker settlement is covered by tests with a **stubbed extractor** - deliberately, since what is
  under test is billing, not whether pdfplumber can read a table.


### Increment: operator console, audited support access and MFA (SaaS 5.11, SR-SEC-7)

**What works.** Operators can see what the service is doing across every workspace without seeing
anybody's documents; the one path that reveals customer-identifying detail demands a written reason
and records it first; and none of it is reachable without a second factor. Verified against a
running instance, including every refusal.

Full flow documented in `docs/saas/04-OPERATIONS.md`, which SR-SEC-7 requires as part of the control
rather than as a description of it.

**The console shows no documents.** SR-SEC-7 says operator status alone must not reveal them, so the
status pages return no filenames, no document content and no warning or error messages. That is a
real constraint, not a stylistic one: `quarterly-redundancies.xlsx` is itself the sensitive part, and
an error message can quote the text that caused it. What is shown instead is error **categories** (a
fixed vocabulary the system produces), counts, timings, attempt counts, lease state, retention
posture and per-workspace credit usage - enough to run the service, which does not require reading
anyone's files.

Two tests assert the absence rather than the presence, checked over the whole row so that adding a
field which happens to carry a filename fails them.

**Queue and retention signals.** Expired leases are counted separately: a lease outliving its worker
means the worker died holding it, which is not data loss (the item becomes claimable again) but is
the earliest visible sign of crashing workers. Retention distinguishes *awaiting* deletion from
**overdue** - bytes past their deletion time are a broken promise to the customer (SR-SEC-6), not a
backlog - with a grace window of one sweep interval so the number does not cry wolf every few
minutes. Both directions are tested: an expired lease on a *finished* item is not counted, and
something one minute past its deadline is not overdue.

**Audited inspection.** One job, addressed by id - no listing, no filename search, no way to sweep a
workspace. A reason of at least ten characters is mandatory, and the audit entry is committed
**before** the data is returned, so a failed write means the caller sees nothing. It is a POST, so a
job's details cannot be opened by following a link somebody sent. It still returns no document
content, and warning *messages* are withheld even here because they quote the document; codes and
locations are enough to diagnose.

The audit table has **no foreign key** to jobs or workspaces, deliberately: the record that someone
looked at a customer's files must outlive those files, and a cascade would erase the evidence at the
moment it becomes relevant. Pinned by a test that hard-deletes the job and asserts the entry
survives.

**MFA (SR-SEC-7).** TOTP via Identity's authenticator support - no SMS, because SIM swapping is
routine and offering a channel we would then warn people not to trust is worse than not offering one.

The policy requires the `Operator` role **and** proof that this session used a second factor
(`amr=mfa`), not merely an account capable of one. Role alone would mean a stolen password reaches
cross-tenant data, which is what the MFA requirement exists to prevent. The consequence - enabling
two-factor does not unlock the console in the session you enabled it from - is correct rather than a
defect, and both the two-factor page and the access-denied page say so.

The role is granted from the command line, never through the web UI: a page that hands out
administrative access is a privilege-escalation surface for something done a handful of times in a
system's life. `list-operators` prints each operator's two-factor state, because an operator without
one cannot use the console and that is otherwise invisible until they try. An operator cannot switch
their own second factor off while holding the role.

**Commands actually run.**

```
dotnet test AI.Document.Converter.sln -c Debug --filter "Category!=Performance"
  -> 121 unit + 157 web + 45 integration = 323 passed, 0 failed
dotnet build AI.Document.Converter.sln -c Release       -> Build succeeded, 0 warnings, 0 errors
dotnet ef database update                               -> OperatorAuditTrail applied to adc_dev
dotnet run --project src/AI.Document.Converter.Web -- grant-operator <email>
  -> "Granted the Operator role" plus the no-second-factor warning
```

**Verified against a running instance.** A fresh account was registered, confirmed, granted the
role, enrolled in TOTP (codes computed from the shared key), and driven through the whole gate:

| Attempt | Result |
|---|---|
| `/operator`, ordinary user | **redirected to access denied** |
| `/operator`, Operator role, password-only session | **redirected to access denied** |
| `/operator`, two-factor enabled but signed in *before* enabling it | **redirected to access denied** |
| Sign in again: password step | redirected to the two-factor challenge |
| Sign in again: authenticator code | signed in |
| `/operator` | **200** |
| Console HTML searched for any sample filename | **0 matches** |
| Inspect with an empty reason | refused, nothing recorded |
| Inspect with a reason | filename, size, status and warning codes shown |
| `/operator/audit` | the reason appears against the operator's email |

**Limitations, stated plainly.**

- ~~**Role changes are not in the operator audit trail.**~~ **Closed 2026-09-10** - see the
  role-change auditing increment below.
- ~~**No recovery codes.**~~ **Closed 2026-09-10** - ten codes are issued at enrolment; see below.
- **No pagination**: 50 jobs, 25 workspaces, 100 audit entries. Fine now, insufficient once the
  trail is long enough to matter.
- The `viewConsole` and `viewAuditTrail` audit actions are defined but **not written** - only
  inspection is recorded. Logging every page view would bury the entries that matter, and the
  console reveals no documents anyway.
- ~~The authorization policy is proven by the **live run above**, not by an automated test.~~
  **Closed 2026-09-10** by the HTTP test harness increment below: the policy now has 11 automated
  tests, and both of its halves were verified by mutation. Building those tests found that the role
  requirement had no independent coverage at all.
- MFA is implemented but **not enforced for ordinary customers**, only required for operators.


### Increment: HTTP test harness for authorization and tenancy

**What works.** The security wiring that previously had no automated coverage now has it, and every
one of those tests has been shown to FAIL when the thing it guards is removed. A passing security
test that has never been seen to fail is decoration, so each rule below was verified by mutation.

**Why this was the top priority.** The operator authorization policy lives in `Program.cs`. No
service-level test executes a line of it, so the only previous proof that a password-only operator
was refused was me running the app and trying it - which says nothing about whether the wiring
survives the next edit. That wiring is what stands between a stolen password and every tenant's
data.

**`WebAppFactory`** boots the REAL pipeline (`WebApplicationFactory<Program>`) against the fixture's
throwaway database, overriding only the connection string. Deliberately not a rebuilt host: an
imitation assembled in the test project would drift from `Program.cs`, and the drift would be
invisible precisely in the security configuration that matters most. `Program` is now
`public partial` for this reason, and the harness runs in Development because the dev-only email
sender throws otherwise - meeting that guard rather than weakening it.

Anti-forgery tokens are **scraped from the rendered forms** rather than disabled, so every POST in
these suites exercises real CSRF validation; one test asserts a token-less POST is rejected, so the
suite cannot silently pass with anti-forgery off. Authenticator codes are computed with a real
RFC 6238 implementation, so the two-factor login step is driven end to end rather than mocked.

**Operator authorization (11 tests).** Anonymous, ordinary user, operator without a second factor,
operator with two-factor enabled but signed in beforehand, wrong code, the challenge reached without
the password step, and the inspection and audit routes individually.

**Mutation-tested, and it found a real hole.** Removing the MFA requirement failed 3 tests, as
intended. Removing the **role** requirement failed **none** - every non-operator case in the suite
also lacked a second factor, so the MFA rule was refusing them and the role rule was never
independently exercised. A customer who happens to use two-factor is exactly who the role check must
stop. `AUserWithTwoFactorButNoRoleIsStillRefusedTheConsole` closes it, and re-running the mutation
now fails that test.

**Tenant isolation over HTTP (12 tests).** `CrossTenantIsolationTests` already proved the queries are
workspace-scoped; that is necessary and not sufficient, because a controller that forgot to pass the
filter would pass every one of them while serving another tenant's artifact. These drive the real
endpoints with a real signed-in cookie: results, preview, download, export, and the write paths
(cancel, retry, delete, reconvert). The write tests also assert the job was **not** modified - a 404
that still performed the action would be the worst of both. An owner control test proves the URLs
are right, so the 404s mean refusal rather than a typo. Foreign and nonexistent jobs return the same
status, so the response is not an oracle for which ids exist.

Mutating the results controller's workspace filter failed 4 of them. Export and the write endpoints
survived that particular mutation because they carry their own separate filters - defence in depth,
confirmed rather than assumed.

**A test bug this caught in itself:** the tenant seeder gave every tenant the same filename, so the
"intruder cannot see the owner's filename" assertion was passing against the intruder's *own*
document. Filenames are now unique per tenant. That is the same class of mistake as the role hole -
a test that looks like coverage and is not.

**Commands actually run.**

```
dotnet test AI.Document.Converter.sln -c Debug --filter "Category!=Performance"
  -> 121 unit + 180 web + 45 integration = 346 passed, 0 failed
dotnet build AI.Document.Converter.sln -c Release       -> Build succeeded, 0 warnings, 0 errors
```

Mutation runs (each reverted immediately afterwards):

| Mutation | Result |
|---|---|
| Drop `RequireClaim(amr, mfa)` from the operator policy | 3 failed |
| Drop `RequireRole(Operator)` from the operator policy | **0 failed** - gap found, test added |
| Drop `RequireRole` again, after adding the missing test | 1 failed |
| Drop the workspace filter from the results job lookup | 4 failed |

**Limitations, stated plainly.**

- Mutation testing here was **manual and selective**, not a tool run over the whole suite. Four
  mutations were tried on the rules I judged most load-bearing. Other tests in the project have not
  been checked this way, and some of them may be decoration too - the role hole is evidence that
  this is not a hypothetical worry.
- The harness covers authorization and tenancy. It does **not** cover the upload pipeline over HTTP
  (multipart, size limits, content sniffing), which is still only tested at the service level.
- These tests share the collection's PostgreSQL fixture, so they run sequentially with the rest.
  The web suite is now ~80 seconds rather than ~25.


### Increment: role-change auditing and two-factor recovery codes

Closes both stated limitations of the operator console.

**Role changes are now audited.** Operator status is what makes every other entry in the trail
possible, so becoming one leaves its own record (`grantOperatorRole` / `revokeOperatorRole`).
Without it the log showed what operators did but never how somebody became one - the first question
an investigation asks, and the easiest thing for an attacker with shell access to leave no trace of.

`ActorUserId` is now **nullable**, because a command-line action genuinely has no application user
behind it. The actor is recorded as `command line: user@machine` instead. Filling the id with
`Guid.Empty` would put a lie in the audit trail, and a trail that lies about who acted is worse than
one that admits it does not know. `TargetUserId` and `TargetEmail` were added so "who did it" and
"who it was done to" are separately answerable, and the audit view shows both.

**Recovery codes.** Ten, issued at the moment two-factor is switched on and shown once. Issuing
them later would mean most people never come back for them, and a second factor with no way back is
not a security control - it is a way to lose an account. Stored hashed, so `/security/recovery-codes`
can only report how many remain; regenerating invalidates the old set; disabling two-factor destroys
them along with the shared secret. Each code works once, and using one logs at warning level with
the number remaining, because burning one means somebody lost their authenticator - or somebody else
has their codes.

**A real bug the tests caught.** The recovery-code sign-in stripped hyphens, copied from the
authenticator path where the grouping is cosmetic. In a recovery code the hyphen is **part of the
stored value** - Identity generates `xxxxx-xxxxx` and compares exactly - so every valid code was
being silently rejected. Shipped as written, recovery would not have worked at all, and would have
been discovered by the person least able to afford it: an operator already locked out.

**Commands actually run.**

```
dotnet test AI.Document.Converter.sln -c Debug --filter "Category!=Performance"
  -> 121 unit + 193 web + 45 integration = 359 passed, 0 failed
dotnet build AI.Document.Converter.sln -c Release       -> Build succeeded, 0 warnings, 0 errors
dotnet ef database update                               -> AuditRoleChanges applied to adc_dev
```

13 new tests: 7 for recovery codes (redeem, single use, wrong code, consumes exactly one, no
password-step bypass, codes shown on enrolment, regeneration invalidates) and 6 for role auditing
(grant recorded, revoke recorded separately, the role really changes, an optional reason is stored,
a failed grant records nothing, and the entry appears through the same service the console renders
from).

The command is exercised directly via `InternalsVisibleTo` rather than by shelling out to
`dotnet run` - what is under test is the audit write, not process startup.

**Limitations, stated plainly.**

- The trail is append-only **by construction** - no service writes an update or delete - but nothing
  stops direct database access from editing it. Hash chaining or off-host shipping has not been
  built.
- An operator who loses **both** authenticator and recovery codes still needs database access. That
  is the floor without a separate identity provider.
- Recovery codes are not mutation-tested. The single-use property is asserted directly, which is the
  one that matters most, but I did not verify the suite fails if Identity's redemption were changed
  to be non-consuming.


## 2. Not started

**Superseded 2026-09-09.** This section previously read "No SaaS code exists yet beyond the
engine/model work above... Nothing in this repository should be described as a working SaaS." That is
no longer true and is corrected here rather than quietly deleted: the web host, identity and
workspaces, upload and validation, persisted jobs with leases, results, history, export, retention,
orphan reconciliation, and now metering and the billing boundary are all implemented with evidence in
§1.

Still not started:

- **Any real payment provider integration.** Blocked on approval; the boundary is ready (§1).
- **Pilot benchmarks** against a realistic corpus. The sample corpus is small and synthetic.
- **Cost testing of the credit ratios.** They are deterministic and documented, but no unit economics
  work has been done, so no price can responsibly be set from them yet.
- **Team collaboration, OCR, public API, SSO, private deployment.** Not implemented, and must not be
  advertised.

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
| ~~1~~ | ~~Database and role~~ | - | **RESOLVED 2026-09-09.** `adc_dev` + `adc_app` created, `InitialSchema` applied. The app role password is randomly generated, stored only in user secrets, and is not the `postgres` password. |
| 2 | **PyMuPDF AGPL** | — | **RESOLVED** — replaced with pdfplumber (MIT). |
| 3 | Billing provider eligibility (Bangladesh seller) | **Live checkout only** | The provider-neutral boundary, plans, credits, metering and subscription rules are **built and tested** (§1). What is blocked is one provider implementation and taking actual money. Stripe merchant eligibility is not assumed. |
| 4 | No container runtime (Docker not installed) | Worker sandboxing (SR-SEC-5); real Linux verification of A-03 | Not blocking Phase 1 on Windows, but the engine's Linux support is still proven only by simulation. |

## 5. Next concrete task

**Phase 3's provider-independent half is done** (§1). What remains in Phase 3 is genuinely blocked on
a business decision: SR-BIL-1 requires implementing ONE approved provider, and eligibility for a
Bangladesh seller is still unverified. Stripe merchant eligibility is **not** assumed; Paddle is a
candidate subject to approval; a local provider such as SSLCOMMERZ needs recurring auto-debit verified
separately, since one-time checkout does not prove it.

When a provider is approved, the work is: one `IBillingProvider` implementation, one line in each
`Program.cs`, a signed-callback endpoint, and end-to-end tests against the provider's sandbox. The
rules that surround it - verification-only entitlements, replay protection, cancellation semantics -
are already implemented and tested.

**Next, without needing that decision** (priority order):

1. **Extend the HTTP harness to the upload pipeline** - multipart handling, the size and count
   limits, and content-based validation are still only tested at the service level, so nothing
   catches a regression in the `RequestSizeLimit` or form-binding configuration.
2. **Cost-test the credit ratios** against measured processing time and storage, so a price can
   eventually be set from evidence rather than from the illustrative figures in the blueprint.
3. **Plumb extraction mode through .NET** - add `mode` to `ExtractRequestPayload` so XLSX summary
   mode is reachable from the host and its Error-severity `sheetTruncated` warning is covered by an
   integration test. Today .NET can only obtain faithful extraction, which is the safe default but
   leaves that warning path proven at the Python level only.
4. **D-05** extract once and fan out. `GenerateChunksAsync` still re-extracts from scratch, spawning
   a second engine subprocess for a file `ConvertAsync` already parsed - 2x metered compute per
   chunked conversion, which now costs the customer credits rather than just latency.
5. **B-08** `createdDate` uses `os.path.getctime`, which on a server is upload time, not authorship
   time. Prefer embedded document metadata, else omit.
6. Real Linux verification of the engine once a container runtime exists (A-03 follow-up). The
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
