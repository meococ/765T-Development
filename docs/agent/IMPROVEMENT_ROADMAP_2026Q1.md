# BIM765T Improvement Roadmap — 2026 Q1
> Generated: 2026-03-21 | Sources: 7 parallel agent research passes (118 tool uses total) + verified GitHub data

---

## Executive Summary

BIM765T Revit Agent is **architecturally mature** with 254 tool definitions, strong family/MEP/QC coverage, and a solid gRPC/named-pipe control plane. **Overall backend score: 8.0/10** (architecture compliance 9.2/10). The primary gaps are:
1. **Frontend** — web app is a single-file visual prototype with zero live functionality
2. **Backend gaps** — workset write, annotation/tag, link management, revision tools missing
3. **AI integration UX** — no streaming chat, no approval gate UI, no live monitoring
4. **Backend bugs found** — ILlmClient unwired, HashEmbedding misrepresented as semantic, pipe timeout missing

---

## Part 0: Backend Code Review — Critical Findings (Score: 8.0/10)

### Architecture Compliance: 9/9 rules pass
All documented boundaries enforced: UI→InternalToolClient only, mutations respect preview/approval, contracts append-only, WorkerHost has no Revit-bound logic.

### P1 — Functional Gaps (Must Fix)

| ID | File | Issue |
|----|------|-------|
| P1-1 | `ScriptOrchestrationService.cs:236` | **Inline C# script execution NOT implemented** — `script.run_safe` with InlineCode always returns `SCRIPT_INLINE_NOT_SUPPORTED`. Tool is registered in catalog but does nothing. |
| P1-2 | `Copilot.Core` | **`ILlmClient` has no concrete implementation** — entire intent classification is keyword/regex only. No LLM call path exists. System is 100% rule-based. |
| P1-3 | `HashEmbeddingClient.cs` | **Hash vectors are NOT semantic embeddings** — Qdrant receives meaningless hash-bag-of-words vectors. Cosine similarity on these = random results. Misleading as "semantic search". |

### P2 — Quality Issues

| ID | File:Line | Issue |
|----|-----------|-------|
| P2-1 | `GenericMutationServices.cs:1264` | Adaptive family placement throws `NotSupportedException` — not surfaced in manifest |
| P2-2 | `MissionOrchestrator.cs:200-214` | Snapshot serialized on EVERY event append — expensive for long workflows |
| P2-3 | `WorkerService.cs:195-199` | Playbook summary double-injected into responseText for sheet_authoring |
| P2-4 | `KernelPipeHostedService.cs:100` | **No per-connection read timeout** — silent client holds thread forever |
| P2-5 | `SqliteMissionEventStore.cs:519-540` | LIKE search on payload_blob with no FTS5 index — degrades at scale |
| P2-6 | `RuntimeHealthService.cs:78-80` | Qdrant unreachable = Degraded=true even though SQLite fallback is fully active |

### P3 — Improvements

| ID | Issue |
|----|-------|
| P3-1 | `IntentClassifier.cs`: Rule ordering — "delete family" routes to analysis, not mutation |
| P3-2 | `ToolExecutionCore.cs:250`: Regex captures only first Count/Score — incomplete journal |
| P3-3 | `FileLogger.cs`: No log rotation/retention policy |
| P3-4 | `McpHost`: tools/list always makes kernel roundtrip — add TTL cache |
| P3-5 | `McpMessageProtocol.cs:18`: Dead code from prior line-framed protocol |
| P3-6 | `AgentHost.cs:220`: title-keyed docs fragile for duplicate unsaved docs |
| P3-7 | `MemoryOutboxProjectorService.cs:175`: BuildSnippet extracts only first JSON property — low quality |
| P3-8 | `AgentPaneViewModel.cs`: Empty stub file — confusing for new devs |

### Production Readiness Scores

| Dimension | Score |
|-----------|-------|
| Architecture compliance | 9.2/10 |
| Code quality | 8.1/10 |
| Production readiness | 7.4/10 |
| Completeness | 7.8/10 |
| Test coverage | 7.5/10 |
| **Overall** | **8.0/10** |

---

## Part 1: Backend — Gap Analysis Scorecard

| Category | Coverage | Key Gaps |
|----------|----------|----------|
| Model QC / Audit | **85%** | Room/space completeness, level/grid consistency |
| Clash Detection | **70%** | Issue tracking write-back, clash grouping |
| Schedule Management | **75%** | Schedule diff/compare, batch create |
| Sheet Setup | **80%** | Revision management, title block bulk |
| Family Management | **90%** | Best-covered domain (authoring benchmark) |
| View Management | **75%** | Crop/range, browser org, bulk properties |
| Export / Reports | **65%** | Image export, NWC, COBie |
| Parameter Management | **80%** | Global parameters, type-param batch |
| Workset Management | **30%** | Critical gap — write ops almost absent |
| Annotation / Tags | **20%** | Largest gap — tags, dims, keynotes |
| Collaboration / Links | **25%** | Only status/SWC — no reload/relink |
| MEP Systems | **55%** | Penetration deep; connectivity missing |

### Priority Gaps (P0 — Production Blockers)

| ID | Tool | Complexity | Wave |
|----|------|-----------|------|
| G01 | `workset.bulk_reassign_elements_safe` | M | 1 |
| G02 | `workset.create_safe` | S | 1 |
| G03 | `annotation.place_tag_safe` | L | 2 |
| G04 | `view.set_crop_region_safe` + `view_range` | M | 1 |
| G05 | `schedule.compare` | M | 1 |
| G06 | `parameter.batch_set_type_params_safe` | M | 1 |
| G07 | `annotation.place_room_tag_safe` | M | 2 |
| G10 | `revision.create_safe` + sequence | M | 1 |
| G12 | `link.reload/unload/relink` | M | 2 |
| G13 | `audit.room_space_completeness` | M | 2 |

### Implementation Waves

**Wave 1 — "Fill Critical Production Holes"** (2-3 weeks)
- G01+G02+G08: Workset CRUD (create, open/close, bulk reassign)
- G04: View crop region + view range
- G05: Schedule compare/diff
- G06: Parameter batch type-set
- G10: Revision create + sequence

**Wave 2 — "Complete Coordination Workflow"** (3-4 weeks)
- G03+G07: Annotation tag placement (room, door, window, pipe)
- G12: Link reload/unload/relink
- G13+G14: Room/level/grid audit
- G17: Family batch reload
- G18: MEP system connectivity

**Wave 3 — "Enhance Production Quality"** (2-3 weeks)
- G09: View property bulk-set
- G11: Image export (PNG/JPEG)
- G15: Schedule batch create from template
- G16: Global parameters
- G29: Family rename batch

---

## Part 2: Frontend — Complete Redesign Plan

### Current State
- Single `App.tsx` (867 lines) — zero component decomposition
- Beautiful dark theme (zinc/indigo) — visual language is **excellent**
- Zero live functionality — all mock data, no API calls
- `@google/genai` imported but never called
- No routing, no state management, no real code editor

### Target Architecture

```
Browser (React SPA)
    ↕ HTTP/SSE (localhost:5290)
BFF REST Gateway (ASP.NET Minimal API)
    ↕ gRPC over named pipe
WorkerHost (.NET 8)
    ↕ private kernel pipe
Revit Agent (net48)
```

### Tech Stack (Keep + Add)

**Keep:** React 19, TypeScript 5.8, Vite 6, Tailwind v4, motion/react, lucide-react

**Add:**
| Package | Purpose |
|---------|---------|
| shadcn/ui | Component primitives (Sidebar, DataTable, Badge, Sheet, Command) |
| @tanstack/react-query v5 | Server state (health, tasks, catalog polling) |
| @tanstack/react-router | Type-safe multi-page routing |
| zustand | Lightweight global UI state |
| @ai-sdk/react | `useChat` hook for streaming AI chat |
| @monaco-editor/react | Real code editor (replace fake regex highlight) |
| recharts | Metrics and charts (via shadcn Charts) |

**Remove/Replace:**
| Current | Replace With |
|---------|-------------|
| `@google/genai` (browser-side) | `@ai-sdk/react` + backend proxy |
| `express` in dependencies | Separate BFF or ASP.NET WebGateway |
| Fake regex syntax highlight | Monaco Editor |

### Page Structure

```
AppShell (Sidebar + Router Outlet)
├── /dashboard    → SystemStatus, ActiveModel, MissionStage, Metrics
├── /chat         → 3-pane: SessionList | MessageList+Input | CodePanel
├── /tasks        → DataTable with live status, RiskTier badges
├── /evidence/:id → Execution trace, element diffs, verification
├── /tools        → Tool catalog browser with risk/category filters
└── /settings     → Connection config, preferences
```

### Implementation Phases

**Phase 1 — Foundation** (no backend needed)
1. Split App.tsx into component files
2. Add TanStack Router (real pages)
3. Initialize shadcn/ui + design system
4. Replace fake code panel with Monaco Editor
5. Fix index.html title, clean package.json

**Phase 2 — Live Chat** (requires BFF)
1. Build ASP.NET WebGateway (BFF)
2. Integrate `useChat` with streaming
3. Tool call card components
4. Approval gate UI (Preview → Approve/Reject)

**Phase 3 — Dashboard & Operations**
1. Health polling (WorkerHost, pipe, Qdrant)
2. Task queue table with live updates
3. Evidence viewer with execution trace

**Phase 4 — Advanced**
1. Tool catalog browser from live registry
2. Mission DAG visualizer
3. Memory/Qdrant search UI
4. Context drift detection banner

---

## Part 3: Key Patterns from Research

### Verified GitHub Repos

| Repo | Stars | Key Pattern for BIM765T |
|------|-------|------------------------|
| RevitLookup | 1.3k | Service architecture, UI patterns |
| pyRevit | 1.7k | Multi-language RAD, extension marketplace |
| Nice3point/RevitTemplates | 395 | Multi-target, WPF/MVVM, DI, Serilog |
| Nice3point/RevitToolkit | 129 | AsyncEventHandler, Context class, DockablePane |
| mcp-server-for-revit-python | 98 | MCP → pyRevit Routes → Revit API chain |
| Autodesk aps-mcp-server | 3 | Official C# MCP for cloud Revit automation |

### Top 5 Patterns to Adopt

1. **AsyncEventHandler** (RevitToolkit) — `RaiseAsync()` for safe Revit thread access
2. **Static Context class** — `Context.ActiveDocument`, `Context.ActiveView` from anywhere
3. **Transaction wrapper with auto-rollback** — prevents model corruption on agent errors
4. **MCP tool chain** — `LLM → MCP → HTTP → Revit API` standardized pipeline
5. **Dual JSON pattern** (Autodesk) — separate context JSON from tool input JSON

### AI Integration Architecture

```
Recommended: Agentic Loop Pattern
User Goal → LLM Planning → Tool Selection → Execution → Validation → Feedback
                                ↕
                    BIM765T WorkerHost (gRPC)
                                ↕
                    Revit Kernel (ExternalEvent)
```

**WebView2 + React in Revit dockable pane** is emerging as industry standard:
- WPF container → WebView2 control → React app
- Communication: `postMessage` API + named pipes
- Benefits: modern web UI, code reuse, faster iteration

---

## Part 4: Recommended Execution Order

### Sprint 1 (Week 1-2): Frontend Foundation
- [ ] Split App.tsx into components
- [ ] Add routing (TanStack Router)
- [ ] Initialize shadcn/ui
- [ ] Add Monaco Editor
- [ ] Create BFF scaffold (ASP.NET Minimal API)

### Sprint 2 (Week 3-4): Backend Wave 1
- [ ] Workset CRUD tools (G01, G02, G08)
- [ ] View crop/range tools (G04)
- [ ] Schedule compare (G05)
- [ ] Revision management (G10)

### Sprint 3 (Week 5-6): Live Chat Integration
- [ ] BFF ↔ WorkerHost gRPC connection
- [ ] useChat streaming integration
- [ ] Tool call cards + approval gates
- [ ] Health monitoring dashboard

### Sprint 4 (Week 7-8): Backend Wave 2
- [ ] Annotation tag placement (G03, G07)
- [ ] Link management (G12)
- [ ] Room/level/grid audit (G13, G14)
- [ ] Family batch reload (G17)

### Sprint 5 (Week 9-10): Polish & Advanced
- [ ] Task queue page with live status
- [ ] Evidence viewer
- [ ] Tool catalog browser
- [ ] Image export (G11)
- [ ] Parameter batch type-set (G06)

---

## Appendix: Architecture Decisions

### BFF Choice: ASP.NET Minimal API > Node.js Express
**Rationale:**
- Zero DTO translation — reuse `BIM765T.Revit.Contracts` directly
- gRPC-to-gRPC on localhost = near-zero overhead
- Team is already in .NET ecosystem
- Kestrel on localhost with CORS = trivial React dev connection

### UI in Revit: WebView2 + React > Pure WPF
**Rationale:**
- Code reuse between web app and dockable pane
- Modern UI/UX (streaming chat, rich cards)
- Faster iteration (hot reload vs XAML compile)
- Microsoft officially supports WebView2 in desktop apps

### State Management: Zustand + TanStack Query > Redux
**Rationale:**
- Minimal boilerplate for UI state (sidebar, theme, selections)
- TanStack Query handles all server state (polling, cache, refetch)
- useChat handles AI chat state
- No need for Redux complexity

---

## Part 5: Revit Dockable Pane UI — Review & Recommendations

### Current State (from UI Engineer review, 31 tool uses)

**Architecture:** Shell v2 with 7 tabs, code-behind (no XAML bindings), `InternalToolClient` as sole Revit API gateway.

| Aspect | Score | Notes |
|--------|-------|-------|
| Token system (AppTheme) | ⭐⭐⭐⭐⭐ | Tailwind-inspired dark palette, frozen brushes, 4px grid |
| Factory (UIFactory) | ⭐⭐⭐⭐⭐ | 25+ factory methods, RichCard/TerminalBlock/TagBadge |
| Animation (AnimationHelper) | ⭐⭐⭐⭐⭐ | Pure WPF, no 3rd party |
| Architecture safety | ⭐⭐⭐⭐⭐ | No tab touches Revit API directly |
| MVVM compliance | ⭐⭐ | Deliberately code-behind (documented trade-off) |
| Testability | ⭐⭐ | Code-behind = needs Revit process to test |

### Key UI Bugs Found

| Bug | Location | Fix |
|-----|----------|-----|
| `_messagePanel` cleared on every render | WorkerTab | Switch to append-only |
| DispatcherTimer not stopped on Unloaded | ActivityTab, EvidenceTab | Add `Unloaded` handler |
| No cancellation on tab navigation | WorkerTab | Add CancellationTokenSource per tab lifecycle |
| ApprovalCard missing RiskTier/Confidence | WorkerTab | Wire existing contract fields to UI |

### Architecture Decision: Pure WPF > WebView2

| Factor | Pure WPF | WebView2+React |
|--------|----------|----------------|
| Revit compatibility | ✅ Proven | ⚠️ Untested enterprise |
| Memory overhead | ~5MB | +150-200MB Chromium |
| Cold start | Instant | 800ms-1.5s |
| Revit theming | ✅ Native | ⚠️ Separate CSS |

**Verdict:** Stay pure WPF. WebView2 only for EvidenceTab artifact viewer (optional).

### New Components Needed

| Component | Priority | Purpose |
|-----------|----------|---------|
| `StreamingMessageBubble` | High | Append tokens in real-time |
| `QueueStatusPanel` | High | Live queue strip (active tool + pending) |
| `ApprovalCard v2` | High | RiskTier + Confidence + RecoveryHint + diff |
| `ContextPill` | Medium | Always-visible doc/view/selection strip |
| `ThinkingBlock` | Medium | Animated reasoning steps during tool chains |
| `ArtifactCard` | Medium | Inline artifact chip in chat |

### CommunityToolkit.Mvvm — Targeted Adoption
- Use for **WorkerTab only** (most complex state: sessions, streaming, approvals)
- Keep all other tabs as code-behind per PATTERNS.md §7
- CommunityToolkit.Mvvm 8.x supports net48 ✅
