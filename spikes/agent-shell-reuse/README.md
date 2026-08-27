# Agent Shell Reuse Spike

This directory contains a throwaway audit and a browser-only Fake Relay contract page. It is not part of `AI.Game.Workbench.sln` and does not modify the production Leader surface.

## Contents

- `upstream/kilocode/`: shallow upstream snapshot used for static inspection. The full Kilo checkout may not be available on Windows because of long paths; Git object inspection is sufficient for the audit.
- `upstream/cline/`: shallow upstream snapshot used for static inspection.
- `fake-relay/`: standalone HTML page that simulates a Workbench Host and validates the boundary a reused Agent shell would consume.
- `extracted/kilo-source/`: bounded Kilo source snapshot plus a Workbench Relay adapter proof. It intentionally does not compile Kilo's full SolidJS context graph.
- `solid-harness/`: isolated Vite + SolidJS harness that renders a Kilo-like transcript/composer against the Relay shim.
- `docs/spikes/2026-08-26-agent-shell-reuse.md`: findings and Go/No-Go decision.

## Run the contract page

Open `fake-relay/index.html` in Edge or Chrome. No build step or network access is required.

For a headless browser smoke, open `fake-relay/index.html?selfTest=1`. The page drives one synthetic turn and marks the protocol checks as they pass.

The page simulates:

- streaming deltas;
- approval request/response correlation;
- stop and steer capability commands;
- host-issued attachment tokens;
- transcript reload from host-owned state;
- immutable Assignment/Attempt metadata;
- completion into a non-authoritative Handoff.

Use the selection check by dragging from blank space beside one message across another message. The page intentionally uses ordinary DOM text rather than a custom transcript renderer.

## Scope boundary

The page is not a Kilo or Cline fork. It is a contract test for the adapter that would sit between a reused shell and Workbench Relay. The next gate is to mount one extracted Kilo or Cline transcript/composer path against this same contract.

## Kilo adapter proof

Open `extracted/kilo-source/index.html` in Edge or Chrome. It uses `relay-adapter.js` to keep Kilo-like composer semantics while the Fake Host owns the transcript and identity. `?selfTest=1` drives a synthetic turn, approval, attachment token, completion, and reload.

The bounded source snapshot is kept beside the adapter so future work can compare behavior with Kilo's real `PromptInput`, `MessageList`, `VscodeUserMessage`, and `markdown-stream` implementations without importing Kilo's server/provider contexts.

## Solid harness

The Solid harness is the next extraction gate. It uses a real SolidJS render path, host-owned transcript snapshots, approval correlation, attachment tokens, Enter/Shift+Enter composer behavior, and busy-state send suppression. It is intentionally a small compatibility harness rather than a claim that Kilo's complete context graph is portable.

The harness now also uses `@kobalte/core/button` through `src/kilo-primitives.tsx`, preserving Kilo's `data-component="button"`, `data-size`, and `data-variant` boundary. Kilo's public Button and Markdown exports currently re-export `@opencode-ai/ui`, so those remain a separate dependency gate.

The Markdown gate is now implemented as `src/workbench-markdown.tsx`: it reuses Kilo's `markdown-stream.ts` plus its pure `markdown-projection.ts`, then renders through `marked` and sanitizes with `DOMPurify`. Code blocks receive a Kilo-style `data-component="markdown-code"` wrapper and copy action without importing OpenCode context or worker state.

```powershell
cd .\spikes\agent-shell-reuse\solid-harness
npm install
npm run dev -- --port 5178
```

Open `http://127.0.0.1:5178/?selfTest=1` for the automated turn, or omit the query to exercise the composer manually.
