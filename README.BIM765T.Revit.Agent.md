# 765T Assistant Platform ? Quick Reference

## Core boundary
- `Revit.Agent` = private execution kernel
- `WorkerHost` = public local control plane + external AI broker
- `Copilot.Core` = packs / playbooks / routing / context / memory orchestration
- `Contracts` = append-only DTOs + proto contracts

## UI truth
Dockable pane hi?n t?i l? **chat-first shell** v?i 4 surfaces:
- Chat
- Commands
- Evidence
- Activity

## Safety truth
- quick path cho read-only ho?c deterministic low-risk actions
- mutation lu?n ph?i gi? `preview -> approval -> execute -> verify`

## Memory truth
- SQLite lexical/event truth b?t bu?c
- Qdrant semantic namespaces l? optional accelerator

## Runtime paths

- shipping runtime workspace root: `%APPDATA%\BIM765T.Revit.Agent\workspaces`

- `workspaces/default/workspace.json` = repo seed/dev fixture, khong phai user runtime root

- manual smoke doc + helper: `docs/assistant/MVP_SMOKE_CHECKLIST.md`, `tools/run_revit_mvp_manual_smoke.ps1`

## Canonical docs
- `AGENTS.md`
- `ASSISTANT.md`
- `docs/ARCHITECTURE.md`
- `docs/PATTERNS.md`
- `docs/assistant/*`
