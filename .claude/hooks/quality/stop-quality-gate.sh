#!/bin/bash
# ──────────────────────────────────────────────────────────
# BIM765T — Stop Quality Gate
# Runs when Claude stops: checks modified .cs files for
# build errors, architecture violations, and anti-patterns.
# Replaces agent hook (agent hooks lack Bash permission).
# ──────────────────────────────────────────────────────────

# Read stdin (Stop hook sends session context JSON)
INPUT=$(cat)

# Find .cs files modified in working tree (staged + unstaged)
CS_FILES=$(git diff --name-only HEAD 2>/dev/null | grep '\.cs$' || true)
CS_FILES_STAGED=$(git diff --cached --name-only 2>/dev/null | grep '\.cs$' || true)
ALL_CS=$(printf "%s\n%s" "$CS_FILES" "$CS_FILES_STAGED" | sort -u | grep -v '^$' || true)

# No .cs changes → nothing to check
if [ -z "$ALL_CS" ]; then
  exit 0
fi

ISSUES=""
FILE_COUNT=$(echo "$ALL_CS" | wc -l | tr -d ' ')

# ── 1. Architecture violations: wrong layer referencing RevitAPI ──
while IFS= read -r f; do
  [ -z "$f" ] && continue
  [ ! -f "$f" ] && continue

  for PROJECT in Contracts WorkerHost McpHost Copilot; do
    if echo "$f" | grep -qi "$PROJECT"; then
      if grep -qE 'using Autodesk\.Revit|RevitAPI' "$f" 2>/dev/null; then
        ISSUES="${ISSUES}\n⚠ ARCH: ${f} — ${PROJECT} must NOT reference RevitAPI"
      fi
    fi
  done
done <<< "$ALL_CS"

# ── 2. .Result deadlock pattern ──
while IFS= read -r f; do
  [ -z "$f" ] && continue
  [ ! -f "$f" ] && continue

  if grep -nE '\.Result[^s]|\.Result$' "$f" 2>/dev/null | head -3 | grep -q '.'; then
    LINES=$(grep -nE '\.Result[^s]|\.Result$' "$f" 2>/dev/null | head -3 | cut -d: -f1 | tr '\n' ',' | sed 's/,$//')
    ISSUES="${ISSUES}\n⚠ DEADLOCK: ${f} lines ${LINES} — .Result on UI thread causes deadlock"
  fi
done <<< "$ALL_CS"

# ── 3. DataMember Order non-sequential (append-only rule) ──
while IFS= read -r f; do
  [ -z "$f" ] && continue
  [ ! -f "$f" ] && continue

  if grep -q '\[DataMember' "$f" 2>/dev/null; then
    ORDERS=$(grep -oP 'Order\s*=\s*\K\d+' "$f" 2>/dev/null | tr '\n' ' ')
    if [ -n "$ORDERS" ]; then
      PREV=0
      for order in $ORDERS; do
        if [ "$order" -lt "$PREV" ]; then
          ISSUES="${ISSUES}\n⚠ CONTRACT: ${f} — DataMember Order non-sequential (${PREV} → ${order})"
          break
        fi
        PREV=$order
      done
    fi
  fi
done <<< "$ALL_CS"

# ── 4. Newtonsoft.Json (should use DataContractJsonSerializer) ──
while IFS= read -r f; do
  [ -z "$f" ] && continue
  [ ! -f "$f" ] && continue

  if grep -q "Newtonsoft" "$f" 2>/dev/null; then
    ISSUES="${ISSUES}\n⚠ SERIALIZER: ${f} — Newtonsoft.Json detected, use DataContractJsonSerializer"
  fi
done <<< "$ALL_CS"

# ── 5. Dotnet build (only if no static issues — build is slow) ──
if [ -z "$ISSUES" ]; then
  SLN=$(find . -maxdepth 1 -name '*.sln' -print -quit 2>/dev/null)
  if [ -n "$SLN" ]; then
    BUILD_OUTPUT=$(dotnet build "$SLN" -c Release --no-restore --verbosity quiet 2>&1 || true)
    if echo "$BUILD_OUTPUT" | grep -qE 'Build FAILED|error CS'; then
      ERROR_COUNT=$(echo "$BUILD_OUTPUT" | grep -c 'error CS' || true)
      FIRST_ERRORS=$(echo "$BUILD_OUTPUT" | grep 'error CS' | head -5)
      ISSUES="${ISSUES}\n🔴 BUILD FAILED: ${ERROR_COUNT} error(s)\n${FIRST_ERRORS}"
    fi
  fi
fi

# ── Output ──
if [ -n "$ISSUES" ]; then
  ESCAPED=$(printf "%b" "$ISSUES" | sed 's/"/\\"/g' | sed ':a;N;$!ba;s/\n/\n/g')
  echo "{\"systemMessage\": \"🛡 Quality Gate (${FILE_COUNT} .cs files checked):\n${ESCAPED}\"}"
fi

exit 0
