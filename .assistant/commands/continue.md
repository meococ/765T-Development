---
description: Resume work from a handoff document
argument-hint: [optional handoff filename]
---

# Continue Command

Resume work from a previous conversation's handoff document.
This command reads the most recent (or specified) handoff and
establishes context to continue the work.

---

## Workflow

### Step 1: Find Handoff Document

**If argument provided:**
```
Read: docs/handoff/{argument}
```

**If no argument:**
```
List: docs/handoff/
Select: Most recent handoff-*.md file (by timestamp in filename)
```

### Step 2: Read and Parse Handoff

Extract from handoff document:
- Topic/Task
- Status
- Completed work
- Pending work
- Key decisions
- Current state (files modified/created)
- Next steps
- Blockers
- Important notes

### Step 3: Verify Current State

Check if files mentioned in handoff still exist and match expected state:
```
Read key files mentioned in handoff
Compare with documented state
Note any discrepancies
```

### Step 4: Present Status to User

Output format:
```markdown
## Resuming from Handoff

**Document:** {filename}
**Created:** {timestamp}
**Topic:** {topic}
**Status:** {status}

---

### Summary

{Brief context summary}

### Completed ({count})
- [x] {task 1}
- [x] {task 2}

### Pending ({count})
- [ ] {task 1}
- [ ] {task 2}

### Next Steps (Priority Order)
1. {step 1}
2. {step 2}
3. {step 3}

### Blockers/Notes
{Any blockers or important notes}

---

**Ready to continue. What would you like to focus on?**
```

### Step 5: Wait for User Direction

After presenting status, wait for user to:
- Confirm next step to work on
- Adjust priorities
- Provide additional context
- Ask questions about previous work

---

## Handling Multiple Handoffs

If multiple handoff files exist:

```markdown
## Available Handoffs

| # | File | Date | Topic | Status |
|---|------|------|-------|--------|
| 1 | handoff-20260120-1430.md | 2026-01-20 14:30 | Feature X | In Progress |
| 2 | handoff-20260119-0900.md | 2026-01-19 09:00 | Bug Fix Y | Completed |

Which handoff would you like to continue from?
(Default: most recent)
```

---

## Handling Missing Handoff

If no handoff documents found:

```markdown
No handoff documents found in docs/handoff/

To create a handoff document, use:
/handoff [optional notes]

Or describe what you'd like to work on and I'll help you get started.
```

---

## State Verification

### File Checks

For each file in handoff's "Files Modified" section:
- Check if file exists
- Note if file has been modified since handoff (git status)

### Discrepancy Handling

If state has changed since handoff:
```markdown
**State Changes Detected**

The following changes occurred since the handoff:

| File | Expected | Current |
|------|----------|---------|
| {file} | {expected state} | {current state} |

Would you like to:
1. Continue with current state (acknowledge changes)
2. Review changes before continuing
3. Start fresh
```

---

## Best Practices

1. **Read Carefully** - Parse the entire handoff before presenting
2. **Verify State** - Check file existence and recent changes
3. **Prioritize** - Present next steps in order of importance
4. **Note Blockers** - Highlight any blockers immediately
5. **Be Concise** - Summarize, don't dump the entire handoff

---

## Integration with Todo

After resuming, optionally populate TodoWrite with pending tasks:

```
TodoWrite([
  { content: "Task 1 from handoff", status: "pending" },
  { content: "Task 2 from handoff", status: "pending" },
  ...
])
```
