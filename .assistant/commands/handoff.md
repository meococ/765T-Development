---
description: Create handoff document for conversation transition
argument-hint: [optional summary or notes]
---

# Handoff Command

Create a handoff document when the conversation becomes too long or context
becomes stale. This allows seamless continuation in a new conversation.

---

## When to Use

- Conversation is very long with accumulated irrelevant context
- Switching focus to a different phase of work
- Need fresh context window for complex next steps
- User requests conversation transition

---

## Handoff Document Structure

Create file at: `docs/handoff/handoff-{timestamp}.md`

### Template

```markdown
# Handoff Document

**Created:** {YYYY-MM-DD HH:MM}
**Topic:** {Main topic/task being worked on}
**Status:** {In Progress / Paused / Blocked / Ready for Review}

---

## Context Summary

{Brief description of what was being worked on}

---

## Completed Work

{List of completed tasks/changes}

- [x] Task 1
- [x] Task 2
- [x] Task 3

---

## Pending Work

{List of remaining tasks}

- [ ] Task 1
- [ ] Task 2
- [ ] Task 3

---

## Key Decisions Made

{Important decisions and their rationale}

| Decision | Rationale | Alternatives Considered |
|----------|-----------|------------------------|
| {Decision 1} | {Why} | {Other options} |

---

## Current State

### Files Modified
{List of files that were changed}

### Files Created
{List of new files}

### Dependencies/Blockers
{Any blockers or dependencies}

---

## Next Steps

{Prioritized list of what to do next}

1. {Step 1}
2. {Step 2}
3. {Step 3}

---

## Important Notes

{Any critical information the next session needs to know}

---

## Resources

{Links, documentation, or references used}
```

---

## Process

1. **Gather Context**
   - Review conversation history
   - Check current todo list
   - Note any active plans

2. **Create Handoff Directory** (if needed)
   ```bash
   mkdir -p docs/handoff
   ```

3. **Generate Timestamp**
   - Format: `YYYYMMDD-HHMM`
   - Example: `20260121-1430`

4. **Write Handoff Document**
   - Fill template with relevant information
   - Be concise but complete
   - Focus on actionable next steps

5. **Confirm with User**
   - Show handoff location
   - Summarize key points
   - Provide instructions for continuing

---

## Output

After creating handoff:

```markdown
## Handoff Created

**File:** `docs/handoff/handoff-{timestamp}.md`

### Summary
- **Topic:** {topic}
- **Status:** {status}
- **Completed:** {count} tasks
- **Pending:** {count} tasks

### To Continue
In a new conversation, use:
```
/continue
```
Or specify the file:
```
/continue handoff-{timestamp}.md
```

Handoff document saved. Ready to start fresh conversation.
```
