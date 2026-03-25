---
description: Check current project status and context
---

# Project Status Check

You are checking the current project status.

## Instructions

1. **Check Project Context**
   - Review any active plans in `docs/handoff/`
   - Check current todo items
   - Review recent git changes

2. **Gather Information**
   ```bash
   # Check git status
   git status --short

   # Check recent commits
   git log --oneline -5

   # List handoff documents
   ls -la docs/handoff/ 2>/dev/null || echo "No handoff directory"
   ```

3. **Summarize Status**
   - What's been done recently
   - What's currently in progress
   - What's pending or blocked

## Output Format

```markdown
## Project Status

### Current Branch
`{branch-name}`

### Recent Activity
{Last few commits or changes}

### Git Status
{Modified/staged files if any}

### Active Handoffs
{List any handoff documents, or "None"}

### Todo Items
{Current todo list if any}

### Summary
- **Completed:** {recent completed work}
- **In Progress:** {current work}
- **Pending:** {upcoming work}

### Notes
{Any important observations or blockers}
```

## Quick Status

For a quick status without full details:

```markdown
## Quick Status

**Branch:** {branch}
**Modified Files:** {count}
**Last Commit:** {commit message}
**Active Handoff:** {yes/no}

Ready to continue working.
```
