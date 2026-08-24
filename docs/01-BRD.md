# 01 — Business Requirements Document (BRD)

**Project:** AI Document Converter
**Status:** Draft — Phase 1 (Business Analysis)
**Author:** Claude Code (assisting mehedi@grameenbank.org.bd)
**Date:** 2026-08-23

---

## 1. Business Problem

Enterprise users at organizations such as Grameen Bank routinely need to feed internal
documents (policies, reports, spreadsheets, presentations) into AI assistants (Claude,
ChatGPT, Copilot, Azure OpenAI) and internal RAG/knowledge-base systems. Today this is
done manually — copy/paste, ad-hoc PDF-to-text tools, or uploading raw files — which
causes:

- Excessive AI token consumption (raw/unstructured text, boilerplate, layout noise).
- Loss of document structure (headings, tables, lists) that AI models rely on for
  accurate retrieval and reasoning.
- No repeatable, auditable process for converting large volumes of documents.
- Risk of sensitive/internal documents being sent to external services during ad-hoc
  conversion (e.g., free online PDF-to-text or PDF-to-Markdown web tools).

There is no internal, offline, enterprise-controlled tool that converts common office
document formats into clean, structured, AI-ready Markdown.

## 2. Business Objectives

1. Reduce unnecessary AI token consumption when enterprise documents are used with AI
   tools.
2. Improve document structure to increase retrieval quality in RAG/vector-search
   scenarios.
3. Preserve important document semantics (headings, tables, lists, references) during
   conversion.
4. Produce AI-ready chunks suitable for downstream embedding/RAG pipelines.
5. Support batch processing of large document sets without manual effort.
6. Operate fully offline to protect sensitive/internal data (no document content leaves
   the user's machine).
7. Provide an enterprise-quality experience usable by non-technical business users.

## 3. Stakeholders

| Stakeholder | Interest |
|---|---|
| Grameen Bank (sponsoring organization) | Wants a secure, offline tool to prepare internal documents for AI use without data leaving the bank's environment. |
| Business Users / Knowledge Management staff | Primary hands-on users; need a simple, low-friction tool. |
| Research / Analyst teams | Use converted Markdown/chunks as input to AI-assisted research and reporting. |
| IT / Information Security | Must approve the tool for offline operation, data handling, and deployment on enterprise Windows machines. |
| Product Owner (mehedi@grameenbank.org.bd) | Owns scope, priorities, and approval gates. |
| Development Team (Mid-level developer, assisted by Claude Code) | Builds and maintains the application; needs the codebase to stay simple and maintainable. |

## 4. Target Users

- Business users in enterprise/financial institutions (primary: Grameen Bank staff).
- Knowledge management teams curating internal document repositories.
- Research teams preparing source material for AI-assisted analysis.
- Government and other enterprise organizations with similar offline/security needs.

Target users are assumed **non-technical**: no scripting, command-line, or AI/ML
background required to operate the application.

## 5. User Pain Points

- Manually copying document text into an AI chat tool is slow and loses formatting
  (tables collapse, headings disappear).
- Large PDFs/spreadsheets consume excessive tokens when pasted raw, increasing AI usage
  cost and sometimes exceeding context limits.
- No safe way to prepare many documents (e.g., hundreds of policy files) for a RAG
  system without a repeatable pipeline.
- Sensitive banking/customer-related documents cannot be uploaded to third-party
  web converters due to data security and compliance concerns.
- No visibility into how much a conversion actually reduces token usage — users cannot
  currently measure the benefit of "cleaning up" a document before using it with AI.

## 6. Business Value

- **Cost savings:** Lower AI token usage reduces API costs for teams using paid AI
  models against converted documents.
- **Time savings:** Batch conversion removes repetitive manual formatting work.
- **Quality improvement:** Structured Markdown improves AI answer accuracy and RAG
  retrieval relevance.
- **Security/compliance:** Fully offline operation avoids sending internal documents to
  external services, supporting banking-sector data protection requirements.
- **Strategic enablement:** Establishes a reusable document pipeline that can later feed
  embeddings, vector databases, and enterprise RAG initiatives (see Future Roadmap).

## 7. Assumptions

- Users operate on Windows 10/11 desktop machines within an enterprise network that may
  have **no or restricted internet access**.
- Users may not have local administrator rights; installation must be as simple as
  possible (ideally a single installer with minimal manual dependency setup).
- Source documents are primarily **digitally created** (not scanned images); OCR for
  scanned/image-based PDFs is out of scope for the initial release.
- Source documents are **not password-protected or encrypted** in the initial release.
- The application is single-user, desktop-only; no server, multi-user, or web
  component is required for the initial release.
- No database is required for the initial release — local file system and
  configuration files are sufficient (to be confirmed in the Data Model phase).
- **Documents are in English only for the MVP** (confirmed by stakeholder decision,
  2026-08-23). Non-English text (e.g., Bangla) is not a requirement for extraction,
  Markdown generation, or tokenization in the initial release.

## 8. Constraints

- Must operate **completely offline** — no document content may be transmitted to any
  external/third-party API in the initial release.
- Must run on Windows desktop using .NET 8 / WPF.
- Must be understandable and maintainable by a **mid-level developer** — architecture
  and code must favor simplicity over sophistication.
- Document processing relies on **Python libraries** that have no direct .NET
  equivalent of comparable quality (PDF/Office parsing); the .NET application must
  integrate with a Python engine through a well-defined, simple mechanism.
- No confidential or real customer/organizational documents may be used as test/sample
  data.

## 9. Dependencies

- .NET 8 runtime (desktop deployment).
- Python 3.x runtime and libraries: `pymupdf`, `python-docx`, `openpyxl`, `pandas`,
  `python-pptx`, `markdownify`, `tiktoken`.
- Enterprise IT approval for whatever Python distribution strategy is selected
  (bundled/embedded vs. externally installed) — this is a deployment risk if not
  resolved early (see Risks).
- Serilog (.NET logging), Microsoft.Extensions.DependencyInjection (DI).

## 10. Risks (Business-Level Summary)

A detailed technical risk register will be produced in `docs/18-RISK-ASSESSMENT.md`
during the Architecture phase. Key business-level risks identified now:

| Risk | Impact | Notes |
|---|---|---|
| Python runtime distribution complexity | High | If Python must be installed separately by IT, adoption friction increases significantly. Needs an architecture decision (Phase 07). |
| Tokenizer mismatch across AI providers | Medium | `tiktoken` does not exactly reflect Claude's or every provider's real tokenizer; must be clearly labeled as an estimate to avoid misleading users. |
| Table/complex-layout extraction fidelity | Medium | PDF table extraction is historically imperfect; may affect perceived quality. |
| Large file / large batch performance | Medium | 100 MB files and multi-hundred-file batches must not freeze the UI or exhaust memory. |
| Data sensitivity | High | As a banking-sector tool, any accidental network call or content logging would be a serious compliance issue. Must be verifiable (e.g., network-call audit during testing). |
| Telemetry/analytics SDKs slipping in via a dependency | Medium | Could silently violate the offline guarantee; must be explicitly banned and checked in code review (see NFR-012 in SRS). |
| Python executable path is user-configurable | Medium | Without validation, a misconfigured/malicious path could execute arbitrary code (see SEC-005 in SRS). |

*Note: multilingual (Bangla) content was raised as an open question in the original
draft and has been resolved — English only for MVP (see Section 7 above).*

## 11. Success Criteria

1. ≥ 95% of supported, non-corrupted, non-protected test documents convert
   successfully without manual intervention.
2. Users observe a measurable, displayed token reduction percentage for typical
   documents. Hypothesis target: **≥ 20% average reduction** on typical
   table/heading-heavy enterprise documents, to be validated (and adjusted if needed)
   during performance testing.
3. The application never makes an outbound network call during normal operation
   (verified via network monitoring in QA).
4. A non-technical user can complete a first successful conversion within
   approximately 5 minutes without external training, using only in-app guidance.
5. The UI remains responsive (no freeze, progress visible) while converting a single
   100 MB file or a batch of 100+ files.
6. Zero critical/blocking defects at release, per the Definition of Done.

## 12. Out of Scope (Business Level)

- Sending documents to any external AI/cloud API in the initial release.
- OCR / scanned document support (planned Phase 2).
- Multi-user, server, or web-hosted deployment.
- Embedding generation and vector database integration (Future Roadmap).
- Non-English / multilingual document support (English only for MVP — confirmed
  2026-08-23).

---

## 13. Revision Note (2026-08-23)

Following a Requirements Quality Review, this document was updated to: confirm
English-only scope, resolve the multilingual risk item, add a hypothesis target for
token-reduction success criteria, and flag two additional risks (telemetry SDKs,
unvalidated Python path) carried forward into `docs/03-SRS.md` as NFR-012 and SEC-005.

---

*Next document: `docs/02-PRD.md`*
