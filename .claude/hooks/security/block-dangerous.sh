#!/bin/bash
# ──────────────────────────────────────────────────────────
# BIM765T — Security: block dangerous commands
# ──────────────────────────────────────────────────────────
set -e

INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.tool_input.command // empty')

BLOCKED=false
REASON=""

if echo "$COMMAND" | grep -qE 'git\s+push\s+.*--force|git\s+push\s+-f'; then
  BLOCKED=true; REASON="Force push blocked - never rewrite shared branch history"
fi

if echo "$COMMAND" | grep -qE 'git\s+reset\s+--hard'; then
  BLOCKED=true; REASON="Hard reset blocked - use git stash or backup branch first"
fi

if echo "$COMMAND" | grep -qE 'rm\s+-rf\s+/|rm\s+-rf\s+~'; then
  BLOCKED=true; REASON="Recursive delete at root blocked"
fi

if echo "$COMMAND" | grep -qiE 'cat\s+.*\.(env|key|pem|credentials)'; then
  BLOCKED=true; REASON="Reading credential files blocked"
fi

if echo "$COMMAND" | grep -qE '--no-verify'; then
  BLOCKED=true; REASON="Skipping git hooks blocked - hooks enforce quality gates"
fi

if [ "$BLOCKED" = true ]; then
  jq -n --arg reason "$REASON" '{
    "hookSpecificOutput": {
      "hookEventName": "PreToolUse",
      "permissionDecision": "deny",
      "permissionDecisionReason": $reason
    }
  }'
fi

exit 0
