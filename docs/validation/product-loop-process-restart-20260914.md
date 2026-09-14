# Product Loop Process Restart Validation

Date: 2026-09-14

## Scope

This validation covers the desktop process boundary for a product-loop
project. It uses an isolated SQLite database and a project path containing
spaces, so the launch contract is exercised as a real Windows process.

It does not claim that a UI click accepted a pending handoff.

## Command

```powershell
pwsh -NoLogo -NoProfile -File .\tools\verify-product-loop-process-restart.ps1 `
  -ProjectPath "...\artifacts\local\ui-certified-20260914-173418\counter" `
  -ExecutablePath "...\artifacts\local\ui-certification\Workbench.App.exe" `
  -DatabasePath "...\artifacts\local\ui-certified-20260914-173418\workbench.db"
```

## Result

```text
First Workbench process started: PID 5036
First Workbench process stopped.
Second Workbench process started with the same project: PID 15028
Product loop process restart harness passed.
```

The isolated database was created and reused by the second process. The
project path and database path were quoted because Windows `Start-Process`
does not quote array elements containing spaces automatically.

## Boundary

Certified:

- Desktop executable starts with an explicit project path.
- Desktop executable starts with an explicit database path.
- The same project and database survive a forced process stop.
- The second process starts without CLI or SQLite recovery.

Still open:

- UIA click-through from Project Home to Pending Review.
- UIA Accept click and visible Accepted State/Summary refresh.
- A complete three-round desktop UI walkthrough.
