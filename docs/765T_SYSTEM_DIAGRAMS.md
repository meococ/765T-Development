# 765T — System Architecture & Build Philosophy

> Tài liệu visual cho toàn bộ kiến trúc và triết lý build của BIM765T.
> Last updated: 2026-03-25

---

## 1. Build Philosophy — Triết lý Build

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│              🎯  "CURSOR FOR BIM" — KHÔNG PHẢI CHATBOT                  │
│                                                                         │
│   ┌──────────────────────────────────────────────────────────────┐      │
│   │                      MINDSET                                  │      │
│   │                                                               │      │
│   │   ❌ Phòng thủ, sợ sai             ✅ Tấn công, tận dụng AI  │      │
│   │   ❌ An toàn trước hết              ✅ Chất lượng trước hết   │      │
│   │   ❌ Hỏi từng bước                  ✅ Batch work, ship fast  │      │
│   │   ❌ Dừng lại khi gặp blocker       ✅ Giải quyết, đề xuất   │      │
│   │   ❌ Code xong = done               ✅ User dùng được = done  │      │
│   │                                                               │      │
│   └──────────────────────────────────────────────────────────────┘      │
│                                                                         │
│   ┌──────────────────────────────────────────────────────────────┐      │
│   │                  CORE PRINCIPLES                              │      │
│   │                                                               │      │
│   │   1. Rules engine first, LLM chỉ 10%                         │      │
│   │   2. Zero UI freeze — DMU + IdlingEvent chunked               │      │
│   │   3. Fully local memory — Qdrant + SQLite on-device           │      │
│   │   4. Role-based tool filtering — 25 tools/role, NOT 192      │      │
│   │   5. Tiered mutation UX — not everything needs 4-step flow    │      │
│   │   6. Standards as Code — machine-readable, AI-enforced        │      │
│   │                                                               │      │
│   └──────────────────────────────────────────────────────────────┘      │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 2. System Architecture — Two-Process Model

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              REVIT PROCESS (net48)                              │
│                                                                                 │
│  ┌───────────────────────────────────────────────────────────────────────┐       │
│  │                  BIM765T.Revit.Agent                                  │       │
│  │                  ═══════════════════                                  │       │
│  │                  EXECUTION KERNEL                                    │       │
│  │                                                                      │       │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────────────────┐      │       │
│  │  │  WPF Pane   │  │  20 Tool     │  │  ExternalEvent         │      │       │
│  │  │  ─────────  │  │  Modules     │  │  Scheduler             │      │       │
│  │  │  Single     │  │  ──────────  │  │  ──────────────        │      │       │
│  │  │  Worker     │  │  192 tools   │  │  Thread-safe           │      │       │
│  │  │  Shell      │  │  registered  │  │  mutation gateway      │      │       │
│  │  └─────────────┘  └──────────────┘  └────────────────────────┘      │       │
│  │                                                                      │       │
│  │  ┌─────────────────────────────────────────────────────────────┐     │       │
│  │  │  ⚠️  NƠI DUY NHẤT chạm Revit API                         │     │       │
│  │  │  ⚠️  Embedded worker.* session (migration window)          │     │       │
│  │  └─────────────────────────────────────────────────────────────┘     │       │
│  └───────────────────────────────────────────────────────────────────────┘       │
│                                          │                                      │
│                                          │ Named Pipes                           │
│                                          │ (Kernel Channel)                      │
│                                          │                                      │
│  ┌───────────────────────────────────────▼───────────────────────────────┐       │
│  │                  BIM765T.Revit.WorkerHost (net8.0)                    │       │
│  │                  ═════════════════════════════════                    │       │
│  │                  CONTROL PLANE + AI BROKER                           │       │
│  │                                                                      │       │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌──────────────────┐  │       │
│  │  │  Mission   │ │  Memory    │ │  External  │ │  Capability      │  │       │
│  │  │  Orchestr. │ │  Stack     │ │  AI Broker │ │  Resolution      │  │       │
│  │  │  ────────  │ │  ────────  │ │  ────────  │ │  ──────────      │  │       │
│  │  │  Planner   │ │  SQLite    │ │  HTTP API  │ │  Packs           │  │       │
│  │  │  Retriever │ │  Qdrant    │ │  SSE       │ │  Workspaces      │  │       │
│  │  │  Safety    │ │  (hash)    │ │  gRPC      │ │  Catalog         │  │       │
│  │  └────────────┘ └────────────┘ └────────────┘ └──────────────────┘  │       │
│  └───────────────────────────────────────────────────────────────────────┘       │
│                                                                                 │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐              │
│  │  Revit.Bridge    │  │  Revit.McpHost   │  │  Copilot.Core    │              │
│  │  ──────────────  │  │  ──────────────  │  │  ──────────────  │              │
│  │  CLI client      │  │  MCP stdio       │  │  AI services     │              │
│  │  named pipes     │  │  JSON-RPC        │  │  LLM abstraction │              │
│  └──────────────────┘  └──────────────────┘  └──────────────────┘              │
│                                                                                 │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Flow — Từ User đến Revit Model

```
                    ┌──────────────────────┐
                    │     👤 USER           │
                    │  "Rename MEP views    │
                    │   to NCS standard"    │
                    └──────────┬───────────┘
                               │
                               ▼
               ┌───────────────────────────────┐
               │        WPF WORKER SHELL       │
               │   Chat-first assistant surface │
               └───────────────┬───────────────┘
                               │
                               ▼
          ┌────────────────────────────────────────┐
          │          WORKERHOST PIPELINE            │
          │                                        │
          │  ┌──────────┐    ┌───────────────────┐ │
          │  │ 1. INTENT │───▶│ 2. CANDIDATE     │ │
          │  │    GATE   │    │    BUILDER        │ │
          │  │ ────────  │    │ ─────────────     │ │
          │  │ Rule-first│    │ Policy-filtered   │ │
          │  │ classify  │    │ tool candidates   │ │
          │  └──────────┘    └────────┬──────────┘ │
          │                           │             │
          │                           ▼             │
          │               ┌───────────────────────┐ │
          │               │ 3. BOUNDED LLM        │ │
          │               │    PLANNER             │ │
          │               │ ─────────────────     │ │
          │               │ Select from 25 tools   │ │
          │               │ (NOT 192)              │ │
          │               │ Plan execution steps   │ │
          │               └────────┬──────────────┘ │
          │                        │                │
          │   ┌───────────────┐    │                │
          │   │ MEMORY LOOKUP │◄───┘                │
          │   │ ─────────────  │                    │
          │   │ SQLite → Qdrant│                    │
          │   │ (retriever)   │                    │
          │   └───────────────┘                    │
          └────────────────────┬───────────────────┘
                               │
                               │ Named Pipes
                               ▼
          ┌────────────────────────────────────────┐
          │          REVIT.AGENT EXECUTION          │
          │                                        │
          │   ┌─────────┐   ┌─────────┐            │
          │   │ PREVIEW  │──▶│ USER    │            │
          │   │ (dry-run)│   │ SEES    │            │
          │   │          │   │ CHANGES │            │
          │   └─────────┘   └────┬────┘            │
          │                      │                  │
          │               ┌──────▼──────┐           │
          │               │   APPROVE?  │           │
          │               │  ┌───┐ ┌──┐ │           │
          │               │  │ ✅ │ │❌│ │           │
          │               │  └─┬─┘ └──┘ │           │
          │               └────┼────────┘           │
          │                    │                    │
          │          ┌─────────▼──────────┐         │
          │          │  EXECUTE           │         │
          │          │  ExternalEvent     │         │
          │          │  → UI Thread       │         │
          │          │  → Transaction     │         │
          │          └─────────┬──────────┘         │
          │                    │                    │
          │          ┌─────────▼──────────┐         │
          │          │  VERIFY            │         │
          │          │  Evidence returned │         │
          │          │  to WorkerHost     │         │
          │          └───────────────────┘         │
          └────────────────────────────────────────┘
```

---

## 4. Tiered Analysis — Tại sao $0.27/month, không phải $252K

```
┌─────────────────────────────────────────────────────────────────────┐
│                    200,000 ELEMENTS IN MODEL                        │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  TIER 0: RULES ENGINE — 75% checks                         │   │
│  │  ══════════════════════════════════                         │   │
│  │  LOCAL. FREE. INSTANT. OFFLINE.                              │   │
│  │                                                              │   │
│  │  • Naming regex    • Null checks    • Workset rules          │   │
│  │  • Level consistency   • Parameter validation                │   │
│  │                                                              │   │
│  │  0 tokens. 0 latency. 0 cost.                                │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                           │                                         │
│                           ▼ only 25% cần thêm                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  TIER 1: LOCAL STATISTICS — 15% checks                      │   │
│  │  ══════════════════════════════════════                      │   │
│  │  LOCAL. FREE. PRE-COMPUTED.                                  │   │
│  │                                                              │   │
│  │  • Category counts           • Parameter distributions       │   │
│  │  • Relationship graphs       • Model fingerprint (~400 tok)  │   │
│  │                                                              │   │
│  │  Pre-computed in SQLite index. 0 API cost.                   │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                           │                                         │
│                           ▼ chỉ 10% thật sự cần LLM               │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  TIER 2: LLM REASONING — 10% checks                        │   │
│  │  ═══════════════════════════════════                         │   │
│  │  CLOUD. 20 queries/day × 1,500 tokens.                      │   │
│  │                                                              │   │
│  │  • Standard interpretation    • Multi-step planning          │   │
│  │  • Cross-model reasoning      • NL report generation         │   │
│  │                                                              │   │
│  │  Cost: $0.009/day = $0.27/month per user                     │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ═══════════════════════════════════════════════════════════════    │
│  TẠI SAO KHÔNG GỬI CẢ MODEL CHO LLM?                              │
│                                                                     │
│  200K elements × 175 tokens = 35M tokens/scan                      │
│  → Không LLM nào có 35M context window                              │
│  → GPT-4o max ~730 elements, Claude max ~1,142 elements             │
│  → Solution: Smart retrieval, gửi 20-50 elements relevant          │
│  ═══════════════════════════════════════════════════════════════    │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 5. Memory Architecture — Fully Local, Privacy-Safe

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        MEMORY STACK (ON-DEVICE)                         │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │                    USER MESSAGE                              │       │
│  └──────────────────────────┬──────────────────────────────────┘       │
│                              │                                          │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────┐                  │
│  │              RETRIEVER AGENT                      │                  │
│  │              ═══════════════                      │                  │
│  │                                                   │                  │
│  │    ┌──────────────────┐    ┌──────────────────┐  │                  │
│  │    │  Qdrant Vector   │    │  SQLite Lexical  │  │                  │
│  │    │  ──────────────  │    │  ──────────────  │  │                  │
│  │    │  6 namespaces    │    │  Durable truth   │  │                  │
│  │    │  Hash embeddings │    │  Event store     │  │                  │
│  │    │  (non-semantic)  │    │  Lexical search  │  │                  │
│  │    │                  │    │                  │  │                  │
│  │    │  🔮 TARGET:      │    │  ✅ ALWAYS       │  │                  │
│  │    │  Ollama semantic │    │  AVAILABLE       │  │                  │
│  │    │  embeddings      │    │                  │  │                  │
│  │    └──────────────────┘    └──────────────────┘  │                  │
│  │              │                      │             │                  │
│  │              └──────────┬───────────┘             │                  │
│  │                         │ merge                   │                  │
│  │                         ▼                         │                  │
│  │              ┌──────────────────┐                 │                  │
│  │              │  PROMPT CONTEXT  │                 │                  │
│  │              │  (top-3 hits +   │                 │                  │
│  │              │   model finger-  │                 │                  │
│  │              │   print ~400tok) │                 │                  │
│  │              └──────────────────┘                 │                  │
│  └──────────────────────────────────────────────────┘                  │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │                    6 NAMESPACES                               │       │
│  │                                                               │       │
│  │  ┌────────────────────┐  ┌────────────────────┐              │       │
│  │  │ atlas-native-      │  │ atlas-custom-      │              │       │
│  │  │ commands           │  │ tools              │              │       │
│  │  └────────────────────┘  └────────────────────┘              │       │
│  │  ┌────────────────────┐  ┌────────────────────┐              │       │
│  │  │ atlas-curated-     │  │ playbooks-         │              │       │
│  │  │ scripts            │  │ policies           │              │       │
│  │  └────────────────────┘  └────────────────────┘              │       │
│  │  ┌────────────────────┐  ┌────────────────────┐              │       │
│  │  │ project-runtime-   │  │ evidence-          │              │       │
│  │  │ memory             │  │ lessons            │              │       │
│  │  └────────────────────┘  └────────────────────┘              │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  HEALTH STATUS                                               │       │
│  │  SQLite UP + Qdrant UP   → vector_available_non_semantic     │       │
│  │  SQLite UP + Qdrant DOWN → lexical_only (vẫn Ready)          │       │
│  └─────────────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Threading Model — Zero UI Freeze

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     REVIT THREADING MODEL                               │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │                     UI THREAD (single)                         │      │
│  │                     ═══════════════════                        │      │
│  │                                                                │      │
│  │   Revit API calls      WPF rendering      User interactions   │      │
│  │        ONLY HERE            ONLY HERE           ONLY HERE     │      │
│  │                                                                │      │
│  │   ⚠️  KHÔNG .Result trên UI thread (deadlock guaranteed)      │      │
│  │   ⚠️  Dispatcher.InvokeAsync cho background → UI updates      │      │
│  │                                                                │      │
│  └─────────────────────────────────┬─────────────────────────────┘      │
│                                    │                                     │
│                                    │ ExternalEvent                       │
│                                    │ (gateway duy nhất)                  │
│                                    │                                     │
│  ┌─────────────────────────────────▼─────────────────────────────┐      │
│  │                  BACKGROUND THREADS                            │      │
│  │                  ═════════════════                              │      │
│  │                                                                │      │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐    │      │
│  │  │ WorkerHost   │  │ AI/LLM       │  │ Memory           │    │      │
│  │  │ Pipeline     │  │ Processing   │  │ Operations       │    │      │
│  │  │ ──────────   │  │ ──────────   │  │ ──────────       │    │      │
│  │  │ Orchestr.    │  │ Intent parse │  │ SQLite R/W       │    │      │
│  │  │ Routing      │  │ LLM calls    │  │ Qdrant search    │    │      │
│  │  │ Planning     │  │ Embeddings   │  │ Event store      │    │      │
│  │  └──────────────┘  └──────────────┘  └──────────────────┘    │      │
│  └───────────────────────────────────────────────────────────────┘      │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │              ZERO-FREEZE SCANNING PATTERN                      │      │
│  │                                                                │      │
│  │  Layer 1: DMU (Dynamic Model Updater)                         │      │
│  │  ──────────────────────────────────                            │      │
│  │  Fires inside transactions. Enqueue dirty IDs only. <1ms.     │      │
│  │  → We know WHAT changed without scanning.                      │      │
│  │                                                                │      │
│  │  Layer 2: IdlingEvent Chunked Processing                       │      │
│  │  ────────────────────────────────────                          │      │
│  │  150 elements/frame, 15ms budget. User never notices.          │      │
│  │  Cold scan 200K elements: ~17s of idle time (invisible).       │      │
│  │                                                                │      │
│  │  Layer 3: DocumentChanged Cache Invalidation                   │      │
│  │  ───────────────────────────────────────                       │      │
│  │  Mark dirty → rescan ONLY dirty elements.                      │      │
│  │  50 dirty elements = <1 second, invisible to user.             │      │
│  │                                                                │      │
│  └───────────────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 7. User Autonomy Graduation — Từ Passive đến Agent

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    USER AUTONOMY SPECTRUM                                │
│                    ══════════════════════                                │
│                                                                         │
│  Level 0                     Level 1              Level 2               │
│  AMBIENT                     QUERY                TASK                  │
│  ═══════                     ═════                ════                  │
│  Passive                     Ask & get            Targeted action       │
│  Zero effort                 answer               Preview first         │
│                                                                         │
│  ┌─────────────┐            ┌─────────────┐      ┌─────────────┐       │
│  │ AI monitors │            │ "How many   │      │ "Rename MEP │       │
│  │ silently    │            │  rooms < 50 │      │  views to   │       │
│  │             │            │  m2?"       │      │  NCS"       │       │
│  │ Badge:      │            │             │      │             │       │
│  │ "3 issues   │            │ AI answers  │      │ Preview →   │       │
│  │  found"     │            │ with model  │      │ Approve →   │       │
│  │             │            │ context     │      │ Execute     │       │
│  └─────────────┘            └─────────────┘      └─────────────┘       │
│       │                          │                     │                │
│       │     ╔════════════════════╧═════════════════════╧═══╗            │
│       │     ║  TRUST BUILDS OVER TIME — LIKE CURSOR        ║            │
│       │     ║  Tab → Cmd+K → Agent → Cloud Agents          ║            │
│       │     ╚════════════════════╤═════════════════════╤═══╝            │
│       │                          │                     │                │
│  Level 3                                          Level 4               │
│  WORKFLOW                                         SCHEDULED             │
│  ════════                                         ═════════             │
│  Multi-step                                       Autonomous            │
│  AI plans                                         Recurring             │
│                                                                         │
│  ┌─────────────────────────┐       ┌─────────────────────────┐         │
│  │ "Prepare model for      │       │ "Every sync, check      │         │
│  │  coordination meeting"  │       │  naming compliance"     │         │
│  │                         │       │                         │         │
│  │ AI: QC → fix critical   │       │ Runs automatically      │         │
│  │ → create views          │       │ Notifies on issues      │         │
│  │ → export report         │       │ Weekly health report    │         │
│  │                         │       │                         │         │
│  │ User approves each gate │       │ "Warning count jumped   │         │
│  └─────────────────────────┘       │  150 since last sync"   │         │
│                                     └─────────────────────────┘         │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  GRADUATION PATH                                             │       │
│  │                                                               │       │
│  │  Month 1: User configures firm standards (NCS, QC rules)     │       │
│  │  Month 2: AI learns patterns (workflow, preferences)         │       │
│  │  Month 3: Project memory accumulates (history, decisions)    │       │
│  │           AI knows: firm + user + project                    │       │
│  │           Switching cost: rebuild ALL THREE from scratch      │       │
│  │           → Lock-in effect (same as Cursor .cursorrules)     │       │
│  └─────────────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 8. Mutation UX — Tiered Quality Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     TIERED MUTATION FLOW                                 │
│         (Không phải mọi thứ cần 4-step flow)                           │
│                                                                         │
│  ┌───────────────────────────────────────────────────────┐              │
│  │  TIER 1: READ-ONLY / HARMLESS                         │              │
│  │  ─────────────────────────────                        │              │
│  │                                                        │              │
│  │  "How many walls on Level 2?"                          │              │
│  │  "Show me unplaced rooms"                              │              │
│  │                                                        │              │
│  │  → QUICK-PATH: Execute immediately, no approval        │              │
│  │  → Zero friction. Instant response.                    │              │
│  └───────────────────────────────────────────────────────┘              │
│                           │                                              │
│                           ▼                                              │
│  ┌───────────────────────────────────────────────────────┐              │
│  │  TIER 2: DETERMINISTIC MUTATION                       │              │
│  │  ──────────────────────────────                       │              │
│  │                                                        │              │
│  │  "Rename this view to NCS standard"                    │              │
│  │  "Set workset for selected elements"                   │              │
│  │                                                        │              │
│  │  → LIGHT CONFIRM: Preview or toast confirm             │              │
│  │  → Per manifest/policy rules                           │              │
│  └───────────────────────────────────────────────────────┘              │
│                           │                                              │
│                           ▼                                              │
│  ┌───────────────────────────────────────────────────────┐              │
│  │  TIER 3: HIGH-IMPACT MUTATION                         │              │
│  │  ────────────────────────────                         │              │
│  │                                                        │              │
│  │  "Rename ALL MEP views to NCS" (23 views)              │              │
│  │  "Delete unplaced views older than 30 days"            │              │
│  │                                                        │              │
│  │  → FULL FLOW:                                          │              │
│  │    ┌──────────┐  ┌──────────┐  ┌─────────┐  ┌──────┐ │              │
│  │    │ PREVIEW  │─▶│ APPROVE  │─▶│ EXECUTE │─▶│VERIFY│ │              │
│  │    │ (dry-run)│  │ (token   │  │ (UI     │  │(auto)│ │              │
│  │    │          │  │  5-min)  │  │  thread) │  │      │ │              │
│  │    └──────────┘  └──────────┘  └─────────┘  └──────┘ │              │
│  │                                                        │              │
│  │  ⚠️  Max 20 items per batch (BIM Manager threshold)   │              │
│  │  ⚠️  Context hash must match between preview & execute │              │
│  └───────────────────────────────────────────────────────┘              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 9. Project Boundary Map — Layer Ownership

```
┌─────────────────────────────────────────────────────────────────────────┐
│                       PROJECT BOUNDARY MAP                              │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  LAYER 1: Contracts (netstandard2.0)                         │       │
│  │  ═══════════════════════════════════                         │       │
│  │  DTOs only. KHÔNG reference RevitAPI.dll                     │       │
│  │  DataMember(Order=N) append-only. KHÔNG null.                │       │
│  │  DataContractJsonSerializer. Composition only.               │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                              │                                          │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  LAYER 2: Agent.Core (netstandard2.0)                        │       │
│  │  ════════════════════════════════════                         │       │
│  │  Core agent services. NO RevitAPI. NO WPF.                   │       │
│  │  Business logic, patterns, utilities.                        │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                              │                                          │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  LAYER 3: Revit.Agent (net48)                                │       │
│  │  ════════════════════════════                                │       │
│  │  NƠI DUY NHẤT reference RevitAPI.dll                         │       │
│  │  WPF UI. ExternalEvent. Tool execution.                      │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                              │                                          │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  LAYER 4: WorkerHost (net8.0)                                │       │
│  │  ════════════════════════════                                │       │
│  │  KHÔNG reference RevitAPI. Orchestration + memory + routing. │       │
│  │  HTTP + gRPC + SSE. SQLite + Qdrant.                         │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                              │                                          │
│  ┌─────────────────────────────────────────────────────────────┐       │
│  │  LAYER 5: Copilot.Core (netstandard2.0)                      │       │
│  │  ══════════════════════════════════════                       │       │
│  │  KHÔNG reference RevitAPI. AI services.                      │       │
│  │  LLM abstraction, memory, routing, packs.                    │       │
│  └─────────────────────────────────────────────────────────────┘       │
│                                                                         │
│  BOUNDARY RULE: Architecture.Tests enforce this at build time.          │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 10. Doc Authority Map — Single Source of Truth

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      DOC AUTHORITY MAP                                   │
│          Mỗi topic có 1 canonical authority duy nhất                    │
│                                                                         │
│  ┌────────────────────┐     ┌──────────────────────────────────────┐   │
│  │ CLAUDE.md          │     │ Startup notes, identity, bối cảnh   │   │
│  │ (đọc đầu tiên)     │     │ hiện tại, critical notes             │   │
│  └────────┬───────────┘     └──────────────────────────────────────┘   │
│           │                                                             │
│  ┌────────▼───────────┐     ┌──────────────────────────────────────┐   │
│  │ README.md          │     │ Repo overview, architecture diagram, │   │
│  │ README.en.md       │     │ honest marketing (chỉ claim đã ship)│   │
│  └────────┬───────────┘     └──────────────────────────────────────┘   │
│           │                                                             │
│  ┌────────▼───────────┐     ┌──────────────────────────────────────┐   │
│  │ AGENTS.md          │     │ Operating constitution, engineering  │   │
│  │ (⚠️ ĐANG THIẾU)    │     │ discipline, triết lý vận hành       │   │
│  └────────┬───────────┘     └──────────────────────────────────────┘   │
│           │                                                             │
│  ┌────────▼───────────┐     ┌──────────────────────────────────────┐   │
│  │ BASELINE.md        │     │ Current runtime truth — product IS   │   │
│  │ (canonical truth)  │     │ gì NGAY BÂY GIỜ                     │   │
│  └────────┬───────────┘     └──────────────────────────────────────┘   │
│           │                                                             │
│  ┌────────▼───────────┐     ┌──────────────────────────────────────┐   │
│  │ ARCHITECTURE.md    │     │ System boundaries, execution model,  │   │
│  │ PATTERNS.md        │     │ capability stack, memory arch        │   │
│  └────────┬───────────┘     └──────────────────────────────────────┘   │
│           │                                                             │
│  ┌────────▼───────────┐     ┌──────────────────────────────────────┐   │
│  │ PRODUCT_VISION.md  │     │ Product direction, target state,     │   │
│  │ (vision, NOT truth)│     │ research findings, go-to-market      │   │
│  └────────────────────┘     └──────────────────────────────────────┘   │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────┐         │
│  │  RULE: Nếu docs conflict → BASELINE.md wins cho runtime  │         │
│  │        ARCHITECTURE.md wins cho boundaries                 │         │
│  │        Code wins cho implementation                        │         │
│  └───────────────────────────────────────────────────────────┘         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 11. Team Structure — Ai làm gì

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        765T TEAM                                        │
│                                                                         │
│  ┌──────────────────────────────────────────────────────┐              │
│  │  🐱 MÈO CỌC (Owner / Product Owner)                  │              │
│  │  ════════════════════════════════════                  │              │
│  │  Vision. Decisions. Final approval.                   │              │
│  │  "Anh muốn chất lượng, không muốn an toàn"           │              │
│  └──────────────────────────┬───────────────────────────┘              │
│                              │                                          │
│  ┌──────────────────────────▼───────────────────────────┐              │
│  │  🧠 LINH (AI — Claude Code / Opus)                    │              │
│  │  ════════════════════════════════                      │              │
│  │                                                        │              │
│  │  Role 1: PROJECT MANAGER                               │              │
│  │    Plan, phân task, theo dõi tiến độ                   │              │
│  │                                                        │              │
│  │  Role 2: PRO BIM EXPERT                                │              │
│  │    Revit API, BIM workflow, industry standards          │              │
│  │                                                        │              │
│  │  Role 3: PRO AI AGENT BUILDER                          │              │
│  │    Agent system design, orchestration, memory           │              │
│  │                                                        │              │
│  │  Role 4: TRUSTED DEV PARTNER                           │              │
│  │    Code quality, research kỹ, không mơ hồ              │              │
│  │                                                        │              │
│  │  NGUYÊN TẮC: Research trước, code sau.                │              │
│  │              Nói thật, không overclaim.                │              │
│  │              Codex là thợ code, Linh là người nghĩ.   │              │
│  └──────────────────────────┬───────────────────────────┘              │
│                              │ dispatch tasks                           │
│                              ▼                                          │
│  ┌──────────────────────────────────────────────────────┐              │
│  │  ⚡ CODEX (AI — Execution Agent)                      │              │
│  │  ════════════════════════════════                      │              │
│  │  Implement. Code. Fix. Test. Deploy.                  │              │
│  │  Follows backlog from Linh.                           │              │
│  │  Reports findings back for review.                    │              │
│  └──────────────────────────────────────────────────────┘              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```
