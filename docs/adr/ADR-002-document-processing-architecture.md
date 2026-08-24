# ADR-002 — Document Processing Architecture

**Status:** Accepted
**Date:** 2026-08-23

## Context

Five input formats (PDF, DOCX, XLSX, PPTX, TXT) must each be extracted differently
(FR-006–FR-010) but converge on the same downstream pipeline: normalize → generate
Markdown → estimate tokens → chunk → export. CLAUDE.md Section 26 suggests a common
`IDocumentProcessor` abstraction but explicitly warns not to implement it blindly.

A related question raised in the Requirements Quality Review (SRS Section 10): not
every format needs the Python engine. TXT is trivially read in .NET; PDF/DOCX/XLSX/
PPTX extraction goes through the Python subprocess (ADR-001).

## Decision

- Define one Application-layer interface, `IDocumentProcessor`, with a single
  responsibility: given a file path, return the normalized `DocumentModel`
  (`docs/09-DATA-MODEL.md`) or throw a categorized exception mapping to FR-029.

  ```csharp
  public interface IDocumentProcessor
  {
      bool CanProcess(string filePath);

      Task<DocumentModel> ExtractAsync(
          string filePath,
          CancellationToken cancellationToken);
  }
  ```

- One implementation per format, in Infrastructure:
  - `PdfDocumentProcessor`, `DocxDocumentProcessor`, `ExcelDocumentProcessor`,
    `PowerPointDocumentProcessor` — each delegates extraction to the Python
    subprocess via a shared `IPythonEngineClient` (the ADR-001 adapter), passing a
    processor-specific request payload and format.
  - `TextDocumentProcessor` — pure .NET, reads the file directly (per FR-010, FR-040
    encoding rules), no Python involved.
- A simple **Strategy** selection: an `IDocumentProcessor[]` collection registered in
  DI; the caller picks the first processor whose `CanProcess` returns true for the
  file's extension. A factory or resolver class is **not** introduced — with only
  five processors, a straightforward "first match" over an injected collection is
  simpler and equally clear (CLAUDE.md Section 25: avoid unnecessary patterns).
- Each processor is independently unit-testable by mocking `IPythonEngineClient`
  (for the four Python-backed processors) or by exercising `TextDocumentProcessor`
  directly against sample files.

## Consequences

- Adding a future format (e.g., HTML, CSV — Phase 2) means adding one new processor
  class and registering it; no change to the Application layer's orchestration code.
- The Python side mirrors this with one extraction module per format
  (`pdf_extractor.py`, `docx_extractor.py`, etc.) behind a single dispatch entry
  point in the bundled executable, keeping the two codebases' structures aligned and
  easy for a mid-level developer to navigate.
- `CanProcess` is deliberately extension-based (not content-sniffing) for MVP
  simplicity; FR-005's validation happens earlier in the pipeline (Application
  layer, before any processor is invoked).
