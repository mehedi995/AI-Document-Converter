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
