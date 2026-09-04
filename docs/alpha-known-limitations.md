# Alpha Known Limitations

Version: `v0.1.0-alpha.20260827`  
Recorded: **2026-08-27**

> Historical Alpha limitation record. Current working-tree verification is tracked separately in [Current Working-Tree Validation](validation/current-working-tree-20260904.md).

This list is intentional. It defines the boundary of the first public Alpha rather than promising unfinished features.

## Agent Surface

- Native Agent Surface remains feature-flagged; the legacy XAML transcript is retained as a fallback.
- The surface is a Workbench relay projection, not the full Codex Desktop or Kilo/Cline shell.
- Provider-specific capabilities can differ. Steer and approval are capability-gated.
- Attachment handling uses host-managed tokens; broad file/image preview and drag/drop parity are not yet universal across runtimes.

## Runtime Coverage

- Codex is the primary runtime path.
- OpenCode participation is proven only by the separately gated local WEIQI3 acceptance run; the default full suite skips live-provider execution, and not every OpenCode model or error mode is covered.
- Claude, Kimi, DSH, and other providers are architectural targets, not Alpha acceptance claims.
- Runtime events are normalized only to the currently implemented provider-neutral contract.

## Project and Governance

- Accepted state still requires an explicit Authority Decision.
- Claims, Handoffs, Summary, Library, and Agent messages do not become accepted state automatically.
- Evidence is recorded as provenance; the Alpha does not independently verify every external locator, commit, or test claim.
- Multi-user organizations, external identity assurance, legal accountability, revocation, and regulatory workflows are out of scope.

## Product Surface

- The Alpha is Windows-first and local-first.
- It is not a complete IDE, terminal, Git workbench, browser controller, MCP manager, or PR review product.
- UI polish, layout density, localization coverage, and accessibility still need broader dogfooding.
- Transcript retention, export, and advanced session management are limited.

## Operational Risk

- Self-contained publishing is validated for the current machine and Windows x64 target; signing and installer distribution are not included.
- A running development process can lock build outputs; close Workbench before republishing.
- The live WEIQI3 acceptance path is an integration test and depends on locally installed Agent executables and credentials.
