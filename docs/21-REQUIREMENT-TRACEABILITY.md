# 21 — Requirement Traceability Matrix

**Project:** AI Document Converter
**Status:** Draft — Phase 21 (Requirement Traceability)
**Date:** 2026-08-23

---

Chain: BRD Objective → SRS Requirement → User Story → Acceptance Criteria → Task
(`docs/14-TODO.md`) → Test (`docs/17-UNIT-TEST-PLAN.md` or integration suite).
"Code" is not yet populated — Gate 5 (production coding) has not started; the Task
column identifies which class(es)/task(s) will implement each requirement per
`docs/15-IMPLEMENTATION-PLAN.md`.

## Functional Requirements

| Req | User Story | Acceptance Criteria | Task(s) | Test Reference |
|---|---|---|---|---|
| FR-001 | US-001 | AC-001 | TASK-014 | Import unit tests |
| FR-002 | US-002 | AC-002 | TASK-015 | Import unit tests |
| FR-003 | US-003 | AC-003 | TASK-016 | Manual (drag-and-drop) |
| FR-004 | US-004 | AC-004 | TASK-017 | Import unit tests |
| FR-005 | US-005 | AC-005 | TASK-018 | Import unit tests |
| FR-006 | US-006 | AC-006 | TASK-024 | Unit Test Plan §PDF Extraction |
| FR-007 | US-007 | AC-007 | TASK-023 | Unit Test Plan §DOCX Extraction |
| FR-008 | US-008 | AC-008 | TASK-025 | Unit Test Plan §XLSX Extraction |
| FR-009 | US-009 | AC-009 | TASK-026 | Unit Test Plan §PPTX Extraction |
| FR-010 | US-010 | AC-010 | TASK-022 | Unit Test Plan §TXT Extraction |
| FR-011 | (internal — supports US-006–010) | AC-006–AC-010 | TASK-007, TASK-028 | Covered per-format above |
| FR-012 | US-006–US-010 | AC-006–AC-011 | TASK-030 | Unit Test Plan §Markdown Generation |
| FR-013 | US-011 | AC-011 | TASK-031 | Unit Test Plan §Metadata Generation |
| FR-014 | US-012 | AC-012 | TASK-036 | Unit Test Plan §Token Estimation |
| FR-015 | US-012 | AC-012 | TASK-036 | Unit Test Plan §Token Estimation |
| FR-016 | US-012, US-013 | AC-012, AC-013 | TASK-035, TASK-036 | Unit Test Plan §Token Estimation |
| FR-017 | US-013 | AC-013 | TASK-037 | Manual UI check + unit test |
| FR-018 | US-014 | AC-014 | TASK-040 | Unit Test Plan §Chunk Generation |
| FR-019 | US-015 | AC-015 | TASK-039 | Unit Test Plan §Chunk Generation |
| FR-020 | US-015 | AC-015 | TASK-039 | Unit Test Plan §Chunk Generation |
| FR-021 | US-014 | AC-014 | TASK-041 | Unit Test Plan §Chunk Generation |
| FR-022 | US-016 | AC-016 | TASK-044 | Batch integration test |
| FR-023 | US-017 | AC-017 | TASK-045 | Batch integration test |
| FR-024 | US-018 | AC-018 | TASK-048 | Batch integration test (responsiveness) |
| FR-025 | US-016 | AC-016 | TASK-044 | Batch integration test |
| FR-026 | US-012, US-017 | AC-012, AC-017 | TASK-037, TASK-045 | Manual UI check |
| FR-027 | US-021 | AC-021 | TASK-056 | Export integration test |
| FR-028 | US-021 | AC-021 | TASK-057 | Export integration test |
| FR-029 | US-019 | AC-019 | TASK-050 | Unit Test Plan §Error Handling |
| FR-030 | US-020 | AC-020 | TASK-051 | Unit Test Plan §Error Handling |
| FR-031 | US-019 | AC-019 | TASK-052 | Unit Test Plan §Error Handling |
| FR-032 | US-022 | AC-022 | TASK-004, TASK-006 | Settings unit test |
| FR-033 | US-022 | AC-022 | TASK-004 | Settings unit test |
| FR-034 | US-024 | AC-024 | TASK-003 | Log-content audit (Sprint 10) |
| FR-035 | US-024 | AC-024 | TASK-003 | Log-content audit (Sprint 10) |
| FR-036 | (batch/export-level, no dedicated US) | AC-033 | TASK-032 | Unit Test Plan §Markdown Generation |
| FR-037 | US-025 | AC-025 | TASK-046 | Batch integration test (cancellation) |
| FR-038 | US-026 | AC-026 | TASK-011 | Manual startup test |
| FR-039 | (supports US-006, US-007, US-009) | AC-027 | TASK-027 | Unit Test Plan §DOCX/PPTX Extraction |
| FR-040 | (supports US-010) | AC-010 | TASK-022 | Unit Test Plan §TXT Extraction |
| FR-041 | (supports US-008) | AC-028 | TASK-025 | Unit Test Plan §XLSX Extraction |
| FR-042 | (supports US-008) | — (Unit Test Plan only) | TASK-025 | Unit Test Plan §XLSX Extraction |
| FR-043 | (supports US-006) | AC-029 | TASK-024 | Unit Test Plan §PDF Extraction |
| FR-044 | (supports US-021) | AC-032 | TASK-033 | Unit Test Plan §Markdown Generation |
| FR-045 | (supports US-016, US-017) | AC-016, AC-017 | TASK-047 | Batch integration test |
| FR-046 | US-021 | AC-021 | TASK-057 | Export integration test |

## Non-Functional Requirements

| Req | Acceptance Criteria | Task(s) | Test Reference |
|---|---|---|---|
| NFR-001 | AC-023 | (cross-cutting) | Offline-compliance verification (TASK-062) |
| NFR-002 | AC-023 | (cross-cutting) | Offline-compliance verification (TASK-062) |
| NFR-003 | AC-018 | TASK-048 | Performance benchmark matrix (TASK-061) |
| NFR-004 | — (architectural property) | ADR-002, `docs/09-DATA-MODEL.md` §3 | Reviewed at each Phase 2 feature addition, not a standalone test |
| NFR-005 | AC-021 (export path) | TASK-058 | Unit Test Plan §File Validation |
| NFR-006 | AC-024 | TASK-003 | Log-content audit |
| NFR-007 | — (architectural property) | `docs/08-APPLICATION-ARCHITECTURE.md` | Enforced by code review, not a runtime test |
| NFR-008 | (qualitative — US-001 "5 minute" success criterion) | (all Presentation-layer tasks) | UAT/manual usability observation |
| NFR-009 | AC-019, AC-020 | TASK-050–TASK-055 | Unit Test Plan §Error Handling |
| NFR-010 | — (deployment property) | `docs/19-DEPLOYMENT-PLAN.md` | Clean-VM install verification (TASK-064) |
| NFR-011 | AC-016–AC-018 | TASK-044 | Batch integration test |
| NFR-012 | — (compliance check) | TASK-012 | Dependency audit |
| NFR-013 | AC-031 | TASK-019 | Import unit tests |

## Security Requirements

| Req | Acceptance Criteria | Task(s) | Test Reference |
|---|---|---|---|
| SEC-001 | AC-023 | (cross-cutting) | Offline-compliance verification |
| SEC-002 | AC-021 | TASK-058 | Unit Test Plan §File Validation |
| SEC-003 | — (code-review only, no runtime data to secure in MVP) | (all tasks) | Code review checklist |
| SEC-004 | — | TASK-054 | Manual crash-then-restart cleanup test |
| SEC-005 | AC-030 | TASK-005 | Unit Test Plan §File Validation |
| SEC-006 | — | TASK-053 | Manual/fuzzed-filename injection test |

## Business Rules

| Rule | Acceptance Criteria | Task(s) | Test Reference |
|---|---|---|---|
| BR-001 | AC-005 | TASK-018 | Import unit tests |
| BR-002 | AC-032 | TASK-033 | Unit Test Plan §Markdown Generation |
| BR-003 | (implicit in AC-006, PDF password-protected case) | TASK-024 | Unit Test Plan §PDF Extraction |
| BR-004 | AC-015 | TASK-039 | Unit Test Plan §Chunk Generation |
| BR-005 | AC-013 | TASK-037 | Manual UI check |
| BR-006 | (implicit in AC-016) | TASK-050 | Unit Test Plan §Error Handling |
| BR-007 | (English-only — no dedicated AC; a documented assumption, not a testable behavior) | — | — |

## Gaps Closed by This Pass

Building this matrix surfaced one requirement with no owning acceptance criterion:
**FR-036** (output filename collision handling) — closed by adding **AC-033** to
`docs/06-ACCEPTANCE-CRITERIA.md` during this pass (see that document's Revision Note).

Two requirements (**FR-042**, **BR-007**) are intentionally left without a dedicated
AC: FR-042 is implementation-detail-level and fully covered by the Unit Test Plan;
BR-007 is a documented assumption (English-only), not an independently testable
behavior.

---

*Next document: `docs/22-BUG-TRACKER.md`*
