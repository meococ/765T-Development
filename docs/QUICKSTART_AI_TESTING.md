# Quick Start: AI Testing cho BIM765T Revit Agent

Huong dan ket noi AI vao Revit UI de test end-to-end.

## Kien truc tong quan

```
Revit UI (Chat)
  -> HTTP POST localhost:50765
    -> WorkerHost (Mission Orchestrator)
      -> Named Pipe -> Revit Kernel
        -> LLM Planning Service (OpenRouter/OpenAI/Anthropic)
        -> Execute BIM commands
      <- Response + Events
    <- SSE stream
  <- Hien thi ket qua
```

## Buoc 1: Lay API Key

| Provider | Link | Ghi chu |
|----------|------|---------|
| **OpenRouter** | https://openrouter.ai/keys | Ho tro 300+ models (GPT, Claude, Gemini). Khuyen dung. |
| **MiniMax** | https://platform.minimax.io/ | OpenAI-compatible lane. Repo mac dinh dung `MiniMax-M2.7-highspeed`; dung qua `MINIMAX_*`, khong nhoi vao `OPENAI_*`. |
| **OpenAI** | https://platform.openai.com/api-keys | Truc tiep tu OpenAI |
| **Anthropic** | https://console.anthropic.com/settings/keys | Claude models |

> **Khuyen nghi**: Bat dau voi **OpenRouter** — mot key dung duoc nhieu model.

## Buoc 2: Setup Environment Variables

```powershell
# Cach 1: Script tuong tac (khuyen dung)
.\tools\setup_ai_providers.ps1

# Cach 2: Truyen key truc tiep
.\tools\setup_ai_providers.ps1 -Provider openrouter -OpenRouterKey "sk-or-..."

# Cach 3: Setup tat ca providers
.\tools\setup_ai_providers.ps1 -OpenRouterKey "sk-or-..." -OpenAiKey "sk-..." -AnthropicKey "sk-ant-..."

# Xem trang thai hien tai
.\tools\setup_ai_providers.ps1 -ListOnly
```

### Thu cong (neu can)

```powershell
# OpenRouter (Priority 1)
[Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'sk-or-...', 'User')

# MiniMax (Priority 2)
[Environment]::SetEnvironmentVariable('MINIMAX_API_KEY', 'sk-...', 'User')
[Environment]::SetEnvironmentVariable('MINIMAX_BASE_URL', 'https://api.minimax.io/v1', 'User')
[Environment]::SetEnvironmentVariable('MINIMAX_MODEL', 'MiniMax-M2.7-highspeed', 'User')
[Environment]::SetEnvironmentVariable('MINIMAX_RESPONSE_MODEL', 'MiniMax-M2.7-highspeed', 'User')
[Environment]::SetEnvironmentVariable('MINIMAX_FALLBACK_MODEL', 'MiniMax-M2.7', 'User')
[Environment]::SetEnvironmentVariable('BIM765T_LLM_PROVIDER', 'MINIMAX', 'User')

# OpenAI (Priority 3)
[Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-...', 'User')

# Anthropic (Priority 4)
[Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', 'sk-ant-...', 'User')
```

> **Luu y**: Provider co priority cao nhat se duoc su dung (OpenRouter > MiniMax > OpenAI > Anthropic).
>
> **Tranh loi 404**: Khong dat `OPENAI_BASE_URL=https://api.minimax.io/v1/chat/completions` trong workspace chung.
> Repo nay da co lane rieng cho MiniMax qua `MINIMAX_BASE_URL=https://api.minimax.io/v1`.
>
> **Tranh xung dot env**: Neu may da tung dung Claude/OpenAI tool khac, giu `BIM765T_LLM_PROVIDER=MINIMAX` de runtime 765T khong bi roi sang provider cu.

## Buoc 3: Start Services

```powershell
# 1. Build solution (lan dau hoac khi co thay doi code)
dotnet build BIM765T.Revit.Agent.sln -c Release

# 2. Start WorkerHost (port 50765)
.\tools\start_workerhost.ps1

# 3. (Optional) Start Qdrant cho semantic search
.\tools\start_qdrant_local.ps1

# 4. Mo Revit 2024 va mo mot project
#    -> Add-in 765T se tu dong load va bat kernel pipe server
```

## Buoc 4: Kiem tra Readiness

```powershell
# Kiem tra toan bo stack
.\tools\check_ai_readiness.ps1

# Ket qua mong muon:
#   [OK] AI Provider Keys        -> Co it nhat 1 provider
#   [OK] WorkerHost Process      -> Dang chay
#   [OK] Revit Process           -> Dang chay
#   [OK] WorkerHost HTTP API     -> Tra loi duoc
#   [OK] LLM Provider Active     -> Co LLM, khong phai rule-first
#   [OK] Kernel Named Pipe       -> Revit kernel reachable
```

## Buoc 5: Test tu Script (khong can UI)

```powershell
# Test co ban
.\tools\test_ai_chat_e2e.ps1

# Test voi message tu chon
.\tools\test_ai_chat_e2e.ps1 -Message "List all walls in the active view"

# Test voi JSON output
.\tools\test_ai_chat_e2e.ps1 -Message "Hello" -AsJson

# Xem response chi tiet
.\tools\test_ai_chat_e2e.ps1 -Message "Create a floor plan view" -ShowVerbose
```

## Buoc 6: Test tu Revit UI

1. Mo Revit 2024 voi mot project (.rvt)
2. Click tab **765T** tren Ribbon
3. Click **Chat** de mo Worker panel
4. Go tin nhan trong **ComposerBar** phia duoi
5. Nhan Enter — message se gui qua HTTP den WorkerHost
6. Xem response AI hien thi trong chat timeline

### Test scenarios khuyen nghi

| # | Message | Ky vong |
|---|---------|---------|
| 1 | "Chao ban" | AI chao lai, xac nhan ket noi |
| 2 | "List all levels in this project" | Tra ve danh sach levels (read-only) |
| 3 | "How many walls are in the active view?" | Dem walls (read-only) |
| 4 | "Create a new level at elevation 12000mm" | Hien thi Preview -> Doi Approval -> Execute |
| 5 | "Delete all unused view templates" | Safety check -> Warning -> Doi approval |

## Troubleshooting

### WorkerHost khong start duoc

```powershell
# Kiem tra port 50765 co bi chiem khong
netstat -ano | findstr 50765

# Xem logs
Get-Content artifacts\workerhost\logs\workerhost-*.out.log -Tail 50
```

### AI tra loi "rule-first" thay vi dung LLM

```powershell
# Kiem tra env var da set chua
.\tools\setup_ai_providers.ps1 -ListOnly

# Restart WorkerHost sau khi set key
# (WorkerHost doc env var luc khoi dong)
```

### Kernel pipe khong ket noi

- Dam bao Revit da mo va co project active
- Kiem tra add-in da deploy: `%APPDATA%\Autodesk\Revit\Addins\2024\`
- Xem tab 765T co hien tren Ribbon khong
- Thu dong project trong Revit de kich hoat kernel

### Response cham (>10s)

- Kiem tra internet connection (LLM API can internet)
- Thu model nhe hon: set `OPENROUTER_PRIMARY_MODEL=openai/gpt-5.4-mini`
- Kiem tra Qdrant co chay khong (timeout semantic search)

## Cac lenh huu ich

```powershell
# Xem trang thai providers
.\tools\setup_ai_providers.ps1 -ListOnly

# Health check WorkerHost
.\tools\check_workerhost_health.ps1

# Xem bridge health
.\tools\check_bridge_health.ps1

# Full readiness check
.\tools\check_ai_readiness.ps1 -AsJson

# Xoa tat ca API keys
.\tools\setup_ai_providers.ps1 -RemoveAll
```

## Cau truc file lien quan

```
tools/
  setup_ai_providers.ps1      <- Setup API keys
  check_ai_readiness.ps1      <- Kiem tra stack
  test_ai_chat_e2e.ps1        <- Test chat API
  start_workerhost.ps1        <- Start WorkerHost
  start_qdrant_local.ps1      <- Start Qdrant
  check_workerhost_health.ps1 <- Health check

src/
  BIM765T.Revit.Copilot.Core/Brain/LlmBackboneServices.cs  <- LLM config resolver
  BIM765T.Revit.WorkerHost/ExternalAi/                      <- HTTP endpoints
  BIM765T.Revit.Agent/Services/Bridge/WorkerToolModule.cs   <- Revit tool execution

workspaces/default/workspace.json  <- Model & provider config
```
