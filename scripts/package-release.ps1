<#
.SYNOPSIS
    Builds the release-ready, self-contained publish output plus both
    distribution artifacts (docs/19-DEPLOYMENT-PLAN.md Section 5): a portable
    ZIP always, and a signed-ready Inno Setup installer if ISCC is available.

.DESCRIPTION
    Run from the repo root: scripts\package-release.ps1
    Prerequisite: scripts\build-python-engine.ps1 must already have been run
    at least once (this script does not rebuild the Python engine itself -
    it only bundles whatever is already at
    src\AI.Document.Converter.Python\dist\AIDocumentConverter.PythonEngine\).

    Steps:
      1. `dotnet publish` the WPF app as a self-contained win-x64 build
         (Release configuration) - the .csproj's Exists() condition then
         copies the already-built Python engine folder into the output.
      2. Move .pdb files out of the publish output into publish\symbols\
         (docs/19-DEPLOYMENT-PLAN.md Section 2: retained separately, not
         shipped).
      3. Zip the publish output as the portable-ZIP fallback artifact.
      4. If Inno Setup's ISCC.exe is on PATH, compile scripts\installer.iss
         into a signed-ready installer .exe. If not, this step is skipped
         with a clear message - it is not installed in every environment.

    Code signing (R-16, docs/18-RISK-ASSESSMENT.md) is NOT performed here -
    it requires the organization's own certificate and is a separate, manual
    step against both this script's installer output and
    publish\AI.Document.Converter\PythonEngine\AIDocumentConverter.PythonEngine.exe.
#>

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$version = "1.0.0"
$publishDir = Join-Path $repoRoot "publish\AI.Document.Converter"
$symbolsDir = Join-Path $repoRoot "publish\symbols"
$wpfProject = Join-Path $repoRoot "src\AI.Document.Converter.Wpf\AI.Document.Converter.Wpf.csproj"
$bundledEnginePath = Join-Path $repoRoot "src\AI.Document.Converter.Python\dist\AIDocumentConverter.PythonEngine\AIDocumentConverter.PythonEngine.exe"

if (-not (Test-Path $bundledEnginePath)) {
    Write-Error "Bundled Python engine not found at '$bundledEnginePath'. Run scripts\build-python-engine.ps1 first."
}

Write-Output "Publishing self-contained win-x64 build (v$version)..."
dotnet publish $wpfProject -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

if (-not (Test-Path (Join-Path $publishDir "PythonEngine\AIDocumentConverter.PythonEngine.exe"))) {
    Write-Error "Publish output is missing PythonEngine\ - check the Exists() condition in AI.Document.Converter.Wpf.csproj."
}

Write-Output "Separating debug symbols into publish\symbols\..."
New-Item -ItemType Directory -Force -Path $symbolsDir | Out-Null
Get-ChildItem -Path $publishDir -Filter "*.pdb" | Move-Item -Destination $symbolsDir -Force

$zipPath = Join-Path $repoRoot "publish\AI.Document.Converter-$version-win-x64-portable.zip"
Write-Output "Building portable ZIP: $zipPath"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) {
    Write-Output "Compiling Inno Setup installer..."
    & $iscc.Source (Join-Path $repoRoot "scripts\installer.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed." }
} else {
    Write-Output "Inno Setup (ISCC) not found on PATH - skipping installer build."
    Write-Output "Install Inno Setup 6 (https://jrsoftware.org/isinfo.php) and re-run this script to produce the signed-ready installer, or compile scripts\installer.iss manually."
}

Write-Output ""
Write-Output "Done. Artifacts:"
Write-Output "  Publish output : $publishDir"
Write-Output "  Debug symbols  : $symbolsDir"
Write-Output "  Portable ZIP   : $zipPath"
if ($iscc) {
    Write-Output "  Installer      : publish\AI.Document.Converter-$version-Setup.exe"
}
Write-Output ""
Write-Output "Remaining before distribution (docs/19-DEPLOYMENT-PLAN.md Section 5):"
Write-Output "  - Code-sign the installer .exe and PythonEngine\AIDocumentConverter.PythonEngine.exe"
Write-Output "    with the organization's certificate (manual step, not automated here)."
Write-Output "  - Install and verify on a clean machine per CLAUDE.md Section 56."
