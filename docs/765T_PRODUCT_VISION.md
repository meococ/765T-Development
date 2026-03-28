# 765T Product Vision — The Cursor for BIM

> Updated: 2026-03-29 | Status: Active | Owner: MII Team
>
> **Đọc file này như: Vision + Research + Roadmap — KHÔNG phải runtime truth.**
> Current runtime truth: `docs/assistant/BASELINE.md`
> Current architecture: `docs/ARCHITECTURE.md`

---

## 1. What We're Building

**BIM765T is an AI agent that lives inside Revit** — understands your project, remembers your standards, and does the work. Not a chatbot. Not a dashboard. An agent that makes BIM professionals faster every day, like Cursor makes developers faster.

### The One-Liner

> *"Mở Revit → AI đã hiểu project → hỏi gì trả lời đúng → ra lệnh thì làm → càng dùng càng thông minh."*

### Why This Matters

| Reality Today | With 765T |
|---------------|-----------|
| BIM Manager dành 60-70% thời gian cho documentation, QC, coordination | AI handle routine, human focus on design decisions |
| Firm standards trong đầu người, mất khi người nghỉ | Standards machine-readable, AI enforce tự động |
| QC check manual, bỏ sót, không nhất quán giữa team | AI scan liên tục, triage by severity, delta-based |
| Mỗi project bắt đầu từ zero context | AI nhớ patterns từ projects trước |

---

## 2. Target User & Market

### Primary: BIM Manager / BIM Coordinator

- Chịu trách nhiệm model quality cho 5-20 modelers
- Firm 50-500 người, projects $10M-$500M+
- Dùng Revit 2024/2025/2026, worksharing, BIM 360/ACC
- Pain: QC takes days, standards drift, knowledge silos

### Secondary: Lead Designer / Project Architect

- Cần answers nhanh: area counts, room schedules, view organization
- Cần compliance checks: planning conditions, client brief, fire codes
- Pain: Revit UI tệ cho queries, manual schedule/filter workflow

### Pricing Target

| Tier | Price | What |
|------|-------|------|
| Starter | Free | Read-only queries, 10/day, rules engine offline |
| Pro | $50/user/month | Full AI + memory + unlimited queries |
| Enterprise | Custom | On-premise LLM, firm standards, SSO, audit trail |

### Competitive Position

| Competitor | What They Do | What They Don't |
|------------|-------------|-----------------|
| Navisworks | Clash detection | No AI, no natural language, no model understanding |
| Solibri | Rule-based checking | No in-Revit, no agent, no memory |
| BIM Track | Issue tracking | No AI analysis, no auto-fix |
| Ideate BIMLink | Data export/import | No intelligence, no standards enforcement |
| **765T** | **All of above + AI reasoning + memory + standards + agent** | |

---

## 3. Technical Research — What Works, What Doesn't

### Research Method

3-agent adversarial debate: Devil's Advocate (Revit plugin dev, 10yr), BIM Practitioner (BIM Manager, 12yr), Solutions Architect (desktop AI specialist). Full debate transcript in project memory.

### FINDING 1: Revit Is NOT a Code Editor — Threading Is Everything

**Constraint:** Revit API is single-threaded. All API calls MUST go through UI thread.

**What this means:**
- Background scanning like Cursor's indexing is IMPOSSIBLE in Revit
- A naive 200K element scan freezes UI for 40-75 seconds
- Users will uninstall after ONE freeze during active modeling

**Solution — 3-Layer Architecture (proven patterns):**

```
Layer 1: DMU (Dynamic Model Updater)
  Revit's built-in change tracking. Fires inside transactions.
  Our handler: ONLY enqueue dirty element IDs. <1ms. Zero UI impact.
  → We know WHAT changed without scanning.

Layer 2: IdlingEvent Chunked Processing
  When Revit is idle (user not clicking), process 150 elements per frame.
  Budget: 15ms per frame. User never notices.
  Cold scan 200K elements: ~17 seconds of idle time.
  SetRaiseWithoutDelay(true) = drain queue during inactivity.

Layer 3: DocumentChanged Cache Invalidation
  On any model change → mark dirty in local cache → rescan ONLY dirty.
  50 dirty elements = <1 second, invisible to user.
```

**Result: Continuous scanning with ZERO UI freeze.** Not "minimal" — zero.

### FINDING 2: Token Cost — $0.27/month, Not $252K

**The wrong approach (what em initially proposed):**
```
200K elements × 175 tokens/element = 35M tokens/scan
GPT-4o: $175/scan × 48 scans/day = $252,000/month
No LLM has 35M token context window. Impossible.
```

**The right approach — Tiered Analysis:**

```
Tier 0: Rules Engine (LOCAL, FREE, INSTANT) — 75% of checks
  Naming regex, null checks, workset rules, level consistency.
  Zero tokens. Zero latency. Works offline.

Tier 1: Statistics (LOCAL, FREE) — 15% of checks
  Category counts, parameter distributions, relationship graphs.
  Pre-computed in SQLite index.

Tier 2: LLM Reasoning (CLOUD) — 10% of checks
  Standard interpretation, multi-step planning, NL report generation.
  20 queries/day × 1,500 tokens = 30K tokens/day
  Cost: $0.009/day = $0.27/month per user
```

**Model Fingerprint — always in LLM context (~400 tokens):**
```json
{
  "total_elements": 198432,
  "categories": {"Walls": 12400, "Rooms": 847, "Doors": 2100},
  "levels": ["B2","B1","GF","L1","L2","L3","RF"],
  "worksets": ["Exterior Shell","Interior","Structural","MEP"],
  "qc_status": {"violations": 23, "warnings": 7}
}
```

### FINDING 3: LLM Context — Smart Retrieval, Not Full Model

**No LLM can hold 200K elements.** Max elements in context:

| LLM | Context Window | Max Elements |
|-----|---------------|--------------|
| GPT-4o | 128K tokens | ~730 |
| Claude Sonnet | 200K tokens | ~1,142 |
| Gemini 1.5 Pro | 1M tokens | ~5,714 |

**Solution — The Cursor Pattern:**
1. Parse intent locally (regex + classifier, no LLM)
2. Run `FilteredElementCollector` locally for matching elements
3. Send ONLY results (20-50 elements) to LLM for formatting/reasoning
4. Total context per query: ~1,500-2,000 tokens

**Example:** "Rooms under 50m2 on Level 2"
- Intent parse: category=Rooms, filter=Area<50, level=Level2 (local, 0 tokens)
- FilteredElementCollector: 23 results (local, 80ms)
- LLM gets: 23 rooms × 20 tokens + format request = 500 tokens
- Cost: $0.0025. Time: 2 seconds total.

### FINDING 4: What BIM Managers Actually Need (Not What We Assumed)

**From 12-year BIM Manager perspective:**

**8/10 daily questions DON'T need AI:**
- "How many doors without numbers?" → Schedule + filter (90 seconds)
- "Total floor area by level?" → Room schedule (existing)
- These need better UI, not AI.

**Questions WHERE AI adds real value (current tools can't answer):**
- "What changed since last issue and is it coordinated with structural?"
- "Which areas will have coordination issues when MEP is added?"
- "Compare model against client brief — what's missing?"
- "How did we handle this on the similar project last year?"

→ Cross-model reasoning, predictive analysis, institutional memory.

**Auto-scan every 30 minutes: WRONG.**
- Too frequent = notification spam. Too infrequent = misses critical moments.
- What works: **delta detection on sync + severity triage.**
- "Warning count jumped 150 since last sync — here are the 3 Critical ones."

**One-click fix 100+ elements: NEVER.**
- BIM Manager approval threshold: 1-5 individual, 6-20 batch with list, 100+ = absolutely not.
- Auto-fix đúng 70%, phá model 30%. User can't tell which case they're in.

### FINDING 5: Memory — Must Be Fully Local for Enterprise

**Legal blocker:** Enterprise BIM firms have NDAs, ISO 27001, government contracts. Data cannot leave device.

**Solution — Fully local Mem0 stack:**
```
Ollama (qwen2.5:7b) — memory extraction LLM
Ollama (nomic-embed-text) — embeddings
Qdrant (localhost:6333) — vector storage
SQLite — episodic memory, firm preferences
= Zero external calls. GDPR safe. Air-gap safe.
```

**Memory scoping (prevents pollution):**

| Scope | Contains | Storage | Who Controls |
|-------|----------|---------|-------------|
| Firm Standards | "Use NCS naming", "Fire rating required on walls" | Local SQLite, sync on-prem | BIM Manager |
| User Patterns | "Prefers metric", "Checks worksets first" | Local per-user file | Individual |
| Project Context | Model stats, issue history, current session | In-process only, cleared on close | System |

### FINDING 6: Multi-Version Revit — Thin Adapter, Not Full Migration

**Full Nice3point migration = 6 months, high risk.** 192 tools × 50 metadata fields. Nice3point covers 5 of those fields.

**Right approach — IRevitContext abstraction:**
```
BIM765T.Core (netstandard2.0)
  └── 192 tools, all business logic, NO RevitAPI reference
  └── Calls IRevitContext interface

BIM765T.Revit2024 (net48) — 200-500 lines thin adapter
BIM765T.Revit2025 (net8.0) — thin adapter
BIM765T.Revit2026 (net8.0) — thin adapter (<1 day to add)
```

**Selective Nice3point adoption — just `RevitTask` (3 days, zero risk):**
```csharp
// Before: 150 lines ExternalEventHandler boilerplate
// After:
await RevitTask.RunAsync(app => doc.GetElement(id));
```

### FINDING 7: Semantic Kernel — DON'T Replace Orchestration

**SK sends all 192 tool schemas per request = 22,000 tokens overhead = $3,300/month just for descriptions.** SK is designed for ASP.NET async, not Revit UI thread. SK agent orchestration is experimental with breaking API changes.

**Rule-first engine is the competitive advantage.** Deterministic, offline, auditable, no hallucination. SK cannot replicate this.

**Right approach:** Keep rule-first engine. Use SK ONLY for LLM provider abstraction (chat completion, embeddings) — not for orchestration.

### FINDING 8: Tool Selection Accuracy Drops with Count

| Tools Available | LLM Selection Accuracy |
|-----------------|----------------------|
| 5-10 | ~95% |
| 20-30 | ~85-90% |
| 50+ | ~70-80% |
| 192 | ~45-60% (coin flip) |

**Solution:** Role-based tool filtering. BIM Manager sees 25 tools. MEP Engineer sees 25 different tools. AI selects from 25, not 192. Accuracy stays >85%.

---

## 4. Autonomy Spectrum — How Users Graduate

```
Level 0: AMBIENT (passive, zero effort)
──────────────────────────────────────
AI monitors model silently.
Badge on ribbon: "3 issues found" or "Model healthy."
User does NOTHING. Value arrives automatically.
= Cursor Tab prediction. Hook before user asks.

Level 1: QUERY (ask, get answer)
──────────────────────────────────────
"How many rooms under 50m2?"
"What changed since last sync?"
"Is this model NCS compliant?"
AI answers with model context + standards.
= Cursor codebase search.

Level 2: TASK (targeted action, preview first)
──────────────────────────────────────
"Rename MEP views to NCS standard"
"Fill missing fire ratings on Level 2 walls"
Preview → Approve (1-20 items) → Execute → Verify.
= Cursor Cmd+K targeted edit.

Level 3: WORKFLOW (multi-step, AI plans)
──────────────────────────────────────
"Prepare model for coordination meeting"
AI: QC → fix critical → create views → export report.
User approves at each gate.
= Cursor Agent mode.

Level 4: SCHEDULED (autonomous, recurring)
──────────────────────────────────────
"Every sync, check naming compliance"
"Weekly model health report"
Runs automatically, notifies on issues.
= Cursor BugBot / CI integration.
```

**Graduation path:** Users start at Level 0 (passive). Trust builds. They move to Level 1, then 2. After 2-3 months, power users reach Level 3-4. This is exactly how Cursor works — Tab → Cmd+K → Agent → Cloud Agents.

---

## 5. First 5 Minutes — How We Hook Users

```
MINUTE 0: Install add-in, open Revit
  Ribbon "765T" appears. Subtle. Badge: "Ready."

MINUTE 1: Open any .rvt file
  BIM765T auto-scans (IdlingEvent, invisible, zero freeze).
  10 seconds later → badge: "3 issues found"
  Toast: "Found 3 naming violations and 2 unplaced views."
  USER DID NOTHING. AI found problems automatically.

  → MAGIC MOMENT (like Cursor's first Tab completion)

MINUTE 2: Click badge → Issue Dashboard opens
  Model Health: 87/100
  3 Naming violations (with suggested fix)
  2 Unplaced views (with age: "created 3 days ago")
  [Fix All] [Review Each]

MINUTE 3: Click [Fix All] for naming
  Preview: "Level 2 - MEP" → "L02-M-MEP Plan" (NCS 7.0)
  [Apply] [Cancel]
  Click Apply → Done in 2 seconds.

MINUTE 4: Try chat
  "What standards are you using?"
  AI: "NCS 7.0 for view naming, detected from your patterns.
  Customize at Settings → Standards."

MINUTE 5: User hooked
  Badge: "0 issues." User returns to work.
  30 min later, creates non-compliant view → gentle nudge.
  THE REFLEX IS BORN.
```

### Day-1 Minimum or Uninstall (from BIM Manager research)

1. **Revit stays stable.** No added sync time. No crashes. EVER.
2. **Warning delta with severity triage.** Not counts — "what changed, what matters."
3. **5+ natural language queries with reliable accuracy.** Verified against schedules.
4. **Read-only mode default.** Analysis only, no auto-changes until user opts in.
5. **Works on 500MB workshared model.** Not 20MB demo files.

---

## 6. Implementation Architecture — Roadmap (TARGET STATE)

> **⚠️ Section này mô tả KIẾN TRÚC MỤC TIÊU.** "What Must Be Built" bên dưới
> là engineering work CHƯA HOÀN THÀNH (~14 tuần). "What Already Exists" là shipped.
> Cho current runtime truth, xem `docs/assistant/BASELINE.md`.

### What Already Exists (DO NOT rebuild)

| Asset | Count | Status |
|-------|-------|--------|
| Tool modules | 20 modules, 237 tools | Production-ready |
| Specialist packs | 14 | Configured |
| Command atlas | ~20+ mapped commands | Working |
| Playbooks | 8 playbook definitions | Working |
| WPF dockable pane | Single worker shell | Working |
| WorkerHost | gRPC + HTTP + SSE streaming | Working |
| Named pipe IPC | Agent ↔ WorkerHost | Working |
| Rule engine | Naming, parameters, worksets, compliance | Working |
| Mutation workflow | Preview → Approve → Execute → Verify | Working |
| Session persistence | SQLite + Qdrant | Working |

### What Must Be Built

| Component | Effort | Why It's Critical |
|-----------|--------|-------------------|
| **SQLite BIM Index** | 3 weeks | Foundation for ALL AI features. Local cache of model data. AI queries this, never live Revit API. |
| **IdlingEvent Scanner** | 1 week | Cold-start indexing without UI freeze. 15ms/frame chunked processing. |
| **DMU Dirty Tracking** | 1 week | Real-time change detection. O(changed elements) not O(all elements). |
| **Tiered Rules Engine Wrapper** | 2 weeks | 75% of checks offline/free. Rules first, LLM only when needed. |
| **Model Fingerprint + Context Builder** | 1 week | Smart retrieval. 400 tokens fingerprint, not 35M raw elements. |
| **Warning Delta + Severity Triage** | 2 weeks | "What changed and what matters." BIM Manager's #1 ask. |
| **IRevitContext Abstraction** | 2 weeks | Multi-version support without full migration. |
| **Local Mem0 + Ollama** | 2 weeks | Privacy-safe memory. Fully on-device. |
| **Nice3point RevitTask Adoption** | 3 days | Clean async threading. Minimal risk. |
| **Role-Based Tool Filtering** | 1 week | 25 tools per role, not 192. LLM accuracy from 50% → 85%+ |

**Total: ~14 weeks for production-grade product.**

### What NOT to Build

| Proposal | Why Not |
|----------|---------|
| Auto-scan every 30 minutes | UI freeze + notification spam. Use DMU delta instead. |
| Send full model to LLM | No LLM can hold 200K elements. Use local index + smart retrieval. |
| Replace orchestration with SK | Thread model mismatch, kills offline, token explosion. Use SK only for LLM abstraction. |
| Full Nice3point migration | 6-month risk. Use thin adapter + selective RevitTask only. |
| One-click fix 100+ elements | BIM Manager will NEVER approve. Max 20 items per batch. |
| AI auto-select from 192 tools | 45-60% accuracy = coin flip. Use role-based filtering. |

---

## 7. Memory & Standards — The Retention Engine

### Why Users Can't Leave After 3 Months

**Cursor's lock-in:** .cursorrules + auto-memory = AI knows your codebase better than a new hire. Switching means losing institutional knowledge.

**765T's lock-in (same pattern):**

```
Month 1: User configures firm standards (NCS naming, QC rules)
Month 2: AI learns user patterns (workflow, preferences, approval thresholds)
Month 3: Project memory accumulates (issue history, coordination decisions)
         AI now knows: firm + user + project.
         Switching cost: rebuild all three from scratch.
         Nobody will.
```

### Standards as Code (firm-overlay pattern)

```json
// standards/firm-naming-convention.json
{
  "version": "2.1",
  "rules": [
    {
      "category": "Views",
      "pattern": "^L\\d{2}-[AMSE]-.*",
      "standard": "NCS 7.0",
      "examples": ["L02-M-MEP Plan", "L01-A-Floor Plan"],
      "severity": "error"
    },
    {
      "category": "Sheets",
      "pattern": "^[A-Z]\\d{3}$",
      "standard": "NCS 7.0",
      "examples": ["A101", "M201"],
      "severity": "error"
    }
  ]
}
```

- BIM Manager writes once, team inherits
- AI enforces automatically (Tier 0 rules engine, no LLM needed)
- Per-project overrides with audit trail
- Version-controlled in git

---

## 8. Metrics That Matter

### Product Health

| Metric | Target | How to Measure |
|--------|--------|----------------|
| Day-1 retention | >60% | User opens Revit with 765T loaded next day |
| Week-1 retention | >40% | User sends >5 queries in first week |
| Month-1 retention | >25% | User has standards configured |
| Query accuracy | >90% | Verified against Revit schedules |
| Time-to-first-value | <60 seconds | From file open to first issue found |
| Revit stability | Zero crashes | Zero added sync time |

### Business Case for $50/user/month

```
Pre-milestone QA: 4 hours → 1 hour (saved 3 hours × $65/hr = $195)
Per milestone per coordinator: $195 saved
5 coordinators × 8 milestones/year = $7,800/year saved on QA alone

Naming compliance: 2 hours/week → 10 min/week (saved 90 min × $65/hr)
Annual: $5,070 saved

Knowledge retention: new BIM coordinator onboarding
  Without 765T: 3 months to learn firm standards
  With 765T: standards machine-readable, AI teaches on-the-job
  Value: priceless (prevents standards drift)

Total measurable savings: ~$13,000/year for 5-person team
Cost: $50 × 5 × 12 = $3,000/year
ROI: 4.3x
```

---

## 9. What Makes This Different From Everything Else

```
Navisworks:  "Here are 500 clashes. Good luck."
Solibri:     "Your model violates 47 rules."
BIM Track:   "Here's an issue. Assign it to someone."

765T:        "3 critical issues since your last sync.
              I can fix the naming violations now (preview first).
              The structural coordination issue needs Bob's input —
              I remember he handled a similar case on Project Horizon.
              Want me to draft the RFI?"
```

The difference: **context + memory + agency.** Not just detection — understanding, action, and learning.

---

## 10. Read Order (Updated)

For any agent (human or AI) entering this repo:

| Order | File | What |
|-------|------|------|
| 0 | `CLAUDE.md` | Project guidance (start here for AI agents) |
| 1 | `README.md` | Repo overview, architecture diagram |
| 2 | `AGENTS.md` | Operating constitution, mindset |
| 3 | `docs/765T_PRODUCT_VISION.md` | **THIS FILE — vision, research, implementation** |
| 4 | `docs/ARCHITECTURE.md` | 5-layer architecture |
| 5 | `docs/PATTERNS.md` | Implementation patterns |
| 6 | `docs/assistant/BASELINE.md` | Current runtime truth |
| 7 | `docs/assistant/CONFIG_MATRIX.md` | Config ownership |

---

*This document is the canonical source for product vision and direction only. For runtime truth, see `docs/assistant/BASELINE.md`. For architecture, see `docs/ARCHITECTURE.md`. No single doc is the authority on everything.*
