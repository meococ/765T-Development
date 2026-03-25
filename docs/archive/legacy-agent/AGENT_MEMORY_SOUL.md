# Agent Memory & Soul Architecture — 765T Dream Team

> Historical reference only. Current startup/runtime truth lives in `../assistant/*`, `../ARCHITECTURE.md`, and `../PATTERNS.md`.


> *"Một agent không có memory chỉ là tool. Một agent có soul mới là đồng đội."*

## Tổng quan

Mỗi agent trong Dream Team 765T có **4 lớp nhớ** riêng biệt — từ short-term working memory đến persistent soul identity. Hệ thống này cho phép agents **nhớ, học, và phát triển** qua thời gian.

```
┌─────────────────────────────────────────────────────────────────┐
│                    AGENT MEMORY STACK                           │
│                                                                 │
│  ┌──────────────────────────────────────────┐                  │
│  │  Layer 4: SOUL (Evolving Identity)       │  ← Who I am      │
│  │  Personality, values, wisdom, growth     │  ← Version-ctrl   │
│  │  File: .assistant/souls/{agent}.soul.json   │  ← Curated       │
│  └──────────────────┬───────────────────────┘                  │
│                     │ informs                                   │
│  ┌──────────────────▼───────────────────────┐                  │
│  │  Layer 3: SEMANTIC MEMORY (Vector DB)    │  ← What I know    │
│  │  Embedded knowledge, patterns, expertise │  ← Qdrant/agent   │
│  │  Collection: bim765t.agent.{agent-id}    │  ← Searchable     │
│  └──────────────────┬───────────────────────┘                  │
│                     │ promoted from                              │
│  ┌──────────────────▼───────────────────────┐                  │
│  │  Layer 2: EPISODIC MEMORY (JSON files)   │  ← What happened  │
│  │  Mission history, decisions, lessons     │  ← Per-agent dir  │
│  │  Path: %APPDATA%/.../agent-memory/{id}/  │  ← Persistent     │
│  └──────────────────┬───────────────────────┘                  │
│                     │ captured from                              │
│  ┌──────────────────▼───────────────────────┐                  │
│  │  Layer 1: WORKING MEMORY (In-RAM)        │  ← What now       │
│  │  Current task context, active reasoning  │  ← Session only   │
│  │  Store: SessionMemoryStore (LRU 150)     │  ← Ephemeral      │
│  └──────────────────────────────────────────┘                  │
│                                                                 │
│  Data Flow:  Working → Episodic → Semantic ← Soul              │
│  Promotion:  Auto (conditions) + Manual (#remember)             │
│  Retrieval:  70% semantic + 30% lexical, top-5, min 0.65       │
└─────────────────────────────────────────────────────────────────┘
```

---

## Layer 1: Working Memory (In-RAM)

| Thuộc tính | Giá trị |
|-----------|---------|
| **Storage** | SessionMemoryStore — LinkedList in-memory |
| **Capacity** | 150 entries/session, LRU eviction |
| **Lifetime** | Session-only (cleared on restart) |
| **Latency** | <1ms |
| **Scope** | Per-agent session |

### Chức năng
- Giữ context hiện tại (document, view, selection)
- Track conversation/reasoning chain
- Immediate retrieval cho follow-up questions
- Auto-flush to Episodic Memory khi session kết thúc

### Retrieval scoring
```
score = token_match(1.0) + document_match(0.5) + recency_bonus(0.25 decay 7d)
```

---

## Layer 2: Episodic Memory (Persistent JSON)

| Thuộc tính | Giá trị |
|-----------|---------|
| **Storage** | JSON files per episode |
| **Path** | `%APPDATA%/BIM765T.Revit.Agent/state/agent-memory/{agent-id}/episodes/` |
| **Lifetime** | Persistent (90 days active, 1 year archive) |
| **Scope** | Per-agent namespace |

### Record structure
```json
{
  "episodeId": "uuid",
  "agentId": "bim-manager-pro",
  "missionId": "mission-uuid",
  "missionType": "review-plan",
  "timestamp": "2026-03-21T10:30:00Z",
  "outcome": "success",
  "duration_seconds": 180,
  "context": {
    "documentKey": "SR_QQ-T_LOD400",
    "viewKey": "3D-Coordination",
    "selectionCount": 67,
    "projectPhase": "CD"
  },
  "keyObservations": [
    "67/73 penetration cuts thành công",
    "6 MISSING_INSTANCE do host element bị ẩn trong workset"
  ],
  "keyDecisions": [
    "Approve rerun cho 6 MISSING_INSTANCE sau khi unhide workset",
    "Không rerun full batch — chỉ targeted source IDs"
  ],
  "toolSequence": [
    "session.get_task_context",
    "review.round_penetration_cut_qc",
    "tool.get_guidance"
  ],
  "lessonsLearned": [
    "Luôn check workset visibility trước khi chạy penetration batch"
  ],
  "emotionalMarkers": {
    "frustration": 0.2,
    "satisfaction": 0.8,
    "surprise": 0.3,
    "confidence": 0.9
  }
}
```

### Retention policy
| Window | Action |
|--------|--------|
| 0–90 days | Full detail |
| 90 days–1 year | Compress to summary (keep lessons + decisions) |
| >1 year | Keep only if has `lessonsLearned` |

---

## Layer 3: Semantic Memory (Vector Database)

| Thuộc tính | Giá trị |
|-----------|---------|
| **Engine** | Qdrant (primary) + SQLite lexical (mandatory fallback) |
| **Embedding** | `all-MiniLM-L6-v2` (384D, via Ollama local-first) |
| **Collection** | `bim765t.agent.{agent-id}` per agent |
| **Shared** | `bim765t.agent.shared` for cross-agent knowledge |
| **Retrieval** | 70% semantic + 30% lexical, top-5, min score 0.65 |

### Collections per agent

| Agent | Collection | Primary content |
|-------|-----------|----------------|
| BIM Manager Pro | `bim765t.agent.bim-manager-pro` | Standards rulings, risk assessments, workflow decisions |
| Revit API Developer | `bim765t.agent.revit-api-developer` | API patterns, performance benchmarks, transaction strategies |
| Revit UI Engineer | `bim765t.agent.revit-ui-engineer` | XAML patterns, layout solutions, performance fixes |
| Research & Frontend | `bim765t.agent.research-frontend-organizer` | User research findings, competitive intel, feature proposals |
| Marketing & Repo | `bim765t.agent.marketing-repo-manager` | Content pieces, release notes, community feedback |
| **Shared** | `bim765t.agent.shared` | Cross-agent lessons, project-wide patterns, team decisions |

### Promotion pipeline

```
Episodic Record
    │
    ▼
Promotion Check:
  ✅ outcome == "success"
  ✅ duration > 120s
  ✅ keyObservations >= 2
  ✅ NOT (failed AND no lesson)
  ✅ NOT dry-run
    │
    ▼
Embed via Ollama (all-MiniLM-L6-v2, 384D)
    │
    ▼
Upsert to Qdrant collection
  + SQLite lexical index (fallback)
    │
    ▼
Available for semantic retrieval
```

### Manual promotion
- User tags mission với `#remember` → force promote regardless of auto conditions
- Agent self-tags với `@promote` → promotes with note

### Shared memory
- Any agent can write to `bim765t.agent.shared` when knowledge is cross-cutting
- Shared memories have `visibility: "shared"` flag
- All agents search shared collection alongside their own

---

## Layer 4: Soul (Evolving Identity)

| Thuộc tính | Giá trị |
|-----------|---------|
| **Storage** | `.assistant/souls/{agent-id}.soul.json` |
| **Lifetime** | Permanent, version-controlled |
| **Evolution** | Every 10 missions OR significant event |
| **Curation** | Auto-suggested + human-approved |

### Soul components

```
Soul
├── Identity (Who am I?)
│   ├── corePurpose        → NEVER changes
│   ├── currentMission     → Updates with project phase
│   ├── values             → Rarely changes (needs lead approval)
│   ├── strengths          → Grows over time
│   └── growthAreas        → Shrinks over time
│
├── Personality (How do I behave?)
│   ├── communicationStyle → How I talk
│   ├── decisionStyle      → How I decide
│   ├── conflictStyle      → How I handle disagreements
│   └── learningStyle      → How I learn best
│
├── Wisdom (What have I learned?)
│   ├── coreBeliefs        → Fundamental truths (grows slowly)
│   ├── patterns           → Successful approaches (grows with experience)
│   └── antiPatterns       → What to avoid (learned from failures)
│
├── Relationships (How do I work with others?)
│   └── teamDynamics       → Trust, collaboration history, communication notes
│
├── Preferences (What do I prefer?)
│   ├── tools              → Favorite/most-effective tools
│   ├── workflows          → Preferred approach patterns
│   ├── codeStyle          → Coding preferences (if applicable)
│   └── reviewFocus        → What I pay attention to
│
└── Growth (How am I developing?)
    ├── experiencePoints   → Total missions completed
    ├── specializations    → Domain × level matrix
    └── milestones         → Achievements and breakthroughs
```

### Evolution rules

| Rule | Detail |
|------|--------|
| **corePurpose** | NEVER changes — this is the agent's reason for existence |
| **values** | Change only with Agent 1 (lead) approval |
| **patterns** | Need ≥3 successful applications before confidence > 0.8 |
| **antiPatterns** | Only 1 failure needed if severity == critical |
| **personality** | Needs ≥20 missions of consistent behavior to change |
| **trust scores** | Update after each collaboration, decay toward 0.5 if no interaction in 30 days |
| **specializations** | Level up after threshold missions (novice→10→competent→25→proficient→50→expert→100→master) |

### Evolution process

```
Every 10 missions:
  1. Review recent episodic memories
  2. Identify new patterns or reinforced beliefs
  3. Update wisdom.patterns and wisdom.antiPatterns
  4. Check personality consistency (≥20 missions)
  5. Update growth.specializations
  6. Log milestone if threshold crossed
  7. Increment soul.version
  8. Update soul.lastEvolved
```

---

## Inter-Agent Memory Sharing

### Read permissions

| Agent | Own memory | Shared memory | Other agent's memory |
|-------|-----------|---------------|---------------------|
| BIM Manager Pro (Lead) | ✅ Full | ✅ Full | ✅ Read-only (all agents) |
| Revit API Developer | ✅ Full | ✅ Full | ⚠️ Read-only (Agent 5 only) |
| Revit UI Engineer | ✅ Full | ✅ Full | ⚠️ Read-only (Agent 2 only) |
| Research & Frontend | ✅ Full | ✅ Full | ❌ Own + shared only |
| Marketing & Repo | ✅ Full | ✅ Full | ❌ Own + shared only |

### Write permissions

| Target | Who can write |
|--------|--------------|
| Agent's own memory | Only that agent |
| Shared memory | Any agent |
| Other agent's memory | ❌ Never (read-only cross-agent) |

### Team memory events

```json
{
  "event": "memory.promoted",
  "agentId": "revit-api-developer",
  "summary": "Learned: FilteredElementCollector with OfCategory pre-filter 10x faster",
  "visibility": "shared",
  "notifyAgents": ["bim-manager-pro", "revit-ui-engineer"]
}
```

---

## Retrieval Strategy

### When an agent needs to answer a question:

```
1. Search own Working Memory (Layer 1)
   → Fast, current context

2. Search own Semantic Memory (Layer 3)
   → Embedded knowledge, similar past experiences

3. Search Shared Memory
   → Cross-agent knowledge, team patterns

4. Search own Episodic Memory (Layer 2)
   → Detailed past missions (if semantic didn't find enough)

5. Consult Soul (Layer 4)
   → Preferences, patterns, anti-patterns for decision guidance

6. If still insufficient → Route to another agent via Router
```

### Scoring fusion
```
final_score = 0.7 × semantic_score + 0.3 × lexical_score
              + 0.1 × recency_bonus
              + 0.05 × usage_frequency_bonus

filter: final_score >= 0.65
limit: top 5 results
dedup: cluster similar results, keep highest score
```

---

## File Structure

```
.assistant/
├── souls/                              ← Soul files (version-controlled)
│   ├── bim-manager-pro.soul.json
│   ├── revit-api-developer.soul.json
│   ├── revit-ui-engineer.soul.json
│   ├── research-frontend-organizer.soul.json
│   └── marketing-repo-manager.soul.json
│
├── config/
│   ├── agent-memory-architecture.json  ← Architecture specification
│   └── agent-router.json              ← Routing & discovery rules
│
└── agents/                             ← Agent definitions (link to soul)

%APPDATA%/BIM765T.Revit.Agent/state/
├── agent-memory/                       ← Per-agent episodic memory
│   ├── bim-manager-pro/
│   │   └── episodes/                   ← JSON episode files
│   ├── revit-api-developer/
│   │   └── episodes/
│   ├── revit-ui-engineer/
│   │   └── episodes/
│   ├── research-frontend-organizer/
│   │   └── episodes/
│   └── marketing-repo-manager/
│       └── episodes/
│
└── workerhost.db                       ← SQLite event store + lexical fallback

Qdrant collections:
├── bim765t.agent.bim-manager-pro       ← 384D vectors
├── bim765t.agent.revit-api-developer
├── bim765t.agent.revit-ui-engineer
├── bim765t.agent.research-frontend-organizer
├── bim765t.agent.marketing-repo-manager
└── bim765t.agent.shared                ← Cross-agent knowledge
```

---

## Embedding Model Setup

### Local-first with Ollama
```powershell
# Install Ollama (if not already)
winget install Ollama.Ollama

# Pull embedding model
ollama pull all-minilm:l6-v2

# Verify
ollama run all-minilm:l6-v2 "test embedding"
```

### Configuration
```json
{
  "embedding": {
    "provider": "ollama",
    "model": "all-minilm:l6-v2",
    "dimensions": 384,
    "endpoint": "http://localhost:11434/api/embeddings",
    "fallback": "sentence-transformers via Python sidecar"
  }
}
```

---

*Memory & Soul Architecture được thiết kế ngày 2026-03-21 cho Dream Team 765T.*
*Spec version: 1.0.0*