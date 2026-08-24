# 19 — Deployment Plan

**Project:** AI Document Converter
**Status:** Draft — Phase 19 (Deployment Plan) — Gate 4
**Date:** 2026-08-23

---

## 1. Build Process

1. `dotnet publish` the `AI.Document.Converter.Wpf` project as a **self-contained**
   .NET 8 deployment (no separate .NET runtime install required on the target
   machine — directly serves NFR-010).
2. Run `scripts/build-python-engine.ps1` to produce the bundled Python engine
   **folder** via PyInstaller (`--onedir`, not `--onefile` — see
   `docs/adr/ADR-001-python-integration.md` Addendum for why: `--onefile`
   measured at ~5 seconds of self-extraction per launch once Phase 3's
   extraction libraries were bundled in, which is unaffordable given one
   process starts per file), from the pinned `requirements.txt` (R-04 in
   `docs/18-RISK-ASSESSMENT.md`). This step also bundles `tiktoken`'s
   pre-fetched vocabulary files (`tiktoken_cache/`) and an explicit
   `--hidden-import` for its plugin-discovered encoding module — both required
   for token estimation to work offline (`docs/18-RISK-ASSESSMENT.md` R-18).
3. Copy the built Python engine folder (`AIDocumentConverter.PythonEngine.exe`
   plus its `_internal/` dependency folder) into the WPF publish output's
   `PythonEngine/` subfolder (path referenced by `AppPaths.BundledPythonEnginePath`).
4. Run the full test suite (`docs/16-TEST-STRATEGY.md`) against the published
   output before packaging.

## 2. Release Configuration

- Release builds use `Release` configuration, with debug symbols retained
  separately (not shipped) for post-release crash diagnosis if ever needed.
- Logging defaults to `Information` level in Release builds; `Debug` level is only
  enabled via an explicit Settings change, never on by default (avoids
  performance/log-volume surprises and keeps NFR-006 easy to reason about).

## 3. Python Runtime Strategy

Per ADR-001: **no separate Python installation is required.** The bundled,
PyInstaller-built **application folder** ships inside the application's install
folder. This is the single biggest lever for NFR-010 (minimal manual dependency
installation) and directly retires the "Python distribution complexity" risk
flagged from the earliest BRD draft.

## 4. Dependencies

| Dependency | How it reaches the target machine |
|---|---|
| .NET 8 runtime | Bundled via self-contained publish — no separate install |
| Python 3.x + libraries | Bundled as a PyInstaller `--onedir` application folder — no separate install |
| Serilog, DI, other NuGet packages | Compiled into the self-contained publish output |

No dependency in this table requires the target machine to have internet access or
administrator rights to install.

## 5. Installer Strategy

Two distribution artifacts, to cover both typical and heavily locked-down
enterprise environments:

1. **Primary: a signed installer** (Inno Setup recommended over WiX — materially
   simpler for a mid-level developer to author and maintain, while still producing
   a standard Windows installer with Start Menu shortcuts and an uninstall entry).
2. **Fallback: a portable ZIP** of the same self-contained publish output, for
   environments where even running a signed installer requires a separate change
   request — IT can extract and run `AI.Document.Converter.exe` directly.

Both the installer and the bundled Python engine's executable are **code-signed**
(R-16, `docs/18-RISK-ASSESSMENT.md`) with an organization-issued certificate
before release.

## 6. Configuration on the Target Machine

- Settings persist to `%LOCALAPPDATA%\AIDocumentConverter\settings.json` —
  deliberately **outside** the install directory, so an upgrade (which typically
  replaces the install directory's contents) never wipes user configuration.
- Default output directory and log directory are also under `%LOCALAPPDATA%`
  unless the user overrides them in Settings.

## 7. Upgrade Strategy

- The installer replaces the application's install-directory files; it does not
  touch `%LOCALAPPDATA%\AIDocumentConverter\` (settings, logs, default output),
  which is preserved across upgrades.
- No auto-update mechanism is included (consistent with the no-telemetry,
  no-phone-home posture, NFR-001/NFR-012) — upgrades are a deliberate, IT-managed
  action: download and run the new installer.

## 8. Rollback Strategy

- The previous version's installer is retained by IT (standard practice, not
  something the application manages itself).
- Because settings/logs/output live outside the install directory and the on-disk
  settings schema is additive (new optional fields default sensibly), reinstalling
  a previous version does not corrupt existing settings.
- If a release introduces a breaking settings-schema change, that change is called
  out explicitly in `CHANGELOG.md` with manual remediation steps — no automatic
  migration script is planned for MVP given the small, flat settings shape.

## 9. Verification Before Release

Per the Release Checklist (CLAUDE.md Section 56): install on a clean VM with no
Python and no .NET runtime pre-installed, convert one sample of each format, run one
batch, confirm zero network activity (US-023), and confirm the uninstaller removes
the install directory cleanly (leaving user data in `%LOCALAPPDATA%` untouched,
consistent with the upgrade strategy above).

---

*Next document: remaining pre-Gate-5 documents (`docs/20`–`22`) — see final report.*
