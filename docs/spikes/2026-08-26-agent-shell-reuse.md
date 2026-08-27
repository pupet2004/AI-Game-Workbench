# Agent Shell Reuse Spike

Date: 2026-08-26  
Scope: static audit plus a throwaway Workbench-owned Relay contract page. No production Leader UI or runtime changes.

## Question

Can Workbench reuse a complete open-source coding-agent shell and replace only its host/transport boundary with a Workbench Relay?

The spike compares Kilo Code and Cline. It deliberately separates four layers:

1. reusable visual components;
2. a browser/WebView shell;
3. the host bridge and transport;
4. agent runtime, session, project, and governance state.

Workbench must continue to own the fourth layer. A reused shell is a projection, not an authority.

## Snapshots audited

| Project | Snapshot | License evidence |
|---|---|---|
| Kilo Code | `ed3380ea494156cd36107d3ac95acaf08bf28b7f` (`v7.5.0`, 2026-08-26) | `packages/kilo-vscode/package.json`, `packages/kilo-ui/package.json`, `packages/kilo-web-ui/package.json`, and `packages/session-ui/package.json` declare MIT. The repository README has historically used different wording, so a release-level dependency/license audit is still required before vendoring. |
| Cline | `HEAD` in `spikes/agent-shell-reuse/upstream/cline` (local shallow clone, 2026-08-26) | Repository `LICENSE` is Apache-2.0. The desktop example is not treated as a separately licensed product until its dependency notices are audited. |

## Kilo Code

### What is reusable

- `packages/kilo-ui` and `packages/kilo-web-ui` expose a broad SolidJS component set, including Markdown, message parts, session turns, tool cards, diff/session review, image preview, prompt input, and attachment-oriented primitives.
- `packages/session-ui` contains session/message rendering that is closer to a coding-agent transcript than a generic chat component.
- `packages/kilo-vscode/webview-ui/src/index.tsx` is a browser entry point and `App.tsx` composes theme, data, server, provider, and session contexts.
- The VS Code webview bridge is typed. The message unions under `packages/kilo-vscode/webview-ui/src/types/messages/` distinguish extension-to-webview and webview-to-extension traffic. `context/vscode.tsx` wraps `acquireVsCodeApi()` and posts structured messages.
- `packages/kilo-vscode/AGENTS.md` explicitly documents that the WebView and extension do not share state; they communicate through typed messages. That is a useful seam for a Relay adapter.

### Coupling that prevents a direct fork today

- The application expects Kilo's own server/session/provider contexts and the `@kilocode/sdk` data model.
- The extension owns CSP, resource URLs, VS Code commands, file opening, terminal integration, and lifecycle/reload behavior.
- Session state is not just transcript text: permissions, questions, provider catalog, worktree state, diffs, and server status are all part of the provider graph.
- Replacing `vscode.postMessage()` alone would leave many components calling Kilo-specific message names and expecting Kilo-specific cached state.

### Assessment

| Reuse target | Decision |
|---|---|
| `kilo-ui` / `kilo-web-ui` visual primitives | **GO** |
| selected `session-ui` transcript/message components | **GO, with an adapter** |
| complete Kilo WebView outside VS Code | **CONDITIONAL** |
| fork the complete Kilo product into Workbench now | **NO-GO** |

The right extraction boundary is a small, Workbench-owned adapter around message/session data, not a wholesale replacement of Kilo's server and context hierarchy.

## Cline

### What is reusable

- `apps/examples/desktop-app/webview/` contains a coherent React shell with chat input, message bubbles, tool message blocks, tool approval UI, attachments, and `use-chat-session` lifecycle logic.
- `apps/examples/desktop-app/webview/lib/desktop-client.ts` demonstrates a host bridge with request/response correlation, event transport, reconnect behavior, endpoint injection, and a Tauri fallback.
- `apps/cli/src/acp/` provides a useful reference for session load/update and permission handling over an agent-client protocol.
- The bridge is already designed for a WebView that can run in more than one host, which makes it a good reference for capability discovery and reconnect semantics.

### Coupling that prevents a direct fork today

- The chat hook and message schema are coupled to Cline's session records, RPC event names, provider configuration, and desktop backend commands.
- The example shell assumes a sidecar/Tauri or Cline desktop backend endpoint. It is not a standalone Workbench surface until those commands are normalized.
- Cline's ACP layer is a transport/protocol reference, not a drop-in Workbench Authority or Continuity implementation.

### Assessment

| Reuse target | Decision |
|---|---|
| chat/approval/attachment interaction patterns | **GO as reference** |
| desktop bridge request/event/reconnect patterns | **GO as reference** |
| selected React components after schema adaptation | **CONDITIONAL** |
| complete Cline desktop shell in Workbench now | **NO-GO** |

## Workbench Relay boundary

The shell-reuse experiment under `spikes/agent-shell-reuse/fake-relay/` uses a deliberately small provider-neutral contract. The browser can send commands, but it cannot mutate identity or authority fields.

### Host -> surface events

`host_state`, `transcript_reload`, `user_message`, `assistant_delta`, `tool_started`, `tool_completed`, `approval_requested`, `approval_resolved`, `steer_acknowledged`, `attachment_received`, `turn_completed`, `handoff_created`.

### Surface -> Host commands

`ready`, `send_message`, `stop`, `steer`, `approval_resolve`, `attachment_prepare`, and `reload_transcript`.

### Non-negotiable invariants

- `assignmentId`, `attemptId`, and `sessionId` are host-issued and rendered read-only.
- Approval responses carry the original `requestId`; a stale or unknown id is rejected.
- Attachments cross the boundary as host-issued tokens plus metadata, never a browser-readable filesystem path.
- Transcript reload is a host snapshot operation; the surface does not reconstruct authority from DOM state.
- Completion creates a non-authoritative Handoff projection. It cannot write Accepted State.
- Capability flags control whether Stop, Steer, Approval, and Attachment actions are shown.

## Decision

1. Keep the existing Native Surface and old transcript renderer as fallback during research.
2. Build a Workbench-owned Relay contract first.
3. Prototype extracted Kilo session/message components against that contract before considering a fork.
4. Use Cline's desktop bridge and ACP code as transport/reconnect references.
5. Do not vendor either complete product until the dependency graph has been reduced and license notices are complete.

**Overall: conditional GO for component reuse; NO-GO for direct full-shell fork today.**

The next technical gate is a real adapter proof: one extracted transcript/composer path, one approval round-trip, one attachment token, and one reconnect/reload cycle over the Workbench Relay. Production Leader integration remains out of scope for this spike.

## Adapter proof result

The next gate is now present under `spikes/agent-shell-reuse/extracted/kilo-source/`:

- bounded snapshots of Kilo `PromptInput`, `MessageList`, `VscodeUserMessage`, `prompt-input-utils`, and `markdown-stream`;
- `relay-adapter.js` that maps provider-neutral Host events into a Kilo-like transcript/composer state;
- a standalone browser page with Enter-to-send, Shift+Enter newline, busy-state send suppression, Stop/Steer, approval correlation, attachment token handling, and transcript reload.

The adapter deliberately does not import Kilo's SolidJS context graph. This is the useful result: the visual interaction boundary can be isolated, while Kilo's server/provider/session contexts remain outside Workbench. JavaScript syntax and the browser module load were verified locally; a full SolidJS build remains intentionally deferred until the adapter schema is accepted.

## SolidJS harness result

`spikes/agent-shell-reuse/solid-harness/` now provides a real Vite + SolidJS render path over the same Relay contract. It confirms that the Kilo-like transcript/composer boundary can be rendered as a Solid surface while Host remains canonical for transcript snapshots and identity. The harness build passed with Vite 7.3.5 and Solid 1.9.12.

This is still an adapter proof, not a Kilo product fork: the original Kilo `PromptInput` and `MessageList` remain coupled to Kilo's SDK, provider, server, and VS Code contexts. The next gate is selective import of Kilo UI primitives, not wholesale context migration.

## Selective primitive result

The Solid harness now compiles a Kilo-style button boundary through `@kobalte/core/button`, with the same `data-component`, `data-size`, and `data-variant` attributes used by Kilo UI. This is a practical extraction seam for composer, approval, Stop, and Steer controls.

The audit also confirms a boundary that should remain explicit: Kilo's public `Button` and `Markdown` modules are re-exports of `@opencode-ai/ui`, so importing them requires a larger upstream UI graph. They are not counted as portable primitives yet.

## Markdown extraction result

The Solid harness now compiles and renders a Workbench-owned Markdown surface using:

- Kilo's pure `markdown-stream.ts`;
- Kilo's pure `markdown-projection.ts`;
- independent `marked` parsing;
- `DOMPurify` sanitization;
- Kilo-style `data-component="markdown"` / `markdown-code` presentation markers.

The original `@opencode-ai/ui/markdown` remains **NO-GO for direct import** because it pulls `useMarked`, `useI18n`, worker/highlighting state, Mermaid lifecycle, and OpenCode utility ownership. The selective path is therefore a **GO**: reuse Kilo's streaming projection and visual language, while Workbench owns the renderer lifecycle and transcript data.

## Production Solid Surface Integration

The Solid bundle is now connected to the production Avalonia WebView host behind the existing `WORKBENCH_NATIVE_AGENT_SURFACE` flag. `LeaderAgentSurfaceView` loads the single-file bundle from `Assets/LeaderAgentSurface/index.html`; the bundle uses the existing `state/event` host contract and posts the existing `ready` / `command` envelopes. The legacy XAML transcript remains the fallback when the flag is off.

Production build verification passed with 0 warnings and 0 errors. A flagged local application launch completed without initialization errors; visual/manual interaction remains the next dogfood check.
