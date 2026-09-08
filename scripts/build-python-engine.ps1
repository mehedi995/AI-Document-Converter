<#
.SYNOPSIS
    Builds the bundled Python processing engine (ADR-001) into a single
    self-contained executable via PyInstaller.

.DESCRIPTION
    Produces src/AI.Document.Converter.Python/dist/AIDocumentConverter.PythonEngine/
    (the exe plus its unpacked dependencies). The WPF project
    (AI.Document.Converter.Wpf.csproj) copies this folder into its own build output
    automatically if it exists - re-run this script after changing any Python
    source, then rebuild the .NET solution to pick up the new build.

    Deliberately --onedir, not --onefile: a --onefile build re-extracts its entire
    payload to a temp directory on every single launch, which measured at ~5
    seconds per invocation once pandas/numpy/lxml were bundled in (Phase 3). Since
    ADR-001 starts one process per file, that cost would be paid on every file in
    every batch - --onedir unpacks once at build time instead, and starts in a
    fraction of a second.

    Not part of `dotnet build` on purpose (docs/10-FOLDER-STRUCTURE.md Section 2):
    a developer working only on the WPF/.NET side should not need PyInstaller
    installed just to compile C#.
#>

$ErrorActionPreference = "Stop"

# Both tools this script drives - pip and PyInstaller - write ordinary progress
# and INFO output to stderr even on a completely successful run. Under Windows
# PowerShell 5.1, $ErrorActionPreference = "Stop" turns any native command's
# stderr into a terminating NativeCommandError, so the script aborted on pip's
# first notice line. It could not be run at all until this was handled.
#
# Do NOT "fix" this with `2>&1`: in 5.1, redirecting a native command's stderr
# is itself what wraps each line in an ErrorRecord, which makes it worse rather
# than better (tried; it still failed). The exit code is the only reliable
# success signal for a native process, so relax the preference around the call
# and check $LASTEXITCODE afterwards.
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string] $Description,
        [Parameter(Mandatory = $true)][scriptblock] $Command
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Command
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

$pythonProjectDir = Join-Path $PSScriptRoot "..\src\AI.Document.Converter.Python"
$pythonProjectDir = Resolve-Path $pythonProjectDir

Push-Location $pythonProjectDir
try {
    Write-Host "Installing build dependencies (requirements-dev.txt)..."
    # requirements-dev.txt includes requirements.txt via -r, plus pyinstaller
    # and the dev-only pymupdf used by the benchmark and fixture generators.
    Invoke-Native "pip install" { python -m pip install --quiet -r requirements-dev.txt }

    Write-Host "Building AIDocumentConverter.PythonEngine (onedir)..."
    # --add-data bundles tiktoken_cache/ as a data folder (not source code) -
    # without it, tokenizer.py's TIKTOKEN_CACHE_DIR would point at a folder
    # that doesn't exist in the built exe, and tiktoken would fall back to
    # fetching over the network (see tokenizer.py's module docstring). The
    # source side must be absolute: --specpath build means PyInstaller
    # resolves a relative source against build\, not this script's CWD.
    # tiktoken discovers its encodings (o200k_base, cl100k_base) via pkgutil
    # plugin scanning over the tiktoken_ext namespace package, not a direct
    # `import` statement - invisible to PyInstaller's static analysis without
    # this explicit hidden-import, which fails at runtime with "Unknown
    # encoding" despite tiktoken itself being bundled correctly.
    #
    # dispatch.py imports each of the four extractor modules lazily via
    # importlib.import_module(), on demand per request, rather than eagerly
    # at module load (NFR-011: measured at ~86MB of process memory otherwise
    # paid by every single subprocess, including ones that only tokenize or
    # health-check). A dynamic importlib string is likewise invisible to
    # PyInstaller's static analysis, so each module needs its own
    # hidden-import too.
    # extractors.model carries the v2 contract helpers (warnings, block IDs,
    # version stamps). It is imported by every extractor, all of which are
    # themselves loaded dynamically, so it needs its own hidden-import too.
    #
    # The --exclude-module flags are not optimisation garnish, they are load-
    # bearing:
    #
    #   pymupdf / fitz    AGPL-3.0. MUST NOT ship - see THIRD-PARTY-NOTICES.md
    #                     section 1. It stays installed as a dev-only tool for
    #                     the benchmark and fixture generators, so PyInstaller
    #                     would otherwise happily bundle it. scripts/
    #                     check-licences.py fails the build if it reappears.
    #   numpy / pandas    Pulled in transitively through an optional openpyxl
    #                     import chain; nothing in this project imports either.
    #                     Excluding them took the bundle from 131 MB to 78 MB
    #                     (40%). openpyxl guards its numpy import in
    #                     compat/numbers.py and simply sets NUMPY = False, so
    #                     dropping it is safe for the plain str/int/float cell
    #                     values this engine reads. Verified after the change
    #                     against all five formats plus tokenize.
    $tiktokenCacheDir = Join-Path $pythonProjectDir "tiktoken_cache"
    Invoke-Native "PyInstaller" { python -m PyInstaller `
        --onedir `
        --name AIDocumentConverter.PythonEngine `
        --add-data "$tiktokenCacheDir;tiktoken_cache" `
        --hidden-import tiktoken_ext.openai_public `
        --hidden-import extractors.model `
        --hidden-import extractors.pdf_extractor `
        --hidden-import extractors.docx_extractor `
        --hidden-import extractors.xlsx_extractor `
        --hidden-import extractors.pptx_extractor `
        --exclude-module pymupdf `
        --exclude-module fitz `
        --exclude-module numpy `
        --exclude-module pandas `
        --distpath dist `
        --workpath build\pyinstaller-work `
        --specpath build `
        --noconfirm `
        dispatch.py }

    Write-Host "Built: $pythonProjectDir\dist\AIDocumentConverter.PythonEngine\AIDocumentConverter.PythonEngine.exe"
    Write-Host "Rebuild the .NET solution to copy it into the WPF app's output."
}
finally {
    Pop-Location
}
