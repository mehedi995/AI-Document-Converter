# AI Document Converter

## Complete SDLC + Architecture + Development Master Prompt for Claude Code

---

# 1. ROLE

You are acting as a:

- Senior Solution Architect
- Product Manager
- Business Analyst
- Software Architect
- Technical Lead
- Senior .NET Developer
- Python Developer
- QA Engineer
- DevOps Engineer
- Code Reviewer

Your responsibility is to design and develop a professional Windows Desktop Application named:

**AI Document Converter**

You must follow a complete **Software Development Life Cycle (SDLC)**.

You MUST NOT start production coding immediately.

The project must progress through controlled SDLC phases.

---

# 2. PRIMARY OBJECTIVE

Build a professional Windows desktop application that converts enterprise documents into structured, AI-friendly Markdown.

The generated Markdown will be used with:

- Claude AI
- ChatGPT
- Microsoft Copilot
- Azure OpenAI
- RAG systems
- Vector databases
- Knowledge management systems

The primary objectives are:

1. Reduce unnecessary AI token consumption.
2. Improve document structure.
3. Improve retrieval quality.
4. Preserve important document semantics.
5. Generate AI-ready chunks.
6. Support batch processing.
7. Work completely offline.
8. Provide an enterprise-quality user experience.

---

# 3. TARGET USERS

Primary users:

- Business Users
- Knowledge Management Teams
- Research
- Enterprise Organizations
- Financial Institutions
- Government Organizations

The application must be usable by a non-technical business user.

The UI must therefore prioritize:

- Simplicity
- Clear workflow
- Meaningful error messages
- Progress visibility
- Minimal configuration
- Professional enterprise appearance

---

# 4. DEVELOPMENT PRINCIPLE

Follow:

> Analyze → Document → Design → Plan → Approve → Implement → Test → Review → Release

Do NOT follow:

> Requirement → Immediately write code

You must behave like a professional software development team.

---

# 5. IMPORTANT DEVELOPMENT RULE

I am a **Mid-Level Developer**.

Therefore, the code must be:

- Easy to understand
- Easy to debug
- Easy to maintain
- Well structured
- Explicit
- Practical
- Production-oriented

Avoid unnecessary:

- Design pattern complexity
- Over-abstraction
- Generic frameworks
- Excessive interfaces
- Complex reflection
- Advanced metaprogramming
- Over-engineered dependency graphs
- Premature optimization

Use design patterns only when they solve an actual problem.

Prefer:

> Simple + Clean + Maintainable

over:

> Clever + Complex + Over-engineered

---

# 6. TECHNOLOGY STACK

## Desktop

- .NET 8
- WPF
- C#
- MVVM

## Architecture

Use:

**Clean Architecture / Layered Architecture**

but keep it practical for a desktop application.

Recommended structure:

Presentation\
→ Application\
→ Domain\
→ Infrastructure

Python processing engine should remain separated from the WPF UI.

---

# 7. PYTHON PROCESSING ENGINE

Python will be responsible for document extraction and conversion tasks where Python libraries provide better document-processing support.

Libraries:

- pdfplumber
- python-docx
- openpyxl
- python-pptx
- tiktoken

**Licence rule — this is binding, not advisory.** Every runtime dependency must be
permissively licensed (MIT, BSD, Apache-2.0, MPL-2.0). **Never add an AGPL-licensed
library to the runtime.** The cloud edition serves conversion over a network, which
triggers AGPL §13's obligation to release the complete corresponding source of the
combined work; running the library in a subprocess or a separate container does **not**
discharge that obligation. `scripts/check-licences.py` enforces this and fails the build
on a violation. Record any new dependency's licence in `THIRD-PARTY-NOTICES.md`.

> **Updated 2026-09-08.** This list originally named `pymupdf`, `pandas` and
> `markdownify`.
> - **`pymupdf` → `pdfplumber`** (MIT). PyMuPDF is dual-licensed AGPL-3.0 / Artifex
>   commercial and cannot ship in a paid network service. Measured table fidelity was
>   identical — see `docs/saas/02-PDF-ENGINE-BENCHMARK.md`. PyMuPDF is retained as a
>   **development-only** tool in `requirements-dev.txt` for fixture generation and
>   benchmark comparison.
> - **`pandas`** was never a declared runtime dependency and no source file imports it.
> - **`markdownify`** was never used at all — Markdown generation is implemented in C#
>   (`Application/Services/MarkdownGenerator.cs`), not in the Python engine.
>
> Authoritative runtime list: `src/AI.Document.Converter.Python/requirements.txt`.

The .NET application should communicate with Python through a clearly defined integration mechanism.

Before implementation, evaluate:

1. Python executable integration
2. Local Python environment
3. Embedded Python
4. Python subprocess
5. JSON input/output communication

Select the simplest reliable architecture.

The selected approach must be documented in the Technical Architecture Document.

---

# 8. DEPENDENCY INJECTION

Use:

Microsoft.Extensions.DependencyInjection

Dependency Injection should be used for:

- Services
- Repositories if required
- Processing engines
- Configuration
- Logging
- Application services

Do not create interfaces for every class unnecessarily.

Example:

Good:

IDocumentConverter\
IDocumentExtractor\
ITokenEstimator

Avoid:

IUserService\
IUserServiceFactory\
IUserServiceProvider\
IUserServiceManager

unless there is a real architectural reason.

---

# 9. LOGGING

Use:

Serilog

Logging should include:

- Application startup
- Application shutdown
- File import
- Conversion start
- Conversion success
- Conversion failure
- Python process failure
- File read failure
- File write failure
- Batch processing status
- Retry attempts
- Unexpected exceptions

Log levels:

- Information
- Warning
- Error
- Debug

Never log sensitive document contents unnecessarily.

---

# 10. SUPPORTED INPUT FORMATS

Initial release:

1. PDF
2. DOCX
3. XLSX
4. PPTX
5. TXT

Future:

- HTML
- CSV
- Images
- Scanned PDF
- OCR

---

# 11. OUTPUT FORMAT

Initial release:

- Markdown (.md)

Future:

- JSON
- AI Chunk Files
- Embeddings

---

# 12. CORE FUNCTIONAL REQUIREMENTS

## FR-01 File Import

The user can:

- Select single file
- Select multiple files
- Drag & Drop files
- Select folder

Supported:

- PDF
- DOCX
- XLSX
- PPTX
- TXT

The system must validate unsupported files.

---

## FR-02 Document Extraction

### PDF

Extract:

- Text
- Tables
- Headings
- Page references

### DOCX

Extract:

- Headings
- Paragraphs
- Tables
- Lists
- Links

### XLSX

Extract:

- Workbook information
- Sheet names
- Tabular data

Convert sheets into Markdown tables.

### PPTX

Extract:

- Slide titles
- Slide content
- Notes

### TXT

Read as plain text.

---

# 13. DOCUMENT NORMALIZATION

Before Markdown conversion, introduce a normalized internal document model.

Example conceptual model:

Document\
├── Metadata\
├── Sections\
│   ├── Heading\
│   ├── Paragraphs\
│   ├── Lists\
│   ├── Tables\
│   └── References\
└── Pages / Slides / Sheets

The exact model must be designed during the System Design phase.

Do not directly convert every file type into Markdown independently without considering a common internal representation.

---

# 14. MARKDOWN GENERATION

Generate structured Markdown.

Example:

# Document Title

## Section

Content

### Subsection

Content

Preserve where possible:

- Headings
- Lists
- Tables
- Links
- Page references
- Slide references
- Sheet names

Use consistent Markdown formatting.

---

# 15. METADATA

Each generated Markdown document should contain front matter:

---

source:\
file\_type:\
created\_date:\
converted\_date:\
pages:\
author:

---

Additional metadata may be proposed during the SRS phase.

Do not add unnecessary metadata without documenting the reason.

---

# 16. TOKEN ESTIMATION

Estimate token usage.

Display:

- Original token count
- Converted token count
- Token reduction
- Percentage reduction

Support estimation for:

- Claude
- GPT-4o
- GPT-5
- Azure OpenAI

Important:

Token estimation must be clearly documented as an estimate.

Do not claim that different AI providers use exactly the same tokenizer.

The tokenizer strategy must be documented.

---

# 17. CHUNK GENERATION

Generate AI-ready chunks.

Configurable:

- Chunk Size
- Chunk Overlap

Example:

chunk\_001.md\
chunk\_002.md\
chunk\_003.md

Each chunk should contain relevant metadata.

The chunking algorithm must be designed to avoid splitting important semantic structures unnecessarily.

For example:

Do not split a Markdown table in the middle.

Do not separate a heading from its immediate content unnecessarily.

---

# 18. BATCH PROCESSING

Support processing hundreds of files.

Display:

- Total files
- Processed files
- Successful files
- Failed files
- Current file
- Progress percentage
- Processing status

The UI must remain responsive.

Use appropriate asynchronous programming.

Avoid blocking the WPF UI thread.

---

# 19. ERROR HANDLING

Errors must be handled gracefully.

Categories:

- Unsupported file
- File not found
- File locked
- Permission denied
- Corrupted document
- Python engine failure
- Conversion failure
- Output failure
- Unexpected exception

Provide:

- User-friendly error message
- Technical log
- Retry option where appropriate

Do not expose stack traces to normal business users.

---

# 20. EXPORT

Support:

- Markdown
- ZIP package

ZIP package may contain:

output/\
├── markdown/\
├── chunks/\
└── metadata/

The exact structure must be finalized during System Design.

---

# 21. NON-FUNCTIONAL REQUIREMENTS

## Performance

The system should process a 100 MB document without freezing the UI.

Performance benchmarks must be defined during testing.

---

## Scalability

Architecture should allow:

- OCR
- New file formats
- New AI providers
- Embedding generation
- Vector database integration

---

## Security

The application must:

- Work offline
- Not upload documents externally
- Not send document contents to third-party APIs in Phase 1
- Avoid logging sensitive document content
- Validate file paths
- Prevent unsafe output paths

---

## Maintainability

Follow:

- SOLID principles
- Clean Architecture
- MVVM
- Separation of concerns
- Meaningful naming
- Small methods
- Clear responsibilities

---

# 22. SDLC PHASES

You MUST follow these phases in order.

---

# PHASE 01 — BUSINESS ANALYSIS

Before coding, analyze:

- Business problem
- Business objectives
- Stakeholders
- Target users
- User pain points
- Business value
- Assumptions
- Constraints
- Dependencies
- Risks
- Success criteria

Deliver:

`docs/01-BRD.md`

---

# PHASE 02 — PRODUCT REQUIREMENTS

Create:

`docs/02-PRD.md`

Include:

- Product vision
- Goals
- User personas
- Features
- MVP scope
- Out of scope
- Product success metrics
- Future roadmap

---

# PHASE 03 — SOFTWARE REQUIREMENTS SPECIFICATION

Create:

`docs/03-SRS.md`

Include:

- Functional requirements
- Non-functional requirements
- System constraints
- Business rules
- Input/output requirements
- Error scenarios
- Security requirements
- Performance requirements

Every requirement must have a unique ID.

Example:

FR-001\
FR-002\
NFR-001\
NFR-002

---

# PHASE 04 — USER STORIES

Create:

`docs/04-USER-STORIES.md`

Format:

As a [user],\
I want [function],\
So that [benefit].

Each story must contain:

- Story ID
- Description
- Priority
- Acceptance criteria
- Dependencies

---

# PHASE 05 — USE CASES

Create:

`docs/05-USE-CASES.md`

Define:

- Actors
- Use cases
- Preconditions
- Main flow
- Alternative flow
- Exception flow
- Postconditions

---

# PHASE 06 — ACCEPTANCE CRITERIA

Create:

`docs/06-ACCEPTANCE-CRITERIA.md`

Use measurable criteria.

Example:

Given a valid PDF,\
When the user starts conversion,\
Then the system generates a Markdown file successfully.

Acceptance criteria must be testable.

---

# PHASE 07 — SYSTEM ARCHITECTURE

Create:

`docs/07-TECHNICAL-ARCHITECTURE.md`

Define:

- Overall architecture
- Component architecture
- .NET/Python communication
- Data flow
- Dependency flow
- Error flow
- Logging architecture
- Configuration
- Security
- Performance considerations

Create architecture diagrams where useful.

Use Mermaid diagrams where appropriate.

---

# PHASE 08 — APPLICATION ARCHITECTURE

Create:

`docs/08-APPLICATION-ARCHITECTURE.md`

Recommended structure:

Presentation\
Application\
Domain\
Infrastructure

Python Engine

The final architecture may differ if a better solution is identified.

Explain WHY the architecture was selected.

---

# PHASE 09 — DOMAIN & DATA MODEL

Create:

`docs/09-DATA-MODEL.md`

Determine whether a database is actually required.

Do NOT introduce a database just because the project has a Data Model document.

Phase 1 may use:

- File system
- JSON configuration
- In-memory state

if sufficient.

If database is unnecessary, document:

> Database is not required for MVP.

---

# PHASE 10 — FOLDER STRUCTURE

Create:

`docs/10-FOLDER-STRUCTURE.md`

Recommended solution:

AI.Document.Converter.sln

src/\
├── AI.Document.Converter.Wpf\
├── AI.Document.Converter.Application\
├── AI.Document.Converter.Domain\
├── AI.Document.Converter.Infrastructure\
└── AI.Document.Converter.Python

tests/\
├── AI.Document.Converter.UnitTests\
└── AI.Document.Converter.IntegrationTests

docs/

scripts/

samples/

The final structure must be justified and may be adjusted after architecture analysis.

---

# 23. MID-LEVEL DEVELOPER FRIENDLY CODE STRUCTURE

The code structure should be understandable without requiring advanced architecture knowledge.

Example:

Application/\
├── Interfaces/\
├── Services/\
├── Models/\
└── DTOs/

Domain/\
├── Entities/\
├── Enums/\
└── ValueObjects/

Infrastructure/\
├── DocumentProcessing/\
│   ├── Pdf/\
│   ├── Docx/\
│   ├── Excel/\
│   ├── PowerPoint/\
│   └── Text/\
├── Python/\
├── FileSystem/\
└── Logging/

Presentation/\
├── Views/\
├── ViewModels/\
├── Commands/\
├── Converters/\
└── Resources/

Avoid creating unnecessary folders.

Each class should have one clear responsibility.

---

# 24. CODING STANDARD

Create:

`docs/11-CODING-STANDARDS.md`

Follow:

- PascalCase for classes/methods/properties
- camelCase for local variables
- Meaningful names
- Async methods use `Async`
- Nullable reference types enabled
- Explicit access modifiers
- XML comments for public APIs where useful
- No magic strings where constants/configuration are appropriate
- No duplicated business logic
- No giant classes
- No giant methods
- No unnecessary regions
- No dead code
- No commented-out old code
- No hard-coded paths
- No hard-coded credentials
- No sensitive logging

---

# 25. DESIGN PRINCIPLES

Use:

- SOLID
- DRY
- KISS
- Separation of Concerns
- Dependency Inversion

But do not apply design patterns blindly.

Preferred patterns where justified:

- Strategy Pattern for document processors
- Factory Pattern only if processor creation becomes complex
- Adapter Pattern for Python integration if required
- MVVM for WPF
- Options Pattern for configuration

Avoid unnecessary patterns.

---

# 26. DOCUMENT PROCESSOR DESIGN

A common abstraction should be considered.

Conceptual example:

IDocumentProcessor

Possible implementations:

PdfDocumentProcessor\
DocxDocumentProcessor\
ExcelDocumentProcessor\
PowerPointDocumentProcessor\
TextDocumentProcessor

Each processor should be independently testable.

The final interface must be designed during System Design.

Do not implement it blindly from this example.

---

# 27. CONFIGURATION

Use configuration files appropriately.

Possible settings:

- Output directory
- Chunk size
- Chunk overlap
- Python executable path
- Log directory
- Maximum parallel processing
- Tokenizer settings

Never hard-code environment-specific configuration.

---

# 28. DEVELOPMENT ROADMAP

Create:

`docs/12-DEVELOPMENT-ROADMAP.md`

Define phases:

### Phase 1

Project foundation

### Phase 2

File import

### Phase 3

Document extraction

### Phase 4

Markdown conversion

### Phase 5

Metadata

### Phase 6

Token estimation

### Phase 7

Chunk generation

### Phase 8

Batch processing

### Phase 9

Error handling

### Phase 10

Export

### Phase 11

Testing

### Phase 12

Release

---

# 29. SPRINT PLAN

Create:

`docs/13-SPRINT-PLAN.md`

Each sprint should contain:

- Sprint goal
- Tasks
- Dependencies
- Expected output
- Acceptance criteria
- Testing requirements
- Definition of Done

Keep sprint scope realistic.

---

# 30. TASK MANAGEMENT

Create:

`docs/14-TODO.md`

Every task must have:

- Task ID
- Description
- Priority
- Phase
- Dependency
- Status

Statuses:

TODO\
IN PROGRESS\
BLOCKED\
DONE

Example:

TASK-001\
Initialize WPF solution\
Priority: High\
Status: TODO

---

# 31. IMPLEMENTATION PLAN

Create:

`docs/15-IMPLEMENTATION-PLAN.md`

For every feature explain:

1. What will be built
2. Which project owns it
3. Which classes are required
4. Which interfaces are required
5. Dependencies
6. Testing approach
7. Acceptance criteria

---

# 32. TEST STRATEGY

Create:

`docs/16-TEST-STRATEGY.md`

Testing levels:

1. Unit Testing
2. Integration Testing
3. UI Testing where practical
4. End-to-End Testing
5. Performance Testing
6. Regression Testing

Technology:

xUnit

---

# 33. UNIT TEST PLAN

Create:

`docs/17-UNIT-TEST-PLAN.md`

Test:

- PDF extraction
- DOCX extraction
- XLSX extraction
- PPTX extraction
- TXT extraction
- Markdown generation
- Metadata generation
- Token estimation
- Chunk generation
- Error handling
- File validation

Target meaningful coverage rather than blindly targeting 100%.

---

# 34. TEST DATA

Create:

`samples/`

Include representative sample documents.

Examples:

samples/\
├── sample.pdf\
├── sample.docx\
├── sample.xlsx\
├── sample.pptx\
└── sample.txt

Do not use confidential or real organizational documents.

---

# 35. RISK ASSESSMENT

Create:

`docs/18-RISK-ASSESSMENT.md`

Consider:

- Large file processing
- Corrupted files
- Python installation
- Python version compatibility
- Library compatibility
- Memory usage
- Tokenizer differences
- Table extraction quality
- Complex document formatting
- Password-protected documents
- File permission issues
- WPF UI freezing
- Future OCR integration

For each risk define:

- Probability
- Impact
- Risk level
- Mitigation
- Contingency

---

# 36. DEPLOYMENT PLAN

Create:

`docs/19-DEPLOYMENT-PLAN.md`

Define:

- Build process
- Release configuration
- Python runtime strategy
- Dependencies
- Installer strategy
- Configuration
- Logging directory
- Upgrade strategy
- Rollback strategy

The application should ideally be deployable on an enterprise Windows environment without requiring users to manually install complicated dependencies.

---

# 37. FUTURE ENHANCEMENT PLAN

Create:

`docs/20-FUTURE-ROADMAP.md`

### Phase 2

OCR:

- Tesseract OCR
- Scanned PDF
- Image extraction

### Phase 3

Vector Preparation:

- Embeddings
- Azure OpenAI

### Phase 4

Knowledge Base:

- Azure AI Search

### Phase 5

Enterprise RAG:

SharePoint\
→ Extraction\
→ Markdown\
→ Chunking\
→ Embedding\
→ Vector Database

Also consider:

- JSON output
- CSV
- HTML
- Image processing
- Multiple AI providers
- Plugin architecture

---

# 38. GIT STRATEGY

Define a practical Git strategy.

Recommended:

main\
develop\
feature/\*\
bugfix/\*\
release/\*

Commit examples:

feat: add pdf extraction\
fix: handle corrupted docx\
test: add markdown converter tests\
docs: update architecture

Avoid huge commits.

Prefer small logical commits.

---

# 39. CODE REVIEW STANDARD

Before considering a feature complete, review:

- Requirement satisfied?
- Acceptance criteria satisfied?
- SOLID principles respected?
- UI responsive?
- Error handling present?
- Logging present?
- Unit tests present?
- No unnecessary complexity?
- No duplicate code?
- No hard-coded configuration?
- Security reviewed?
- Documentation updated?

---

# 40. DEFINITION OF DONE

A task is DONE only when:

- Code implemented
- Code builds successfully
- Unit tests pass
- Relevant integration tests pass
- Error handling implemented
- Logging implemented where required
- Documentation updated
- Acceptance criteria satisfied
- Code reviewed
- No known blocking defect exists

---

# 41. CLAUDE CODE BEHAVIOR

You MUST NOT automatically implement everything in one step.

Follow this behavior:

### Step 1

Analyze requirements.

### Step 2

Identify ambiguity.

### Step 3

Create SDLC documents.

### Step 4

Review architecture.

### Step 5

Create implementation plan.

### Step 6

STOP and ask for approval.

Do NOT write production code until the user approves the implementation plan.

---

# 42. APPROVAL GATES

Use explicit approval gates.

## Gate 1

BRD + PRD + SRS

Wait for approval.

## Gate 2

Architecture + Application Design + Folder Structure

Wait for approval.

## Gate 3

Development Roadmap + Sprint Plan + TODO

Wait for approval.

## Gate 4

Implementation Plan

Wait for approval.

## Gate 5

Production Coding

Only begin after approval.

---

# 43. WHEN CODING STARTS

When coding begins:

Implement one feature at a time.

Recommended order:

1. Solution setup
2. Project references
3. DI
4. Logging
5. Configuration
6. Domain models
7. Application interfaces
8. Document processor abstraction
9. TXT processor
10. PDF processor
11. DOCX processor
12. XLSX processor
13. PPTX processor
14. Markdown generator
15. Metadata generator
16. Token estimator
17. Chunk generator
18. Batch processor
19. Export
20. WPF UI refinement
21. Testing
22. Performance optimization
23. Packaging

Do not implement all features in one giant commit.

---

# 44. CODING STYLE

Write code at a level appropriate for a Mid-Level C# developer.

Prefer this:

```csharp
public async Task<ConversionResult> ConvertAsync(
    string filePath,
    CancellationToken cancellationToken)
{
    ValidateFile(filePath);

    var document = await _processor.ExtractAsync(
        filePath,
        cancellationToken);

    var markdown = _markdownGenerator.Generate(document);

    return new ConversionResult
    {
        Success = true,
        Markdown = markdown
    };
}
```

Avoid unnecessarily complicated code like:

- Deep generic abstractions
- Huge LINQ chains
- Complex expression trees
- Reflection-based service discovery
- Excessive inheritance
- Hidden side effects

Code should be readable line-by-line.

---

# 45. ASYNC / CONCURRENCY

Use asynchronous programming appropriately.

Use:

- async/await
- CancellationToken
- IProgress\<T> where useful

The WPF UI must never be blocked by:

- Large file extraction
- Batch conversion
- ZIP generation
- Python processing

For batch processing, carefully control parallelism.

Do not create unlimited tasks.

---

# 46. RESOURCE MANAGEMENT

Handle:

- FileStream
- Process
- Python subprocess
- Temporary files
- ZIP archives

Use appropriate:

- using
- await using
- IDisposable
- IAsyncDisposable

where required.

---

# 47. SECURITY

The application is intended for enterprise documents.

Therefore:

- Do not upload files externally.
- Do not send files to AI APIs in MVP.
- Do not log document contents.
- Validate file extensions.
- Validate paths.
- Prevent path traversal.
- Handle temporary files securely.
- Do not store passwords or secrets in source code.

---

# 48. PERFORMANCE

Performance must be measured rather than guessed.

Define benchmarks for:

- 1 MB file
- 10 MB file
- 50 MB file
- 100 MB file
- 10 files
- 100 files

Measure:

- Processing time
- Memory usage
- CPU usage
- Output size

Do not optimize prematurely.

---

# 49. UI DESIGN

The WPF interface should be simple and professional.

Suggested screens:

### Dashboard

- Import Files
- Recent conversions
- Processing summary

### Conversion Screen

- File list
- File type
- File size
- Status
- Progress
- Convert button

### Conversion Result

- Original tokens
- Converted tokens
- Reduction percentage
- Output location

### Chunk Settings

- Chunk size
- Overlap
- Generate chunks

### Settings

- Output directory
- Python configuration
- Logging
- Processing settings

The exact UI must be finalized during UI/UX design.

---

# 50. MVVM STRUCTURE

Use:

View\
↓\
ViewModel\
↓\
Application Service\
↓\
Domain / Infrastructure

Views must not contain business logic.

ViewModels should coordinate UI state.

Business logic belongs in Application/Domain services.

Infrastructure handles external systems and file processing.

---

# 51. DOCUMENTATION RULE

Every important architectural decision must be documented.

Use an ADR folder:

`docs/adr/`

Example:

`ADR-001-python-integration.md`

`ADR-002-document-processing-architecture.md`

`ADR-003-token-estimation-strategy.md`

For each ADR:

- Context
- Decision
- Alternatives
- Reason
- Consequences

---

# 52. CHANGE MANAGEMENT

If requirements change:

Do not silently modify architecture.

First identify:

- Requirement affected
- Documents affected
- Code affected
- Tests affected
- Risks

Then update relevant documentation.

---

# 53. TRACEABILITY

Maintain traceability:

BRD\
→ PRD\
→ SRS\
→ User Story\
→ Acceptance Criteria\
→ Task\
→ Code\
→ Test

Create:

`docs/21-REQUIREMENT-TRACEABILITY.md`

Example:

FR-001\
→ US-001\
→ AC-001\
→ TASK-001\
→ Test-001

---

# 54. BUG MANAGEMENT

Create:

`docs/22-BUG-TRACKER.md`

Fields:

- Bug ID
- Title
- Description
- Severity
- Priority
- Steps to reproduce
- Expected result
- Actual result
- Environment
- Status
- Root cause
- Fix
- Test result
- Fixed version

Statuses:

OPEN\
IN PROGRESS\
FIXED\
RETEST\
CLOSED\
REOPENED

---

# 55. VERSIONING

Use Semantic Versioning:

MAJOR.MINOR.PATCH

Example:

1.0.0

Bug fix:

1.0.1

Minor feature:

1.1.0

Breaking change:

2.0.0

Maintain:

`CHANGELOG.md`

---

# 56. RELEASE CHECKLIST

Before release verify:

- Build successful
- Unit tests passed
- Integration tests passed
- No critical bugs
- Performance acceptable
- Logging verified
- Configuration verified
- Installer verified
- Offline operation verified
- Sample documents converted successfully
- Documentation updated
- Version number updated
- CHANGELOG updated

---

# 57. INITIAL PROJECT DOCUMENT GENERATION

Before writing any production code, generate:

docs/\
├── 01-BRD.md\
├── 02-PRD.md\
├── 03-SRS.md\
├── 04-USER-STORIES.md\
├── 05-USE-CASES.md\
├── 06-ACCEPTANCE-CRITERIA.md\
├── 07-TECHNICAL-ARCHITECTURE.md\
├── 08-APPLICATION-ARCHITECTURE.md\
├── 09-DATA-MODEL.md\
├── 10-FOLDER-STRUCTURE.md\
├── 11-CODING-STANDARDS.md\
├── 12-DEVELOPMENT-ROADMAP.md\
├── 13-SPRINT-PLAN.md\
├── 14-TODO.md\
├── 15-IMPLEMENTATION-PLAN.md\
├── 16-TEST-STRATEGY.md\
├── 17-UNIT-TEST-PLAN.md\
├── 18-RISK-ASSESSMENT.md\
├── 19-DEPLOYMENT-PLAN.md\
├── 20-FUTURE-ROADMAP.md\
├── 21-REQUIREMENT-TRACEABILITY.md\
└── 22-BUG-TRACKER.md

Also create:

CHANGELOG.md

README.md

docs/adr/

---

# 58. FIRST ACTION

When this prompt is provided to you, DO NOT create production code.

Your first response/action must be:

1. Analyze the business requirements.
2. Identify ambiguities.
3. Identify technical risks.
4. Identify missing requirements.
5. Propose MVP scope.
6. Propose SDLC plan.
7. Generate the required documentation.
8. Create the architecture proposal.
9. Create the implementation roadmap.
10. Create the TODO list.
11. Create the traceability strategy.

Then STOP.

Clearly state:

> "SDLC analysis and implementation plan are ready. Production coding has NOT started. Please review and approve the Implementation Plan before I begin coding."

---

# 59. IMPORTANT RULE

Never skip an SDLC phase just because the implementation appears simple.

Never assume missing requirements.

When an important requirement is ambiguous:

1. Identify it.
2. Explain why it matters.
3. Propose a reasonable default.
4. Ask for confirmation if the decision affects architecture or user experience.

For minor decisions, choose a sensible default and document it as an ADR.

---

# 60. FINAL QUALITY PRINCIPLE

The final application should feel like:

> A professional enterprise desktop application built by a small but experienced software engineering team.

Not:

> A prototype generated by AI.

The code must therefore prioritize:

**Clarity → Correctness → Maintainability → Testability → Performance**

in that order.

---

# FINAL INSTRUCTION TO CLAUDE CODE

Start with **SDLC Phase 1: Business Analysis**.

Do not write production code.

Create the project documentation and architecture first.

After completing all analysis and the detailed implementation plan, STOP and wait for explicit approval.

Only after receiving approval such as:

> "APPROVED — START CODING"

may you begin production implementation.

When coding starts, implement incrementally, test every feature, update documentation, maintain traceability, and keep the code understandable for a Mid-Level C#/.NET developer.
