# AI Document Converter

A professional, fully offline Windows desktop application that converts enterprise
documents (PDF, DOCX, XLSX, PPTX, TXT) into clean, structured, AI-ready Markdown —
reducing token usage and improving retrieval quality for use with AI assistants
(Claude, ChatGPT, Copilot, Azure OpenAI) and RAG/vector-search systems.

Built for enterprise and financial-institution use (primary stakeholder: Grameen
Bank), where documents cannot leave the organization's machines. No document content
is ever sent over the network.

## Project Status

**v1.0.0 — feature-complete, all 12 roadmap phases implemented and tested.** This
project followed a strict SDLC process (defined in `CLAUDE.md`) with explicit
approval gates, all of which are closed:

| Gate | Contents | Status |
|---|---|---|
| Gate 1 | Business Analysis & Requirements (`docs/01`–`06`) | ✅ Approved |
| Gate 2 | Architecture (`docs/07`–`10`, `docs/adr/`) | ✅ Approved |
| Gate 3 | Roadmap, Sprint Plan, TODO (`docs/12`–`14`) | ✅ Approved |
| Gate 4 | Coding Standards, Implementation Plan, Test Strategy, Unit Test Plan, Risk Assessment, Deployment Plan, Future Roadmap, Traceability, Bug Tracker (`docs/11`, `15`–`22`) | ✅ Approved |
| Gate 5 | Production Coding (`docs/12-DEVELOPMENT-ROADMAP.md` Phases 1–12) | ✅ Complete |

See `docs/14-TODO.md` for the full per-task status and `CHANGELOG.md` for what
shipped in each phase.

## Features

- Import single/multiple files, drag-and-drop, or a whole folder (PDF, DOCX, XLSX,
  PPTX, TXT).
- Converts each to structured Markdown preserving headings, tables, lists, links,
  and page/slide/sheet references, with YAML front matter.
- Displays original vs. converted token estimates (Claude-style and GPT-4o-style)
  and the reduction percentage.
- Generates AI-ready chunk files with configurable size/overlap, never splitting a
  table or separating a heading from its content.
- Batch processing with bounded parallelism, live per-file progress, and
  cancellation.
- Categorized, user-friendly error handling with a per-file Retry action.
- Exports a batch's output as a single ZIP (`markdown/`, `chunks/`, `metadata/`).
- Fully offline — verified, not just assumed (see `docs/16-TEST-STRATEGY.md`
  Section 5).

## Building and Running

Prerequisites: .NET 8 SDK, Python 3.13 with the packages in
`src/AI.Document.Converter.Python/requirements.txt` (only needed to build the
bundled engine, not to run the app afterward).

```powershell
# One-time: build the bundled Python engine (PyInstaller --onedir)
python -m pip install -r src\AI.Document.Converter.Python\requirements.txt
.\scripts\build-python-engine.ps1

# Build and run the app
dotnet build AI.Document.Converter.sln
dotnet run --project src\AI.Document.Converter.Wpf

# Run the test suite
dotnet test tests\AI.Document.Converter.UnitTests
dotnet test tests\AI.Document.Converter.IntegrationTests --filter "Category!=Performance"

# Performance benchmarks (run separately - see docs/03-SRS.md Section 8)
dotnet test tests\AI.Document.Converter.IntegrationTests --filter "Category=Performance"
```

## Packaging a Release

```powershell
.\scripts\package-release.ps1
```

Produces a self-contained `publish\AI.Document.Converter\` build, separated debug
symbols under `publish\symbols\`, and a portable ZIP. If Inno Setup 6 is installed
(`iscc` on `PATH`), it also compiles `scripts\installer.iss` into a Windows
installer. See `docs/19-DEPLOYMENT-PLAN.md` for the full release process, including
the code-signing step (requires the organization's own certificate — not automated).

## Key Design Decisions

- **Offline-first:** no outbound network calls anywhere in the application
  (`docs/03-SRS.md` NFR-001/002); verified live during a real conversion cycle, not
  just assumed (`docs/16-TEST-STRATEGY.md` Section 5).
- **Python integration:** document extraction uses Python libraries (pymupdf,
  python-docx, openpyxl, python-pptx, tiktoken), bundled as a self-contained
  application folder and invoked as a short-lived subprocess per file — no separate
  Python install required on the target machine
  (`docs/adr/ADR-001-python-integration.md`).
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
- `docs/22-BUG-TRACKER.md` — bug tracking log

## Contributing

See `docs/11-CODING-STANDARDS.md` for coding conventions and
`docs/15-IMPLEMENTATION-PLAN.md` for the per-feature class/interface breakdown.
`docs/18-RISK-ASSESSMENT.md` documents every non-obvious defect found during
development and why the fix works — read it before touching Python-engine
integration, tokenization, or batch/concurrency code.
