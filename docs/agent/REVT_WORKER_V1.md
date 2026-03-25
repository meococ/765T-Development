# 765T Revit Worker v1

## Product intent
`765T Revit Worker` is the product shell that sits on top of the existing bridge/tool platform.

It is not a separate chat app.
It is a mission-oriented worker surface inside Revit that follows one safe flow:

`Ask -> Understand -> Plan -> Preview -> Approve -> Run -> Verify -> Report`

## Front door
- The first visible tab in the docked pane is now **Worker**.
- `WorkerTab` is the front door for internal pilot users.
- Power-user tabs such as Workflows, Evidence, Activity, and Inspector stay available as secondary views.

## Public orchestration surface
UI and MCP now share the same small public worker surface:
- `worker.message`
- `worker.get_session`
- `worker.list_sessions`
- `worker.end_session`
- `worker.set_persona`
- `worker.list_personas`
- `worker.get_context`

Rule: the Revit UI must go through the same orchestration path as MCP. No second private execution path for the Worker shell.

## Worker shell v1
The v1 Worker shell is intentionally simple and operational:
- header with persona, session, mission, document, active view
- context strip
- conversation area
- pending approval card
- tool result cards
- quick suggestion chips
- input box + send

The UI renders `WorkerResponse` objects. It does not own mutation logic and does not bypass the approval kernel.

## Mission spine
Every meaningful worker interaction must map to a mission.

Mission states:
- `Idle`
- `Understanding`
- `Planned`
- `AwaitingApproval`
- `Running`
- `Verifying`
- `Completed`
- `Blocked`
- `Failed`

This keeps the user experience auditable for both developers and BIM managers.

## Rule-first brain v1
The first Worker brain is intentionally rule-first.

Supported intents:
- greeting
- help
- context query
- QC request
- sheet analysis request
- family analysis request
- mutation request
- approval
- reject
- cancel
- resume

Routing defaults:
- model/QC -> `review.model_health`, `review.model_warnings`, `review.smart_qc`
- sheet -> `sheet.capture_intelligence`, `data.extract_schedule_structured`
- family -> `family.xray`
- context -> `session.get_task_context`, `context.get_delta_summary`
- similar historical hints -> episodic memory retrieval

Mutation rule:
- v1 never mutates directly from natural language
- preview first
- wait for approval
- execute only through the existing approval token kernel

## Memory v1
Memory v1 stays narrow on purpose.

### Session memory
- in RAM
- per worker session
- capped with LRU eviction
- stores structured entries like user message, worker response, tool result, approval decision

### Episodic memory
- durable JSON files under `%APPDATA%\BIM765T.Revit.Agent\state\worker\episodes\`
- one record per completed/blocked/failed mission worth remembering
- used for similar-run hints and operator continuity

### Out of scope for v1
- semantic memory
- embeddings
- ONNX
- vector databases
- self-learning autopromotion

## Personas
Built-in personas are lightweight configuration, not separate kernels:
- `revit_worker`
- `qa_reviewer`
- `helper`

A persona may change tone, greeting, expertise labels, and wording of guardrails.
A persona may not change safety policy or bypass approvals.

## LLM policy
LLM is not part of Worker v1 runtime.

When the team opens the next phase:
- `Copilot.Core` will only hold interfaces such as `ILlmClient`
- any vendor SDK must live in a sidecar `.NET 8` host or separate provider project
- no Anthropic/OpenAI/ONNX dependency is allowed in the Revit add-in or `Copilot.Core` for v1

## Acceptance for internal pilot
A pilot build is good enough when:
1. a BIM user can open Revit and use the Worker tab first
2. the worker shows mission, context, approvals, and results clearly
3. read-only requests work through `worker.message`
4. mutation requests stop at preview + approval
5. restarting Revit still preserves episodic run memory
6. MCP callers see the same orchestration behavior as the UI
