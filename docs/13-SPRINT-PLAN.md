# 13 — Sprint Plan

**Project:** AI Document Converter
**Status:** Draft — Phase 13 (Sprint Plan) — Gate 3
**Date:** 2026-08-23

---

Twelve sprints, one per Development Roadmap phase (`docs/12-DEVELOPMENT-ROADMAP.md`),
sized for a single mid-level developer. Durations are relative sizing (S/M/L), not
calendar commitments — adjust to actual team capacity when scheduling begins. Each
sprint's Definition of Done is CLAUDE.md Section 40, applied to that sprint's scope.

---

## Sprint 1 — Project Foundation

- **Sprint Goal:** A running application shell with logging, configuration, and a
  working Python subprocess round-trip.
- **Size:** L
- **Tasks:** TASK-001 through TASK-013 (see `docs/14-TODO.md`).
- **Dependencies:** Gate 2 approval.
- **Expected Output:** Buildable solution; Settings screen functional; bundled
  Python executable responds to a stub JSON request.
- **Acceptance Criteria:** Phase 1 exit criteria (`docs/12-DEVELOPMENT-ROADMAP.md`).
- **Testing Requirements:** Unit tests for settings persistence and the Python-path
  validator (SEC-005); a manual smoke test of the subprocess round-trip.
- **Definition of Done:** Builds cleanly; unit tests pass; no known blocking defect;
  logging visibly captures startup/shutdown.

## Sprint 2 — File Import

- **Sprint Goal:** Users can get files into the app in every supported way.
- **Size:** M
- **Tasks:** TASK-014 through TASK-021.
- **Dependencies:** Sprint 1.
- **Expected Output:** Conversion list populated via picker, multi-select,
  drag-and-drop, and folder import; unsupported files rejected with clear messaging;
  batch ceiling enforced.
- **Acceptance Criteria:** AC-001–AC-005, AC-031.
- **Testing Requirements:** Unit tests for validation logic; manual UI test of
  drag-and-drop (not practically unit-testable in WPF).
- **Definition of Done:** All listed ACs pass; error handling implemented per FR-005;
  no known blocking defect.

## Sprint 3 — Document Extraction

- **Sprint Goal:** Every supported format extracts into the normalized document
  model.
- **Size:** L (largest sprint — five processors plus the Python-side extractors)
- **Tasks:** TASK-022 through TASK-029.
- **Dependencies:** Sprint 1 (Python engine), Sprint 2 (files to extract).
- **Expected Output:** All five `IDocumentProcessor` implementations working against
  `samples/`.
- **Acceptance Criteria:** AC-006–AC-010, AC-027–AC-029.
- **Testing Requirements:** Integration tests per processor against sample files
  (`tests/IntegrationTests/DocumentProcessing`); unit tests for encoding detection
  and the placeholder/summarization rules.
- **Definition of Done:** All listed ACs pass against every sample file; extraction
  failures map to FR-029 categories (even though full error-handling polish is
  Sprint 9, each processor must not throw unhandled exceptions).

## Sprint 4 — Markdown Conversion & Metadata

- **Sprint Goal:** Converted documents become well-formed, metadata-tagged Markdown.
- **Size:** M
- **Tasks:** TASK-030 through TASK-034.
- **Dependencies:** Sprint 3.
- **Expected Output:** `.md` files with correct structure and front matter for every
  sample document; collision-safe, overwrite-on-reconvert output naming.
- **Acceptance Criteria:** AC-006–AC-011, AC-032.
- **Testing Requirements:** Unit tests for the Markdown generator against
  `DocumentModel` fixtures (no Python dependency needed for these tests).
- **Definition of Done:** All listed ACs pass; front-matter schema is consistent
  across all five formats.

## Sprint 5 — Token Estimation

- **Sprint Goal:** Users see the value of the conversion.
- **Size:** M
- **Tasks:** TASK-035 through TASK-038.
- **Dependencies:** Sprint 4.
- **Expected Output:** Conversion Result screen showing both tokenizer-style
  estimates and the reduction percentage, clearly labeled as estimates.
- **Acceptance Criteria:** AC-012, AC-013.
- **Testing Requirements:** Unit tests for `ITokenEstimator` (mocked Python
  response); a manual check that the UI disclaimer is visible.
- **Definition of Done:** Both ACs pass; ADR-003's labeling requirement is visibly
  satisfied in the UI, not just in code comments.

## Sprint 6 — Chunk Generation

- **Sprint Goal:** Converted documents can be split into AI-ready chunks.
- **Size:** M
- **Tasks:** TASK-039 through TASK-043.
- **Dependencies:** Sprint 5.
- **Expected Output:** Chunk Settings screen; chunk files generated with correct
  sequencing, table/heading safety, and configurable size/overlap.
- **Acceptance Criteria:** AC-014, AC-015.
- **Testing Requirements:** Unit tests specifically targeting the table-not-split
  and heading-with-content rules, plus the overlap/no-content-loss property from the
  corrected AC-014 wording.
- **Definition of Done:** Both ACs pass, including the edge case of a table larger
  than the configured chunk size.

## Sprint 7 — Batch Processing

- **Sprint Goal:** The single-file pipeline works reliably across hundreds of files
  without freezing the UI, and can be cancelled.
- **Size:** L
- **Tasks:** TASK-044 through TASK-049.
- **Dependencies:** Sprints 2–6 (batches the full single-file pipeline).
- **Expected Output:** Batch conversion with live progress, bounded parallelism, and
  a working Cancel button.
- **Acceptance Criteria:** AC-016, AC-017, AC-018, AC-025.
- **Testing Requirements:** Integration test with 100+ sample-derived files; a
  responsiveness check (UI accepts input) during a large-batch run; a cancellation
  test mid-batch.
- **Definition of Done:** All listed ACs pass; no UI freeze observed in manual
  testing (formal benchmark numbers come in Sprint 11).

## Sprint 8 — Error Handling

- **Sprint Goal:** Failures are categorized, safe, and recoverable.
- **Size:** M
- **Tasks:** TASK-050 through TASK-055.
- **Dependencies:** Sprint 7 (most failure surface area is in extraction + batch
  orchestration).
- **Expected Output:** Every failure path maps to an FR-029 category with a
  user-friendly message and a retry option where applicable; no stack traces reach
  the UI; subprocess calls are injection-safe; temp files are cleaned up.
- **Acceptance Criteria:** AC-019, AC-020.
- **Testing Requirements:** Unit/integration tests simulating each error category
  (locked file, corrupted file, missing file, engine timeout).
- **Definition of Done:** Both ACs pass; a single failed file in a batch does not
  stop the rest (BR-006), re-verified here under real error conditions.

## Sprint 9 — Export

- **Sprint Goal:** Converted output leaves the app safely.
- **Size:** S
- **Tasks:** TASK-056 through TASK-059.
- **Dependencies:** Sprints 4–6 (needs Markdown + chunks to export).
- **Expected Output:** Individual Markdown export and single-ZIP-per-batch export,
  both with validated output paths.
- **Acceptance Criteria:** AC-021.
- **Testing Requirements:** Unit tests for path validation rejection; integration
  test producing and inspecting a real ZIP's folder layout.
- **Definition of Done:** AC-021 passes; ZIP layout matches
  `docs/10-FOLDER-STRUCTURE.md`/FR-046.

## Sprint 10 — Settings & Logging Hardening

- **Sprint Goal:** Close out the remaining Settings/logging/security acceptance
  criteria not fully exercised by earlier sprints.
- **Size:** S
- **Tasks:** Verification tasks against TASK-004–TASK-006, TASK-012 outputs; no new
  TASK IDs — this sprint is a hardening pass.
- **Dependencies:** Sprint 1 (revisits Foundation work with the full app now built
  around it).
- **Expected Output:** Confirmed settings persistence, confirmed offline operation,
  confirmed log content contains no document text.
- **Acceptance Criteria:** AC-022, AC-023, AC-024.
- **Testing Requirements:** A dedicated network-monitoring pass across a full
  import→convert→chunk→export cycle (US-023); a log-content audit.
- **Definition of Done:** All three ACs pass; this is the last sprint before formal
  Testing (Sprint 11).

## Sprint 11 — Testing

- **Sprint Goal:** Full verification against the SRS and Acceptance Criteria; defect
  fixing.
- **Size:** L
- **Tasks:** TASK-060 through TASK-063.
- **Dependencies:** Sprints 1–10 complete.
- **Expected Output:** Full test suite green; performance benchmark matrix measured;
  `docs/22-BUG-TRACKER.md` populated and closed out for blocking defects.
- **Acceptance Criteria:** All AC-001–AC-032.
- **Testing Requirements:** Unit, integration, and manual UAT-style testing per
  `docs/16-TEST-STRATEGY.md` (Gate 4).
- **Definition of Done:** CLAUDE.md Section 40 (Definition of Done) satisfied for
  every requirement; CLAUDE.md Section 56 (Release Checklist) items other than
  packaging are satisfied.

## Sprint 12 — Release

- **Sprint Goal:** Ship v1.0.0.
- **Size:** S
- **Tasks:** TASK-064 through TASK-067.
- **Dependencies:** Sprint 11 sign-off.
- **Expected Output:** Signed/packaged installer; updated `CHANGELOG.md`; tagged
  release.
- **Acceptance Criteria:** Release Checklist fully checked off (CLAUDE.md Section
  56).
- **Testing Requirements:** Clean-machine install-and-convert smoke test (no manual
  Python setup required).
- **Definition of Done:** Installer verified on a clean VM; documentation updated;
  version 1.0.0 tagged.

---

*Next document: `docs/14-TODO.md`*
