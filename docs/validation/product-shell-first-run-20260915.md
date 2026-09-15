# Product Shell First-run Validation

Date: 2026-09-15

## Scope

This stage validates the first-run project setup surface. It checks that a
new or incomplete project can be inspected, configured, and entered without
writing AcceptedProjectState during readiness checks.

## Readiness Projection

The setup page reports:

- detected project type and `project.godot` for Godot projects;
- Git availability and repository state;
- Workbench database readiness;
- Godot executable readiness for Godot projects;
- available Agent worker models;
- whether an accepted project state exists.

Godot resolution is read-only and uses this order:

1. saved Settings path;
2. `WORKBENCH_GODOT_EXECUTABLE`;
3. `godot.exe` / `godot_console.exe` on `PATH`;
4. the configured Windows fallback location used by this workstation.

Settings accepts either the executable path or the Godot installation directory.
When a directory is supplied, the console executable is preferred.

The check does not launch Godot and does not modify project files.

## Recovery Paths

- Agent discovery failure shows an unavailable state and a Retry action.
- Agent configuration can be opened from the setup page and returns to the
  same setup flow.
- Godot path can be saved in Settings and is rechecked on return.
- Readiness failures do not create governance, claims, completions, or
  AcceptedProjectState.

## Setup Safety

The primary action is staged:

1. establish governance;
2. preview the first project responsibility and assignment;
3. confirm the verified preview;
4. enter the project.

Changing the principal, role, responsibility, outcome, or assignment
invalidates the previous preview. A stale preview cannot be committed.

## Verification

- `dotnet build src/Workbench.App/Workbench.App.csproj --no-restore /p:UseSharedCompilation=false /p:UsedAvaloniaProducts=`
  - passed with 0 warnings and 0 errors.
  - The Avalonia telemetry task was disabled because this workstation denied
    access to its user-level telemetry log; this does not affect application
    compilation.
- App test suite:
  - `677 passed`
  - `5 skipped` (live provider tests)
  - `0 failed`
- First-run targeted tests cover:
  - failed model discovery and retry;
  - no truth written by readiness checks;
  - preview invalidation;
  - Settings persistence for the Godot executable path;
  - executable path resolution.

## Boundary

This document does not certify a complete unfamiliar-user three-round
Worker -> Review -> Accept journey. That remains covered by the existing
Product Loop release gate and must be rerun after Product Shell changes.
