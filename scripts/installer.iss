; AI Document Converter — Inno Setup installer script
; docs/19-DEPLOYMENT-PLAN.md Section 5: primary distribution artifact,
; alongside the portable ZIP fallback built by scripts/package-release.ps1.
;
; Prerequisite: a self-contained Release publish already exists at
; publish\AI.Document.Converter\ (produced by scripts/package-release.ps1,
; which runs `dotnet publish` before invoking this script).
;
; Build (requires Inno Setup 6, not installed in every dev environment -
; https://jrsoftware.org/isinfo.php):
;   iscc scripts\installer.iss
;
; Code signing (docs/19-DEPLOYMENT-PLAN.md Section 5, R-16 in
; docs/18-RISK-ASSESSMENT.md) is a separate, manual step performed with the
; organization's own certificate - NOT part of this script. Sign both this
; installer's output .exe and PythonEngine\AIDocumentConverter.PythonEngine.exe
; (already inside publish\AI.Document.Converter\) before distributing either.

#define MyAppName "AI Document Converter"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Grameen Bank"
#define MyAppExeName "AI.Document.Converter.Wpf.exe"
#define PublishDir "..\publish\AI.Document.Converter"

[Setup]
AppId={{8F2E4B9A-7C3D-4A1E-9B5F-2D6C8E1A4F70}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; docs/19-DEPLOYMENT-PLAN.md Section 6: settings/logs/output live under
; %LOCALAPPDATA%, entirely outside this install directory - uninstalling
; (or upgrading, which reinstalls into the same directory) never touches them.
OutputDir=..\publish
OutputBaseFilename=AI.Document.Converter-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; The entire self-contained publish output, including the bundled
; PythonEngine\ subfolder (ADR-001) - no separate Python or .NET runtime
; install is ever required on the target machine (NFR-010).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

; docs/19-DEPLOYMENT-PLAN.md Section 8 (Rollback): the uninstaller only ever
; removes {app} (the install directory) - it never touches
; %LOCALAPPDATA%\AIDocumentConverter\ (settings/logs/output), so reinstalling
; a previous version afterward finds existing settings intact. No [UninstallDelete]
; entries are added here for that directory - this is deliberate, not an oversight.
