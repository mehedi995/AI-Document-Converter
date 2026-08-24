# 10 — Folder Structure

**Project:** AI Document Converter
**Status:** Draft — Phase 10 (Folder Structure) — Gate 2
**Date:** 2026-08-23

---

## 1. Solution Layout

```
AI.Document.Converter.sln

src/
├── AI.Document.Converter.Wpf/
│   ├── Views/
│   ├── ViewModels/
│   ├── Commands/
│   ├── Converters/
│   ├── Resources/
│   └── App.xaml / App.xaml.cs        (DI composition root)
│
├── AI.Document.Converter.Application/
│   ├── Interfaces/
│   ├── Services/
│   ├── Models/
│   └── DTOs/
│
├── AI.Document.Converter.Domain/
│   ├── Entities/
│   ├── Enums/
│   └── ValueObjects/
│
├── AI.Document.Converter.Infrastructure/
│   ├── DocumentProcessing/
│   │   ├── Pdf/
│   │   ├── Docx/
│   │   ├── Excel/
│   │   ├── PowerPoint/
│   │   └── Text/
│   ├── Python/
│   ├── FileSystem/
│   ├── Logging/
│   └── Configuration/
│
└── AI.Document.Converter.Python/
    ├── extractors/
    │   ├── pdf_extractor.py
    │   ├── docx_extractor.py
    │   ├── xlsx_extractor.py
    │   └── pptx_extractor.py
    ├── tokenizer.py
    ├── dispatch.py                    (JSON stdin/stdout entry point)
    ├── requirements.txt
    └── build/                         (PyInstaller spec + build script)

tests/
├── AI.Document.Converter.UnitTests/
│   ├── Application/
│   ├── Domain/
│   └── Infrastructure/
└── AI.Document.Converter.IntegrationTests/
    ├── DocumentProcessing/            (real sample files through the real Python engine)
    └── EndToEnd/                      (import → convert → chunk → export)

docs/
├── 01-BRD.md ... 22-BUG-TRACKER.md
└── adr/
    ├── ADR-001-python-integration.md
    ├── ADR-002-document-processing-architecture.md
    └── ADR-003-token-estimation-strategy.md

scripts/
├── build-python-engine.ps1            (invokes PyInstaller, outputs into src/.../bin)
└── package-installer.ps1              (future — Phase "Packaging")

samples/
├── sample.pdf
├── sample.docx
├── sample.xlsx
├── sample.pptx
└── sample.txt

CHANGELOG.md
README.md
```

## 2. Justification

- **Matches CLAUDE.md Sections 10 and 23 directly** — the recommended structure is
  followed as-is, since nothing about this project's requirements argues for
  deviating from it (no additional bounded contexts, no microservices, no
  multi-tenant concerns).
- **`AI.Document.Converter.Python` is a sibling `src/` folder, not a `.NET` project**
  — it has its own toolchain (PyInstaller) and its own dependency file
  (`requirements.txt`), but living under `src/` keeps it versioned and reviewed
  alongside the .NET code it's contractually tied to (the JSON schema in ADR-001),
  rather than being an unrelated top-level folder.
- **No `Repositories/` folder** — there is no database (`docs/09-DATA-MODEL.md`
  Section 1), so a repository abstraction would be pure ceremony (CLAUDE.md
  Section 8's warning against unnecessary interfaces).
- **`Infrastructure/DocumentProcessing/*` mirrors `AI.Document.Converter.Python/extractors/*`
  one-for-one** (Pdf↔pdf_extractor.py, Docx↔docx_extractor.py, etc.), so a developer
  working on PDF support knows exactly which two files to open regardless of which
  side of the process boundary they're debugging.
- **`tests/IntegrationTests` explicitly separates `DocumentProcessing` (exercises the
  real bundled Python engine against `samples/`) from `EndToEnd`** (exercises the
  full Application-layer pipeline) — this split lets the slower, Python-dependent
  tests be run/skipped independently of the faster in-process integration tests,
  which matters once CI is set up (`docs/16-TEST-STRATEGY.md`, future).
- **`scripts/` holds build automation, not application code** — keeps the Python
  packaging step (PyInstaller) out of the .NET build (`dotnet build`) so a
  mid-level developer can build/debug the WPF app without needing PyInstaller
  installed for routine .NET-only work; the Python engine is rebuilt explicitly or
  as a CI step.

## 3. Deviations From the CLAUDE.md Template

None. The template in CLAUDE.md Section 10 is adopted directly, with the one
addition (`AI.Document.Converter.Python/build/` and `scripts/build-python-engine.ps1`)
needed to operationalize the ADR-001 decision to bundle a PyInstaller-built
executable — this wasn't spelled out in the template but is a direct, minimal
consequence of that ADR.

---

*This completes the Phase 07–10 Architecture documentation set (Gate 2). Sprint
planning and the implementation plan (Gate 3/4) require explicit approval before
proceeding.*
