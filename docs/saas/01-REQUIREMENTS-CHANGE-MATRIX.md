# SaaS Requirements Change Matrix

Traces every requirement in `docs/03-SRS.md` (46 FR, 13 NFR, 7 BR, 6 SEC) against the authorized
cloud SaaS scope change. **Nothing here alters the desktop edition**, which keeps the original SRS
verbatim. Where a requirement is superseded, the superseding rule is stated — a requirement is never
simply deleted.

**Disposition key**

| Code | Meaning |
|---|---|
| **KEEP** | Carries into the SaaS unchanged in intent. |
| **ADAPT** | Same user value, different mechanism (desktop affordance → web affordance). |
| **STRENGTHEN** | SaaS demands more than the desktop requirement did; the old rule is insufficient. |
| **SUPERSEDE** | The cloud authorization explicitly overrides this constraint. |
| **DESKTOP-ONLY** | Meaningless in the cloud; retained for the desktop edition. |

---

## 1. Requirements superseded by the cloud authorization

These are the legacy constraints the scope change explicitly lifts. They remain binding on the
desktop edition and are **not** grounds for refusing SaaS work.

| ID | Original requirement | Disposition | SaaS rule that replaces it |
|---|---|---|---|
| NFR-001 | Operate fully offline; no outbound network calls | **SUPERSEDE** | The cloud edition is a network service by definition. The narrower guarantee survives: **the conversion pipeline itself makes no outbound call, and no customer content is sent to any third party.** |
| NFR-002 | Never transmit content to a third-party or cloud AI API | **KEEP, narrowed** | Still binding for the P0 pipeline: **no paid generative-AI call in the conversion path.** Runtime AI cleanup, embeddings and document chat remain future opt-in features with their own disclosure and data-transfer policy. |
| NFR-012 | No telemetry or analytics library | **ADAPT** | A service cannot be operated blind. Replaced by **SR-TEL-1**: operational telemetry carries IDs, timings, sizes and safe error categories only — never document content, and never sensitive filenames. |
| FR-032 | Settings screen incl. **Python executable path** | **SUPERSEDE (security)** | A tenant-settable executable path is arbitrary-code execution on a shared host (audit A-04). The engine path becomes **fixed server-side configuration, not a customer setting.** |
| SEC-005 | Validate the configured Python executable path | **SUPERSEDE** | Moot once the path is not configurable. Replaced by **SR-SEC-5**: the worker runs a pinned engine image with resource limits. |
| FR-033 | Persist user configuration between sessions | **ADAPT** | Per-user preferences move to the tenant database rather than a local JSON file. |
| — | "single-user, no authentication, no database" | **SUPERSEDE** | Identity, workspaces, memberships and a relational database are P0 (SaaS §5.2, §5.3). |

---

## 2. File import → browser upload

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-001/002/003 | File picker (single, multiple), drag-and-drop | **ADAPT** | Browser multi-file upload with drag-and-drop. Same user value; a WPF dialog cannot become a browser control. |
| FR-004 | Folder import, top level only | **ADAPT** | Browsers cannot read a folder path. Directory upload (`webkitdirectory`) or client-side ZIP expansion, **top level only**, matching the original limit. Must not be advertised as recursive. |
| FR-005 | Validate extension against the supported list | **STRENGTHEN** | Extension check is insufficient on a public endpoint (audit E-02). Adds **SR-SEC-1**: magic-byte/container signature validation, declared-vs-actual size check, archive expansion limits, and filename sanitization. |
| FR-038 | Verify engine availability at startup | **ADAPT** | Becomes a worker readiness/health probe and an operator-console signal, not a customer-visible startup check. |
| NFR-013 | Max batch 500 files / 5 GB, configurable | **STRENGTHEN** | Becomes **server-enforced, plan-versioned** limits. A client-supplied limit is not authorization. |

## 3. Extraction — conversion integrity

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-006/007/009/010 | PDF / DOCX / PPTX / TXT extraction | **KEEP** | Verified working (audit A-01, C-05). Reused. |
| FR-008 | XLSX sheets → Markdown tables | **STRENGTHEN** | See FR-041 below — the binding change. |
| **FR-041** | **A sheet over the threshold is summarized (header + sample rows)** | **SUPERSEDE — the single most important change** | Audit **C-01** proved 1000 rows silently become 5 with `success: true`. SaaS §6 forbids labelling a sample as complete extraction. New rule **SR-INT-1**: *faithful mode exports all supported data or fails with a clear supported-limit error. Sampling survives only as an explicitly selected, explicitly labelled summary derivative, never as the default and never reported as complete.* |
| FR-042 | Computed values, not formula text; merged value repeated | **KEEP + STRENGTHEN** | Keep the no-fabrication rule (verified, C-04). Add **SR-INT-2**: warn when cached formula values are absent; record merge span as provenance (C-02). Never execute macros or external workbook links to obtain a value. |
| FR-043 | Untextable PDF page marked with a placeholder | **STRENGTHEN** | Placeholder verified present (C-03), but the job still reports plain success. Add **SR-INT-3**: a structured warning plus a `completed_with_warnings` job status. |
| FR-039 | Images not extracted; placeholder inserted | **KEEP + STRENGTHEN** | Placeholder verified. Add **SR-INT-4**: omissions must appear in the results warning panel and the export manifest, not only inline. |
| FR-011 | Normalized internal document model | **STRENGTHEN** | Model v2 adds **stable block IDs, a `warnings[]` collection, `modelVersion` and `engineVersion`** (audit B-04/05/06) — the absence of a warnings channel is the root cause of C-01 and C-03. |
| FR-040 | TXT BOM detection, else UTF-8 | **KEEP** | Verified correct, including throwing rather than substituting replacement characters. |
| BR-003 | Password-protected files clearly rejected | **KEEP** | Verified working for both PDF and OOXML (E-01). |
| BR-007 | English assumed; other languages best-effort | **KEEP, restated honestly** | Must be a published limitation, not a silent one. OCR and Bengali handling stay P1 and **must not be advertised until validated**. |

## 4. Markdown, metadata and tokens

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-012 | Structured Markdown preserving headings/tables/links/references | **KEEP** | Verified. Reused. |
| FR-013 | YAML front-matter | **STRENGTHEN** | Add source hash, engine version, preset, tokenizer version, run ID and warning summary (B-07) so an artifact is reproducible and attributable. |
| FR-014/015 | Show original and converted token counts, and reduction | **KEEP** | Baseline is already a documented extracted-text baseline, not source bytes (**C-10 verified**) — this already satisfies SaaS §6. Negative reduction already permitted (C-12). |
| FR-015 | Reduction may be zero or negative | **KEEP + fix** | **SR-INT-5**: an empty baseline must render **N/A**, not "0%" (C-11). |
| FR-016 | Two tokenizer profiles simultaneously | **KEEP** | Reused. |
| FR-017 / BR-005 | Label all counts as estimates | **KEEP — now a UI obligation** | The engine is already honest (**C-13 verified**): `cl100k_base` is an acknowledged Claude proxy. The new web UI must name the estimator and model configuration explicitly. No guaranteed savings percentage may ever be published. |

## 5. Chunking

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-018 | Configurable chunk size and overlap | **KEEP + fix** | **SR-INT-6**: validate size > 0 and overlap < size at the API boundary (audit C-09 — currently unvalidated and degenerate when violated). |
| FR-019 / BR-004 | Never split a table across chunks | **KEEP** | Verified (C-07). |
| FR-020 | Keep a heading with its content | **KEEP** | Verified (C-06). |
| FR-021 | Chunk metadata identifies source and sequence | **STRENGTHEN** | Add run ID, block IDs and source references, enabled by model v2. |
| — | *(new)* | **ADD SR-INT-7** | Oversized tables: preserve the intact canonical artifact, show a size warning, and block or flag exports that violate a provider's hard limit (C-08). |
| — | *(new)* | **ADD SR-INT-8** | Prove coverage against the canonical extracted model: excluding intentional overlap, chunking must not silently drop supported extracted content. This proves formatting fidelity only — **not** that the parser recovered every fact from the source. |

## 6. Batch, jobs and reliability

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-022 | Process multiple files in one batch | **KEEP** | |
| FR-023 | Live batch progress counters | **ADAPT** | Server-pushed progress; stages must reflect **real work**, never a fabricated percentage. |
| FR-024 | UI thread not blocked | **ADAPT** | Irrelevant server-side; replaced by async request handling and a separate worker process. |
| FR-025 / NFR-011 | Bounded configurable parallelism | **KEEP + STRENGTHEN** | Bounded per worker **and** per tenant, so one tenant cannot starve another. |
| FR-037 | Cancel an in-progress batch | **KEEP** | Semantics verified sound (D-02) and ported: cancellation prevents unstarted work and safely ends active work. |
| FR-045 | Independent per-file pipeline status | **STRENGTHEN** | Becomes the explicit job state machine: validating, queued, extracting, formatting, chunking, exporting, completed, completed_with_warnings, failed, cancelled, expired. |
| BR-006 | One failed file does not stop the batch | **KEEP** | Verified (D-01). |
| — | *(new)* | **ADD SR-JOB-1** | Persist the job **before** scheduling it; outbox or equivalent recovery-safe scheduling. Replaces in-memory `Task.WhenAll` (audit **D-04**), which SaaS §7 rules insufficient. |
| — | *(new)* | **ADD SR-JOB-2** | Leases, heartbeats and bounded retries to recover abandoned work. Queue delivery is **at least once**; duplicate delivery must not publish conflicting results or charge twice. Exactly-once execution is **not** claimed. |
| — | *(new)* | **ADD SR-JOB-3** | Staged artifact publication — an incomplete file is never downloadable as a successful result. |
| — | *(new)* | **ADD SR-JOB-4** | Batch summary distinguishes **successful, warning, failed and cancelled** (audit D-03 — cancelled and warning counts are currently missing). |
| — | *(new)* | **ADD SR-JOB-5** | Extract once and fan out to markdown/chunks/export. Audit **D-05**: `GenerateChunksAsync` currently re-extracts from scratch, doubling metered compute per chunked conversion. |

## 7. Export and history

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-026 | Result summary with token figures | **ADAPT** | Becomes the results workbench (source/output side by side, tabs, warning panel). |
| FR-027/028/046 | Individual Markdown, batch ZIP, `metadata/` JSON | **KEEP** | Reused; add an immutable run/version identifier to the manifest. |
| FR-036 | Avoid silently overwriting a different source's output | **ADAPT** | Filesystem collision logic is replaced by tenant- and run-scoped object-storage keys. Audit D-06 notes the current resolver never consults the filesystem — moot under the new scheme. |
| FR-044 | Re-conversion overwrites prior output | **SUPERSEDE** | Cloud runs are **immutable and versioned**. A reconvert creates a new run; it does not overwrite history. |
| BR-002 | Never overwrite the source file | **KEEP** | Structurally guaranteed: source bytes are write-once in object storage. |
| — | *(new)* | **ADD SR-EXP-1** | Conversion history with search/filter, authorized re-download, reconvert, explicit deletion, and expiration states. |

## 8. Errors, logging, security

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| FR-029 | Categorized errors | **KEEP + STRENGTHEN** | 9 categories verified working end-to-end (D-07). Add a **retryability flag and a correlation ID**. |
| FR-030 | Retry transient errors | **STRENGTHEN** | Retry a failed **export** without rerunning a successful extraction. |
| FR-031 | No stack traces to the business user | **KEEP + STRENGTHEN** | Also hide Python paths, executable settings, infrastructure jargon and database details from all customer flows. |
| FR-034 | Serilog operational logging | **KEEP** | Becomes structured logs with IDs and timings. |
| FR-035 / NFR-006 | Never log document content | **KEEP + STRENGTHEN** | Verified currently honoured (**E-06**). Extend to treat **filenames as potentially sensitive** server-side, and to cover analytics and email. |
| SEC-001 | No content to any network endpoint | **ADAPT** | Reinterpreted: content crosses the network to *our own* service by design. Still binding — **no third-party endpoint, and never a silent external token-count call.** |
| SEC-002 / NFR-005 | Path validation, no traversal | **ADAPT** | Desktop path validation verified sound (E-04) but irrelevant server-side. Replaced by tenant-scoped object keys with no raw path exposed. |
| SEC-003 | No hard-coded secrets | **KEEP** | Extended to environment-specific configuration with no real secrets committed. |
| SEC-004 | Temp files controlled and cleaned up | **KEEP + STRENGTHEN** | Cleanup must survive **failure and restart**. |
| SEC-006 | No argument/command injection to the engine | **KEEP** | Verified genuinely safe (**A-02**): `UseShellExecute = false` with no process arguments at all. |
| NFR-009 | Handle corrupt/locked files without crashing | **KEEP** | Verified. |

### New security requirements with no desktop antecedent

| ID | Requirement |
|---|---|
| **SR-SEC-1** | Content-based file validation: signature/container checks, declared-vs-actual size, archive expansion ratio and entry-count limits, page/cell/resource limits, safe filenames (audit **E-02, E-03** — currently absent). |
| **SR-SEC-2** | **Tenant isolation on every record and object operation.** Workspace membership resolved server-side; a client-supplied `TenantId` is never authorization. Applies to queries, updates, job messages, cache keys, export manifests and download access. Cross-tenant integration tests with two real users. |
| **SR-SEC-3** | Sanitize Markdown/HTML preview; disable scripts, unsafe URLs and third-party image loading; safe text rendering for filenames. Audit **E-07**: this is a brand-new XSS surface with no existing defence to inherit. |
| **SR-SEC-4** | Artifacts private and outside any public webroot; authenticated download gate preferred over signed URLs. |
| **SR-SEC-5** | Restricted worker execution environment with CPU, memory, runtime, disk and network limits (audit **A-06** — only a wall-clock timeout exists today). |
| **SR-SEC-6** | Retention and deletion: proposed defaults 24h source bytes, 7d output bytes, **disclosed before upload**. Deletion must prevent queued or retried jobs from recreating artifacts; restored backups must reapply tombstones before serving. |
| **SR-SEC-7** | Customer document inspection by support requires a documented, narrowly scoped, audited access flow. Operator status alone must not reveal documents. MFA for production administrators. |
| **SR-SEC-8** | **Prohibited claims.** Do not claim offline cloud processing, end-to-end encryption while the server decrypts for parsing, regulatory certification, or any unestablished compliance. Do not advertise OCR, team collaboration, public API, SSO or private deployment before they are implemented and verified. |

## 9. Performance and architecture

| ID | Original | Disposition | SaaS behaviour |
|---|---|---|---|
| NFR-003 | 100 MB document without freezing the UI | **ADAPT** | No UI to freeze. Becomes a worker throughput/latency benchmark. **Note audit SR-11**: the 100 MB benchmark already fails intermittently under memory pressure — more serious for a long-lived worker than for a desktop session. |
| NFR-004 | Extensible to OCR / formats / providers | **KEEP** | |
| NFR-007 | SOLID, layered, MVVM | **KEEP (MVVM → DESKTOP-ONLY)** | Layering verified genuine and is the migration's main asset. MVVM applies to WPF only. |
| NFR-008 | Usable by a non-technical business user | **KEEP** | Carried into the design specification. |
| NFR-010 | Deployable on enterprise Windows | **DESKTOP-ONLY** | Replaced by container deployment for the cloud edition. |
| — | *(new)* | **ADD SR-ARCH-1** | The Python engine **must run on Linux**. Audit **A-03**: it currently cannot start at all on Linux — module-scope `ctypes.WinDLL("kernel32")` in `extractors/common.py`. Top-priority migration task. |

## 10. Commercial requirements (no desktop antecedent)

| ID | Requirement |
|---|---|
| **SR-BIL-1** | Provider-neutral billing boundary; implement **one** approved provider first. Seller may operate from Bangladesh — Stripe merchant eligibility is **not** assumed. Paddle is a candidate subject to approval; a local provider such as SSLCOMMERZ needs recurring auto-debit verified separately (one-time checkout does not prove it). Manual renewal is an acceptable, clearly labelled initial local flow. |
| **SR-BIL-2** | Only **verified server-side provider evidence** grants paid entitlements. Never a browser redirect, query-string status, or client-submitted amount. Signature validation, raw-body handling, amount/currency matching, replay protection, unique event processing. |
| **SR-BIL-3** | Subscription lifecycle: trialing, active, past_due, cancellation scheduled, cancelled/expired. Handle duplicate, delayed and reordered events without regressing newer state. Periodic reconciliation against the provider. Cancelling at period end is **not** immediate entitlement removal. |
| **SR-BIL-4** | Conversion credits as a deterministic processing unit, **separate from AI token estimates**. Candidate baselines for benchmarking: PDF pages, PPTX slides, 3,000 Unicode scalar values per DOCX/TXT, 1,000 non-empty source cells per XLSX/CSV, rounded up per non-empty file. Count original source cells **before** merged-cell expansion; count a formula cell once. Characters are not UTF-8 bytes and not UTF-16 code units. |
| **SR-BIL-5** | Atomic allowance reservation on job acceptance; concurrent jobs cannot overspend. Ledger entries carry idempotency keys and plan/period policy versions. Release reservations for rejected, failed or cancelled unfinished work. System retries, repeated webhooks, re-downloads and export retries must not duplicate charges. Never exceed the accepted quote — pause and requote. Automatic monetary overages disabled initially. |
| **SR-BIL-6** | Plans, allowances and prices live in **server-controlled versioned configuration**. Figures in any blueprint are illustrative and must not be published as live commercial terms. Plan changes must not retroactively rewrite an existing usage ledger. |

## 11. Licensing (no desktop antecedent — audit F-01)

| ID | Requirement |
|---|---|
| **SR-LIC-1** | **PyMuPDF 1.28.2 is AGPL-3.0 or an Artifex commercial licence.** AGPL §13's network clause is triggered by serving PDF conversion over a network. Subprocess or container separation does **not** by itself discharge the obligation. A documented decision is required before commercial launch: (a) purchase the Artifex commercial licence, (b) replace with a permissively licensed PDF library, or (c) release the service source under AGPL. **Blocks launch of PDF conversion, not development.** |
| **SR-LIC-2** | Preserve attribution and licence files for all dependencies and ship third-party notices. Audit **F-03**: the repository currently has no `LICENSE` file, no attribution file, and zero licence discussion across 22 documents. |
