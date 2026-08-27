# Workbench: A Project Continuity Layer Architecture for Long-Term AI Collaboration

**Status:** Architecture Preview  
**Version:** Whitepaper v0.1  
**Date:** 2026-08-27  
**Author:** pupet  
**Software status:** Working reference implementation; a Windows-first `v0.1.0-alpha.20260827` candidate exists in the implementation workspace, while broader public product release and usability validation remain in progress.

**Implementation snapshot:** Project-first persistent state; Claim → Decision → Accepted State; Authority / Context / Execution separation; Handoff and restart recovery; Agent participation adapters; evidence provenance; WEIQI³ cross-Agent acceptance experiment; **1,022 / 1,022 tests passed in the 2026-08-27 local working-tree snapshot**. Agent Surface UX, broader provider validation, and public continuity/token experiments remain in progress. No claim is made here about large-scale user validation, proven token savings, multi-user governance, or production readiness.

---

## Abstract

When AI agents, models, sessions, and tools change over time, how does a long-term project maintain coherent existence? Current approaches—conversation memory, knowledge bases, and agent frameworks—address fragments of this problem but do not consistently expose or integrate an independent substrate for project identity, decision provenance, and state recovery across changing AI participants. This paper presents Workbench, a project continuity layer that is designed to decouple long-term project existence from the transient nature of AI participants. Workbench contributes: (1) a three-lane information architecture separating Authority, Context, and Execution, designed to prevent AI outputs from automatically becoming project truth; (2) a Claim–Decision–State model governing which proposed changes enter project reality; and (3) a multi-layer compression framework (Session → Handoff → Summary → Library → Accepted State) designed to reduce long-term context re-comprehension costs while preserving traceability. We analyze Workbench's relationship to Event Sourcing, CQRS, Git, ADR, and project management systems, positioning it as a complementary layer rather than a replacement for existing tools. An implementation verifies the architectural kernel with 1,022 passed tests across five modules, establishing that the proposed design constitutes a working system substrate rather than a purely conceptual contribution. We discuss governance cost tradeoffs, known failure modes, and the boundary between project governance and AI safety. Workbench occupies a specific niche: governing project meaning and decision provenance in the face of participant flux.

**Keywords:** project continuity, long-term projects, AI agent collaboration, decision governance, architecture design, agent replaceability, information architecture, event sourcing, claim-decision-state model

---

## 1\. Introduction

### 1.1 From AI-Assisted to AI-Participating: A Paradigm Shift

The relationship between artificial intelligence and software engineering is undergoing a significant transformation. In the early era of AI-assisted development, tools such as code completion systems \[1], static analysis engines, and refactoring assistants operated as peripheral helpers—useful but peripheral to the core creative and engineering process. The developer remained the sole author of project decisions, and AI served as a sophisticated autocomplete mechanism, predicting the next token or suggesting the next line of code based on local context \[2].

The emergence of large language models (LLMs) such as GPT-4 \[3], Claude \[4], Gemini, and their successors has catalyzed a notable shift. AI systems are no longer confined to suggesting the next token; they now participate in architectural reasoning \[5], generate multi-file code changes \[6], conduct research analysis \[7], debug complex systems, propose design alternatives, and produce creative artifacts that shape project direction. Agent frameworks such as AutoGPT \[8], ReAct \[9], AutoGen \[10], LangChain \[11], and CrewAI have further extended this capability, enabling AI to plan, execute tool calls, iterate on complex tasks, and coordinate with other agents autonomously.

This shift changes the nature of AI involvement. The relevant question is no longer "Can AI complete this task?" but rather "When multiple AI participants contribute to a project across months or years, how does the project itself remain coherent?" The distinction is important: capability addresses what an agent can do in a single turn; continuity addresses what happens to the project across all turns, all agents, and all sessions combined.

The continuity problem is already present in current workflows. Developers using Cursor, Copilot, and Claude across sessions experience context loss and decision drift. Game designers using AI for level design must re-explain project constraints at each session. The problem is domain-agnostic: it arises wherever AI participates in sustained work across multiple interactions.

We present Workbench, a project continuity layer that addresses this problem. Software and game development are the first implementation domains; they are not intended as the architectural boundary.

### 1.2 The Continuity Problem in Long-Term AI Projects

The continuity problem can be stated precisely: *In long-term projects involving AI participants, the project's coherence can degrade as participants change because existing execution, memory, and storage tools do not consistently provide an independent substrate for project identity, decision provenance, and accepted state.*

This problem has three structural causes. First, AI sessions are ephemeral by design: they begin, accomplish a task, and end, carrying no persistent state. Second, AI models are unstable participants: they are deprecated, upgraded, and replaced on timescales shorter than most projects. Third, current tools optimize for either execution (agent frameworks) or storage (knowledge bases) but not for governance (deciding what constitutes project reality).

The result is a systematic pattern: each participant change introduces a small risk of context loss. Over months and dozens of transitions, these small risks compound into significant project drift.

To make this concrete, consider a scenario that illustrates the operational reality of long-term AI-assisted projects. (A detailed version appears in §2.2.)

A game development team creates a roguelike game called *JadeFixio*, built with the Godot engine. Over eighteen months, multiple AI participants contribute. Through extensive design exploration, the team establishes a multi-phase boss combat structure. Later agents, lacking access to this decision history, propose contradictory approaches. A testing agent detects inconsistencies but cannot determine whether they are intentional or drift. The original model is deprecated; a new agent joins with no access to the reasoning behind existing decisions.

This scenario reflects the operational reality of any project spanning months with multiple AI participants. Sessions end, models change, and the understanding of *why the project is the way it is* dissipates with each transition.

### 1.3 Existing Approaches and Their Limitations

Existing solutions address fragments of this problem but generally not the whole:

**Conversation memory systems** \[12, 19] preserve chat history but do not distinguish between exploration and commitment. A conversation that considers and rejects three approaches preserves all three with equal weight, making it impossible for a future participant to know which was actually adopted.

**Knowledge bases** \[13] (Notion, Obsidian, Wikis) store information effectively but do not govern which information constitutes project reality. They optimize for information retrieval, not for decision governance. A knowledge base might contain a design document proposing health-scaling bosses alongside a later document proposing three-phase structure, with no mechanism to indicate which represents the current accepted direction.

**Agent frameworks** \[8, 9, 10, 11] optimize for task execution but assume that project context is externally maintained. The agent receives a task, executes it, and returns results. The framework does not govern whether those results become part of the project's accepted reality or remain as uncommitted suggestions.

**Git** \[14] tracks code changes with exquisite precision but operates at the implementation layer. A commit records that `EnemyAI.cs` was modified but not *why* the three-phase boss structure was chosen, what alternatives were considered, or what evidence supported the decision.

**Architecture Decision Records** (ADRs) \[15] capture decisions but require manual discipline and traditionally cover only architectural-level choices. In a project where dozens of design decisions are made weekly across gameplay, narrative, art, and technical domains, manual ADR maintenance becomes impractical.

### 1.4 Contributions of This Paper

This paper makes the following contributions:

1. **Problem Formalization.** We articulate the project continuity problem for long-term AI collaboration, distinguishing it from related problems in agent memory, knowledge management, and version control. We argue that the instability of AI participants—rather than their capability limitations—is the central challenge for sustained AI-assisted work, and that this instability requires a new architectural layer rather than incremental improvements to existing tools.
2. **Three-Lane Information Architecture.** We design an information architecture comprising Authority Lane (governing what the project accepts as reality), Context Lane (providing background for understanding), and Execution Lane (recording how tasks are performed). This separation prevents AI-generated outputs from automatically becoming project truth and establishes a governance boundary that persists across agent transitions. The three-lane model draws on established separation-of-concerns principles but applies them to a novel domain: the governance of project meaning.
3. **Claim–Decision–State Formalization.** We introduce a formalization model in which AI outputs are captured as Claims, which undergo explicit Authority Decisions before entering the Accepted Project State. This model provides a clear governance mechanism compatible with varying levels of automation—from fully manual review to policy-based auto-acceptance—while maintaining the structural invariant that no change enters project reality without passing through a decision gate.
4. **Multi-Layer Compression Framework.** We design a compression model spanning Session, Handoff, Summary, Library, and Accepted State layers, each with defined roles regarding authority, cost, and traceability. This framework addresses long-term token efficiency not by making individual interactions cheaper, but by eliminating the repeated re-comprehension of project context that plagues conventional approaches. The compression model includes governance constraints that prevent compressed artifacts from acquiring authority over their sources.
5. **Implementation Verification.** We report on an implementation that verifies the core architectural kernel, with 1,022 passed tests across five system modules, establishing that the proposed design constitutes a working system substrate rather than a purely conceptual contribution. Full architectural validation—through user studies, multi-agent experiments, and longitudinal case studies—remains future work.

---

## 2\. Background and Motivating Scenarios

### 2.1 Lifecycle Characteristics of Long-Term AI Projects

Long-term projects—whether in software development, game design, scientific research, or sustained content creation—share several characteristics that differentiate them from one-shot AI interactions. Understanding these characteristics is essential for designing a continuity layer that addresses real operational needs.

**Temporal span.** These projects unfold over months or years, far exceeding the lifespan of any individual conversation session. The game project *JadeFixio* described below illustrates a realistic development timeline. A research project might span three to five years. A large software system may be under active development for a decade. The temporal dimension introduces challenges: participant changes, technology evolution, and the accumulation of decisions that must remain coherent over time. The importance of continuity generally increases with project duration.

**Participant flux.** The humans and AI agents involved in a project change over time. Team members rotate due to organizational changes. AI models are upgraded, deprecated, or replaced by superior alternatives. Tools and frameworks evolve, sometimes breaking backward compatibility. Each transition risks losing accumulated project understanding. The probability of participant change becomes increasingly likely over sufficiently long timeframes. A project that cannot survive participant changes cannot survive time itself.

**Accumulated decisions.** A mature project is defined not only by its current state but by the history of decisions that produced it. Understanding *why* the combat system uses a three-phase boss structure—rather than a health-scaling approach—requires access to decision provenance: the alternatives considered, the evidence evaluated, the tradeoffs acknowledged, and the reasoning that led to acceptance. This provenance is often more valuable than the decision itself, because it enables future participants to evaluate whether the decision's premises still hold. A decision without provenance is a rule without justification—and rules without justification are brittle.

**Multi-modal artifacts.** Long-term projects produce diverse outputs: source code, documentation, design specifications, test results, visual assets, audio files, strategic analyses, meeting notes, and user feedback. These artifacts exist in different formats, are produced by different tools, and require different forms of governance. A code change might be governed by automated tests; a design decision might require human review; a strategic pivot might require organizational approval.

**Reversibility requirements.** Mistakes are inevitable over long timeframes. The ability to trace a current state back to its originating decisions—and to safely reverse or modify those decisions—is essential for project health.

### 2.2 A Motivating Example: The *JadeFixio* Project

To illustrate the continuity problem with a concrete case, we use *JadeFixio*—a real game development project that motivated the design of Workbench. This is not a validation case where Workbench managed the project; rather, it is the project whose operational pain points drove the architectural decisions described in this paper.

**Project:** *JadeFixio* is an independent game built with the Godot engine, featuring a Sichuan Mahjong roguelike core mechanic, a jade token system, a relic system, and boss challenge design. Development spans over a year with multiple AI participants contributing to design discussion, code implementation, and review.

**Project Identity.** The project's identity is established at inception: a roguelike game combining mahjong strategy with procedural progression, emphasizing tactical depth and replayability. This identity is not a feature list but a design philosophy that guides all subsequent decisions.

**Phase 1: Design (Months 1–3).** The project creator uses AI agents to assist with core system design. Through extensive dialogue, multiple approaches to boss difficulty are explored: pure numerical scaling, phase-based mechanic changes, and adaptive difficulty. After analysis, a multi-phase boss structure is selected—each phase introduces distinct mechanics rather than simply increasing stats. Additional decisions (relic system design, jade token economy) follow the same explore-then-commit pattern.

**Phase 2: Implementation (Months 4–8).** A code generation agent takes over. It needs to understand not only *what* to build but *why* specific design choices were made. Without access to design rationale, the agent might reintroduce rejected alternatives or implement features inconsistently with the established philosophy.

**Phase 3: Review (Months 9–12).** A testing agent identifies balance issues. Boss encounters feel well-designed, but relic progression feels inconsistent with the core combat philosophy. To diagnose the problem, the agent needs the original design intent and the decisions that shaped the current implementation.

**Phase 4: Evolution (Months 13+).** The original AI model is deprecated. A new agent joins. The core question: can this new participant understand not just the project's current state but its identity, history, and decision rationale?

This scenario encapsulates the project continuity problem. The pain points observed in *JadeFixio*—lost decision context, contradictory agent proposals, inability to trace design rationale—motivated the architecture described in the remainder of this paper.

```
Month 0          Month 3          Month 8          Month 12         Month 18
   |                |                |                |                |
   v                v                v                v                v
Project         Design           Implementation   Testing          Leader
Identity        Decisions        Agent Change     Agent            Replacement
   |                |                |                |                |
   └─── Project Identity persists ────────────────────────────────────┘
                    |                               |
              Three-phase                    Inconsistency
              boss decided                    detected, but
                                                 provenance
                                                 unavailable

Question: Can the project survive participant change?
Fig. 1. Project lifecycle with participant transitions. The project's
identity and decisions persist across agent changes, but without a
governance layer, decision provenance is lost at each transition.
```

### 2.3 The Compounding Cost of Context Loss

The continuity problem is not merely an inconvenience; it has compounding costs that affect project quality, efficiency, and coherence. When a new AI agent joins a project, it must reconstruct project understanding from available artifacts. If decision provenance is lost, the agent may:

* **Reintroduce rejected alternatives.** Without knowing that health-scaling was explicitly rejected, an agent might propose it as an improvement, consuming user time to re-reject an already-settled question.
* **Inconsistently apply design philosophy.** Without understanding the project's identity as a strategic RPG emphasizing player agency, an agent might implement mechanics that prioritize convenience over strategic depth, subtly undermining the project's design coherence.
* **Duplicate prior exploration.** Without access to previous analysis, an agent might spend tokens and time evaluating approaches that were already considered and rejected, wasting computational resources and delaying progress.
* **Misinterpret existing artifacts.** Without context for why certain choices were made, an agent might view intentional design decisions as mistakes to be corrected, introducing regressions rather than improvements.
* **Propose conflicting changes.** Without understanding the full decision history, an agent might propose changes that are logically inconsistent with established decisions, creating internal contradictions in the project.

Each of these failures degrades project coherence. Over months and dozens of agent transitions, the cumulative effect can be severe: the project drifts from its original vision through accumulated small losses of context.

The cost is not only qualitative. Repeated re-comprehension consumes attention, tokens, and time, and can also introduce new inconsistencies. The magnitude of this cost is an empirical question for future measurement rather than a quantified result claimed here.

---

## 3\. Related Work and Boundary Analysis

This section examines existing technologies and methodologies that address aspects of the project continuity problem, identifying what each solves and where gaps remain. Our goal is not to disparage existing tools—they are excellent at what they do—but to precisely delineate the space that Workbench occupies.

### 3.1 Version Control Systems: Git

Git \[14] is the most successful and widely adopted tool for managing the temporal evolution of software artifacts. It provides a distributed, immutable record of code changes, enabling developers to track modifications, attribute changes to authors, create parallel development branches, merge divergent work, and traverse history with arbitrary granularity. Git answers the questions: *What changed? Who changed it? When? What was the state at any prior point?*

Git's design philosophy—immutable content-addressed storage, distributed architecture, lightweight branching—has proven remarkably durable and has influenced numerous subsequent systems. Its success demonstrates the value of treating history as a first-class concern.

However, Git operates at the implementation layer. A commit records that `EnemyAI.cs` was modified, with a diff showing the specific code changes and a message describing *what* was done. But Git does not capture *why* the three-phase boss structure was chosen over alternatives, what evidence supported that decision, or how the decision relates to the project's broader design philosophy. The design rationale—the space of considered alternatives, the tradeoffs evaluated, the reasoning that led to acceptance—exists outside Git's representational scope.

Git also does not distinguish between exploratory work and committed decisions. A branch containing an abandoned prototype and a branch containing the accepted implementation are structurally identical in Git's model. The governance question—which work represents project reality—is orthogonal to version control.

Workbench does not compete with Git. Rather, it operates at a complementary layer: the project's semantic evolution. A Decision in Workbench might correspond to multiple Git commits across different files, repositories, and time periods. The relationship is: Workbench decides *why* and *what*; Git records *how*.

### 3.2 Architecture Decision Records (ADR)

Architecture Decision Records \[15] provide a structured format for documenting significant design choices. A typical ADR includes the decision's context (the problem and constraints), the options considered, the chosen approach, and the consequences (positive and negative). ADRs are typically stored as markdown files alongside the codebase, creating a human-readable history of architectural choices.

ADRs share Workbench's fundamental concern with decision provenance. They recognize that the *reasoning* behind a decision is often more valuable than the decision itself, and that documenting this reasoning enables future participants to evaluate whether a decision's premises still hold.

However, ADRs exhibit several limitations in the context of long-term AI collaboration:

* **Scope limitation.** ADRs traditionally address architectural decisions—choices about technology, structure, and design patterns that affect the entire system. But projects involve decisions at every level: feature design, UI text, numerical balance, narrative direction, testing strategy, and more. A game project might make dozens of significant design decisions per week, most of which fall below the architectural threshold but are nonetheless essential to project coherence.
* **Manual maintenance.** ADRs require human discipline to create, update, link to subsequent decisions, and mark as superseded when appropriate. In AI-intensive workflows where agents produce dozens of proposals per session, manual ADR maintenance becomes impractical. The overhead of documenting every decision in ADR format would overwhelm the development process.
* **No governance integration.** ADRs document decisions but do not enforce a governance model. There is no mechanism to prevent an AI agent from treating a rejected alternative as established fact in a subsequent session. An ADR might record that health-scaling was rejected, but if the agent does not read the ADR—or reads it but does not understand its authority—the rejection has no effect.
* **No state projection.** ADRs are narrative documents; they do not generate a machine-readable project state. Answering the question "What is the current accepted approach to boss combat?" requires reading and interpreting multiple ADRs, a process that is error-prone and does not scale.

Workbench extends the ADR concept along three dimensions: scope (all project decisions, not just architectural ones), automation (AI agents can propose decisions that enter the governance pipeline automatically), and integration (decisions project into an Accepted Project State that provides a definitive answer to "what is currently true?").

### 3.3 Event Sourcing and CQRS

Event Sourcing \[16, 17] is an architectural pattern in which system state is derived from an immutable sequence of events rather than stored directly as a mutable snapshot. The current state is a *projection* of all historical events, enabling complete reconstruction, auditing, and temporal queries. If a projection becomes corrupted or a new query pattern is needed, the system can recompute the projection from the event history.

CQRS (Command Query Responsibility Segregation) \[18] complements Event Sourcing by separating the write model (commands that produce events) from the read model (projections optimized for queries). This separation allows each side to be independently optimized, scaled, and evolved.

Workbench draws significant inspiration from both patterns. The Authority Decision → Accepted Project State relationship mirrors Event Sourcing's event → projection model: the Accepted Project State is not stored directly but derived from the projection of all accepted Authority Decisions. The separation between Authority Lane (write side, governing what enters project reality) and Library (query side, providing browsable views) reflects CQRS principles.

However, Workbench differs from traditional Event Sourcing in a critical respect that reflects the unique nature of project governance. Conventional Event Sourcing manages *system state*—orders, payments, user registrations, inventory levels. These are data mutations with well-defined semantics. Workbench manages *project meaning*—design decisions, responsibility assignments, accepted directions, evolving vision. An `AuthorityDecision` asserting "the boss combat system uses three phases" is not a database update; it is a governance act that changes what the project considers to be real.

This distinction has practical implications. In traditional Event Sourcing, events are typically append-only and non-contradictory (an order is created, then paid, then shipped; each event advances the state). In Workbench, decisions may conflict (a new decision supersedes an old one), may be conditional (a decision applies only under certain circumstances), and may be contested (different participants may disagree about whether a decision should be accepted). The governance model must handle these complexities while maintaining the core Event Sourcing property that the current state is derivable from the event history.

### 3.4 Agent Memory Systems

Recent research on LLM agent memory addresses the problem of maintaining context across interactions. Approaches include conversation buffers that preserve recent interaction history within the context window \[12], retrieval-augmented generation (RAG) systems that store and retrieve relevant past interactions based on semantic similarity \[19, 20], and structured memory architectures that organize information into semantic categories with varying levels of abstraction.

These systems share a common optimization target: *recall*. They aim to ensure the agent can access relevant past context when needed, improving the quality and consistency of agent outputs. The underlying assumption is that more relevant information leads to better agent performance.

Workbench addresses a fundamentally different problem. The challenge is not information availability but *governance*: distinguishing between information that the project has accepted as reality and information that merely exists in the project's history. A conversation buffer preserves both the proposal "bosses should use health scaling" and the final decision "bosses use three phases" with equal weight. A RAG system might retrieve either one based on semantic similarity to a query, with no indication of which represents the current accepted direction.

Workbench explicitly separates these categories. The proposal is a Claim (historical artifact, no authority). The decision is an Authority Decision (project reality, highest authority). A future agent querying the system receives not a ranked list of relevant passages but a structured answer: "The boss combat system uses three-phase structure, as established by Decision #52 on 2026-03-15, superseding Decision #19's health-scaling approach."

Moreover, agent memory systems are typically coupled to specific agents or models. When the agent changes, the memory is lost or must be migrated—a process that risks information loss and inconsistency. Workbench's state is independent of any particular agent, surviving participant transitions by design. The project's memory is a property of the project, not of any participant.

### 3.5 Agent Frameworks

Agent frameworks such as AutoGPT \[8], ReAct \[9], AutoGen \[10], LangChain \[11], and CrewAI focus on enabling AI agents to perform complex tasks through planning, tool use, and iterative refinement. They answer the question: *How does an agent accomplish a goal?* These frameworks provide mechanisms for task decomposition, tool invocation, self-reflection, and multi-agent coordination.

These frameworks assume that the project context is maintained externally. The agent receives a task description, executes it using available tools, and returns results. The framework optimizes for the quality and efficiency of task execution but does not govern whether those results become part of the project's accepted reality.

Workbench complements agent frameworks by providing the governance layer that sits between agent output and project state. An agent framework ensures the agent can work effectively; Workbench ensures the project remains coherent after the agent's work is done. The relationship is analogous to the relationship between a CPU scheduler (which manages execution) and a file system (which manages persistent state). Both are necessary; neither is sufficient.

### 3.6 Change Management and ITIL

Enterprise IT operations have long employed Change Management frameworks, notably ITIL \[21], to govern changes to production systems. The typical flow is: Request → Review → Approval → Implementation → Verification. These frameworks address the risk that uncontrolled changes to production systems can cause outages, data loss, or security vulnerabilities.

Workbench shares Change Management's concern with governance over changes, but the focus differs significantly. Traditional Change Management governs *operational risk*: "Will this server modification cause downtime?" Workbench governs *project meaning*: "Does this design direction become part of the project's accepted reality?" The risk models, approval processes, and rollback mechanisms are different because the domains are different.

Additionally, Workbench's governance model is designed for the specific characteristics of AI participation: high-volume proposals, rapid iteration, and the need for varying levels of automation. Traditional Change Management assumes human-driven workflows with relatively low change frequency and does not translate directly to AI-intensive environments.

### 3.7 Project Management Systems

A natural question for any reader familiar with software engineering is: "How does Workbench differ from project management tools like Jira, Linear, Notion, or GitHub Projects?" These tools are ubiquitous in software development, and the distinction is important.

Traditional project management systems operate on a task-centric model:

```
Task → Assignment → Status → Completion
```

They answer: *Who is doing what? When will it be done? What is the current status?* Jira tracks work items through a workflow. Linear manages issues with priorities and cycles. Notion provides flexible documentation and task boards. GitHub Projects integrates task tracking with code repositories.

These tools are excellent at their intended purpose. But they share a fundamental limitation for long-term AI collaboration: they manage *work* but not *meaning*. A Jira ticket might record that "Implement boss combat system" was completed in Sprint 7, but it does not record *why* the three-phase structure was chosen over alternatives, what evidence supported the decision, or what design philosophy governs the choice.

Workbench operates on a decision-centric model:

```
Project Reality → Decision → Accepted State → Assignment → Execution
```

It answers: *Why is the project the way it is? What has been decided? Who authorized it? How can we recover if a decision proves wrong?*

The relationship between the two is complementary, not competitive. Project management tools track the *execution* of work. Workbench governs the *meaning* of work. A well-integrated workflow might use Workbench for decision governance and Jira for task tracking, with Workbench Decisions linked to Jira Epics.

### 3.8 Recent Convergent Systems

Recent systems indicate that the problem space is beginning to converge rather than remaining unoccupied. PROJECTMEM explores local-first, event-sourced memory and judgment for coding agents, including typed development events and pre-action warnings. The Agentic Engineering Framework applies structural governance, task gates, tiered approval, context budgeting, and audit mechanisms to agentic software work. Continuity Kernel work focuses on transactional activation of accepted state and explicit lineage across changing participants. SPM and other project-memory efforts discussed by the community approach adjacent parts of the same space. These names are included as community-visible adjacent efforts rather than formal literature citations; the list is intentionally non-exhaustive and should not be read as a feature-complete comparison.

These systems are important neighbors, not straw targets. They show that project memory, continuity, provenance, and governance are emerging as related concerns. Workbench explores a particular integration centered on a first-class Project World, explicit Authority / Context / Execution separation, a Claim → Decision → Accepted State pipeline, and replaceable participants across software, game, research, writing, and other long-lived project domains. The claim is therefore one of architectural emphasis and combination, not that no related system exists.

### 3.9 Gap Identification

Table 1 summarizes the gap analysis across existing technologies.

```
+---------------------------+------+-----+----------+-------+---------+-------+------+
| Capability                | Git  | ADR | EventSrc | Agent | Agent   |  PM   | WB   |
|                           |      |     | /CQRS    | Mem   | Framewk | Tools |      |
+---------------------------+------+-----+----------+-------+---------+-------+------+
| Implementation tracking   |  ✓   |     |          |       |         |       |      |
| Decision documentation    |      |  ✓  |          |       |         |       |  ✓   |
| Task tracking             |      |     |          |       |         |   ✓   |      |
| Full-scope governance     |      |     |          |       |         |       |  ✓   |
| State from events         |      |     |    ✓     |       |         |       |  ✓   |
| Read/write separation     |      |     |    ✓     |       |         |       |  ✓   |
| Context preservation      |      |     |          |   ✓   |         |       |  ✓   |
| Agent-independent state   |      |     |          |       |         |       |  ✓   |
| Claim-Decision governance |      |     |          |       |         |       |  ✓   |
| Agent replaceability      |      |     |          |       |         |       |  ✓   |
| Multi-layer compression   |      |     |          |       |         |       |  ✓   |
| Authority provenance      |      |     |          |       |         |       |  ✓   |
+---------------------------+------+-----+----------+-------+---------+-------+------+

Table 1: Gap identification across existing technologies. Workbench (WB)
addresses the intersection of governance, agent-independence, and project
continuity. PM tools (Jira, Linear, Notion) manage tasks but not meaning.
```

The gap that Workbench targets is an intersection that existing systems address with different emphases: (1) governance over what constitutes project reality across decision levels, (2) independence from specific AI participants, models, and sessions, and (3) long-term continuity through multi-layer information architecture with explicit authority hierarchies. Workbench's contribution is to make these concerns one project-centered contract rather than a set of loosely connected features.

The gap is not a failure of existing tools. Git, ADRs, agent frameworks, knowledge bases, and emerging project-memory systems each excel at particular purposes. The open design question is how to integrate governance, continuity, and replaceability around a durable Project World without making the system another monolithic agent platform.

```
              Persistent
                  |
                  |
     Workbench  ★ |
                  |
          ADR  ●  |
                  |
       Git  ●     |
                  |
──────────────────┼──────────────────────────
           Execution          Governance
                  |
                  |
  Agent           |          ●  PM Tools
  Frameworks  ●   |
                  |
  Memory      ●   |
  Systems         |
                  |
              Transient

Fig. 6. Positioning of Workbench relative to existing tools along two
dimensions: execution vs. governance and transient vs. persistent.
Workbench occupies the persistent-governance quadrant,
combining long-term state maintenance with decision governance.
```

### 3.10 Domain-Driven Design and Bounded Contexts

Domain-Driven Design (DDD) \[22, 23] provides another relevant theoretical foundation. DDD's concept of *bounded contexts*—explicitly defined boundaries within which a particular domain model applies—resonates with Workbench's lane separation. The Authority Lane defines the bounded context for project reality; the Context Lane defines the bounded context for project understanding; the Execution Lane defines the bounded context for task performance.

DDD's emphasis on *ubiquitous language*—a shared vocabulary between developers and domain experts—also informs Workbench's design. The concepts of Claim, Decision, Accepted State, and Supersedes form a ubiquitous language for project governance that is shared between human users and AI agents.

However, DDD was designed for software systems with human developers, not for projects with AI participants. The challenges of agent replaceability, automated governance, and compression are outside DDD's scope. Workbench extends DDD's principles to this new domain.

---

## 4\. System Design

This section presents the core design of Workbench. We begin with the design principles that motivate the architecture, then describe the information architecture, formalization model, and system components in detail.

### 4.1 Design Principles

Workbench's architecture is governed by five design principles, each addressing a specific failure mode of conventional approaches to long-term AI collaboration. These principles are not abstract ideals; they are engineering constraints that directly shape the system's structure.

**Principle 1: Project as First-Class Entity.** The project—not the agent, not the session, not the user—is the primary entity in the system. Users, agents, models, and tools are all *participants* in the project. No single participant is equivalent to the project itself.

*Motivation:* In conventional AI-assisted workflows, the project's identity is implicitly encoded in a sequence of chat sessions. When sessions end or agents change, the project's coherence degrades because there is no entity that persists independently of the participants. By elevating the project to first-class status, Workbench ensures that the project has an identity, history, and state that persist regardless of which participants are currently active.

*Architectural implication:* The project is represented as a persistent object with its own identity, independent of any agent, session, or runtime. All other objects (agents, sessions, decisions) are related to the project but not constitutive of it.

**Principle 2: Authority Separation.** Information generation (by AI or humans) is strictly separated from project state acceptance. A generated output is a *Claim*; only an explicit *Authority Decision* can transform a Claim into part of the *Accepted Project State*.

*Motivation:* AI agents generate plausible but potentially incorrect or inconsistent outputs. In a system without authority separation, the most recent AI output may overwrite established decisions, leading to project drift. An agent in month 6 might propose an approach that was explicitly rejected in month 2, and without a governance boundary, the proposal might silently become project reality.

*Architectural implication:* The system enforces a strict pipeline: Claim → Authority Decision → (possibly) Accepted State. No shortcut exists. An agent cannot directly write to the Accepted State regardless of its role or capabilities.

**Principle 3: Compression Does Not Increase Authority.** When information is compressed—summarized, abstracted, or distilled—the compressed form does not gain authority over the original. A Summary is more convenient to read than the original Session, but it is not more true.

*Motivation:* Long-term projects accumulate vast amounts of information. Compression is necessary for practical management. However, conventional memory systems often treat compressed information as equivalent to or superior to the original, leading to subtle distortions. An early summary that inaccurately characterizes a decision may be treated as authoritative by future agents, even though the original discussion contains nuances that the summary omitted.

*Architectural implication:* Every compressed artifact explicitly references its sources and carries an authority label indicating that it is derived (contextual) rather than primary (authoritative). The system enforces the hierarchy: Accepted State > Authority Decision > Claim > Context.

**Principle 4: Replaceable Agent Principle.** The system does not assume the persistence of any particular agent, model, or runtime. Agents are interchangeable participants whose role is to produce Claims and execute tasks, not to embody the project.

*Motivation:* The AI landscape changes rapidly. Models are deprecated, new agents emerge, and capabilities shift. A system that binds project continuity to a specific agent creates a single point of failure at the most volatile layer. Workbench's design ensures that agent changes are operational transitions, not existential threats.

The historical record supports this concern. In the span of three years (2023–2026), the AI landscape has seen the emergence and subsequent evolution of GPT-3.5, GPT-4, Claude 1/2/3/4, Gemini, Llama, Mistral, and numerous specialized coding agents. Any system that bound its continuity to a specific model from this list would have required multiple migrations. Workbench's replaceability principle ensures that such migrations affect only the adapter layer, not the project's core state.

*Architectural implication:* All agent interactions are mediated through a uniform interface contract (Assignment → Attempt → Claim/Handoff). The internal implementation of the agent is irrelevant to the project continuity layer. Agent adapters handle translation between this contract and agent-specific interfaces.

**Principle 5: Highest-Density-Sufficient Reading.** When a new participant needs to understand the project, the system should provide the *smallest sufficient* information layer. The goal is not to provide more information but to provide exactly enough.

*Motivation:* Token costs and cognitive load are real constraints. Requiring every new session to re-read the entire project history is wasteful. Providing only a superficial summary risks misunderstanding. The highest-density-sufficient principle optimizes the cost-benefit tradeoff by enabling participants to read the most compressed layer that contains sufficient information for their current task.

*Architectural implication:* The system maintains multiple compression layers (Session, Handoff, Summary, Library, Accepted State) and provides a reading protocol that selects the appropriate layer based on task complexity. Simple tasks read the Accepted State; complex tasks traverse down to Decisions and Handoffs; forensic analysis reaches the original Sessions.

### 4.2 Three-Lane Information Architecture

Workbench organizes all project information into three lanes, each with distinct governance rules, data types, and purposes. This separation is the architectural foundation that enables the governance model to function.

```
                    Human User
                        |
                        v
                  Leader Agent
                        |
            ---------------------------
            |                       |
            v                       v
     Worker Agents          Governance Layer
     (routed by task)       (Claim → Decision → State)
            |                       |
            v                       v
      Execution Result ------> Claim ------> Authority Decision
            |
            v
          Handoff
                                    |
                                    v
                         Accepted Project State
                                    |
                    -------------------------------
                    |                             |
                    v                             v
            Library Projection          Git / Artifacts
            (read-only view)            (implementation)

      Handoff + Accepted State ------> Next Participant

Fig. 2. Overall Workbench architecture. Agents produce Execution Results and
Handoffs. Results may inform Claims; Claims enter the Governance Layer, which
determines whether they enter project reality. Accepted State projects into
Library (read), while Handoffs preserve execution continuity for the next
participant.
```

```
┌─────────────────────────────────────────────────────────────────────┐
│                         PROJECT WORLD                               │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    AUTHORITY LANE                             │  │
│  │                                                               │  │
│  │  Claim ──→ AuthorityDecision ──→ AcceptedProjectState        │  │
│  │                                                               │  │
│  │  Purpose: Define what the project considers real              │  │
│  │  Rule: Only Authority Decisions can modify this lane          │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                              ▲                                      │
│                              │ governs                              │
│                              │                                      │
│  ┌───────────────────────────┴────────────────────────────────────┐  │
│  │                    CONTEXT LANE                                │  │
│  │                                                                │  │
│  │  Session ──→ Handoff ──→ Summary ──→ Library                 │  │
│  │                                                                │  │
│  │  Purpose: Help participants understand the project            │  │
│  │  Rule: Subordinate to Authority Lane; no direct state writes  │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    EXECUTION LANE                             │  │
│  │                                                               │  │
│  │  Assignment ──→ Attempt ──→ Result                           │  │
│  │                                                               │  │
│  │  Purpose: Record how work is performed                        │  │
│  │  Rule: Feeds into Context Lane; does not directly write       │  │
│  │        to Authority Lane                                      │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

Fig. 7. Three-Lane Information Architecture. The Authority Lane governs
what the project accepts as reality. The Context Lane provides background
for understanding. The Execution Lane records how tasks are performed.
Data flows upward: Execution produces Handoffs and informs Claims, while
Accepted State provides authoritative context. Only Authority Decisions define
project reality.
```

**Authority Lane.** This lane contains the project's accepted reality. The flow is: a participant (human or AI) produces a **Claim** (a proposed change to the project); an **Authority Decision** determines whether the Claim is accepted; if accepted, the change enters the **Accepted Project State**. This lane is immutable in the sense that Authority Decisions, once made, form a permanent historical record. The Accepted Project State is a projection of all accepted decisions, not a mutable database that can be directly edited.

The Authority Lane's immutability is essential for project integrity. It means that even if an agent makes a mistake, the mistake is recorded as a Claim (a proposal) rather than as a state mutation. The Authority Decision gate ensures that only explicitly accepted changes enter project reality.

**Context Lane.** This lane preserves the information that helps participants understand the project. **Sessions** capture complete interaction histories, including explorations, dead ends, and corrections. **Handoffs** distill a session's relevant outcomes for the next participant, providing a structured handover of work-in-progress. **Summaries** provide high-density project context for quick orientation. The **Library** offers long-term navigation and evolution viewing through projected views of the project's decision history.

Critically, information in the Context Lane is *subordinate* to the Authority Lane. A Handoff may contain unfinished work, a proposed change, or a pending Claim; a Summary that states "the boss uses health scaling" does not override an Authority Decision establishing three-phase structure. Context artifacts are flagged as incomplete, outdated, or inaccurate when appropriate, but the Authority Decision stands. This subordination prevents the common failure mode in which the most recent (or most concise) context document is mistaken for project truth.

**Execution Lane.** This lane records how work is performed. **Assignments** define who is responsible for what task, including the task's scope, objectives, and reference baseline. **Attempts** capture the execution process, including intermediate results and iterations. **Results** document what was produced and how it was verified.

The Execution Lane feeds into the Context Lane (execution results produce Handoffs and inform understanding) but does not directly modify the Authority Lane (execution results require governance before becoming project reality). This separation is critical: an agent that completes an implementation task has produced a Result and possibly a Handoff, but neither automatically becomes part of the project's accepted reality. A proposed change enters the governance pipeline as a Claim, awaiting an Authority Decision.

**Data flow.** Information flows upward through the lanes: Execution results become context, context informs claims, claims undergo governance, and accepted decisions modify project state. The reverse flow—where execution or context directly modifies authority—is prohibited by architectural constraint.

### 4.3 Claim–Decision–State Model

The Claim–Decision–State model formalizes how changes enter the project's accepted reality. This model is the governance backbone of Workbench.

**Definition 1 (Claim).** A Claim *C* is a proposed change to the project, comprising: the proposing participant (*p*), the proposed content (*content*), optional supporting evidence (*evidence*), and a timestamp. Claims can propose additions, modifications, or removals of project elements.

Claims are the entry point for all changes. Whether generated by a human user, a Leader agent, or a Worker agent, every proposed change enters the system as a Claim. Claims carry no inherent authority—they are proposals, not facts.

**Definition 2 (Authority Decision).** An Authority Decision *D* is a governance act that evaluates a Claim and determines its fate, comprising: the claim being decided (*C*), the outcome (*outcome* ∈ {accept, reject, defer}), the authorizing entity (*authority*), a timestamp, and an optional reference to a prior decision being superseded (*supersedes*).

Authority Decisions are the sole mechanism for modifying the Accepted Project State. They are immutable once made—the decision itself cannot be altered, though it can be superseded by a subsequent decision.

**Definition 3 (Accepted Project State).** The Accepted Project State *S* at time *t* is the projection of all Authority Decisions with `outcome = accept` made before *t*, ordered by timestamp, with later decisions superseding earlier ones when conflicts exist.

```
                    ┌─────────────┐
                    │   Claim     │
                    │  (Proposed) │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
                    │  Authority  │
                    │  Decision   │
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
        ┌─────▼─────┐ ┌───▼────┐ ┌────▼─────┐
        │  Accept    │ │ Reject │ │  Defer   │
        └─────┬─────┘ └────────┘ └──────────┘
              │
        ┌─────▼─────────────────────┐
        │  Accepted Project State   │
        │  (Projection of all       │
        │   accepted decisions)     │
        └───────────────────────────┘

Fig. 3. Claim–Decision–State transition model. Claims are proposed
changes that undergo Authority Decisions. Only accepted claims
enter the Accepted Project State through projection.
```

**Supersedes mechanism.** When a new Authority Decision supersedes a prior one, the prior decision remains in the historical record but is marked as superseded. The Accepted Project State reflects the newer decision's content. This enables project evolution without historical revisionism—the full history of alternation is preserved, enabling future participants to understand both the current state and the trajectory that produced it.

For example, if Decision #19 established "bosses use health scaling" and Decision #52 later establishes "bosses use three phases" (superseding #19), the project state reflects three-phase bosses, but the historical record preserves the fact that health scaling was once the accepted approach and explains why it was changed.

### 4.4 Leader-Worker Architecture

Workbench adopts a Leader-Worker architecture for agent participation, with explicit separation of concerns motivated by the different roles agents play in long-term projects.

**Leader Agent.** The Leader is a *State Interpreter and Proposal Coordinator*—not a Truth Generator. It serves as the primary interface between the project and the user. Its responsibilities include:

* **State comprehension:** Reading the Accepted Project State, current Assignments, and relevant Context to understand the project's current situation and active work.
* **Task decomposition:** Breaking user goals into specific, assignable sub-tasks that can be distributed to Worker agents with appropriate capabilities.
* **Assignment creation:** Defining clear responsibilities, objectives, and reference baselines for Worker agents, including the specific Accepted State revision that serves as the work's foundation.
* **Result aggregation:** Collecting Worker outputs, organizing them into coherent presentations, and identifying decision points that require user attention.

Critically, the Leader does *not* possess authority to modify the Accepted Project State. It can propose (via Claims) but not decide. It interprets the project's current state and coordinates proposals—it does not generate truth. This design prevents leader agent errors from directly corrupting project reality.

**Worker Agents.** Workers execute specific tasks assigned by the Leader. Each Worker receives an Assignment, performs work using its specific capabilities, and produces a Result Claim and Handoff. Workers may use different models, tools, and runtimes depending on task requirements.

The Leader-Worker separation addresses a fundamental challenge: different tasks require different capabilities. Architectural reasoning benefits from high-reasoning models; code implementation benefits from specialized coding agents; routine organization benefits from lightweight models. The Leader routes tasks to the most appropriate Worker without requiring a single agent to handle all responsibilities.

### 4.5 Agent Routing and Replaceability

Agent replaceability is a first-class design goal, not an afterthought. The system must function correctly regardless of which specific agent, model, or runtime is currently active. This is achieved through a uniform interface contract:

1. **Input:** An Agent receives an Assignment containing the task description, relevant context (drawn from the appropriate compression layer), and reference to the current Accepted State.
2. **Process:** The Agent executes the task using its specific capabilities (reasoning, code generation, analysis, etc.).
3. **Output:** The Agent produces a Result Claim and Handoff in a standardized format, ready for governance.

The output format is invariant across agents. Whether the claim was generated by GPT-5, Claude, Codex, or a local model, it enters the same governance pipeline: Claim → Authority Decision → (possibly) Accepted State.

An **Agent Adapter** layer handles the technical translation between Workbench's internal representation and the specific interfaces of different agent providers. The adapter manages session creation, task delivery, output reception, and Handoff conversion—but explicitly does not modify project state or override authority boundaries. The adapter is a connector, not a governor.

This design ensures that the project's continuity layer is orthogonal to the agent ecosystem. As new agents emerge and old ones are deprecated, only the adapter layer changes; the project's identity, decisions, and state remain intact.

### 4.6 Library as Projection Layer

The Library serves as the primary interface through which users observe project state. It is explicitly *not* a second database, *not* a truth store, and *not* a knowledge base. It is a read-only projection of the Accepted Project State, analogous to a materialized view in a CQRS system.

The Library presents:

* **Timeline:** A chronological view of Authority Decisions, showing what was decided, when, by whom, what it superseded, and what evidence supported it.
* **Evolution views:** How specific project aspects (e.g., combat system, UI design, narrative structure) have changed over time, with each change linked to its originating Decision.
* **Current state summaries:** The current Accepted Project State for each project dimension, providing a quick reference for "what is currently true?"

The Library's relationship to the Authority Lane mirrors the CQRS query model's relationship to the command model. Information flows one way: Authority Decisions project into Library views. The reverse flow—modifying project state by editing Library entries—is structurally prohibited. This constraint is essential for maintaining the integrity of the governance boundary.

### 4.7 Lightweight Architecture and Local-First Design

Workbench deliberately avoids becoming a comprehensive AI platform. It does not implement its own agent runtime, code execution environment, cloud infrastructure, or knowledge management system. Instead, it focuses on the project continuity layer and delegates other concerns to appropriate existing tools.

**What Workbench is:**

* A project identity layer (what is this project?)
* A decision governance layer (what has been decided and why?)
* A state continuity layer (what is the current accepted reality?)
* A traceability layer (how did we get here and how do we recover?)

**What Workbench is not:**

* An IDE (delegates to existing development environments)
* A cloud platform (local-first by default)
* An agent runtime (delegates to agent frameworks and adapters)
* A knowledge base (knowledge provides context, not authority)
* A project management tool (tracks decisions, not tasks)

The local-first design ensures that the project's core state resides with the user, not in a cloud service that might be deprecated. This aligns with the principle that project longevity should not depend on infrastructure longevity.

The analogy to SQLite and Git is instructive. SQLite succeeded not because it was the most feature-rich database but because it was simple, reliable, and self-contained. Git succeeded not because it offered the most project management features but because it solved one problem—distributed version control—with elegance. Workbench follows the same philosophy: solve one problem—project continuity for AI collaboration—thoroughly, and delegate everything else.

### 4.8 User Interaction Model

A recurring concern with governance-centric systems is usability: if every change requires explicit governance action, does the user spend more time governing than working? This section describes how Workbench's user interaction model avoids this trap through progressive disclosure and implicit governance.

**First launch.** When a user first opens Workbench, the experience is deliberately simple:

```
Create Project
  ↓
Define project goal (one sentence)
  ↓
Workbench creates Project Identity
  ↓
User works normally
```

The user does not configure governance policies, define authority structures, or learn system terminology. The governance model activates implicitly: when the user makes a significant change (or when an agent proposes one), the system captures it as a Claim. The user sees a natural-language prompt: "This change will affect the combat system. Accept?" Not: "AuthorityDecision required for Claim #47."

**Ongoing work.** During normal work, the governance layer operates largely invisibly:

```
User says: "Change boss to five phases"
  ↓
Leader interprets as Claim
  ↓
System shows: "Boss combat: 3 phases → 5 phases?"
  ↓
User confirms
  ↓
Decision recorded, Accepted State updated
```

The user experiences this as a confirmation dialog, not as a governance ceremony. The underlying Claim → Decision → State pipeline is architecturally present but interactionally lightweight.

**When governance matters.** The governance model becomes visible only when it matters: when a change conflicts with an existing decision, when an agent proposes something that was previously rejected, or when a user wants to understand why the project is in its current state. These moments are rare but critical—and the system surfaces them clearly when they occur.

This design ensures that the governance cost is proportional to the governance risk. Routine changes incur near-zero overhead. Significant changes incur a small confirmation cost. Conflicting changes incur the cost of explicit resolution. The user is never surprised by governance overhead because the system scales its intrusiveness to the significance of the change.

---

## 5\. Information Flow and Context Re-comprehension Cost

### 5.1 Reframing the Efficiency Problem

In AI-assisted development, efficiency is typically understood as a per-interaction concern. Workbench reframes efficiency as a *long-term cumulative* concern: how much effort is wasted across an entire project lifecycle due to repeated re-comprehension of project context?

Consider the conventional approach. Each new AI session begins with limited project knowledge. The user must re-explain goals, constraints, prior decisions, and current state. For a project spanning dozens of sessions, this cost compounds—not only in tokens but in the risk that each re-explanation introduces inconsistencies.

Workbench's approach is captured in two principles:

1. *Understand once.* Information should be comprehended at most once, stored in compressed form, and provided to future participants at the right level of detail.
2. *Store only the delta.* The system preserves changes relative to the previous layer, not complete state snapshots—similar to how version control stores diffs and event sourcing stores events.

### 5.2 Multi-Layer Compression Model

Workbench implements a five-layer compression model, each layer trading detail for density while maintaining explicit authority relationships:

```
Layer           Role                    Authority    Token Cost   Traceability
─────────────────────────────────────────────────────────────────────────────────
Session         Full interaction log    None         Highest      Complete
Handoff         Task handover packet    None         High         Partial
Summary         High-density context    None         Medium       References
Library         Long-term navigation    Projection   Low          Decision-level
Accepted State  Project reality         Highest      Lowest       Full (via chain)
─────────────────────────────────────────────────────────────────────────────────

Fig. 4. Multi-layer compression model. Authority and token cost are
inversely related: the most compressed layers carry the highest authority
per token but the least detail. Compressed layers never acquire
authority over their sources.
```

The reading protocol follows a highest-density-sufficient principle:

```
New participant joins project
            |
            v
    Read Accepted State
            |
      Sufficient?
       /        \\\_\_
      Yes          No
       |             |
    Begin work    Read Summary
                    |
               Sufficient?
                /        \\\_\_
               Yes          No
                |             |
            Begin work    Read Decision chain
                            |
                       Sufficient?
                        /        \\\_\_
                       Yes          No
                        |             |
                    Begin work    Read Handoff
                                    |
                                 Read Session

Fig. 5. Compression reading protocol. Participants start at the most
compressed sufficient layer and descend only when needed.
```

**Session (Layer 1).** The complete interaction history between a user/agent and the system. Sessions contain everything: explorations, dead ends, corrections, partial analyses, and final outputs. They are the most detailed but least dense layer. Sessions are the authoritative source for *what happened during the interaction* but not for *what was decided*—decisions must be explicitly made via Authority Decisions to enter the project's accepted reality.

**Handoff (Layer 2).** A structured packet that captures the relevant outcomes of a session for the next participant. A Handoff includes: the task attempted, the result produced, the key decisions made or pending, unresolved issues, and references to relevant Accepted State elements. Handoffs are the primary mechanism for inter-agent communication and are designed to be self-contained enough for a new agent to pick up work without reading the entire session. A Handoff is a structured design target whose appropriate size depends on task complexity; no empirical compression ratio is claimed here.

**Summary (Layer 3).** A high-density representation of the project's current state, recent decisions, and active work. Summaries are generated artifacts—compressed views of the project—and therefore carry no inherent authority. They reference Authority Decisions by ID rather than restating their content as standalone facts. A well-designed Summary allows a new participant to quickly orient without being misled into treating the Summary itself as ground truth. Any token range is an illustrative design target, not an empirical result.

**Library (Layer 4).** The read-only projection of the Accepted Project State, providing timeline views, evolution tracking, and current state summaries. The Library is the most compressed and most navigable layer, designed for quick reference rather than detailed understanding. Library entries should be concise enough for efficient browsing; any token range is an illustrative design target, not an empirical result.

**Accepted State (Layer 5).** The authoritative project reality, derived from the projection of all accepted Authority Decisions. This is the highest-authority, lowest-detail layer. For many routine tasks, reading the Accepted State alone may provide sufficient context to begin work; the appropriate size depends on the project and task. The Accepted State answers "what is currently true?" without requiring the participant to understand the full history of how it became true.

### 5.3 Governance in Compression

A critical design concern is ensuring that compression does not introduce governance failures. The risk is illustrated by the following scenario:

1. Session A discusses three possible boss combat approaches: health scaling, three-phase structure, and adaptive difficulty.
2. The Session is compressed into a Summary that mentions all three approaches without clearly indicating which was accepted.
3. A future agent reads the Summary and treats "adaptive difficulty" as an established decision, when in fact only "three-phase structure" was accepted.

Workbench prevents this failure through two mechanisms:

**Authority tagging.** Every compressed layer explicitly marks which information derives from Authority Decisions and which is contextual inference. Summaries include structured references: "Current boss design: three-phase structure \[Decision #52]. Alternative considered: health scaling \[Decision #19, superseded]." The authority source is always traceable.

**Supersedes tracking.** When a Decision supersedes a prior one, the superseded decision is marked as inactive but preserved in the historical record. Compression layers reflect this by showing the current state and linking to the decision chain, rather than silently omitting superseded content. This ensures that the full evolutionary trajectory is available even in compressed views.

### 5.4 Long-Term Cost Analysis

The architectural expectation is qualitative: conventional workflows repeatedly reconstruct project context from conversation excerpts, documents, code summaries, and user explanations, while Workbench lets a participant begin with Accepted State and descend into Decisions, Handoffs, and Sessions only as needed. This may reduce repeated re-comprehension and may improve context quality, but the magnitude of any token or time benefit is an empirical question for future measurement.

Context re-comprehension reduction is a secondary benefit, not the primary design goal. The primary goal is to provide *better* context. An agent that receives a clear statement of the project's accepted decisions and their provenance will produce higher-quality work than an agent that receives a jumble of past conversations, even if the total token count is similar.

---

## 6\. Traceability, Responsibility, and Recoverability

### 6.1 The Purpose of Responsibility: Recovery, Not Punishment

In Workbench, the concept of responsibility serves a specific and limited purpose: enabling recovery when things go wrong. Responsibility is not about assigning blame for errors; it is about maintaining the provenance chain that allows the system to answer: *Why is the project in this state, and how can we safely change it?*

This framing has practical implications. When a design decision proves problematic months after adoption, the system must be able to identify:

* **Who proposed the decision** (the Claim author) — to understand the perspective and assumptions behind the proposal.
* **Who authorized it** (the Authority) — to determine who accepted responsibility for the decision's consequences.
* **What evidence supported it** (the Evidence references) — to evaluate whether the decision's premises were sound.
* **What it superseded** (the prior Decision) — to understand what was abandoned and why.
* **What depends on it** (the affected project state) — to assess the blast radius of a potential change.

This information enables targeted correction rather than wholesale reversion. Instead of discarding months of work because a foundational decision was flawed, the team can trace the problematic decision to its source, assess its impact on downstream choices, and make a new Decision that supersedes it while preserving unrelated work.

### 6.2 Object Attribution

Every object in Workbench's Authority Lane carries explicit attribution that forms part of the governance record:

* **Claims** are attributed to their proposing participant (agent or human), including the agent's identity, the session in which the claim was made, and the assignment (if any) that motivated it.
* **Authority Decisions** are attributed to their authorizing entity, including whether the authority was a human user, an automated policy, or a delegated agent.
* **Accepted State changes** are attributed to the Decision that produced them, creating a chain from current reality back through governance acts to originating proposals.

This attribution is not merely metadata; it is integral to the governance model. The system can answer not only "what is the current boss combat structure?" but also "who proposed three-phase structure?" and "who authorized it?" and "what was the previous approach?" and "what evidence was cited?"

Object attribution also enables future governance refinements. For example, a policy might specify that claims from certain agents require human review while claims from established agents with strong track records can be auto-accepted. The attribution chain provides the information needed to evaluate and enforce such policies.

### 6.3 Recoverable State Transitions via the Supersedes Mechanism

The Supersedes mechanism is Workbench's primary tool for managing project evolution without historical revisionism. It enables the project to change direction while preserving the complete record of its evolutionary trajectory.

When Decision #52 supersedes Decision #19:

* Decision #19 remains in the historical record, marked as superseded but not deleted.
* The Accepted Project State reflects Decision #52's content, projecting only active decisions.
* The supersession relationship is explicit: Decision #52 records that it supersedes #19, and #19 records that it was superseded by #52.
* The reasoning for the change (captured in #52's supporting Claim and Evidence) is preserved alongside the change itself.

This design supports several recovery scenarios:

**Partial rollback.** A problematic decision can be superseded by a new one that restores the prior approach, with the full alternation history preserved.

**Impact analysis.** By tracking which Accepted State objects depend on which Decisions, the system can identify the blast radius of a potential change before it is made.

**Audit trail.** The complete chain of Claims → Decisions → Accepted State changes provides a full audit trail for any aspect of the project's evolution.

### 6.4 Agent Autonomy and Governance Boundaries

A key design question is how much autonomy agents should have within Workbench's governance model. The answer is governed by a structural distinction: autonomy in execution, governance in acceptance.

**Agents are free to:**

* Propose any Claim, regardless of its quality or correctness. The governance pipeline will evaluate the claim on its merits.
* Execute tasks using whatever tools, methods, and approaches are available within their assigned scope.
* Produce Handoffs with detailed results, recommendations, and supporting evidence.
* Explore alternatives, conduct analysis, and generate creative proposals without governance constraints.

**Agents are *not* free to:**

* Directly modify the Accepted Project State. No agent, regardless of its role, can bypass the Claim → Decision → State pipeline.
* Override existing Authority Decisions. An agent cannot unilaterally reverse a decision that was formally accepted.
* Bypass the governance pipeline by claiming authority they do not possess.

This separation enables a spectrum of automation policies that can be tuned to the project's needs:

**Full manual governance.** Every Claim requires explicit human approval before entering the Accepted State. This is the default for high-stakes decisions (architectural changes, feature scope changes, design philosophy shifts) and for projects in early stages where the governance model is being established.

**Policy-based automation.** Low-risk Claims (routine formatting, minor corrections, well-understood implementation tasks) can be auto-accepted based on predefined rules. High-risk Claims (architectural changes, design direction shifts) still require human review. The policy itself is a governance decision that must be explicitly made and recorded.

**Hybrid governance.** The Leader agent can be authorized to accept certain categories of Claims on behalf of the user, with the user retaining the ability to review and reverse any decision. This model is suitable for projects where the Leader has demonstrated reliability and the user trusts its judgment within defined boundaries.

The governance model is extensible: new authorization policies can be added without modifying the core architecture, because the Authority Lane's structure is invariant across all policies.

---

## 7\. Positioning and Design Philosophy

### 7.1 What Workbench Is Not

Clarity about a system's boundaries is as important as clarity about its capabilities. Workbench explicitly does not aspire to be:

**Not an AI chat application.** Chat belongs to the Session layer (Context Lane). Workbench governs what emerges from conversations; it does not replace them.

**Not a universal agent.** Workbench does not reason, generate code, or execute tasks. It governs outcomes, not execution.

**Not a knowledge base.** Knowledge bases answer "what information exists?" Workbench answers "what has this project decided?" Knowledge provides context; governance defines reality.

**Not a project management tool.** PM tools (Jira, Linear, Notion) track *tasks*: who does what, when. Workbench tracks *decisions*: why the project is the way it is. The two are complementary.

**Not an execution platform.** Code execution is delegated to IDEs, containers, and coding agents. Workbench governs *what* should be built, not *how*.

**Not an automated decision black box.** Workbench does not make decisions. It provides the structure within which decisions are made, recorded, and traced.

### 7.2 Applicability Boundary

Workbench is not universally applicable. Its value is highest under specific conditions and negligible under others.

**Well-suited for:**

* Long-cycle projects (months to years) with accumulated decisions
* Workflows involving multiple AI agents, models, or sessions
* Projects where model migration or agent replacement is expected
* High context-recomprehension cost (complex domains, large codebases)
* Environments requiring auditability or regulatory compliance

**Not suited for:**

* One-shot Q\&A or single-session tasks
* Small scripts, throwaway prototypes, or experiments
* Projects with a single, stable AI participant and short duration
* Domains where decisions are trivial or reversible at near-zero cost

The crossover point—where governance costs are justified by continuity benefits—depends on project duration, participant count, and decision complexity. A rough heuristic: if a project involves more than 10 significant decisions and more than 20 AI sessions, Workbench's governance overhead is likely justified. Below that threshold, simpler approaches (conversation memory, manual notes) may suffice.

### 7.3 Relation to AI Agent Safety

Workbench's scope is deliberately limited with respect to AI agent safety. It does not address alignment, model capability control, or content safety. These are important concerns, but they belong to the agent and model layers, not to the project continuity layer.

What Workbench does provide is a governance substrate that supports *accountability through provenance and recoverability*: the ability to trace any project change to its authorizing decision, its originating proposal, and its supporting evidence, and to reverse or supersede decisions when they prove problematic. This is not safety in the alignment sense, but organizational traceability: the ability to answer *who authorized what, why, and with what consequences*.

The distinction matters. The authority workflow is intended for semantic project changes, not every execution artifact. A bug-fix result requires governance confirmation before entering the accepted state; an intermediate screenshot or log output does not. Whether this boundary is sufficiently clear in practice remains to be explored.

Alignment research asks: "Can we make AI behave correctly?" Workbench asks: "When AI participates in a project, can we trace what happened and recover if something goes wrong?" These are complementary concerns, not competing ones. A well-governed project with misaligned agents is problematic; a well-aligned agent in a project with no governance is also problematic. Workbench addresses the latter.

This positions Workbench within the broader landscape of responsible AI development \[26]. It contributes to the *transparency and auditability* dimensions of responsible AI, without claiming to address the *safety and alignment* dimensions.

### 7.4 Relationship to Existing Theories

Table 2 summarizes Workbench's relationship to established theoretical and practical foundations.

```
+------------------------+--------------------------------------------+-------------------------------------------+
| Theory / Practice      | Core Contribution                          | Workbench's Relationship                  |
+------------------------+--------------------------------------------+-------------------------------------------+
| Event Sourcing         | State derived from immutable events        | Adopted: AuthorityDecision → Accepted     |
| \[16, 17]               |                                            | State mirrors event → projection model    |
+------------------------+--------------------------------------------+-------------------------------------------+
| CQRS \[18]              | Separation of read and write models        | Adopted: Authority Lane (write) and       |
|                        |                                            | Library (read) are explicitly separated   |
+------------------------+--------------------------------------------+-------------------------------------------+
| Git \[14]               | Version control for implementation         | Complementary: Git tracks code changes;   |
|                        | artifacts                                   | Workbench tracks decision evolution       |
+------------------------+--------------------------------------------+-------------------------------------------+
| ADR \[15]               | Documentation of architectural decisions   | Extended: all project decisions, not      |
|                        |                                            | just architectural; integrated with       |
|                        |                                            | governance pipeline                       |
+------------------------+--------------------------------------------+-------------------------------------------+
| Change Management      | Governance over production system changes  | Specialized: Workbench governs project    |
| (ITIL) \[21]            |                                            | meaning, not operational risk; designed   |
|                        |                                            | for AI-intensive workflows                |
+------------------------+--------------------------------------------+-------------------------------------------+
| Knowledge Management   | Information storage and retrieval          | Differentiated: Knowledge provides        |
| \[13]                   |                                            | context; Workbench defines reality        |
+------------------------+--------------------------------------------+-------------------------------------------+
| Domain-Driven Design   | Ubiquitous language, bounded contexts      | Influenced: Project identity and          |
| \[22, 23]               |                                            | responsibility model reflect DDD          |
|                        |                                            | principles                                |
+------------------------+--------------------------------------------+-------------------------------------------+
| Agent Frameworks       | Agent planning and execution capabilities  | Complementary: Agent frameworks enable    |
| \[8-11]                 |                                            | work; Workbench governs outcomes          |
+------------------------+--------------------------------------------+-------------------------------------------+

Table 2: Workbench's relationship to existing theories and practices.
```

Workbench is best understood not as a new contribution to any single field but as an integration of established principles—event sourcing, command-query separation, decision documentation, change management, and domain-driven design—applied to a new problem domain: the governance of long-term projects with AI participants.

The novelty lies not in any individual technique but in the specific combination and application: using event sourcing for project meaning (rather than system state), applying CQRS to decision governance (rather than data access), extending ADR to full project scope (rather than architectural decisions only), and designing the entire system for agent replaceability (rather than agent optimization).

---

## 8\. Implementation Status

### 8.1 Verified Components

The Workbench architecture has been validated through a working implementation that demonstrates the core governance model is not merely conceptual but constitutes a functioning system substrate. The implementation comprises five modules, each independently tested:

|Module|Tests Passed|Failed|Skipped|Description|
|-|-|-|-|-|
|Core|128|0|0|Core domain objects and governance primitives|
|Runtime|63|0|0|Runtime management and session handling|
|Project|28|0|0|Project identity and responsibility structures|
|Storage|307|0|0|Persistence, retrieval, and projection logic|
|App|496|0|0|Application layer, UI integration, workflows|
|**Total**|**1,022**|**0**|**0**||

Table 3: Test coverage across Workbench modules. All tests pass with zero failures.

Additionally, the build system reports zero errors and zero warnings, and diff hygiene checks pass. These results confirm that the proposed architecture is internally consistent and implementable.

### 8.2 Component Status

|Component|Status|Description|
|-|-|-|
|Project Identity|Implemented|Independent project identity, decoupled from agents|
|Authority Model|Implemented|Claim → Decision → State governance pipeline|
|Accepted State|Implemented|Projection-based state derivation from decision history|
|Legacy Boundary|Implemented|Coexistence with pre-Workbench projects without corruption|
|Library Projection|Implemented|Read-only projection of Authority Lane into browsable views|
|Recovery \& Provenance|Implemented|Full traceability from current state to originating claims|
|Product Interaction Layer|Basic slice complete|User-facing project entry and manual workflow experience|
|Agent Participation Layer|Basic validation complete|Basic Agent integration has been validated, but Agents remain participants that submit results and cannot directly modify the project's accepted reality.|
|Accountability Layer|Partial / Future Research|A first version of evidence records can capture sources and hashes; more complex evidence, delegation chains, and impact propagation remain future work.|

Table 4: Component implementation status as of the current development phase.

### 8.3 Current Phase and Next Steps

The current phase focuses on transitioning from architectural validation to product experience. The primary gap is not architectural—the core governance model is verified—but experiential: can a user naturally create a project, understand its state, perform work, see changes preserved, and return to continue later?

The development follows a deliberate sequence:

**Validated baseline.** The continuity kernel, Authority model, manual project workflow, Agent participation adapters, evidence provenance records, restart recovery, and an initial WEIQI³ cross-Agent acceptance path have been implemented and exercised. The current working-tree snapshot reports 1,022 passing tests across five modules.

**Current refinement.** The user-facing Agent Surface, interaction polish, localization coverage, and workflow ergonomics remain under active refinement. Agent participation remains bounded: Agents submit results and Claims but cannot directly modify Accepted Project State.

**Next validation.** Broader provider coverage, reproducible public packaging, cross-Agent handoff demonstrations, continuity/token-efficiency experiments, and sustained real-project use remain to be validated. Multi-user authority delegation, impact propagation, and deeper evidence integrations remain future work.

This ordering is intentional: the project world and governance boundary must remain valid whether work is performed manually or by an Agent. The Alpha candidate demonstrates the baseline; continued work now focuses on making that baseline easy for new users to inspect, operate, and challenge.

### 8.4 Open Research and Community Validation

Workbench is presented as an open-source architecture experiment. The current implementation demonstrates architectural feasibility rather than claiming final completeness. The true value of this system—whether project continuity mechanisms can function effectively in long-term real-world projects—needs to be validated across real-world timescales, participant transitions, and decision accumulations.

By making the design documentation, implementation status, and known limitations public, the project aims to explore:

Whether project continuity mechanisms can become useful infrastructure for long-term AI collaboration;

What level of automation the governance model requires in practice;

How much explicit confirmation users can accept without governance fatigue;

Whether the multi-layer compression framework genuinely reduces long-term context re-comprehension costs.

The current implementation's goal is not to establish an industry standard, but to propose a verifiable architectural hypothesis and obtain long-term feedback through open source.

---

## 9\. Discussion

### 9.1 Governance Cost vs. Usage Efficiency

The most significant tradeoff in Workbench's design is between governance rigor and operational efficiency. Every Claim must pass through the Authority Decision pipeline before entering the Accepted State. This introduces friction: users must review and approve changes, even when the AI's proposal is likely correct.

This friction is intentional but must be managed carefully. If every minor text edit requires explicit governance approval, the system becomes impractical for routine work. Conversely, if governance is too permissive, the integrity of the Accepted State is compromised, and the system degenerates into a conventional memory system with extra overhead.

The proposed mitigation is *policy-based governance*: categorizing changes by risk level and applying appropriate governance intensity. Routine, low-risk changes (formatting, minor corrections, well-understood implementation tasks) can be auto-accepted; significant changes (architectural decisions, feature scope changes, design philosophy shifts) require human review. The categorization itself is a governance decision that must be explicitly made, recorded, and periodically reviewed as the project evolves.

An important design question is who defines the governance policies. In the simplest model, the project creator defines policies at project inception. In more sophisticated models, policies themselves are governed—they can be proposed, debated, and modified through the same Claim → Decision → State pipeline that governs other project changes. This meta-governance ensures that the governance model can evolve with the project.

An open question is whether the governance cost is justified for small, short-lived projects. Workbench's value proposition is strongest for projects that span months or years and involve multiple AI participants. For one-off tasks or short-term projects with a single agent, the governance overhead may exceed the continuity benefits. Identifying the crossover point—where governance costs are justified by continuity benefits—is an important empirical question that future research should address.

### 9.2 Leader Agent Reliability

The Leader agent occupies a critical position in the architecture: it is the primary interface between the user and the project, responsible for task decomposition, Worker selection, and result aggregation. If the Leader makes poor task decompositions or selects inappropriate Workers, the project's efficiency degrades—though the governance model prevents the Leader from corrupting project state.

Current mitigation relies on the governance boundary: the Leader proposes (via Claims), and the user decides (via Authority Decisions). This prevents the Leader from causing irreversible damage but does not prevent it from wasting time, producing low-quality proposals, or making suboptimal routing decisions.

Future research should explore Leader evaluation mechanisms, including self-assessment against project goals, cross-validation by independent agents, and user feedback integration. The governance model provides a foundation for such mechanisms: since all Leader actions produce traceable Claims, the quality of Leader decisions can be retrospectively analyzed to identify patterns of strength and weakness.

### 9.3 Adoption Threshold and User Friction

Workbench introduces concepts (Claims, Decisions, Authority, Projection, Supersedes) that are unfamiliar to most users. The adoption threshold—the effort required to understand and begin using the system—is a practical concern that affects real-world viability.

The design addresses this through progressive disclosure: the default interface shows project state in natural language (“Boss combat system: three-phase structure, accepted 2026-03-15”), not in system terminology (“AuthorityDecision #52, outcome: accept, supersedes: #19”). Users can drill into the technical details when needed but are not required to understand the governance model to use the system effectively.

However, the governance model's value becomes apparent only over time. A user who creates a project and makes a few decisions will not immediately experience the benefits of traceability and recoverability. The value compounds over months of accumulated decisions—which means the system requires sustained use before its benefits become visible. This creates a bootstrapping challenge: users must invest in the system before they can appreciate its returns.

Potential mitigations include: (1) demonstrating value through immediate benefits like structured project organization and clear task tracking, not just long-term continuity; (2) providing templates and guided workflows that reduce the learning curve; (3) enabling incremental adoption, where users can start with simple decision tracking and gradually engage with more sophisticated features; and (4) offering import mechanisms that can bootstrap a project from existing artifacts (Git history, design documents, meeting notes) without requiring users to retroactively create formal decisions for every past choice.

The import mechanism is particularly important for adoption. Most users will not start a new project from scratch with Workbench; they will have existing projects with months or years of history. The ability to import this history—even in a simplified or approximate form—lowers the adoption threshold by allowing users to experience the benefits of governance without the cost of starting from zero.

### 9.4 Scalability Considerations

**Multi-user collaboration.** The current design addresses single-user scenarios. Extending to multi-user environments requires additional design for authority delegation, organizational hierarchies, concurrent decision governance, and conflict resolution. The Actor model provides a foundation (each user is a LogicalActor with defined Responsibilities), but the specifics of multi-user authority policies—Who can propose? Who can decide? How are conflicts resolved?—remain unexplored.

**Large-scale projects.** Projects with thousands of decisions, hundreds of participants, and years of history will stress the compression model and Library projection performance. The multi-layer architecture is designed for scalability (participants read the highest-density sufficient layer, not the full history), but empirical validation at scale is needed. Index design, query optimization, and projection caching become important engineering concerns at scale.

**Cross-project relationships.** Real-world development often involves multiple related projects (e.g., a game and its engine, a product and its documentation, a suite of microservices). Workbench currently treats each project as an independent entity. Extending the model to handle inter-project relationships—shared decisions, dependency tracking, coordinated evolution—is a significant open problem that may require a project-of-projects abstraction.

### 9.5 Limitations and Open Questions

Workbench validates architectural feasibility, not long-term effectiveness. The current evaluation verifies implementation correctness (1,022 tests, zero failures) and architectural consistency, but does not yet establish productivity improvement through controlled user studies. We acknowledge the following honestly.

**Implementation limitations:**

* **No multi-user support.** The current system is single-user. Multi-user authority delegation is designed but not implemented.
* **Basic agent integration is complete.** Basic Agent integration has been validated, but Agents remain participants that submit results and cannot directly modify the project's accepted reality. Complete Leader-Worker integration and multi-provider routing remain future work.
* **Evidence support is at a first-version boundary.** A first version of evidence records can capture sources and hashes; more complex evidence such as code diffs, test reports, and multimedia remains future work.
* **No automated governance.** Policy-based governance is designed but not implemented. All governance requires manual review.
* **Compression quality unvalidated.** The multi-layer compression model is based on architectural reasoning, not empirical measurement.
* **Current domain coverage.** The implementation and scenarios focus on software and game development, while applicability to research, writing, content, and other long-lived projects remains to be demonstrated.
* **No formal verification.** Correctness properties of the governance model (such as the Accepted State always being a valid projection of accepted Decisions) are verified by testing rather than formal proof.
* **Single-project scope.** The current design addresses individual projects. Cross-project governance (shared Decisions and coordinated evolution across related projects) is not addressed.

**Structural failure modes:**

* **Erroneous authority decisions.** The governance model prevents unauthorized changes but not bad decisions. Correction requires detection via the Supersedes mechanism.
* **Governance policy misconfiguration.** Automated policies may miscategorize risk. Calibration requires domain expertise.
* **Compression fidelity loss.** Summaries and Handoffs may subtly distort original decisions. Over multiple cycles, distortions compound.
* **Authority bottleneck.** Single-user designs create a single point of authority. Unavailability stalls the project.

Empirical Validation Limitations:

* No user studies. The practical usability, interaction naturalness, and cognitive load of the governance model have not been validated through user testing.
* No long-term data. Whether the multi-layer compression maintains sufficient semantic fidelity over years of project evolution has only architectural reasoning, not empirical data.
* No real multi-person governance. Multi-user authority delegation, concurrent decision governance, and conflict resolution have not been validated in real collaborative scenarios.
* Token efficiency not quantified. Claims about compression reducing re-comprehension costs are based on architectural reasoning, not empirical measurement. The specific magnitude of benefits remains to be verified.
* Governance fatigue not assessed. How much explicit confirmation users can accept without fatigue has not been verified through usability studies.

These limitations are inherent to the current design stage. They represent the boundary between architectural feasibility and practical effectiveness, to be gradually bridged through community validation and long-term use.

**Open questions for future work:**

* How much governance overhead do users tolerate before the system becomes impractical?
* Does multi-layer compression preserve sufficient semantic fidelity over years of project evolution?
* How should automated authority policies be calibrated across different project types?
* How do multi-agent projects behave over long timeframes with heterogeneous models?
* What is the crossover point where governance costs justify continuity benefits?

---

## 10\. Future Research Directions

### 10.1 From Continuity to Accountability

Workbench's current focus is *continuity*: ensuring the project can survive participant changes. The next research frontier is *accountability through provenance and recoverability*: ensuring that every project change can be fully explained, traced, and recovered.

Continuity asks: "Can the project continue when participants change?" Accountability asks: "Can every change be traced to its origin, its evidence, and its authorization? And if a change proves wrong, can its impact be precisely identified and safely reversed?"

The transition from continuity to accountability requires advances in several areas. Decision provenance must be deepened: every Accepted State change should be traceable not only to its authorizing Decision but to the full chain of Claims, Evidence, and reasoning that informed that Decision. Impact analysis must be developed: when a Decision is revised, the system must identify all downstream objects that are affected. Causal chains must be supported: the system should enable queries like "Why is the boss combat system structured this way?" with answers that traverse the complete provenance chain.

### 10.2 Evidence Integrity

The current implementation includes a first version of evidence records that capture pointers to external artifacts (files, documents, test results) together with hashes. Future work should extend this boundary with richer verifiable evidence references, including Git commit references, CI artifact links, test report archives, and other implementation approaches. Evidence references can carry integrity information to detect whether evidence has been modified since the reference was made. If evidence is updated, a mismatch would alert the system that related decisions may need review. This is not about establishing absolute truth—it is about maintaining the integrity of the evidence chain that supports project decisions.

This direction also faces practical challenges: the long-term maintenance cost of hashes, the complexity of cross-tool integration, and the varying needs for verification granularity across different evidence types. Therefore, adopting the broader framework of "verifiable evidence references" rather than "cryptographic integrity" may be more feasible.

### 10.3 Authority Delegation Chains

Multi-user and multi-agent environments require delegation mechanisms. A Project Owner might delegate combat design authority to a Lead Designer, who might further delegate specific tasks to Worker Agents. The delegation chain must satisfy several constraints: delegation does not expand authority beyond the delegator's own scope; delegated authority can be revoked, and revocation propagates to sub-delegations; and the delegation chain is itself part of the project's governance record.

### 10.4 Impact Propagation Analysis

When a Decision is made or revised, its effects may propagate through the project in ways that are not immediately obvious. A change to the combat model affects enemy balance, skill trees, boss design, UI layout, and testing plans. Future research should develop mechanisms for forward propagation (given a new Decision, identify all downstream objects that may need revision), backward propagation (given a discovered problem, identify all upstream Decisions that may have contributed), and propagation policies (rules for how impact notifications are generated and who receives them).

### 10.5 Binding the Semantic World to the Implementation World

Workbench manages the *semantic* layer (decisions, responsibilities, accepted state). Git manages the *implementation* layer (code changes, file history). Future work should establish formal bindings between these layers: a Decision in Workbench should be linkable to the Git commits that implemented it, and a Git commit should be traceable back to the Decision that motivated it. This integration enables queries that span both layers: "Why was `EnemyAI.cs` changed?" traces to Decision #52; "Is Decision #52 fully implemented?" checks for corresponding Git commits.

### 10.6 Governed Compression

Long-term projects require information compression, but compression risks information loss or distortion. Future research should develop *governed compression* mechanisms that ensure: every compressed artifact references its source Decisions and Claims; compressed artifacts explicitly mark which content derives from Authority Decisions and which is contextual inference; and the system supports verification that a compressed artifact faithfully represents its sources. This is the difference between a Summary that states "the boss uses three phases" and one that states "per Decision #52, the boss uses three phases (superseding Decision #19's health-scaling approach)." The latter preserves governance; the former obscures it.

Governed compression also raises questions about compression policy. Who decides when a Session should be compressed into a Handoff? Who validates that the Handoff faithfully represents the Session's outcomes? In a multi-agent environment, should compression be performed by the same agent that produced the original content, or by an independent agent? These questions connect governance to the broader accountability agenda: compression is itself a governance act that shapes what future participants will know about the project's history.

### 10.7 Multi-Modal Evidence and Verification

Current Workbench implementations focus on text-based evidence: design documents, code comments, test reports, and discussion transcripts. Future work should extend evidence support to multi-modal formats: images (UI mockups, architecture diagrams), video (playtesting recordings, demo walkthroughs), audio (meeting recordings, voice notes), and structured data (analytics dashboards, performance benchmarks).

Multi-modal evidence introduces new challenges for integrity verification. Text evidence can be hashed; image and video evidence require more sophisticated verification mechanisms. The provenance chain must accommodate evidence that is itself the product of complex tool chains (e.g., a UI mockup produced by a design tool, exported as an image, and cited as evidence for a layout decision).

### 10.8 Cross-Project Governance

Real-world organizations often manage portfolios of related projects. A game studio might develop multiple games sharing an engine; a software company might maintain a product suite with shared components; a research group might conduct related studies building on common foundations.

Cross-project governance extends the project continuity problem to the inter-project level. Decisions in one project may affect others; shared components require coordinated governance; and organizational-level policies may constrain individual project decisions. Workbench's current single-project model does not address these concerns, but the governance primitives (Claim, Decision, Authority) provide a foundation for extension.

---

## 11\. Conclusion

This paper presents Workbench, an architectural proposal for a project continuity layer for long-term AI collaboration. The core question: when AI participants change over time, the project itself must maintain coherent existence.

Workbench designs three interlocking mechanisms. A three-lane information architecture separates Authority, Context, and Execution lanes, designed to prevent AI outputs from automatically becoming project truth. A Claim–Decision–State model governs which proposed changes enter the Accepted Project State. A multi-layer compression framework reduces re-comprehension costs while preserving traceability.

Workbench does not compete with agent frameworks, knowledge bases, or version control. It occupies a complementary layer, governing project meaning and decision provenance. The implementation verifies architectural feasibility, but long-term effectiveness awaits real-world validation. Limitations have been candidly acknowledged: the system is currently single-user, basic Agent integration has been validated but remains non-authoritative, and the compression model awaits empirical verification.

Workbench is presented as an open-source architecture experiment, and its true value awaits validation through community engagement and long-term practice. The research agenda extends from continuity toward accountability through provenance and recoverability—from ensuring that projects survive participant change, to ensuring that every change can be fully explained, traced, and recovered. Future directions include multi-person collaboration, deeper traceability, development tool integration, and community-driven continuous validation.

Agents will change. Tools will change. Participants will change. But the project itself should have the capacity to continue existing—and that is the direction we need to explore and build together.

---

## References

\[1] M. Chen et al., "Evaluating Large Language Models Trained on Code," *arXiv preprint arXiv:2107.03374*, 2021.

\[2] J. Austin et al., "Program Synthesis with Large Language Models," *arXiv preprint arXiv:2108.07732*, 2021.

\[3] OpenAI, "GPT-4 Technical Report," *arXiv preprint arXiv:2303.08774*, 2023.

\[4] Anthropic, "The Claude Model Family," Technical Report, 2024.

\[5] S. Madaan et al., "Self-Refine: Iterative Refinement with Self-Feedback," *Advances in Neural Information Processing Systems*, vol. 36, 2023.

\[6] T. B. Brown et al., "Language Models are Few-Shot Learners," *Advances in Neural Information Processing Systems*, vol. 33, pp. 1877–1901, 2020.

\[7] A. Paranjape et al., "ART: Automatic multi-step reasoning and tool-use for large language models," *arXiv preprint arXiv:2303.09014*, 2023.

\[8] T. Richards, "AutoGPT: An Autonomous GPT-4 Experiment," GitHub Repository, 2023.

\[9] S. Yao et al., "ReAct: Synergizing Reasoning and Acting in Language Models," *International Conference on Learning Representations (ICLR)*, 2023.

\[10] Q. Wu et al., "AutoGen: Enabling Next-Gen LLM Applications via Multi-Agent Conversation," *arXiv preprint arXiv:2308.08155*, 2023.

\[11] H. Chase, "LangChain: Building applications with LLMs through composability," GitHub Repository, 2022.

\[12] P. Lewis et al., "Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks," *Advances in Neural Information Processing Systems*, vol. 33, pp. 9459–9474, 2020.

\[13] G. von Krogh, "Knowledge Management in the Stock Market: An Empirical Study of the Relationship between Knowledge Management and Stock Returns," *Journal of Knowledge Management*, vol. 13, no. 2, pp. 1–15, 2009.

\[14] L. Torvalds and J. Hamano, "Git: Fast Version Control System," 2005. \[Online]. Available: https://git-scm.com

\[15] M. Nygard, "Documenting Architecture Decisions," 2011. \[Online]. Available: https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions

\[16] G. Young, "Event Sourcing," in *Patterns, Principles, and Practices of Domain-Driven Design*, Wrox Press, 2015, ch. 14.

\[17] R. Richardson, "Pattern: Event Sourcing," *microservices.io*, 2018. \[Online]. Available: https://microservices.io/patterns/data/event-sourcing.html

\[18] G. Young, "CQRS," in *Patterns, Principles, and Practices of Domain-Driven Design*, Wrox Press, 2015, ch. 15.

\[19] Y. Gao et al., "Retrieval-Augmented Generation for Large Language Models: A Survey," *arXiv preprint arXiv:2312.10997*, 2023.

\[20] W. Zhong et al., "MemoryBank: Enhancing Large Language Models with Long-Term Memory," *Proceedings of the AAAI Conference on Artificial Intelligence*, vol. 38, no. 17, pp. 19724–19731, 2024.

\[21] ITIL Foundation, *ITIL 4 Foundation: ITIL 4 Edition*, AXELOS, 2019.

\[22] E. Evans, *Domain-Driven Design: Tackling Complexity in the Heart of Software*, Addison-Wesley, 2003.

\[23] V. Vernon, *Implementing Domain-Driven Design*, Addison-Wesley, 2013.

\[24] M. Fowler, *Patterns of Enterprise Application Architecture*, Addison-Wesley, 2002.

\[25] C. Rich and R. C. Waters, "The Programmer's Apprentice: A Research Overview," *IEEE Computer*, vol. 21, no. 11, pp. 10–25, 1988.

\[26] D. A. Norman, *The Design of Everyday Things*, Revised and Expanded Edition, Basic Books, 2013.

\[27] A. Cockburn, *Writing Effective Use Cases*, Addison-Wesley, 2001.

\[28] I. Jacobson, G. Booch, and J. Rumbaugh, *The Unified Software Development Process*, Addison-Wesley, 1999.

\[29] R. C. Martin, *Clean Architecture: A Craftsman's Guide to Software Structure and Design*, Prentice Hall, 2017.

\[30] G. Hohpe and B. Woolf, *Enterprise Integration Patterns: Designing, Building, and Deploying Messaging Solutions*, Addison-Wesley, 2003.

---

## Appendix A: Implementation Mapping

The following table maps the abstract concepts defined in this paper to their concrete implementation forms in the current Workbench system.

|Concept|Implementation Form|Storage Model|
|-|-|-|
|Project Identity|Project aggregate root|SQLite record|
|Authority Decision|Immutable decision event|Append-only table|
|Accepted Project State|Projection service|Derived from decision log|
|Claim|Claim repository|Versioned artifact store|
|Handoff|Context artifact|Structured JSON document|
|Summary|Generated context view|Read model (recomputable)|
|Library|Read-only projection|Materialized view|
|Assignment|Responsibility record|Relational table|
|LogicalActor|Actor aggregate|SQLite record|
|Supersedes link|Decision relationship|Foreign key reference|

This mapping is provided for implementational reference. The paper's arguments are independent of any specific storage technology; the same architecture could be implemented on PostgreSQL, Datomic, event stores, or file-based systems.
