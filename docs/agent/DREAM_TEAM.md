# 765T Dream Team — 5 Agent Chuyên Biệt (Claude Code Official Format)

> Historical reference only. Current startup/runtime truth lives in `../assistant/*`, `../ARCHITECTURE.md`, and `../PATTERNS.md`.


> *"Một đội ngũ xuất sắc không cần nhiều người — cần đúng người, đúng vai trò, và mỗi người có trí nhớ riêng."*

## Tổng quan

Dream Team là hệ thống **5 agent chuyên biệt** được xây dựng theo **Claude Code Sub-Agents specification**. Mỗi agent có:
- **Persistent memory** (`memory: project`) — trí nhớ riêng qua `.assistant/agent-memory/<name>/MEMORY.md`
- **Scoped tools** — chỉ access tools phù hợp vai trò
- **Permission mode** — `plan` cho review-only agents, `acceptEdits` cho implementation agents
- **Domain knowledge** — preloaded context từ `docs/agent/skills/`

**Orchestrator** (Claude Code main thread) điều phối toàn bộ routing giữa các agents.

```
┌───────────────────────────────────────────────────────────────────────┐
│                       765T DREAM TEAM v3                              │
│              Claude Code Sub-Agent Architecture                       │
│                                                                       │
│   ┌───────────────────────────────────────────────────────────┐      │
│   │  🧠 ORCHESTRATOR (Main Claude Code Thread)                 │      │
│   │  Routes tasks, manages context, coordinates pipelines      │      │
│   └──────────────┬──────────┬──────────┬──────────┬──────────┘      │
│                  │          │          │          │                   │
│     ┌────────────▼──┐  ┌───▼──────┐  ┌▼─────────┐  ┌▼──────────┐   │
│     │ 🎯 AGENT 1    │  │ 🔧 AGT 2 │  │ 🎨 AGT 5 │  │ 🔍 AGT 3  │   │
│     │ BIM Manager   │  │ Revit API│  │ Revit UI │  │ Research & │   │
│     │ Pro           │  │ Developer│  │ Engineer │  │ Frontend   │   │
│     │               │  │          │  │          │  │            │   │
│     │ permMode:plan │  │ permMode:│  │ permMode:│  │ permMode:  │   │
│     │ tools: R/G/W  │  │ acceptE  │  │ acceptE  │  │ plan       │   │
│     │ memory:project│  │ tools:ALL│  │ tools:ALL│  │ tools:R/G/W│   │
│     └───────────────┘  └──────────┘  └──────────┘  └────────────┘   │
│                                                                       │
│     ┌───────────────┐                                                │
│     │ 🚀 AGENT 4    │  Legend:                                       │
│     │ Marketing &   │  R = Read, G = Glob/Grep, W = web_search/fetch│
│     │ Repo Manager  │  ALL = Read/Edit/Write/Bash/Glob/Grep/web     │
│     │               │  permMode:plan = read-only exploration         │
│     │ tools: ALL    │  permMode:acceptE = can edit/write code        │
│     │ memory:project│  memory:project = persistent per-agent memory  │
│     └───────────────┘                                                │
│                                                                       │
│   ┌───────────────────────────────────────────────────────────┐      │
│   │  💾 PERSISTENT MEMORY (Built-in Claude Code)              │      │
│   │  .assistant/agent-memory/                                     │      │
│   │  ├── bim-manager-pro/MEMORY.md                            │      │
│   │  ├── revit-api-developer/MEMORY.md                        │      │
│   │  ├── revit-ui-engineer/MEMORY.md                          │      │
│   │  ├── research-frontend-organizer/MEMORY.md                │      │
│   │  └── marketing-repo-manager/MEMORY.md                     │      │
│   └───────────────────────────────────────────────────────────┘      │
│                                                                       │
│   Pipelines (orchestrated by main thread):                           │
│   Feature:  A1→A3(research)→A5(XAML)+A2(backend)→A4(release)        │
│   Bug Fix:  A2(diagnose)→A1(review)→A2+A5(fix)→A4(commit)           │
│   Content:  A3(research)→A1(validate)→A4(publish)                    │
│   UI:       A3(wireframe)→A5(XAML)→A2(binding)→A1(review)→A4(commit)│
└───────────────────────────────────────────────────────────────────────┘
```

---

## YAML Frontmatter Summary (Official Format)

| Agent | `name` | `model` | `memory` | `permissionMode` | `tools` |
|-------|--------|---------|----------|-------------------|---------|
| Agent 1 | bim-manager-pro | sonnet | project | plan | Read, Glob, Grep, web_search, web_fetch, Context7 |
| Agent 2 | revit-api-developer | sonnet | project | acceptEdits | Read, Edit, Write, Bash, Glob, Grep, web_search, web_fetch, Context7 |
| Agent 5 | revit-ui-engineer | sonnet | project | acceptEdits | Read, Edit, Write, Bash, Glob, Grep, web_search, web_fetch, Context7 |
| Agent 3 | research-frontend-organizer | sonnet | project | plan | Read, Glob, Grep, web_search, web_fetch, Context7 |
| Agent 4 | marketing-repo-manager | sonnet | project | — | Read, Edit, Write, Bash, Glob, Grep, web_search, web_fetch |

---

## Agent 1: BIM Manager Pro 🎯

| Thuộc tính | Giá trị |
|-----------|---------|
| **File** | `.assistant/agents/bim-manager-pro.md` |
| **Model** | sonnet |
| **Memory** | `.assistant/agent-memory/bim-manager-pro/MEMORY.md` |
| **Permission** | `plan` (read-only, cannot edit code) |
| **Vai trò** | Team Lead / BIM Strategist / Quality Gate |

### Tools có quyền dùng
`Read`, `Glob`, `Grep`, `web_search`, `web_fetch`, `Context7` — **Không có Edit/Write/Bash**

### Chuyên môn
- BIM Execution Planning (BEP), LOD 100→500
- Tiêu chuẩn ISO 19650, NBS, Singapore BIM Guide, TCVN/QCVN
- Clash detection, model coordination, construction sequencing
- MEP/Structural coordination, CDE workflows

### Trách nhiệm chính
1. **Định hướng chiến lược BIM** cho toàn bộ platform
2. **Quality Gate** — review và approve mọi kế hoạch trước khi thực hiện
3. **Domain Translation** — bridge giữa kiến thức xây dựng và yêu cầu phần mềm
4. **Thiết kế workflow** end-to-end
5. **Đánh giá rủi ro** từ góc độ ngành xây dựng

### Memory ghi nhớ gì
- Các quyết định standards đã approved/rejected
- BIM strategies cho từng project phase
- Review history và rationale

---

## Agent 2: Revit API Developer 🔧

| Thuộc tính | Giá trị |
|-----------|---------|
| **File** | `.assistant/agents/revit-api-developer.md` |
| **Model** | sonnet |
| **Memory** | `.assistant/agent-memory/revit-api-developer/MEMORY.md` |
| **Permission** | `acceptEdits` (can edit/write code) |
| **Vai trò** | Technical Powerhouse / API Expert |

### Tools có quyền dùng
`Read`, `Edit`, `Write`, `Bash`, `Glob`, `Grep`, `web_search`, `web_fetch`, `Context7` — **Full code access**

### Chuyên môn
- Revit API (2020–2025+): DB, UI, MEP, Structure, Export
- C# / .NET Framework 4.8 (add-in) / .NET 8 (services)
- Transaction management, ExternalEvent, FilteredElementCollector
- Service Bundles pattern, partial class validators

### Trách nhiệm chính
1. **Nghiên cứu API** — deep-dive Revit SDK
2. **Phát triển tool** C# theo kiến trúc 765T
3. **Script automation** — PowerShell scripts
4. **Performance engineering** — tối ưu Revit API calls

### Memory ghi nhớ gì
- API gotchas và version-specific behaviors
- FilteredElementCollector optimization patterns
- Transaction strategies và rollback patterns

---

## Agent 3: Research & Frontend Organizer 🔍

| Thuộc tính | Giá trị |
|-----------|---------|
| **File** | `.assistant/agents/research-frontend-organizer.md` |
| **Model** | sonnet |
| **Memory** | `.assistant/agent-memory/research-frontend-organizer/MEMORY.md` |
| **Permission** | `plan` (read-only, cannot edit code) |
| **Vai trò** | User Advocate / UX Strategist |

### Tools có quyền dùng
`Read`, `Glob`, `Grep`, `web_search`, `web_fetch`, `Context7` — **Không có Edit/Write/Bash**

### Chuyên môn
- UX Research: workflow observation, task analysis, competitive benchmarking
- Feature prioritization & value proposition design
- BIM user persona mapping (5 personas)

### Trách nhiệm chính
1. **Nghiên cứu người dùng** — tìm 80/20 pain points
2. **Phân tích đối thủ** — pyRevit, DiRoots, BIMOne, Ideate, CTC
3. **Feature discovery** — user needs → feature proposals
4. **Frontend design** — wireframes cho Worker shell

### Memory ghi nhớ gì
- User pain points và workflow observations
- Competitive landscape data
- Feature proposal history với priority scores

---

## Agent 4: Marketing & Repo Manager 🚀

| Thuộc tính | Giá trị |
|-----------|---------|
| **File** | `.assistant/agents/marketing-repo-manager.md` |
| **Model** | sonnet |
| **Memory** | `.assistant/agent-memory/marketing-repo-manager/MEMORY.md` |
| **Permission** | default (standard) |
| **Vai trò** | DevOps Lead / Marketing Specialist |

### Tools có quyền dùng
`Read`, `Edit`, `Write`, `Bash`, `Glob`, `Grep`, `web_search`, `web_fetch` — **Full access trừ git push**

### Chuyên môn
- Git workflow, Conventional Commits, release engineering
- Web development, documentation sites
- Technical marketing, content creation, community

### Trách nhiệm chính
1. **Repository health** — commit history, branches, PRs
2. **Release pipeline** — versioning, changelog, packaging
3. **Content marketing** — announcements, tutorials
4. **Documentation** — keep docs up-to-date

### Memory ghi nhớ gì
- Release milestones và commit patterns
- Marketing content plan
- Repo health metrics

---

## Agent 5: Revit UI Engineer 🎨

| Thuộc tính | Giá trị |
|-----------|---------|
| **File** | `.assistant/agents/revit-ui-engineer.md` |
| **Model** | sonnet |
| **Memory** | `.assistant/agent-memory/revit-ui-engineer/MEMORY.md` |
| **Permission** | `acceptEdits` (can edit/write code) |
| **Vai trò** | UI Craftsman / WPF Specialist |

### Tools có quyền dùng
`Read`, `Edit`, `Write`, `Bash`, `Glob`, `Grep`, `web_search`, `web_fetch`, `Context7` — **Full code access**

### Chuyên môn
- WPF/XAML layout, styling, animations, custom controls
- MVVM architecture (ViewModel, ICommand, data binding)
- Revit Dockable Pane integration, UI threading
- Accessibility (WCAG 2.1), virtualization

### Trách nhiệm chính
1. **XAML Implementation** — biến wireframe thành production code
2. **ViewModel Architecture** — kết nối UI với backend
3. **Component Library** — reusable UI components
4. **Theme System** — light/dark mode

### Tại sao cần Agent riêng?
Agent 2 chuyên **backend** Revit API. Agent 3 chuyên **design** UX. Agent 5 bridge gap — nhận wireframe từ Agent 3, nhận API từ Agent 2, rèn thành giao diện production.

### Memory ghi nhớ gì
- WPF threading solutions
- Component library evolution
- Theme/styling decisions

---

## Orchestrator Routing Rules

**Orchestrator (main Claude Code thread)** điều phối routing. Sub-agents KHÔNG thể gọi nhau trực tiếp — chỉ báo orchestrator "cần hỗ trợ từ agent X".

### Routing by domain:
| Keyword/Domain | Route to | Reason |
|----------------|----------|--------|
| LOD, BIM standards, coordination, clash | bim-manager-pro | Domain strategy |
| Revit API, C#, transaction, tool dev | revit-api-developer | Backend code |
| XAML, WPF, ViewModel, theme, UI code | revit-ui-engineer | Frontend code |
| User research, wireframe, competitive | research-frontend-organizer | UX design |
| Commit, release, website, PR, docs | marketing-repo-manager | DevOps/marketing |
| Tier 2 mutation approval | bim-manager-pro | Quality gate |

### Parallel execution:
Orchestrator CAN launch multiple agents in parallel when tasks are independent:
```
Phase 1 (parallel): bim-manager-pro + research-frontend-organizer
Phase 2 (sequential): revit-api-developer → revit-ui-engineer
Phase 3 (sequential): marketing-repo-manager
```

---

## Workflow Pipelines

### 🔵 Feature Pipeline
```
bim-manager-pro    → "Tính năng cần thiết, LOD300+, follow ISO 19650"
  ↓
research-frontend  → "Users mất 30 phút/ngày, đây là wireframe"
  ↓
revit-api-dev      → "Tool built, transaction-safe, tests pass"
  +
revit-ui-engineer  → "XAML implemented, ViewModel bound, accessible"
  ↓
marketing-repo     → "Committed, changelog updated, PR created"
```

### 🔴 Bug Fix Pipeline
```
revit-api-dev      → "Root cause: race condition trong ExternalEvent"
  ↓
bim-manager-pro    → "Fix không ảnh hưởng coordination, approved"
  ↓
revit-api-dev + revit-ui-engineer → "Fixed, tests added"
  ↓
marketing-repo     → "fix(agent): resolve race condition"
```

---

## Memory Architecture (Built-in Claude Code)

### How it works
Mỗi agent có `memory: project` trong YAML frontmatter → Claude Code tự động:
1. Tạo `.assistant/agent-memory/<agent-name>/MEMORY.md`
2. Auto-load first 200 lines khi agent khởi động
3. Agent có thể read/write memory directory
4. Memory **persist across sessions** — agent nhớ từ phiên trước

### Migration from old system
| Old (v2) | New (v3) |
|----------|----------|
| `.assistant/souls/*.soul.json` | `.assistant/agent-memory/<name>/MEMORY.md` |
| `.assistant/config/agent-memory-architecture.json` | Built-in `memory: project` field |
| Custom Qdrant collections | Claude Code native memory |
| `agent-router.json` | Orchestrator routing (main thread) |

### Ưu điểm của built-in memory
- ✅ Zero config — hoạt động ngay
- ✅ Human-readable (MEMORY.md)
- ✅ Version-controlled (git)
- ✅ Per-agent isolation
- ✅ Auto-loaded at startup
- ✅ No external dependencies (no Qdrant required)

---

## Files

```
.assistant/agents/
├── bim-manager-pro.md              ← Agent 1 (memory: project, permissionMode: plan)
├── revit-api-developer.md          ← Agent 2 (memory: project, permissionMode: acceptEdits)
├── revit-ui-engineer.md            ← Agent 5 (memory: project, permissionMode: acceptEdits)
├── research-frontend-organizer.md  ← Agent 3 (memory: project, permissionMode: plan)
├── marketing-repo-manager.md       ← Agent 4 (memory: project)
└── plan-schema.json                ← Execution plan schema

.assistant/agent-memory/               ← Auto-created by Claude Code
├── bim-manager-pro/MEMORY.md       ← Persistent BIM strategy memory
├── revit-api-developer/MEMORY.md   ← Persistent API patterns memory
├── revit-ui-engineer/MEMORY.md     ← Persistent UI patterns memory
├── research-frontend-organizer/MEMORY.md ← Persistent UX research memory
└── marketing-repo-manager/MEMORY.md     ← Persistent repo/marketing memory

docs/agent/
├── DREAM_TEAM.md                   ← This documentation
└── personas/*.json                 ← Worker shell personas (runtime, not Claude Code)
```

---

*Dream Team v3 — Claude Code Official Sub-Agent Format. Ngày 2026-03-21.*
*Built-in persistent memory per agent. Scoped tools. Permission modes.*