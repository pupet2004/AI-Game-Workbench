# Desktop Launcher Recovery

Date: 2026-09-15

## Diagnosis

The desktop shortcut runs `tools/launch-native-surface.ps1` in a hidden
PowerShell window. Previously every invocation built a new `dev-*` output
before opening Workbench. The application executable did not exist while
the build was still running.

A diagnostic build with shared compilation disabled completed successfully
in 6 minutes 55.69 seconds, with no warnings or errors. This exceeded the
earlier five-minute window wait. The compiler was consuming CPU during the
wait; the precise cause of the build duration has not been established.
This is not evidence of an application startup crash or a proven shared
compiler defect.

## Launch Behavior

- Default: activate an existing Workbench window, or open the newest complete
  development build without compiling.
- Missing required output files, pending builds and failed builds are excluded.
- A per-workspace mutex prevents simultaneous shortcut launches from starting
  competing builds.
- If no build is available, show a notice before compiling.
- Build/start errors show a dialog. Launch transcripts are stored under
  `artifacts/local/launch-*.log`.
- Source edits are **not** automatically rebuilt on normal launch.

To explicitly build the current source, first close Workbench, then run:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File tools/launch-native-surface.ps1 -Rebuild
```

The desktop shortcut target has not changed.

## Verification

`tools/verify-launch-native-surface.ps1` passed under Windows PowerShell.
It covers an empty build directory, incomplete output, pending and failed
builds, unrelated fixture directories and selection of the newest complete
build. Its temporary directories are isolated; it never launches the app.

An actual invocation of the updated launcher reused the existing responsive
Workbench process (PID 22792) without creating another process or build
directory. The process runs from
`artifacts/local/dev-20260915-142907-756/Workbench.App.exe`.

The existing application was not closed to test a cold launch. No Agent work,
project-state changes, or Product Loop certification were performed during
this launcher repair.
