---
description: Review a Revit task or current bridge context with 765T safety rules
allowed-arguments: ["task"]
argument-description: "Task or review goal"
---

# Revit Review

You are reviewing a Revit task in the 765T Revit Bridge project.

## Instructions

1. Read:
   - `CLAUDE.md` — repo-specific critical notes and latest working guidance
   - `AGENTS.md` — constitution and safety boundaries
   - `ASSISTANT.md` — adapter/runtime truth for this assistant lane
   - `.assistant/context/revit-task-context.latest.json` if available
   - `docs/ARCHITECTURE.md`
   - `docs/PATTERNS.md`
   - `docs/BIM765T.Revit.Snapshot-Strategy.md`

2. Review the user's task:
   - scope
   - likely bridge tools
   - model safety concerns
   - required validation steps
   - whether a snapshot should be captured first

3. If the task implies model changes, explicitly enforce:
   - dry-run
   - approval
   - execute
   - validate
   - diff / journal

Task:
{{task}}

## Output format

```markdown
## Review Summary
- ...

## Recommended Tool Plan
1. tool ...
2. tool ...

## Risks
- ...

## Validation
- ...
```
