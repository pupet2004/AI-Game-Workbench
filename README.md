# AI Game Workbench

Windows-first AI project workbench.

## Windows Alpha Package

Build the self-contained Windows package from a checkout:

```powershell
pwsh -NoLogo -NoProfile -File .\tools\publish-windows.ps1 -Version alpha
```

The resulting ZIP is in `artifacts\release`. Extract it and run `Workbench.App.exe`; no .NET runtime, Node.js, Codex, OpenCode, or other Agent installation is required for Manual mode. Enable an Agent only when needed in `Settings`, where an optional executable path can override the detected local installation.

## Local Native Surface

To run the development build with the Native Agent Surface enabled, run the helper from any PowerShell directory:

```powershell
& 'C:\Users\pupet\Documents\ChatGPT\AI Game Workbench\tools\run-native-surface.cmd'
```

This helper resolves the project path and sets `WORKBENCH_NATIVE_AGENT_SURFACE=1` automatically.
If Workbench is already open, close the existing window first so the development build can replace its DLLs.

M0 — Empty Office
Completed

M1 — Leader Lives
SEALED

Completed:
M1-01
M1-02
M1-02A
M1-03
M1-04
M1-05A
M1-05B
M1-05C

M1.5 — Project Memory

M1.5A — Memory Foundation
Completed

M1.5B — Session-Derived Memory Intelligence
Completed

Current:
M1.5C — Memory-Aware Leader Boot
