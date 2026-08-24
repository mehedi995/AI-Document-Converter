<#
.SYNOPSIS
    Builds the bundled Python processing engine (ADR-001) into a single
    self-contained executable via PyInstaller.

.DESCRIPTION
    Produces src/AI.Document.Converter.Python/dist/AIDocumentConverter.PythonEngine.exe.
    The WPF project (AI.Document.Converter.Wpf.csproj) copies this file into its own
    build output automatically if it exists - re-run this script after changing any
    Python source, then rebuild the .NET solution to pick up the new executable.

    Not part of `dotnet build` on purpose (docs/10-FOLDER-STRUCTURE.md Section 2):
    a developer working only on the WPF/.NET side should not need PyInstaller
    installed just to compile C#.
#>

$ErrorActionPreference = "Stop"

$pythonProjectDir = Join-Path $PSScriptRoot "..\src\AI.Document.Converter.Python"
$pythonProjectDir = Resolve-Path $pythonProjectDir

Push-Location $pythonProjectDir
try {
    Write-Host "Installing build dependencies (pyinstaller + requirements.txt)..."
    python -m pip install --quiet --upgrade pyinstaller
    python -m pip install --quiet -r requirements.txt

    Write-Host "Building AIDocumentConverter.PythonEngine.exe..."
    python -m PyInstaller `
        --onefile `
        --name AIDocumentConverter.PythonEngine `
        --distpath dist `
        --workpath build\pyinstaller-work `
        --specpath build `
        --noconfirm `
        dispatch.py

    Write-Host "Built: $pythonProjectDir\dist\AIDocumentConverter.PythonEngine.exe"
    Write-Host "Rebuild the .NET solution to copy it into the WPF app's output."
}
finally {
    Pop-Location
}
