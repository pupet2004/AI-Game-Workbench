# Zero Hour Demo Data Contract

| Demo field | Existing source | Authority / status |
|---|---|---|
| Project identity and path | `ProjectOpenService`, `ProjectRepository`, Home recent-project model | Project metadata |
| Single closed timeline and other constraints | B1 `AuthorityDecision` → `AcceptedProjectState` via `B1Projector` | Accepted fact |
| Assignment and bounded Chapter 1 contract | B1 Assignment/Revision projection or existing Task/TaskRevision path | Durable assignment; not completion proof |
| Worker runtime/session | `WorkerExecutionRepository`, `AgentSession`, runtime binding | Execution provenance |
| B1 result Handoff | `GuidedHandoffComposerService`, B1 Claims/Handoff | Non-authoritative |
| Legacy Worker completion | `WorkerSessionRouter`, completion package/task events | Legacy completion; not a B1 Handoff |
| Artifact and changed paths | Worker completion verifier and workspace baseline/delta records | Evidence/path verification |
| “Time permit” | Handoff proposed contribution or Library proposal | Proposal |
| “Single closed timeline” | Accepted contribution and Library projection | Accepted fact / projection |
| Chapter 1 completed summary | Leader/project summary repositories and reviewed completion records | Summary / review output |
| Library category/topic/object timeline | `ProjectLibraryRepository` and projection services | Navigation/projection |
| Search result provenance | Existing Library materials/source refs plus contributing Claim/Handoff/summary source | Source evidence |
| New Leader recovery display | Existing `LeaderBootContext`, Accepted State, selected Handoff, summaries, Library projection | Recovery presentation, not new memory |

Presentation gaps to close: one Handoff display model, free-text Library search over multiple owned sources, and a recovery view assembled from existing context data.
