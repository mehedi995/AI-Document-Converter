# 22 — Bug Tracker

**Project:** AI Document Converter
**Status:** Template — Phase 22 (Bug Management)
**Date:** 2026-08-23

---

No bugs logged yet — production coding (Gate 5) has not started. This document is
the template and process definition; entries are added once implementation and
testing begin (`docs/12-DEVELOPMENT-ROADMAP.md` Phase 11).

## Fields

Every bug entry uses this shape:

| Field | Description |
|---|---|
| Bug ID | `BUG-001`, sequential, never reused |
| Title | One-line summary |
| Description | What's wrong, in plain language |
| Severity | Critical / High / Medium / Low (impact if unfixed) |
| Priority | Critical / High / Medium / Low (urgency to fix) |
| Steps to Reproduce | Numbered, exact steps |
| Expected Result | What should happen |
| Actual Result | What actually happens |
| Environment | OS version, app version, sample file used (never attach a real confidential document — use `samples/` or a redacted repro file) |
| Status | `OPEN` / `IN PROGRESS` / `FIXED` / `RETEST` / `CLOSED` / `REOPENED` |
| Root Cause | Filled in once diagnosed |
| Fix | Description of the change (with a link/reference to the commit once one exists) |
| Test Result | Result of retesting after the fix |
| Fixed Version | Semantic version the fix ships in |

## Severity Guidance for This Project

- **Critical:** violates an offline/security guarantee (NFR-001, NFR-002, NFR-012,
  SEC-00x), causes data loss, or crashes the application.
- **High:** a supported file type fails to convert, or a batch operation corrupts/
  loses output.
- **Medium:** an extraction-quality issue (e.g., a table renders imperfectly) that
  doesn't lose data outright.
- **Low:** cosmetic UI issues, wording, minor performance variance within an
  acceptable range of the benchmark matrix.

## Log

| Bug ID | Title | Severity | Priority | Status | Fixed Version |
|---|---|---|---|---|---|
| _(none yet)_ | | | | | |

---

*This is the final document in the Phase 1–22 SDLC documentation set. Gate 5
(Production Coding) may begin only after explicit approval.*
