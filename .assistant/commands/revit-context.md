---
description: Summarize current live Revit Bridge context from the latest exported context file
---

# Revit Context Summary

You are summarizing the latest live Revit context for the 765T Revit Bridge project.

## Instructions

1. Read:
   - `CLAUDE.md` — repo-specific critical notes and latest working guidance
   - `AGENTS.md` — constitution and safety boundaries
   - `ASSISTANT.md` — adapter/runtime truth for this assistant lane
   - `.assistant/context/revit-task-context.latest.json` if it exists
   - `docs/ARCHITECTURE.md`
   - `docs/BIM765T.Revit.Agent-Architecture.md`
   - `docs/BIM765T.Revit.Snapshot-Strategy.md`

2. Summarize:
   - active document
   - active view
   - level / selection state
   - recent operations / recent events
   - current risks and missing context

3. If the context file does not exist:
   - say so clearly
   - instruct the user to run `tools/get_revit_task_context.ps1`

## Output format

```markdown
## Revit Context

- **Document:** ...
- **View:** ...
- **Level:** ...
- **Selection:** ...
- **Recent Operations:** ...
- **Recent Events:** ...

## Risks
- ...

## Suggested Next Actions
1. ...
2. ...
```
