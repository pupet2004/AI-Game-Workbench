# Verification Classification Gate

Date: 2026-09-15 (Asia/Shanghai)

## Result

`VerificationClassificationGate=Passed`

The verifier now correctly classifies the bounded Godot source scope in a
real clean-configuration desktop run. The certified run completed:

`+1 -> +2 -> Reset -> High Score`

using OpenCode with `deepseek/deepseek-v4-flash`, real Worker execution,
desktop Review/Accept, six normal tray exits, three process reopen cycles,
fresh Leader continuity checks, and independent Godot behavior corroboration.

Evidence:

`artifacts/local/first-run-ui-20260915-204635-875/`

## Classification Evidence

Round 2 and Round 3 Review screens both recorded:

- `declared-deliverable=Passed`
- `scope=Passed`
- `Changed paths are within the declared target set`

The verifier no longer treats:

- `--headless`, `--check-only`, and `--script` as file paths;
- the absolute Godot executable path as a source target;
- `.godot/` generated files as unexpected workspace changes.

Windows slash direction and relative source paths are normalized before
comparison. Source paths declared in free text, including
`scripts/main.gd` and `scenes/main.tscn`, are recognized consistently.

The overall Review label remains `NotVerifiable` in these rounds because the
acceptance criteria are free text without a structured machine-checkable
form. That is an intentional and accurate boundary. It does not invalidate
the passed deliverable and scope classifications, and it does not prevent
the user from making the Authority decision.

## Product Certification

The same run also passed:

- first-use UI from a previously nonexistent Workbench database;
- real Worker completion and Review/Accept for all three rounds;
- AcceptedProjectState persistence after every normal tray Exit;
- fresh Leader reading the previously accepted state after reopen;
- preservation of prior accepted state while adding each new change;
- independent Godot behavior check on the final project;
- no SQL repair, seed state, CLI rescue, or forced termination as a passing exit.

Normal tray exits are recorded in:

`artifacts/local/first-run-ui-20260915-204635-875/ui-exits.jsonl`

## Verification

- App tests: `700` total, `695` passed, `5` skipped, `0` failed.
- PowerShell certification scripts passed syntax checks.
- Verifier-focused regression tests passed: `9` passed, `0` failed.
- Full first-use UI gate: `FirstRunUiThreeRounds=Passed`.

## Scope

This gate certifies verification classification for the current Godot
bounded-source workflow. It does not certify:

- structured semantic verification of arbitrary free-text acceptance criteria;
- clean-machine installation;
- first-time provider authentication;
- installer or upgrade behavior;
- Unity or other engine adapters.

The kernel and Acceptance Spine were not changed by this gate.
