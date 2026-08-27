// Workbench adapter for the bounded Kilo shell extraction.
// It preserves Kilo-like session/composer semantics while keeping identity,
// transcript, and governance events in the Host Relay.

export function createKiloRelayAdapter({ send }) {
  const state = {
    sessionId: undefined,
    assignmentId: undefined,
    attemptId: undefined,
    status: "idle",
    capabilities: { stop: false, steer: false, attachments: false },
    messages: [],
    approval: undefined,
    listeners: new Set(),
  }

  const notify = () => state.listeners.forEach((listener) => listener(state))
  const onChange = (listener) => {
    state.listeners.add(listener)
    return () => state.listeners.delete(listener)
  }
  const ensureAssistant = () => {
    const last = state.messages.at(-1)
    if (last?.role === "assistant") return last
    const message = { id: `assistant-${state.messages.length}`, role: "assistant", text: "", parts: [] }
    state.messages.push(message)
    return message
  }

  const onHostEvent = (event) => {
    if (event.type === "host_state") {
      state.status = event.status
      state.capabilities = { ...state.capabilities, ...event.capabilities }
    }
    if (event.type === "transcript_reload") state.messages = event.messages ?? []
    if (event.type === "user_message") state.messages.push({ id: event.messageId ?? crypto.randomUUID(), role: "user", text: event.text, parts: [] })
    if (event.type === "assistant_delta") ensureAssistant().text += event.delta
    if (event.type === "tool_started") ensureAssistant().parts.push({ type: "tool", status: "running", name: event.name })
    if (event.type === "tool_completed") ensureAssistant().parts.push({ type: "tool", status: "completed", name: event.name, output: event.output })
    if (event.type === "approval_requested") state.approval = event
    if (event.type === "approval_resolved") state.approval = undefined
    if (event.type === "attachment_received") ensureAssistant().parts.push({ type: "attachment", token: event.token, name: event.name })
    if (event.type === "turn_completed" || event.type === "turn_interrupted") state.status = "idle"
    if (event.type === "steer_acknowledged") state.status = "running"
    if (event.sessionId) state.sessionId = event.sessionId
    if (event.assignmentId) state.assignmentId = event.assignmentId
    if (event.attemptId) state.attemptId = event.attemptId
    notify()
  }

  return {
    state,
    onChange,
    onHostEvent,
    ready() { send({ type: "ready" }) },
    reload() { send({ type: "reload_transcript" }) },
    sendPrompt(text) { if (text.trim() && state.status !== "running") send({ type: "send_message", text }) },
    stop() { if (state.capabilities.stop) send({ type: "stop" }) },
    steer(text) { if (state.capabilities.steer) send({ type: "steer", text }) },
    resolveApproval(decision) { if (state.approval) send({ type: "approval_resolve", requestId: state.approval.requestId, decision }) },
    prepareAttachment(file) { if (state.capabilities.attachments) send({ type: "attachment_prepare", name: file.name, size: file.size, mimeType: file.type }) },
  }
}
