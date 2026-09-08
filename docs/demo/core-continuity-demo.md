# Core Continuity Demo

## Repeatability Contract

The demo has a frozen baseline and a disposable run directory.

- `prepare-core-continuity-demo-baseline.ps1` captures the current `零刻` Project World and project files once.
- `run-core-continuity-demo.ps1` creates a fresh run by copying that baseline.
- Leader replacement inside one run uses the run database and keeps all new Candidates, Proposals, Handoffs, Summaries, and Library changes.
- Starting another fresh run returns to the frozen baseline. The demo never mutates the real project database.
- Use `-Resume` only when you intentionally want to reopen the same run and keep its state.

This is the intended meaning of “reopen”: reopen through the Demo launcher for a fresh frozen run. Ordinary Workbench startup keeps its normal database and is not silently reset.

This gives two useful behaviors without ambiguity: a clean replay for every presentation, and an optional resume path for debugging or recovery checks.

## Prepare Once

Exit Workbench, then capture the baseline:

```powershell
& .\tools\prepare-core-continuity-demo-baseline.ps1
```

The baseline is stored under `artifacts\demo-baseline`. It is generated from the current frozen state, not hand-written seed rows.

## Start A Fresh Run

```powershell
& .\tools\run-core-continuity-demo.ps1
```

The script builds the current app, creates `artifacts\demo-runs\run-*`, copies the baseline, and opens the copied `零刻` project automatically.

## Demo Sequence

1. **Recovered project** — ask the Leader for current formal rules, Library knowledge, recent Worker work, and recent project changes.
2. **Authority change** — discuss one explicit new formal rule and let the Leader produce an Evolution Candidate. Open Governance Preview and accept it through the normal Authority flow.
3. **Library change** — discuss one recurring story or cultural detail that is not a world rule. Generate a Library Proposal, select it in the Library pane, and click `接受`.
4. **Worker work** — ask for a small bounded artifact. Show the Artifact, FinalReport, Handoff, and durable Summary.
5. **Evolution view** — show the distinction between AcceptedProjectState, Library projection, Worker/Handoff history, Summary, and pending governance.
6. **Leader replacement** — use `换脑` inside the same run and ask the same recovery questions. The newly selected Leader must answer from durable Project World context, not the old transcript.

## Suggested Prompts

Authority candidate:

> 我确定一条正式规则：因果编号只负责标识和追踪同一次时间旅行产生的因果链，不参与风险评分，也不能阻止任何人的时间旅行。请正常回答，并在确实需要治理时提出 Candidate，不要直接修改正式状态。

Library candidate:

> 下一章固定加入一种“时间旅行者黑话”：这是时间旅行者群体长期使用的文化细节，后续章节会反复出现，但它不是世界规则，也不改变时间旅行的物理机制。

Worker task:

> 根据当前已接受的因果编号设定，写一个约 500 字的小场景草稿，并返回 Artifact、FinalReport 和交接说明。

## What To Show

- Candidate: object, before, after, impact, route, source;
- Authority Preview before acceptance;
- Library Proposal status changing from `Pending` to `Accepted`;
- Library Object / Timeline Node / material reference;
- Worker Artifact, FinalReport, Handoff;
- durable Summary and its source refs;
- Evolution Index separating Truth, Library, Work, Summary, and pending items;
- new Leader recovery after `换脑`.

## What Not To Claim

Do not use this demo to claim ideal semantic granularity, token efficiency, provider parity, benchmark performance, or complete product maturity. The demo proves continuity and governance behavior.

## Stop Conditions

Stop the run if any of these occur:

- a Candidate changes AcceptedProjectState before user acceptance;
- a Library Proposal is accepted in the UI but remains `Pending` durably;
- a Library change appears in Truth;
- a Worker result has no FinalReport, Handoff, or durable Summary;
- a Leader replacement cannot recover the project change from durable context;
- the run touches the real Workbench database or the real `零刻` project folder.
