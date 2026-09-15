# Why Workbench

AI sessions end. Agents change. Projects still need to know what they have
actually accepted.

Chat is useful for thinking, but a transcript is not a project state. A model
can describe a change without making it true. A handoff can carry context
without carrying authority. Evidence can support a claim without becoming the
claim's decision.

Git is essential, but Git answers a different question: what artifacts changed
and when? It does not by itself say which proposed meaning a human accepted,
which evidence was reviewed, or what the next Agent should treat as the
current project world.

Workbench adds that missing acceptance layer:

```text
execution
  -> completion
  -> evidence and proposed change
  -> review
  -> authority decision
  -> accepted project state
  -> continuity
```

The distinction is deliberate:

- **Completion** says that an Agent finished its work.
- **Evidence** says what can be inspected or verified.
- **Claim** says what change is being proposed.
- **Authority decision** says whether the project accepts it.
- **Accepted project state** is the durable truth used by the next session.

This lets an Agent be useful without giving an Agent implicit write access to
project truth. It also means imperfect evidence can be shown honestly and
decided on explicitly rather than silently converted into certainty.

Workbench is therefore a continuity and acceptance layer for long-running AI
projects. AI game development is its first vertical: Godot projects, runtime
evidence, and replaceable providers make the problem concrete.

The current Alpha has been exercised with deterministic fixtures, real Codex,
OpenCode, DeepSeek, Godot, process restarts, first-use UI, review decisions,
and provider-owned authentication. The validation records state what was
proven and what remains outside the claim.
