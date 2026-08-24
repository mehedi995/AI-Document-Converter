# AI Document Converter

A professional, fully offline Windows desktop application that converts enterprise
documents (PDF, DOCX, XLSX, PPTX, TXT) into clean, structured, AI-ready Markdown —
reducing token usage and improving retrieval quality for use with AI assistants
(Claude, ChatGPT, Copilot, Azure OpenAI) and RAG/vector-search systems.

Built for enterprise and financial-institution use (primary stakeholder: Grameen
Bank), where documents cannot leave the organization's machines. No document content
is ever sent over the network.

## Project Status

**Pre-implementation.** This project follows a strict SDLC process (defined in
`CLAUDE.md`) with explicit approval gates. As of 2026-08-23:

| Gate | Contents | Status |
|---|---|---|
| Gate 1 | Business Analysis & Requirements (`docs/01`–`06`) | ✅ Approved |
| Gate 2 | Architecture (`docs/07`–`10`, `docs/adr/`) | ✅ Approved |
| Gate 3 | Roadmap, Sprint Plan, TODO (`docs/12`–`14`) | ✅ Approved |
| Gate 4 | Coding Standards, Implementation Plan, Test Strategy, Unit Test Plan, Risk Assessment, Deployment Plan, Future Roadmap, Traceability, Bug Tracker (`docs/11`, `15`–`22`) | ✅ Approved |
| Gate 5 | Production Coding | ⏳ Not started |

No `.sln`, no source code, and no dependencies have been installed yet. All work so
far is documentation under `docs/`.

## Key Design Decisions

- **Offline-first:** no outbound network calls anywhere in the application
  (`docs/03-SRS.md` NFR-001/002); verified, not just assumed.
- **Python integration:** document extraction uses Python libraries (pymupdf,
  python-docx, openpyxl, python-pptx, tiktoken), bundled as a self-contained
  executable and invoked as a short-lived subprocess per file — no separate Python
  install required on the target machine (`docs/adr/ADR-001-python-integration.md`).
- **Token estimates, not exact counts:** displayed token counts are clearly labeled
  as estimates and are never claimed to match a specific provider's real tokenizer
  exactly (`docs/adr/ADR-003-token-estimation-strategy.md`).
- **No database:** settings persist to a local JSON file; there is no
  conversion-history store in the MVP (`docs/09-DATA-MODEL.md`).
- **English only for MVP:** non-English document support is not a current
  requirement (see `docs/01-BRD.md` Section 7).

## Technology Stack

- **Desktop:** .NET 8, WPF, MVVM
- **Document Processing:** Python (pymupdf, python-docx, openpyxl, pandas,
  python-pptx, markdownify, tiktoken), bundled via PyInstaller
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Logging:** Serilog
- **Testing:** xUnit

## Documentation Map

All SDLC documentation lives under `docs/`, numbered in the order CLAUDE.md
specifies:

- `docs/01-BRD.md` – `docs/06-ACCEPTANCE-CRITERIA.md` — business & requirements
- `docs/07-TECHNICAL-ARCHITECTURE.md` – `docs/10-FOLDER-STRUCTURE.md` — architecture
- `docs/adr/` — architecture decision records
- `docs/11-CODING-STANDARDS.md` — coding conventions
- `docs/12-DEVELOPMENT-ROADMAP.md` – `docs/14-TODO.md` — planning
- `docs/15-IMPLEMENTATION-PLAN.md` – `docs/19-DEPLOYMENT-PLAN.md` — implementation
  readiness (test strategy, unit test plan, risk register, deployment plan)
- `docs/20-FUTURE-ROADMAP.md` — post-MVP roadmap
- `docs/21-REQUIREMENT-TRACEABILITY.md` — requirement → story → test mapping
- `docs/22-BUG-TRACKER.md` — bug tracking template (empty until Gate 5)

## Contributing

Production coding has not started. Once Gate 5 is approved, see
`docs/11-CODING-STANDARDS.md` for coding conventions and
`docs/15-IMPLEMENTATION-PLAN.md` for what to build first.
