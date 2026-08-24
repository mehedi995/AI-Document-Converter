# 11 — Coding Standards

**Project:** AI Document Converter
**Status:** Draft — Phase 11 (Coding Standards) — Gate 4
**Date:** 2026-08-23

---

## 1. Naming

- **PascalCase:** classes, methods, properties, public fields, enum members.
- **camelCase:** local variables, method parameters, private fields (with `_`
  prefix, e.g. `_documentProcessor`).
- Every async method name ends in `Async` (`ExtractAsync`, `ConvertAsync`).
- Names describe what a thing *is* or *does* — no `Manager`/`Helper`/`Util` suffixes
  used as a substitute for a clear responsibility (CLAUDE.md Section 8's `IUserService`
  vs. `IUserServiceManager` example applies equally here — e.g., prefer
  `IChunkGenerator` over `IChunkHelper`).

## 2. Nullability & Access Modifiers

- Nullable reference types (`<Nullable>enable</Nullable>`) are enabled solution-wide.
- Every member has an explicit access modifier — never rely on the default.
- A nullable return (e.g., `DocumentMetadata.Author`) means "genuinely absent," not
  "caller should assume empty string" — see `docs/09-DATA-MODEL.md` Section 2 for
  why `Author` is omitted from front matter, not blanked, when null.

## 3. Documentation

- XML doc comments on every public interface and public method that crosses a
  project boundary (e.g., everything in `Application/Interfaces/`). Internal
  implementation details do not need XML comments unless a non-obvious constraint
  needs explaining (CLAUDE.md's "why, not what" rule — see project-wide comment
  policy below).
- Comments in method bodies explain **why**, never **what** — e.g., "PDF page order
  from pymupdf is not guaranteed for multi-column layouts, see
  docs/18-RISK-ASSESSMENT.md R-08" is a good comment; "loop through pages" is not.

## 4. No Magic Strings

- Error categories are the `ErrorCategory` enum (`docs/09-DATA-MODEL.md`), never
  bare strings compared with `==`.
- Settings keys are strongly-typed Options classes, never `IConfiguration["..."]`
  string lookups scattered through the codebase.
- File extensions, JSON field names for the Python contract (ADR-001), and log
  event templates are each defined once as constants and referenced everywhere else.

## 5. Size & Structure

- Guideline, not a hard gate: a class over ~300 lines or a method over ~40 lines is
  a signal to look for a missing abstraction — not an automatic rule violation.
  `IDocumentProcessor` implementations are the expected exception if a format's
  extraction logic is inherently long; even then, prefer splitting private helper
  methods over one large method.
- No regions used to hide length — if a class needs regions to stay readable, split
  the class.
- No dead code, no commented-out code blocks. Delete it; git history keeps it if
  ever needed again (CLAUDE.md Section 24).

## 6. Async & Cancellation

- Every method that touches the file system, a subprocess, or the Python engine is
  `async` and accepts a `CancellationToken` as its last parameter, propagated all the
  way down to `PythonEngineClient`'s process-kill logic (FR-037).
- Never `async void` except WPF event handlers, which immediately delegate to an
  `async Task` method wrapped in a top-level try/catch that logs and maps to
  FR-029.
- `IProgress<T>` is used for progress reporting (`BatchService` → ViewModel), never
  a raw event that assumes the UI thread.

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

## 7. Resource Management

- `Process` instances (the Python subprocess) are wrapped in `using`/`await using`
  and always have their stdin/stdout streams disposed even on the exception path.
- `FileStream`, `ZipArchive`, and any other `IDisposable` follow the same rule.
- `PythonEngineClient` implements `IAsyncDisposable` to guarantee subprocess cleanup
  if the client itself is disposed mid-operation (e.g., on cancellation).

## 8. Error Handling

- A single small exception hierarchy maps to `ErrorCategory`
  (`DocumentConversionException` with an `ErrorCategory` property), rather than one
  exception subtype per FR-029 category — enough to categorize without an
  unnecessary class explosion.
- Exceptions are caught at the Application-service boundary (e.g.,
  `ConversionService.ConvertAsync`), logged via Serilog with technical detail
  (never document content, per NFR-006/FR-035), and translated into a
  `ConversionResult` carrying a user-friendly message — they are not caught and
  silently swallowed anywhere.
- No `catch (Exception) { }` empty catch blocks.

## 9. Dependency Injection

- Constructor injection only. No service-locator pattern, no static singletons for
  injectable services.
- Interfaces live in `Application/Interfaces/`; implementations live in
  `Infrastructure/`; DI registration is centralized in `App.xaml.cs`, not scattered
  across the codebase.

## 10. Security-Sensitive Code

- Every file-path-accepting method calls the shared path-validation helper
  (SEC-002) before use — no ad hoc `Path.Combine` + direct I/O without validation.
- The Python executable path passes through the SEC-005 validator before
  `PythonEngineClient` ever starts a process with it.
- Subprocess arguments are passed as an argument array (`ProcessStartInfo.ArgumentList`)
  or via stdin — never string-concatenated into a single command line (SEC-006).
- No secrets, connection strings, or credentials appear in source or config files
  (none are expected in this application, but the rule stands for any future
  addition).

## 11. Testing Conventions

- Test project: xUnit (`docs/16-TEST-STRATEGY.md`).
- Test naming: `MethodName_Scenario_ExpectedResult` (e.g.,
  `ExtractAsync_CorruptedPdf_ThrowsCategorizedException`).
- Arrange/Act/Assert sections separated by a blank line (no explicit comments
  needed once the convention is known team-wide).
- One behavior per test; prefer several small tests over one large test asserting
  many unrelated outcomes.

---

*Next document: `docs/15-IMPLEMENTATION-PLAN.md`*
