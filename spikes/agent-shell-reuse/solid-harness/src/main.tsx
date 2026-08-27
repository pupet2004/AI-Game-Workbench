import { createSignal, For, Show } from "solid-js"
import { render } from "solid-js/web"
import { KiloButton } from "./kilo-primitives"
import { WorkbenchMarkdown } from "./workbench-markdown"
import "./styles.css"

type Part = { kind: "tool" | "attachment"; name?: string; token?: string; status?: string }
type SurfaceMessage = { id: string; role: "user" | "assistant" | "error"; text: string; parts: Part[]; streaming?: boolean }
type Approval = { requestId: string; description: string; options: { id: string; label: string; description?: string }[] }
type SurfaceState = {
  session: {
    projectId?: string
    role?: string
    assignmentId?: string
    attemptId?: string
    agentSessionId?: string
    provider?: string
    model?: string
    capabilities: { streaming: boolean; approval: boolean; interrupt: boolean; steer: boolean; attachments: boolean; transcript: boolean }
  }
  messages: SurfaceMessage[]
  approval?: Approval
  busy: boolean
  runtimeStatus?: string
}
type SurfaceEvent = { type: "attachment_received" | "error" | "handoff_created"; payload?: { token?: string; name?: string; message?: string; authorityDecisionCreated?: boolean } }
type SurfaceCommand =
  | { command: "ready" | "interrupt" | "reload_transcript" }
  | { command: "send_turn" | "steer"; text: string }
  | { command: "respond_approval"; requestId: string; option: string }
  | { command: "attach"; file: { name: string; size: number; type: string; dataUrl: string } }

const emptyState: SurfaceState = {
  session: { capabilities: { streaming: true, approval: true, interrupt: true, steer: false, attachments: true, transcript: true } },
  messages: [],
  busy: false,
}

function cloneMessages(messages: SurfaceMessage[]) {
  return messages.map((message) => ({ ...message, parts: message.parts.map((part) => ({ ...part })) }))
}

type StateListener = (state: SurfaceState) => void
type EventListener = (event: SurfaceEvent) => void
type HostBridge = { mode: "production" | "fake"; send: (command: SurfaceCommand) => void; onState: (listener: StateListener) => () => void; onEvent: (listener: EventListener) => () => void }

function createFakeBridge(): HostBridge {
  let state: SurfaceState = { ...emptyState, session: { ...emptyState.session, projectId: "fake-project", role: "leader", assignmentId: "fake-assignment", attemptId: "fake-attempt", agentSessionId: "fake-session", provider: "Fake Relay", model: "Kilo Solid" } }
  const stateListeners = new Set<StateListener>()
  const eventListeners = new Set<EventListener>()
  const publishState = () => stateListeners.forEach((listener) => listener({ ...state, messages: cloneMessages(state.messages) }))
  const publishEvent = (event: SurfaceEvent) => eventListeners.forEach((listener) => listener(event))
  const send = (command: SurfaceCommand) => {
    if (command.command === "ready" || command.command === "reload_transcript") { publishState(); return }
    if (command.command === "send_turn") {
      state = { ...state, busy: true, messages: [...state.messages, { id: crypto.randomUUID(), role: "user", text: command.text, parts: [] }] }
      publishState()
      setTimeout(() => { state = { ...state, messages: [...state.messages, { id: crypto.randomUUID(), role: "assistant", text: "Real Solid component path: **Kilo projection**\n\n", parts: [], streaming: true }] }; publishState() }, 100)
      setTimeout(() => { const last = state.messages.at(-1); if (last?.role === "assistant") last.text += "```ts\nconst relay = hostOwned()\n```\n"; publishState() }, 180)
      setTimeout(() => { const last = state.messages.at(-1); if (last?.role === "assistant") last.parts = [...last.parts, { kind: "tool", name: "fake.inspect", status: "completed" }]; publishState() }, 260)
      setTimeout(() => { state = { ...state, approval: { requestId: "fake-approval-01", description: "fake.write", options: [{ id: "allow", label: "Allow" }, { id: "deny", label: "Deny" }] } }; publishState() }, 430)
      return
    }
    if (command.command === "respond_approval") {
      state = { ...state, approval: undefined, busy: command.option === "allow" }
      publishState()
      if (command.option === "allow") setTimeout(() => { state = { ...state, busy: false }; publishState(); publishEvent({ type: "handoff_created", payload: { authorityDecisionCreated: false } }) }, 160)
      return
    }
    if (command.command === "interrupt") { state = { ...state, busy: false, runtimeStatus: "Interrupted" }; publishState(); return }
    if (command.command === "steer") { state = { ...state, runtimeStatus: `Steered: ${command.text}` }; publishState(); return }
    if (command.command === "attach") { const last = state.messages.at(-1); if (last?.role === "assistant") last.parts = [...last.parts, { kind: "attachment", name: command.file.name, token: "fake-attachment-token" }]; publishState(); publishEvent({ type: "attachment_received", payload: { token: "fake-attachment-token", name: command.file.name } }) }
  }
  return { mode: "fake", send, onState: (listener) => { stateListeners.add(listener); return () => stateListeners.delete(listener) }, onEvent: (listener) => { eventListeners.add(listener); return () => eventListeners.delete(listener) } }
}

function createProductionBridge(): HostBridge {
  const webview = (window as Window & { chrome?: { webview?: { postMessage: (value: unknown) => void; addEventListener: (type: "message", listener: (event: MessageEvent) => void) => void } } }).chrome?.webview
  if (!webview) return createFakeBridge()
  const stateListeners = new Set<StateListener>()
  const eventListeners = new Set<EventListener>()
  webview.addEventListener("message", (event) => {
    const value = typeof event.data === "string" ? JSON.parse(event.data) : event.data
    if (value?.type === "state") stateListeners.forEach((listener) => listener(value as SurfaceState))
    if (value?.type === "event") eventListeners.forEach((listener) => listener({ type: value.eventType, payload: value.payload }))
  })
  return { mode: "production", send: (command) => webview.postMessage({ type: command.command === "ready" ? "ready" : "command", ...command }), onState: (listener) => { stateListeners.add(listener); return () => stateListeners.delete(listener) }, onEvent: (listener) => { eventListeners.add(listener); return () => eventListeners.delete(listener) } }
}

function KiloTranscriptSurface(props: { state: () => SurfaceState; onApproval: (option: string) => void }) {
  return <>
    <div class="transcript" aria-label="Transcript">
      <For each={props.state().messages}>{(message) => <article class={`message ${message.role}`}>
        <span class="label">{message.role}</span>
        {message.role === "assistant" ? <WorkbenchMarkdown text={message.text} live={message.streaming} /> : <div class="text">{message.text}</div>}
        <For each={message.parts}>{(part) => <div class="part">{part.kind === "attachment" ? `${part.name} (${part.token})` : `${part.name} · ${part.status}`}</div>}</For>
      </article>}</For>
    </div>
    <Show when={props.state().approval}>{(approval) => <div class="approval"><strong>Approval required</strong><div>{approval().description} · {approval().requestId}</div><For each={approval().options}>{(option) => <KiloButton variant={option.id === "allow" ? "primary" : "secondary"} onClick={() => props.onApproval(option.id)}>{option.label}</KiloButton>}</For></div>}</Show>
  </>
}

function KiloPromptSurface(props: { state: () => SurfaceState; onSend: (text: string) => void; onStop: () => void; onSteer: (text: string) => void; onAttachment: (file: File) => void }) {
  const [text, setText] = createSignal("")
  const submit = () => { const value = text().trim(); if (!value || props.state().busy) return; props.onSend(value); setText("") }
  return <>
    <div class="actions"><KiloButton disabled={!props.state().busy || !props.state().session.capabilities.interrupt} onClick={props.onStop}>Stop</KiloButton><KiloButton disabled={!props.state().busy || !props.state().session.capabilities.steer} onClick={() => { const value = text().trim(); if (value) { props.onSteer(value); setText("") } }}>Steer</KiloButton><label class="file"><input type="file" accept="image/*" onChange={(event) => { const file = event.currentTarget.files?.[0]; if (file) props.onAttachment(file) }} />Attach</label></div>
    <form class="composer" onSubmit={(event) => { event.preventDefault(); submit() }}><textarea value={text()} disabled={props.state().busy} placeholder="Enter sends; Shift+Enter inserts a newline" onInput={(event) => setText(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); submit() } }} /><KiloButton type="submit" variant="primary" disabled={props.state().busy || !text().trim()}>Send</KiloButton></form>
  </>
}

function App() {
  const bridge = createProductionBridge()
  const [state, setState] = createSignal<SurfaceState>(emptyState)
  const [checks, setChecks] = createSignal<string[]>([])
  const mark = (value: string) => setChecks((current) => current.includes(value) ? current : [...current, value])
  bridge.onState((next) => setState({ ...next, messages: next.messages.map((message) => ({ ...message, parts: message.parts ?? [] })) }))
  bridge.onEvent((event) => { if (event.type === "attachment_received") mark("attachment"); if (event.type === "handoff_created") mark("handoff"); if (event.type === "error") setState((current) => ({ ...current, messages: [...current.messages, { id: crypto.randomUUID(), role: "error", text: event.payload?.message ?? "Relay error", parts: [] }] })) })
  bridge.send({ command: "ready" })
  const send = (text: string) => { bridge.send({ command: "send_turn", text }); mark("send") }
  const attach = (file: File) => { const reader = new FileReader(); reader.onload = () => { bridge.send({ command: "attach", file: { name: file.name, size: file.size, type: file.type, dataUrl: String(reader.result) } }) }; reader.readAsDataURL(file) }
  const selfTest = bridge.mode === "fake" && new URLSearchParams(location.search).get("selfTest") === "1"
  if (selfTest) { send("solid production bridge self test"); setTimeout(() => bridge.send({ command: "reload_transcript" }), 1200) }
  return <><header><h1>Kilo Solid Surface + Workbench Relay</h1><div>{bridge.mode === "production" ? "Production WebView Relay" : "Fake Relay preview"} · Host owns identity and authority</div></header><main><section><KiloTranscriptSurface state={state} onApproval={(option) => { const approval = state().approval; if (approval) { bridge.send({ command: "respond_approval", requestId: approval.requestId, option }); mark("approval") } }} /><KiloPromptSurface state={state} onSend={send} onStop={() => bridge.send({ command: "interrupt" })} onSteer={(text) => bridge.send({ command: "steer", text })} onAttachment={attach} /></section><aside><h2>Host binding</h2><dl><dt>Session</dt><dd>{state().session.agentSessionId ?? "-"}</dd><dt>Assignment</dt><dd>{state().session.assignmentId ?? "-"}</dd><dt>Attempt</dt><dd>{state().session.attemptId ?? "-"}</dd><dt>Status</dt><dd>{state().busy ? "running" : "idle"}</dd><dt>Provider</dt><dd>{state().session.provider ?? "-"}</dd><dt>Authority</dt><dd>Host only</dd></dl><h2>Surface checks</h2><For each={["send", "approval", "attachment", "handoff"]}>{(check) => <div class={checks().includes(check) ? "pass" : "pending"}>{checks().includes(check) ? "PASS" : "PENDING"} · {check}</div>}</For></aside></main></>
}

render(() => <App />, document.getElementById("root")!)
