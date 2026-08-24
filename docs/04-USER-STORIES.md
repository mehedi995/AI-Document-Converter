# 04 — User Stories

**Project:** AI Document Converter
**Status:** Draft — Phase 4
**Date:** 2026-08-23

Priority scale: **High / Medium / Low**

---

### US-001 — Import a single file
As a business user,
I want to select and import a single document,
So that I can convert it to Markdown.

- Priority: High
- Acceptance Criteria: AC-001
- Dependencies: none

### US-002 — Import multiple files
As a business user,
I want to select multiple documents at once,
So that I can convert several files in one operation.

- Priority: High
- Acceptance Criteria: AC-002
- Dependencies: US-001

### US-003 — Drag and drop files
As a business user,
I want to drag files from Windows Explorer onto the application,
So that importing feels fast and natural.

- Priority: Medium
- Acceptance Criteria: AC-003
- Dependencies: US-001

### US-004 — Import a folder
As a knowledge management officer,
I want to select an entire folder of documents,
So that I don't have to pick files one by one for large document sets.

- Priority: Medium
- Acceptance Criteria: AC-004
- Dependencies: US-001

### US-005 — Reject unsupported files clearly
As a business user,
I want to be told clearly when a file type is not supported,
So that I understand why it wasn't converted instead of assuming the app failed.

- Priority: High
- Acceptance Criteria: AC-005
- Dependencies: US-001

### US-006 — Convert PDF to Markdown
As a research analyst,
I want a PDF converted into structured Markdown with headings, tables, and page
references preserved,
So that I can use it accurately in AI tools.

- Priority: High
- Acceptance Criteria: AC-006
- Dependencies: US-001

### US-007 — Convert DOCX to Markdown
As a business user,
I want a Word document converted into Markdown preserving headings, lists, tables, and
links,
So that the structure of the original document is not lost.

- Priority: High
- Acceptance Criteria: AC-007
- Dependencies: US-001

### US-008 — Convert XLSX to Markdown
As a business user,
I want an Excel workbook's sheets converted into Markdown tables,
So that tabular data can be understood by an AI assistant.

- Priority: High
- Acceptance Criteria: AC-008
- Dependencies: US-001

### US-009 — Convert PPTX to Markdown
As a business user,
I want a PowerPoint presentation converted into Markdown with slide titles, content,
and notes,
So that presentation content is usable as AI reference material.

- Priority: Medium
- Acceptance Criteria: AC-009
- Dependencies: US-001

### US-010 — Convert TXT to Markdown
As a business user,
I want a plain text file wrapped into a Markdown document with metadata,
So that all my source materials share a consistent AI-ready format.

- Priority: Low
- Acceptance Criteria: AC-010
- Dependencies: US-001

### US-011 — See document metadata in output
As a knowledge management officer,
I want each converted Markdown file to include source metadata (file type, dates,
author) in a front-matter block,
So that I can trace converted content back to its original source.

- Priority: Medium
- Acceptance Criteria: AC-011
- Dependencies: US-006, US-007, US-008, US-009, US-010

### US-012 — See token reduction
As a research analyst,
I want to see the original and converted token counts and the percentage reduction,
So that I understand the value the conversion provided.

- Priority: High
- Acceptance Criteria: AC-012
- Dependencies: US-006

### US-013 — Understand token estimates are approximate
As a business user,
I want the app to clearly state that token counts are estimates,
So that I don't mistake them for an exact count for a specific AI provider.

- Priority: Medium
- Acceptance Criteria: AC-013
- Dependencies: US-012

### US-014 — Generate AI-ready chunks
As a research analyst,
I want to split a converted document into configurable chunks,
So that I can feed it into a RAG or vector database pipeline.

- Priority: High
- Acceptance Criteria: AC-014
- Dependencies: US-006

### US-015 — Chunking preserves tables
As a research analyst,
I want tables to never be split across two chunks,
So that tabular data remains meaningful in each chunk.

- Priority: Medium
- Acceptance Criteria: AC-015
- Dependencies: US-014

### US-016 — Batch convert many files
As a knowledge management officer,
I want to convert hundreds of files in one batch operation,
So that I don't have to convert documents individually.

- Priority: High
- Acceptance Criteria: AC-016
- Dependencies: US-002, US-004

### US-017 — Monitor batch progress
As a knowledge management officer,
I want to see live progress (current file, percentage, success/failure counts) during
batch conversion,
So that I know the app is working and can estimate completion.

- Priority: High
- Acceptance Criteria: AC-017
- Dependencies: US-016

### US-018 — Keep the UI responsive during processing
As a business user,
I want the application to remain usable (not frozen) while converting large or many
files,
So that I trust the app hasn't crashed.

- Priority: High
- Acceptance Criteria: AC-018
- Dependencies: US-016

### US-019 — Understand conversion errors
As a business user,
I want a clear, non-technical explanation when a file fails to convert,
So that I know what went wrong and what I can do about it.

- Priority: High
- Acceptance Criteria: AC-019
- Dependencies: US-001

### US-020 — Retry a failed file
As a business user,
I want to retry a failed conversion (e.g., after closing the file elsewhere),
So that I don't have to restart the entire batch.

- Priority: Medium
- Acceptance Criteria: AC-020
- Dependencies: US-019

### US-021 — Export converted files
As a business user,
I want to export converted Markdown (and chunks/metadata) as files or a ZIP package,
So that I can move the output into another system.

- Priority: High
- Acceptance Criteria: AC-021
- Dependencies: US-006, US-014

### US-022 — Configure output and processing settings
As an IT/knowledge systems administrator,
I want to configure the output directory, chunk defaults, Python engine path, logging,
and parallelism,
So that the tool fits our environment and workflow.

- Priority: Medium
- Acceptance Criteria: AC-022
- Dependencies: none

### US-023 — Trust offline operation
As an IT/security reviewer,
I want assurance and evidence that the application never sends document content over
the network,
So that I can approve it for use with sensitive internal documents.

- Priority: High
- Acceptance Criteria: AC-023
- Dependencies: none

### US-024 — Review application logs
As an IT/knowledge systems administrator,
I want to review logs of conversion activity and errors (without document content),
So that I can troubleshoot issues and audit usage.

- Priority: Medium
- Acceptance Criteria: AC-024
- Dependencies: US-019

### US-025 — Cancel a batch conversion
As a business user,
I want to cancel a batch conversion in progress,
So that I can stop the operation if I selected the wrong files or need my machine back.

- Priority: Medium
- Acceptance Criteria: AC-025
- Dependencies: US-016

### US-026 — Know immediately if the processing engine isn't configured
As an IT/knowledge systems administrator,
I want the app to tell me immediately at startup if the document-processing engine
isn't available or configured correctly,
So that I don't waste time importing files that will all fail one by one.

- Priority: Medium
- Acceptance Criteria: AC-026
- Dependencies: none

---

*Revision Note (2026-08-23): Added US-025 and US-026 following the Requirements
Quality Review (batch cancellation and startup engine health check were previously
referenced in use cases but had no owning user story).*

---

*Next document: `docs/05-USE-CASES.md`*
