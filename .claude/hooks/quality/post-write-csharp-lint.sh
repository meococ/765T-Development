#!/bin/bash
# ──────────────────────────────────────────────────────────
# BIM765T — Post-write C# lint check
# Validates DataContract, naming, anti-patterns after file writes
# ──────────────────────────────────────────────────────────
set -e

INPUT=$(cat)
FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')

# Only check C# files
if [[ ! "$FILE_PATH" =~ \.(cs|csproj)$ ]]; then
  exit 0
fi

ISSUES=""

if [[ "$FILE_PATH" =~ \.cs$ ]] && [ -f "$FILE_PATH" ]; then

  # 1. Newtonsoft.Json (should use DataContractJsonSerializer)
  if grep -q "Newtonsoft" "$FILE_PATH" 2>/dev/null; then
    ISSUES="${ISSUES}| Newtonsoft.Json detected - use DataContractJsonSerializer per ARCHITECTURE.md"
  fi

  # 2. .Result (potential deadlock)
  if grep -qE '\.Result[^s]|\.Result$' "$FILE_PATH" 2>/dev/null; then
    ISSUES="${ISSUES}| .Result detected - potential UI thread deadlock, use await instead"
  fi

  # 3. Direct Revit API in ViewModel
  if echo "$FILE_PATH" | grep -qiE 'viewmodel|vm\.cs'; then
    if grep -qE 'FilteredElementCollector|Transaction|Document\.' "$FILE_PATH" 2>/dev/null; then
      ISSUES="${ISSUES}| Revit API detected in ViewModel - use InternalToolClient bridge"
    fi
  fi
fi

if [ -n "$ISSUES" ]; then
  echo "{\"systemMessage\": \"Code Review: ${ISSUES}\"}"
fi

exit 0
