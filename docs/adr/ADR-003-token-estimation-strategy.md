# ADR-003 — Token Estimation Strategy

**Status:** Accepted
**Date:** 2026-08-23

## Context

`docs/03-SRS.md` FR-014–FR-017 require displaying original/converted token counts and
a reduction percentage, for at least a Claude-style and a GPT-4o/Azure-OpenAI-style
estimate, always clearly labeled as an estimate (CLAUDE.md Section 16 explicitly
forbids claiming providers share a tokenizer).

`tiktoken` (already a listed dependency) ships several named encodings. Two are
relevant:

- `o200k_base` — used by GPT-4o and newer OpenAI models.
- `cl100k_base` — used by GPT-3.5/GPT-4 (older) and, informally, the closest
  publicly available approximation for Claude's token granularity. Anthropic does
  not publish a downloadable tokenizer, and any exact/official Claude token count
  requires a network call to Anthropic's API — which is disallowed offline
  (NFR-001, NFR-002).

## Decision

- **GPT-4o/Azure OpenAI-style estimate:** use `tiktoken`'s `o200k_base` encoding
  directly. Label in the UI as "GPT-4o-style estimate."
- **Claude-style estimate:** approximate using `tiktoken`'s `cl100k_base` encoding,
  since no official offline Claude tokenizer exists. Label in the UI explicitly as
  "Claude-style estimate (approximated — Anthropic's exact tokenizer is not
  available offline)."
- Both estimates are computed and displayed **simultaneously** (SRS FR-016), not
  behind a single-provider selector, so the user always sees the caveat alongside
  the number.
- The chunking algorithm (FR-018) uses the GPT-4o-style (`o200k_base`) count as its
  sizing measure, since it is the exact encoding rather than an approximation; this
  is documented in the chunk metadata so downstream tooling knows which measure was
  used.
- This mapping is implemented once, in a single Infrastructure component
  (`ITokenEstimator`), and the UI/Application layers never reference `tiktoken`
  encoding names directly.

## Consequences

- If Anthropic later publishes an offline tokenizer, or the estimate should be
  labeled differently, only `ITokenEstimator`'s implementation and its UI label
  strings change — no ripple into the chunking or extraction pipeline.
- Users are never told a number is "the Claude token count" without qualification,
  satisfying FR-017 and CLAUDE.md Section 16.
- Both encodings are pure Python (`tiktoken`) computations; no additional runtime
  dependency beyond what is already listed in `docs/01-BRD.md` Section 9.

## Addendum (2026-08-24) — `tiktoken` is not offline out of the box

Implementing Phase 6 surfaced two real problems, both fixed before release, not just
noted:

1. **`tiktoken` does not ship its vocabulary files.** By default it downloads
   `o200k_base`/`cl100k_base` over HTTPS on first use and caches the result under
   `%TEMP%/data-gym-cache`. On a genuinely offline target machine with no
   pre-existing cache, that first token-estimation attempt would fail (or hang) — a
   silent violation of NFR-001/002 that would only surface once a user actually
   tried the feature, not during any earlier check. **Fix:** the two vocabulary
   files were fetched once on a dev machine and checked into
   `src/AI.Document.Converter.Python/tiktoken_cache/`; `tokenizer.py` sets
   `TIKTOKEN_CACHE_DIR` to point at this bundled folder (resolved via
   `sys._MEIPASS` when frozen) before `tiktoken` is ever imported, so it always
   reads the local file and never attempts a network call. Verified by hiding the
   OS-level cache directory entirely and confirming `tokenize` still succeeds
   without recreating it.
2. **PyInstaller's static analysis cannot see `tiktoken`'s encoding registrations.**
   `tiktoken` discovers `o200k_base`/`cl100k_base` via `pkgutil` plugin scanning
   over the `tiktoken_ext` namespace package, not a direct `import` statement — a
   pattern invisible to PyInstaller's import graph. Without an explicit
   `--hidden-import tiktoken_ext.openai_public`
   (`scripts/build-python-engine.ps1`), the bundled exe fails at runtime with
   `Unknown encoding o200k_base` despite `tiktoken` itself being bundled correctly.

Both are now tracked as a permanent risk-register entry
(`docs/18-RISK-ASSESSMENT.md` R-18) specifically because they are the kind of thing
a routine `tiktoken` version bump could silently reintroduce (a new tiktoken release
could change its cache-file hash or add a new namespace-plugin module) without any
compile-time signal — only a real, offline-simulated test catches it.
