# 765T Agentic BIM OS

> AI agent vận hành trực tiếp bên trong Autodesk Revit thông qua kiến trúc local an toàn.

![Build](https://img.shields.io/badge/build-passing-brightgreen)
![Tests](https://img.shields.io/badge/tests-250%20passed-brightgreen)
![.NET](https://img.shields.io/badge/.NET-4.8%20%7C%208.0-blue)
![Revit](https://img.shields.io/badge/Revit-2024%20%7C%202026-orange)
![License](https://img.shields.io/badge/license-proprietary-lightgrey)

## Tong Quan

`765T Agentic BIM OS` la mot he thong AI agent chay trong Autodesk Revit, cho phep AI assistant tuong tac truc tiep voi mo hinh BIM thong qua cac cong cu co kiem soat. He thong hoat dong hoan toan local — khong gui du lieu mo hinh len cloud.

**Demo use-case chinh:** Mot AI agent (Claude Code, Cursor, hoac bat ky MCP client nao) ket noi vao Revit 2026 qua named pipes va MCP protocol, doc mo hinh, phan tich, va thuc hien tac vu BIM — tat ca dieu khien tu IDE.

## Kien Truc

```text
IDE (Claude Code / Cursor / VS Code)
          |
          v
    MCP Protocol (stdio)
          |
          v
    BIM765T.Revit.McpHost ─── JSON-RPC bridge
          |
          v
    BIM765T.Revit.WorkerHost ─── Control plane (net8.0)
    - AI orchestration          - HTTP / gRPC / SSE
    - Memory projection         - SQLite + Qdrant
    - External AI gateway       - LLM routing
          |
          v
    Named-pipe kernel channel
          |
          v
    BIM765T.Revit.Agent ─── Execution kernel (net48)
    - Revit API boundary        - 237 guarded tools
    - ExternalEvent scheduler   - Preview/Approve/Execute flow
    - WPF dockable pane         - Operation journal
```

**Ranh gioi quan trong:** Chi duy nhat `BIM765T.Revit.Agent` duoc phep goi Revit API. Rang buoc nay duoc kiem tra tu dong boi Architecture Tests.

## Projects

| Project | Framework | Vai Tro |
| --- | --- | --- |
| `BIM765T.Revit.Agent` | net48 / WPF | Revit add-in, execution kernel, 237 guarded tools |
| `BIM765T.Revit.WorkerHost` | net8.0 | Control plane, AI orchestration, memory, external AI gateway |
| `BIM765T.Revit.Bridge` | net8.0 | CLI bridge qua named pipes |
| `BIM765T.Revit.McpHost` | net8.0 | MCP stdio adapter — cho phep IDE ket noi vao Revit |
| `BIM765T.Revit.Copilot.Core` | netstandard2.0 | AI services, LLM routing, pack management |
| `BIM765T.Revit.Contracts` | netstandard2.0 | Shared DTOs va contracts |
| `BIM765T.Revit.Agent.Core` | netstandard2.0 | Core logic tach khoi Revit API dependency |

## Tinh Nang Chinh

- **237 guarded tools** across 14 specialist packs — doc, sua, phan tich mo hinh BIM
- **Mutation safety flow:** Preview -> Approval -> Execute -> Verify cho moi thao tac nguy hiem
- **LLM provider cascade:** OpenRouter -> MiniMax -> OpenAI -> Anthropic (first-found-wins)
- **Conversational fast-path:** 7 intent categories tra loi trong 1-3 giay thay vi 5-18 giay
- **Semantic memory:** SQLite (durable) + Qdrant (vector search) + Ollama fallback
- **MCP integration:** Bat ky AI IDE nao ho tro MCP deu ket noi duoc vao Revit
- **Centralized timeout config:** `LlmTimeoutProfile` thong nhat timeout/token across toan bo codebase

## Demo: AI Agent Tuong Tac Voi Revit 2026

He thong nay cho phep ban:

1. **Mo Revit 2026** voi mo hinh BIM bat ky
2. **Mo IDE** (VS Code + Claude Code extension, Cursor, hoac terminal)
3. **Ket noi qua MCP** — IDE tu dong nhan dien Revit context
4. **Ra lenh bang ngon ngu tu nhien:**
   - "Liet ke tat ca cac Wall trong view hien tai"
   - "Kiem tra model co warning nao khong"
   - "Tao mot Floor Plan moi cho tang 2"
   - "Phan tich thong ke vat lieu"

AI agent doc Revit context, lap ke hoach, xin duyet (neu can), va thuc thi — tat ca trong IDE.

## Quick Start

### 1. Build

```powershell
dotnet build BIM765T.Revit.Agent.sln -c Release
```

### 2. Setup AI Provider

```powershell
# Script tuong tac (khuyen dung)
.\tools\infra\setup_ai_providers.ps1

# Hoac truyen key truc tiep
.\tools\infra\setup_ai_providers.ps1 -Provider openrouter -OpenRouterKey "sk-or-..."
```

### 3. Deploy Revit Add-in

```powershell
powershell -ExecutionPolicy Bypass -File .\src\BIM765T.Revit.Agent\deploy\install-addin.ps1
```

### 4. Start WorkerHost

```powershell
dotnet run --project src/BIM765T.Revit.WorkerHost -c Release
```

### 5. Ket Noi Tu IDE

Cau hinh MCP client cua IDE tro den `BIM765T.Revit.McpHost.exe`:

```json
{
  "mcpServers": {
    "revit-agent": {
      "command": "path/to/BIM765T.Revit.McpHost.exe"
    }
  }
}
```

## Test

```powershell
# Chay tat ca tests
dotnet test BIM765T.Revit.Agent.sln -c Release

# Ket qua: 250 passed, 0 failed
```

## Trang Thai Hien Tai

| Component | Status |
| --- | --- |
| Kernel + 237 tools | Production-ready |
| Named pipe IPC | Production-ready |
| WorkerHost (HTTP/gRPC/SSE) | Production-ready |
| MCP bridge | Production-ready |
| CLI bridge | Production-ready |
| Conversational fast-path | Shipped |
| Semantic memory (Qdrant) | Shipped |
| Centralized config | Shipped |
| WPF chat pane | Alpha (disabled by default — `EnableUiPane = false`) |
| Standalone chat (no Revit) | In progress |
| Role-based tool filtering | Planned |

## Tai Lieu

| Tai lieu | Noi dung |
| --- | --- |
| `CLAUDE.md` | Huong dan cho AI agent lam viec voi repo |
| `AGENTS.md` | Hien phap van hanh |
| `docs/ARCHITECTURE.md` | Kien truc he thong va ranh gioi |
| `docs/PATTERNS.md` | Patterns va mutation safety flow |
| `docs/assistant/BASELINE.md` | Runtime truth hien tai |
| `docs/assistant/CONFIG_MATRIX.md` | Config ownership matrix |
| `docs/QUICKSTART_AI_TESTING.md` | Huong dan test AI end-to-end |
| `docs/INDEX.md` | Navigator toan bo tai lieu |

## License

Proprietary. Built by the MII development team.
