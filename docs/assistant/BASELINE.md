# Current Baseline

## Product Shape

- Chat-first worker shell in Revit — user ra lệnh, AI thực thi
- Command atlas + workflow compose + delivery loops
- WorkerHost = public control plane + AI broker orchestration

## Runtime Ownership

- `BIM765T.Revit.Agent` = execution kernel, duy nhất chạm Revit API, hiện vẫn host `worker.*` session/reasoning trong migration
- `BIM765T.Revit.WorkerHost` = control plane, mission orchestration, external AI gateway
- `BIM765T.Revit.Copilot.Core` = shared packs/playbooks/context/runtime services across stack

## UI Truth

- Single worker shell với một assistant surface
- Chat / Commands / Evidence / Activity = surface concepts trong cards, rail, inspector
- Không claim 4 tabs riêng trừ khi tabbed shell quay lại

## Quality Flow — Mutation UX

- Read-only / harmless → quick-path trực tiếp, không bottleneck
- Deterministic mutation → preview hoặc light confirm (theo manifest/policy)
- High-impact mutation → preview → approval → execute → verify (UX cho user tin tưởng)

## Memory Stack

- WorkerHost durable truth = SQLite + lexical projection
- Agent runtime = JSON task/queue/episodic state (migration window)
- Qdrant = vector layer, hash embeddings hiện tại (chưa semantic thật)
- Health phân biệt: `vector_available_non_semantic` vs `lexical_only`

## External AI Truth

- External AI qua WorkerHost HTTP gateway
- Provider env vars: OpenRouter / MiniMax / OpenAI / Anthropic (first found wins)
- MiniMax default = `MiniMax-M2.7`, multi-key pin bằng `BIM765T_LLM_PROVIDER=MINIMAX`
- Embedded worker lane trong Agent vẫn active — chưa fully decoupled
- Provider config local không phải highest-priority repo truth
