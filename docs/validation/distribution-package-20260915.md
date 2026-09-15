# Distribution Package Validation

Date: 2026-09-15 (Asia/Shanghai)

## Result

`DistributionPackage=Passed`

The Windows self-contained package now includes a per-user installer and
uninstaller. The package was published for `win-x64` and contains:

- `Workbench.App.exe`
- `install-windows.ps1`
- `uninstall-windows.ps1`

Package:

`artifacts/release/AI.Game.Workbench-distribution-alpha-win-x64.zip`

## Install Smoke Test

An isolated package copy was installed into a temporary workspace directory
with shortcuts disabled. The test confirmed:

- application executable copied successfully;
- installed uninstaller generated successfully;
- uninstall removed only the application directory;
- project data preservation message was emitted.

The default install location is:

`%LOCALAPPDATA%\Programs\AI Game Workbench`

The default Workbench data location remains:

`%LOCALAPPDATA%\AI Game Workbench`

When run without test flags, the installer creates Start menu and desktop
shortcuts. Uninstall refuses to remove a running Workbench process and
preserves project data.

## Boundaries

This validates the local Windows package and install/uninstall mechanics. It
does not certify a clean physical machine, first-time provider authentication,
Godot prerequisite detection, signing, upgrades, or a final distribution
release tag.
