# 02 — Product Requirements Document (PRD)

**Project:** AI Document Converter
**Status:** Draft — Phase 2 (Product Requirements)
**Date:** 2026-08-23

---

## 1. Product Vision

A professional, offline Windows desktop application that converts enterprise documents
(PDF, DOCX, XLSX, PPTX, TXT) into clean, structured, AI-ready Markdown — reducing token
usage and improving retrieval quality for AI assistants and RAG systems, without any
document content ever leaving the user's machine.

## 2. Goals

- G1: Make document-to-Markdown conversion a one-click (or near one-click) operation
  for non-technical users.
- G2: Visibly demonstrate the value of conversion via token estimation (before/after).
- G3: Produce Markdown output structured well enough to be used directly in RAG/vector
  pipelines (via chunking).
- G4: Support realistic enterprise batch volumes (hundreds of files) reliably.
- G5: Guarantee offline, secure operation suitable for a banking/financial environment.

## 3. User Personas

### Persona 1 — "Knowledge Management Officer" (Primary)
Non-technical business user responsible for curating a repository of internal policy
and procedure documents for eventual AI-assisted search. Needs simplicity, clear
progress feedback, and confidence that nothing leaves the organization's machines.

### Persona 2 — "Research Analyst"
Prepares research reports and reference materials for use with Claude/ChatGPT/Copilot
during analysis work. Cares about token efficiency and preserving tables/figures
references accurately.

### Persona 3 — "IT / Knowledge Systems Administrator" (Secondary/Admin)
Configures output locations, batch/parallelism limits, and reviews logs. Needs
confidence in error handling, logging, and offline guarantees for compliance sign-off.

## 4. Features (Initial Release Candidate List)

| # | Feature | Persona(s) |
|---|---|---|
| F1 | Import files (single, multiple, drag-and-drop, folder) | 1, 2 |
| F2 | Format validation with clear rejection messages | 1, 2 |
| F3 | Extraction engine per format (PDF, DOCX, XLSX, PPTX, TXT) | 1, 2 |
| F4 | Normalized internal document model | (internal) |
| F5 | Markdown generation with front-matter metadata | 1, 2 |
| F6 | Token estimation (multi-provider, labeled as estimate) | 1, 2 |
| F7 | Configurable chunk generation (size/overlap, structure-aware) | 2 |
| F8 | Batch processing with live progress | 1 |
| F9 | Conversion result summary (tokens, reduction %, output path) | 1, 2 |
| F10 | Graceful error handling with user-friendly messages and retry | 1, 2, 3 |
| F11 | Export to Markdown files and ZIP package | 1, 2 |
| F12 | Settings (output directory, Python config, logging, parallelism, chunk defaults) | 3 |
| F13 | Application logging (Serilog) | 3 |

## 5. MVP Scope

**In scope for MVP (v1.0):**

- Input formats: PDF, DOCX, XLSX, PPTX, TXT.
- Output format: Markdown (`.md`), plus ZIP export bundling markdown/chunks/metadata.
- Single file, multiple file, drag-and-drop, and folder import.
- Normalized internal document model shared across all processors.
- Structured Markdown generation (headings, lists, tables, links, page/slide/sheet
  references) with YAML front-matter metadata. Embedded images are represented as a
  placeholder marker in MVP (full image extraction is a Future Roadmap item).
- Token estimation (clearly labeled as an estimate) for at least Claude and
  GPT-4o/Azure OpenAI style tokenization via `tiktoken`.
- Configurable chunk generation that avoids splitting tables or separating headings
  from their immediate content.
- Batch processing (up to a configurable ceiling, default 500 files / 5 GB — see
  SRS NFR-013) with responsive UI, progress reporting, per-file status, and the
  ability to cancel an in-progress batch.
- Categorized error handling with user-friendly messages, technical logs, and retry
  where appropriate.
- Settings screen for output directory, Python engine configuration, logging, chunk
  defaults, and parallelism.
- Fully offline operation; Serilog-based logging without sensitive content.

## 6. Out of Scope for MVP

- OCR / scanned PDF / image-based text extraction.
- Additional input formats: HTML, CSV, images.
- Additional output formats: JSON, embeddings.
- Password-protected / encrypted document support.
- Multi-user, server, or cloud-hosted deployment.
- Sending any document content to external/third-party AI APIs.
- User authentication or role-based access control.
- Conversion history/audit database (may use a lightweight local JSON list only if
  needed — to be confirmed in Data Model phase).
- Non-English / multilingual document support (English only for MVP — confirmed
  2026-08-23).
- Recursive (subfolder) folder import — MVP scans the top level of a selected folder
  only.

## 7. Phase 2 (Near-Term Post-MVP)

- OCR support for scanned PDFs and images (Tesseract OCR).
- Additional input formats: HTML, CSV.
- Additional output format: JSON / structured AI chunk metadata files.
- Refinement of table-extraction fidelity based on user feedback.
- Recursive (subfolder) folder import.
- Non-English/multilingual support — not currently planned; revisit only if a
  confirmed business need emerges.

## 8. Future Roadmap (Long-Term)

- Embedding generation and Azure OpenAI integration.
- Azure AI Search / vector database integration.
- Enterprise RAG pipeline: SharePoint → Extraction → Markdown → Chunking → Embedding →
  Vector Database.
- Plugin architecture supporting multiple AI providers.
- Image processing support.

## 9. Product Success Metrics

- **Adoption:** number of active users / documents converted per week post-release.
- **Efficiency:** average measured token reduction percentage across converted
  documents (displayed per conversion; aggregated for reporting). Hypothesis target:
  ≥ 20% average reduction on typical table/heading-heavy documents (see BRD Section
  11).
- **Reliability:** conversion success rate ≥ 95% on supported file types.
- **Performance:** batch of 100 average-sized office documents completes within an
  agreed time budget (to be defined in performance testing, `docs/16-TEST-STRATEGY.md`).
- **Compliance:** zero verified instances of outbound network calls or logged document
  content.
- **Usability:** first-conversion completion by a new user without training, tracked
  qualitatively during UAT.

## 10. Assumptions Carried from BRD

See `docs/01-BRD.md` Section 7 for full assumption list (offline operation, no OCR,
no password-protected files, single-user desktop, no database for MVP).

## 11. Revision Note (2026-08-23)

Following a Requirements Quality Review, this document was updated to: confirm
English-only scope, state the MVP image-placeholder policy, note the batch size
ceiling and cancellation capability, add a token-reduction hypothesis target, and
move recursive folder import explicitly to Phase 2.

---

*Next document: `docs/03-SRS.md`*
