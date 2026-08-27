# Kilo extraction manifest

Snapshot: `ed3380ea494156cd36107d3ac95acaf08bf28b7f` (`v7.5.0`, 2026-08-26)

The files in this directory are a bounded source snapshot for the spike. They are not compiled into Workbench and are not production dependencies.

| Snapshot file | Upstream role | Why kept |
|---|---|---|
| `packages__kilo-vscode__webview-ui__src__components__chat__PromptInput.tsx` | Kilo composer | Keyboard, busy-state, attachment and command interaction reference |
| `packages__kilo-vscode__webview-ui__src__components__chat__MessageList.tsx` | Kilo transcript list | Turn/list lifecycle and auto-scroll reference |
| `packages__kilo-vscode__webview-ui__src__components__chat__VscodeUserMessage.tsx` | User message renderer | Message/part projection reference |
| `packages__kilo-vscode__webview-ui__src__components__chat__prompt-input-utils.ts` | Composer helpers | `isPromptBusy`, mention insertion, and input semantics |
| `packages__session-ui__src__components__markdown-stream.ts` | Streaming Markdown projection | Incremental Markdown/code block handling reference |

The accompanying `KILO-LICENSE` is retained with the snapshot. The spike adapter below does not import these SolidJS files because their original context graph requires Kilo's SDK, server, provider, and VS Code host. Instead it implements the smallest equivalent seam against the Workbench Relay contract.
