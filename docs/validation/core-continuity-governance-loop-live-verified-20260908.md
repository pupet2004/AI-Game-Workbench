# Core Continuity Governance Loop

## Status

**CLOSED — live-verified end to end on 2026-09-08**

This status applies only to the Core Continuity Governance Loop. It does not claim that every Workbench feature, provider, project type, semantic-granularity policy, or productization concern is complete.

## What Was Verified

The verification used the real `零刻` project, the real Workbench runtime, a real GPT-5.6 Leader session, the real Library UI, durable SQLite persistence, a process restart, and a new Leader recovery context.

The critical path was:

```text
real user conversation
  -> GPT-5.6 detects an Evolution Candidate
  -> durable Candidate persistence
  -> Governance / Library Proposal
  -> real UI Accept action
  -> command handler with explicit proposal id
  -> accept service transaction
  -> Accepted Library Proposal
  -> Library Object / Timeline Node / Material References
  -> durable Candidate provenance
  -> Workbench restart
  -> replacement Leader recovery
```

## Live Acceptance Evidence

Project: `零刻`

- Project ID: `db5333e2-573d-432f-b457-1fc3b063a122`
- Evolution Candidate: `d7ab5445-baf2-44ec-897f-6081dc445c03`
- Library Proposal: `351e7bc7-c31e-4af9-8379-29eb42f4f9b0`
- Proposal result: `Accepted`
- Existing Library Object: `ba48dad7-6ed4-4099-9d8d-f3c352f2b202`
- New Timeline Node: `e50ac59b-9ddc-45f2-b7fb-b25578d88d90`
- Candidate material reference: `workbench:evolution-candidate/d7ab5445baf244ec897f6081dc445c03`

The proposal was selected and accepted through the visible Workbench Library UI. No direct database write or direct service call was used for the live acceptance. The resulting database state was then read back in read-only mode.

## Restart / Recovery Check

After the UI acceptance:

1. Workbench was closed and restarted from the current build.
2. The restarted application reopened the `零刻` project through the project home.
3. The recovered Project World displayed the accepted Truth context, the existing Library projection, the new `时间旅行者黑话` Library content, and its durable Candidate source reference.

This confirms that the accepted Library state and its provenance survive both runtime restart and Leader replacement/recovery context construction.

## Previously Verified Companion Paths

The live closure also relies on the companion checks already completed in this validation series:

- AcceptedProjectState and Authority Decision acceptance;
- Authority Candidate provenance;
- durable Evolution Candidate persistence and restart recovery;
- Worker FinalReport and Handoff persistence;
- Worker FinalReport to durable Summary production;
- Summary recovery in a replacement Leader context;
- policy rejection of an incorrect model route;
- Library and Truth separation.

## Engineering Test Coverage

The focused Library proposal UI regression suite passed after the live fix:

```text
ProjectLibraryProposalUiTests: 7 passed, 0 failed
```

The regression coverage includes explicit proposal selection, UI accept behavior, durable proposal status, Library projection creation, and adding a node under an existing object with a different topic.

## Boundary Of This Closure

This record does not certify:

- ideal Candidate semantic granularity;
- all providers or all project domains;
- UI polish or production resilience maturity;
- retry/reconciliation workers;
- broader benchmark or token-efficiency claims;
- future Authority or Evolution policy versions.

The precise claim is:

> A project change can be discovered by a real Agent, governed without silently changing Truth, accepted by a human through the real UI, persisted as Project/Library state with provenance, and recovered after runtime restart and Leader replacement.

## Reproduction Checklist

Use the same project and a fresh non-Truth Library idea.

1. Start a current Workbench build with the real GPT-5.6 provider.
2. Open `零刻` without relying on the old transcript as the source of truth.
3. Ask for a normal creative change that is durable Library knowledge, not a formal world rule.
4. Confirm the Leader creates an Evolution Candidate.
5. Confirm the Candidate remains after restart and appears in the Evolution / Governance context.
6. Ask the Leader to create a Library Proposal for that Candidate.
7. Select the proposal in the Library pane and click `接受`.
8. Verify the proposal changes from `Pending` to `Accepted`.
9. Verify a Library Object or Timeline Node and material references are created.
10. Verify at least one material reference is `workbench:evolution-candidate/<candidate-id>`.
11. Restart Workbench, reopen the project, and confirm the Library content and source reference are recoverable.

Do not call the result `CLOSED` for a run that substitutes a service/unit test for the real UI acceptance or skips the restart/recovery check.
