#!/bin/bash
# BIM765T — Architecture guard: layer boundaries + contract rules
set -e

INPUT=$(cat)
FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')

if [[ ! "$FILE_PATH" =~ \.cs$ ]]; then exit 0; fi

VIOLATIONS=""

for PROJECT in Contracts WorkerHost McpHost Copilot; do
  if echo "$FILE_PATH" | grep -qi "$PROJECT"; then
    if grep -qE 'using Autodesk\.Revit|RevitAPI' "$FILE_PATH" 2>/dev/null; then
      VIOLATIONS="${VIOLATIONS}| VIOLATION: ${PROJECT} must NOT reference RevitAPI.dll"
    fi
  fi
done

if grep -q '\[DataMember' "$FILE_PATH" 2>/dev/null; then
  ORDERS=$(grep -oP 'Order\s*=\s*\K\d+' "$FILE_PATH" 2>/dev/null | sort -n)
  if [ -n "$ORDERS" ]; then
    PREV=0
    while IFS= read -r order; do
      if [ "$order" -lt "$PREV" ]; then
        VIOLATIONS="${VIOLATIONS}| DataMember Order non-sequential - verify append-only rule"
        break
      fi
      PREV=$order
    done <<< "$ORDERS"
  fi
fi

if [ -n "$VIOLATIONS" ]; then
  echo "{\"systemMessage\": \"Architecture Guard: ${VIOLATIONS}\"}"
fi
exit 0
