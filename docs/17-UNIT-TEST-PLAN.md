# 17 — Unit Test Plan

**Project:** AI Document Converter
**Status:** Draft — Phase 17 (Unit Test Plan) — Gate 4
**Date:** 2026-08-23

---

Target: meaningful coverage of behavior, not a percentage number (CLAUDE.md Section
33). Each row is a test case group, not a single test — implementers should write as
many individual `[Fact]`/`[Theory]` cases as needed to cover the listed scenarios.

## PDF Extraction (FR-006, FR-043)

| Scenario | Expected Result |
|---|---|
| Well-formed PDF with headings, a table, page breaks | All three preserved in the `DocumentModel`, with correct `SourceLocation.PageNumber` |
| PDF with a page containing no extractable text | That page becomes an `UnextractableTextBlock`; overall extraction still succeeds |
| Corrupted/truncated PDF | Throws `DocumentConversionException` with `ErrorCategory.CorruptedDocument` |
| Password-protected PDF | Throws with `ErrorCategory.UnsupportedFile` (per BR-003, not a generic failure) |

## DOCX Extraction (FR-007, FR-039)

| Scenario | Expected Result |
|---|---|
| Headings, paragraphs, a table, a list, a hyperlink | All five preserved |
| Embedded image | Becomes an `ImagePlaceholderBlock` |
| Corrupted DOCX (invalid zip/XML) | `ErrorCategory.CorruptedDocument` |

## XLSX Extraction (FR-008, FR-041, FR-042)

| Scenario | Expected Result |
|---|---|
| Multi-sheet workbook with tabular data | Each sheet → one or more Markdown tables |
| Sheet with a formula cell | Computed value extracted, not the formula text |
| Sheet with a merged cell | Value repeated across the spanned Markdown cells |
| Sheet exceeding the row/column threshold | Summarized representation with a note, not the full dump |

## PPTX Extraction (FR-009, FR-039)

| Scenario | Expected Result |
|---|---|
| Slides with titles, content, and speaker notes | All three preserved per slide, with `SourceLocation.SlideNumber` |
| Slide with an embedded image | `ImagePlaceholderBlock` |

## TXT Extraction (FR-010, FR-040)

| Scenario | Expected Result |
|---|---|
| UTF-8 file, with and without BOM | Correct text, no mangled characters |
| Non-UTF-8/undecodable byte sequence | `ErrorCategory.CorruptedDocument` |

## Markdown Generation (FR-012, FR-036, FR-044, BR-004)

| Scenario | Expected Result |
|---|---|
| `DocumentModel` with nested headings/lists/tables/links | Markdown output preserves structure and ordering |
| Two different source files that would produce the same output filename | Collision resolved per FR-036 (mirrored path or numeric suffix) |
| Re-converting the same source to the same output path | Prior output overwritten (FR-044), no error raised |

## Metadata Generation (FR-013)

| Scenario | Expected Result |
|---|---|
| Document with full metadata available | Front matter contains all six fields |
| Document with no author metadata (e.g., TXT) | `author` field omitted entirely, not blank |

## Token Estimation (FR-014–FR-017, ADR-003)

| Scenario | Expected Result |
|---|---|
| Given fixed Markdown input | `TokenEstimator` returns consistent counts for both `o200k_base` and `cl100k_base` (mocked Python response asserted against a known fixture value) |
| Reduction is zero or negative | UI/DTO still populates correctly, no exception, no assumption of a positive value |

## Chunk Generation (FR-018–FR-021, BR-004)

| Scenario | Expected Result |
|---|---|
| Document larger than one chunk, default size/overlap | Multiple sequentially numbered chunks, overlap present, no content lost (corrected AC-014 property) |
| Table larger than the configured chunk size | Table stays intact in a single chunk even though it exceeds the configured size |
| Document smaller than one chunk | Exactly one chunk file is produced, equal to the full document |
| Heading immediately followed by content | Not split across a chunk boundary where avoidable |

## Error Handling (FR-029–FR-031, BR-006)

| Scenario | Expected Result |
|---|---|
| Each FR-029 category individually (unsupported file, file not found, locked, permission denied, corrupted, Python engine failure, conversion failure, output failure, unexpected exception) | Maps to the correct `ErrorCategory`, produces a non-technical message, logs technical detail |
| One file fails in a batch of many | Remaining files still process (BR-006) |
| Any exception's message | Never contains a raw stack trace string intended for end-user display |

## File Validation (FR-005, NFR-013, SEC-002, SEC-005)

| Scenario | Expected Result |
|---|---|
| Unsupported extension | Rejected before reaching any processor |
| Path traversal sequence in a supplied path | Rejected (SEC-002) |
| Batch exceeding the configured file-count or byte-size ceiling | Accepted up to the ceiling; excess reported (NFR-013) |
| Python executable path pointing to a non-Python binary | Rejected by `PythonPathValidator` (SEC-005) |

---

*Next document: `docs/18-RISK-ASSESSMENT.md`*
