# 765T Technical Research — Evidence-Based Decision Log

> **Date:** 2026-03-22
> **Purpose:** Moi quyet dinh ky thuat phai co bang chung tu research thuc te.
> Tai lieu nay la "so tay ky thuat" — moi muc co: SU THAT, NGUON, va QUYET DINH.

---

## MUC LUC

1. [Revit API Constraints — Su that ve threading va events](#1-revit-api)
2. [WebView2 trong Revit — Hien trang va workaround](#2-webview2)
3. [.NET 8 Migration — Revit 2025 thuc te](#3-dotnet8)
4. [LLM Pricing — Bang gia thuc te 03/2026](#4-llm-pricing)
5. [LLM Strategy — Chon model nao cho 765T](#5-llm-strategy)
6. [Knowledge Storage — Vector DB vs SQLite FTS5 vs Context Stuffing](#6-knowledge-storage)
7. [Safe Code Execution — Roslyn Scripting Sandbox](#7-safe-execution)
8. [OpenClaw Analysis — Doi thu va bai hoc](#8-openclaw)
9. [Revit.Async — Giai phap async cho Revit API](#9-revit-async)
10. [Quyet dinh ky thuat tong hop](#10-quyet-dinh)

---

## 1. Revit API

### 1a. Single-Threaded — KHONG co ngoai le

**SU THAT:**
Revit API BAT BUOC chay tren UI thread (main thread).
Moi API call (FilteredElementCollector, doc/ghi parameter, Transaction)
deu phai thuc hien tren thread chinh cua Revit.

**NGUON:**
- Autodesk Revit API Documentation: *"Your API application must perform
  all Autodesk Revit API calls in the main thread"*
- StackOverflow (52161965): Xac nhan khong the chay async Revit API
  tu background thread
- Building Coder blog: Chi co the giao tiep voi Revit tu ben ngoai
  qua ExternalEvent hoac IdlingEvent

**QUYET DINH cho 765T:**
- 765T Scan PHAI doc data tren UI thread, serialize ra JSON
- Moi xu ly phuc tap (phan tich, LLM call, multi-agent) chay
  NGOAI Revit, tren WorkerHost hoac background thread rieng
- Pattern: Revit (doc data) → Serialize → WorkerHost (xu ly) → Ket qua


### 1b. ExternalEvent vs IdlingEvent

**SU THAT:**

| Dac diem | ExternalEvent | IdlingEvent |
|---|---|---|
| Trigger | Code ben ngoai goi Raise() | Revit goi khi ranh |
| Do tin cay | Cao — luon duoc goi | Thap — chi khi Revit idle |
| Timing | Revit xu ly khi co the | Khong dam bao thoi gian |
| Use-case | Nhan lenh tu Chat UI | Background scan nhe |
| Han che | Khong chay ngay lap tuc | Time slice rat ngan |

**NGUON:**
- Autodesk Forum (9389997): So sanh chi tiet ExternalEvent vs IdlingEvent
- Autodesk Forum (8643136): Bug report ve ExternalEvent khong fire
  trong mot so truong hop dac biet (2025 van con)

**QUYET DINH cho 765T:**
- Chat UI commands: Dung ExternalEvent (tin cay)
- 765T Suggest (background): Dung IdlingEvent nhung RAT HAN CHE
  Chi kiem tra nhe (count warnings, check naming) — KHONG scan nang
- Luon co timeout va fallback khi event khong fire


### 1c. Transaction va Undo

**SU THAT:**
- Revit KHONG co Undo API de undo tu code
- Transaction co the RollBack() TRUOC khi Commit()
- TransactionGroup co the RollBack() SAU khi cac Transaction
  ben trong da Commit() — DAY LA CACH DUY NHAT de "undo"
- Sau khi TransactionGroup.Commit() thi KHONG the undo tu code

**QUYET DINH cho 765T:**
- Moi mutation cua AI phai nam trong 1 TransactionGroup
- Neu user bam "Undo" TRUOC khi ket thuc → RollBack()
- Neu user da xac nhan xong → TransactionGroup.Commit() → khong undo duoc
- Phai CANH BAO user truoc khi commit: "Sau buoc nay khong the hoan tac"
- Fallback: Luu danh sach ElementId + parameter values TRUOC khi mutation
  de co the "soft undo" bang cach dat lai gia tri cu

---

## 2. WebView2

### 2a. Hien trang: CEF Sharp conflict

**SU THAT:**
WebView2 (Chromium wrapper cua Microsoft) XUNG DOT voi CEF Sharp
(Chromium wrapper cu ma Revit dung noi bo).
- Revit 2024 va truoc: Dung CEF Sharp v65 (rat cu)
- WebView2 trong Dockable Panel → CRASH Revit khi doi View hoac dong file
- WebView2 trong popup Window → HOAT DONG binh thuong

**NGUON:**
- archi-lab.net (Konrad Sobon): Bai viet chi tiet ve WebView2 + Dockable Panel
  *"WebView2 has a conflict with older versions of CEF Sharp...
  causing Revit to crash"*
- Microsoft WebView2Feedback #719: Xac nhan conflict WebView2 + CEF Sharp
- Deyan Nenov workaround: Dispose WebView2 khi ViewActivated/DocumentOpened
  → hoat dong nhung Dynamo van crash (Dynamo cung dung Chromium wrapper)

### 2b. Revit 2025/2026: Autodesk da len ke hoach chuyen sang WebView2

**SU THAT:**
- Building Coder (07/2024): Autodesk thong bao se chuyen tu CEF Sharp sang
  WebView2 trong "next major release" (Revit 2026)
- revitapidocs.com/2025/news: Revit 2025 da bat dau don dep CEF Sharp,
  yeu cau devs khong ship CEF Sharp rieng
- Revit 2026 (du kien): WebView2 native → het conflict

**QUYET DINH cho 765T:**

| Target | Giai phap UI |
|---|---|
| Revit 2024 | WPF UserControl (khong dung WebView2 trong Dockable Panel) |
| Revit 2025 | WPF UserControl hoac WebView2 popup (test ky) |
| Revit 2026+ | WebView2 Dockable Panel (native support) |

- **MVP: Dung WPF UserControl** (XAML + data binding) cho Chat UI
  Khong phu thuoc WebView2 → khong bi crash
- Khi Revit 2026 ra: Chuyen sang WebView2 (React/HTML UI dep hon)
- Alternative: Dung CefSharp cung phien ban voi Revit (ricaun.Revit.CefSharp
  NuGet package tu dong dung dung version)

---

## 3. .NET 8

### 3a. Thuc trang migration

**SU THAT:**
- Revit 2025 chuyen hoan toan sang .NET 8 (khong con .NET Framework 4.8)
- KHONG the dung 1 DLL cho ca Revit 2024 (.NET FW 4.8) va 2025 (.NET 8)
- Phai co 2 project/solution rieng biet hoac dung multi-targeting
- Nhieu add-in lon van chua migrate xong (tinh den 03/2026)

**NGUON:**
- Autodesk Forum (13301211): User xac nhan phai dung separate project
- Autodesk Forum (12727039): Kho khan khi migrate, thieu tai lieu
- Autodesk Forum (12682053): Khong co huong dan step-by-step chinh thuc

**QUYET DINH cho 765T:**
- **Primary target: Revit 2025+ (.NET 8)**
  → Don gian hoa TOAN BO kien truc
  → WorkerHost va Agent chay cung runtime
  → Co the dung gRPC, System.Text.Json, modern async patterns
- **Optional: Revit 2024 support** qua separate build target
  (chi khi co demand tu user)
- Loi the lon: .NET 8 cho phep dung Roslyn Scripting, ML.NET,
  va cac thu vien AI/ML hien dai ma .NET FW 4.8 khong ho tro

---

## 4. LLM Pricing

### 4a. Bang gia thuc te (03/2026, per 1M tokens)

**NGUON:** ai.google.dev/gemini-api/docs/pricing, costgoat.com/pricing/claude-api,
pricepertoken.com

| Model | Input | Output | Context | Ghi chu |
|---|---|---|---|---|
| **Gemini 2.5 Flash** | $0.30 | $2.50 | 1M tokens | Hybrid reasoning, thinking budgets |
| **Gemini 2.5 Flash-Lite** | $0.10 | $0.40 | 1M tokens | Nho nhat, re nhat cua Google |
| **Gemini 2.5 Pro** | $1.25-2.50 | $10-15 | 1M tokens | SOTA, coding + reasoning |
| **Gemini 3 Flash Preview** | TBD | TBD | TBD | Moi ra, co search + grounding |
| **Gemini 2.0 Flash** | DEPRECATED | — | — | **DA BI DEPRECATED — KHONG DUNG** |
| | | | | |
| **Claude Haiku 3** | $0.25 | $1.25 | 200K | Re nhat cua Anthropic |
| **Claude Haiku 3.5** | $0.80 | $4.00 | 200K | Nhanh, tot cho classification |
| **Claude Sonnet 4** | $3.00 | $15.00 | 200K | Balanced |
| **Claude Sonnet 4.5** | $3.00 | $15.00 | 200K | **Best for coding** |
| **Claude Opus 4.6** | $5.00 | $25.00 | 200K | Most intelligent |
| | | | | |
| **MiniMax M1** | $0.20 | $1.10 | 1M tokens | 1M context, re |
| **MiniMax M2.5** | TBD | TBD | TBD | Moi nhat cua MiniMax |

### 4b. Phan tich chi phi cho 765T

**Kich ban: 1 user, 50 interactions/ngay, 20 ngay/thang = 1000 interactions/thang**

| Strategy | Model | Cost/thang | Chat luong |
|---|---|---|---|
| A: Chi Gemini Flash-Lite | $0.10+$0.40/M | ~$2-5 | Co ban, du cho chat+scan |
| B: Chi Gemini 2.5 Flash | $0.30+$2.50/M | ~$5-15 | Tot, co reasoning |
| C: Flash-Lite + Sonnet (code) | Mixed | ~$10-30 | Tot nhat, dat nhat |
| D: Chi MiniMax M1 | $0.20+$1.10/M | ~$3-8 | 1M context, gia tot |
| **E: BYOK (user tu nhap key)** | User chon | **$0 cho 765T** | Tuy user |

**QUYET DINH:**
- **MVP: Strategy E (BYOK) + Default Gemini 2.5 Flash-Lite**
  User nhap API key cua ho (Gemini free tier co $0/thang voi rate limit)
  765T khong phai tra tien LLM → giam burn rate startup
- **V1: Them option Gemini 2.5 Flash** (cho user muon chat luong cao hon)
- **V2: LLM Router** khi da co data thuc te ve usage patterns
- **KHONG dung Gemini 2.0 Flash** — da deprecated, se shutdown

---

## 5. LLM Strategy

### 5a. Tai sao BYOK la tot nhat cho startup

**SU THAT tu OpenClaw:**
- OpenClaw dung mo hinh BYOK (user tu mang API key)
- Chi phi cho OpenClaw team: $0 cho LLM
- User tra truc tiep cho Google/Anthropic/OpenAI
- Ket qua: *"I sent ~12 messages and it cost $40"* (eesel.ai blog)
  → User khong hieu cach toi uu → can giup ho tiet kiem

**QUYET DINH cho 765T:**
1. BYOK la default
2. 765T tu dong cau hinh de TIET KIEM cho user:
   - Chat thong thuong: Gemini Flash-Lite (~$0.001/message)
   - Task phuc tap: Gemini 2.5 Flash (~$0.005/message)
   - Code gen (tuong lai): Claude Sonnet 4.5 (~$0.05/message)
3. Hien thi chi phi uoc tinh cho moi action tren 765T Flow
   VD: "[Plan] Chi phi uoc tinh: $0.003"
4. Prompt Caching cua Gemini: Luu system prompt + project brief
   vao cache → giam 90% chi phi input tokens cho cac call lap lai

### 5b. Context Window Strategy

**SU THAT:**
- Gemini 2.5 Flash: 1M tokens context
- BEP trung binh: 50 trang = ~25,000 tokens
- Project Brief: ~2,000 tokens
- Standards file: ~5,000 tokens
- Revit scan data (full): ~50,000-200,000 tokens tuy du an
- TONG: ~80,000-230,000 tokens = VUA DU cho 1M context

**QUYET DINH:**
- **KHONG can Vector DB cho MVP**
- Nhet NGUYEN project brief + standards + scan data vao context
- Chi can Vector DB khi:
  - Tong tai lieu > 500K tokens
  - Hoac can search across nhieu du an
  - Luc do dung SQLite FTS5 truoc (xem Section 6)

---

## 6. Knowledge Storage

### 6a. So sanh 3 phuong phap

| Phuong phap | Do phuc tap | Chat luong RAG | Use-case |
|---|---|---|---|
| **Context Stuffing** | Thap nhat | Cao nhat (LLM thay het) | Tai lieu < 500K tokens |
| **SQLite FTS5** | Thap | Trung binh (keyword) | Tim kiem chinh xac theo tu khoa |
| **SQLite FTS5 + sqlite-vec** | Trung binh | Cao (hybrid) | Semantic + keyword search |
| **Qdrant/Chroma** | Cao nhat | Cao | Scale lon, nhieu du an |

**NGUON:**
- blog.sqlite.ai: *"Combined FTS5 with semantic search powered by
  sqlite-vector. Queries run through both systems, merged using
  Reciprocal Rank Fusion"*
- ZeroClaw blog: *"On Raspberry Pi Zero, memory retrieval takes under 3ms
  total"* → SQLite + sqlite-vec du nhanh cho desktop
- firecrawl.dev: *"For per-user vector stores, sqlite-vec is unbeatable"*

**QUYET DINH cho 765T:**
```
Phase 1 (MVP): Context Stuffing
  - Nhet nguyen file text vao prompt Gemini (1M context)
  - $0 infrastructure, 0 complexity
  - Giu brief.json + standards.json duoi 100K tokens

Phase 2 (V1): SQLite FTS5
  - Khi user co nhieu tai lieu (> 500K tokens)
  - FTS5 da co san trong SQLite (khong can cai them)
  - Tim kiem keyword nhanh, chinh xac

Phase 3 (V2): SQLite FTS5 + sqlite-vec
  - Them semantic search bang sqlite-vec extension
  - Hybrid search (keyword + embedding) cho ket qua tot nhat
  - Van dung SQLite — khong can server rieng

Phase 4 (Future): Qdrant chi khi can multi-user/multi-project scale
```

---

## 7. Safe Execution

### 7a. Roslyn Scripting — Co the dung cho 765T?

**SU THAT:**
- Roslyn (Microsoft.CodeAnalysis.CSharp.Scripting) cho phep
  compile va chay C# code at runtime
- Hoat dong tren .NET 8 (Revit 2025+)
- KHONG co sandbox tich hop san — phai tu xay
- .NET Core/8 da bo AppDomain security → khong the sandbox nhu .NET FW

**NGUON:**
- GitHub dotnet/roslyn #10830: *"Is there a secure method of sandboxing
  Roslyn's code execution?"* → KHONG co cach chinh thuc
- Rick Strahl blog: Chi tiet ve Roslyn scripting API
- StackOverflow: Khuyen nghi parse source truoc khi compile,
  blacklist cac namespace nguy hiem

**QUYET DINH cho 765T — Mo hinh 4 cap do an toan:**

```
CAP 1: PRE-BUILT TOOLS (MVP) — AN TOAN TUYET DOI
  - 765T team viet san 30-50 tool bang C#, da test ky
  - AI chi GOI tool voi parameters, KHONG viet code
  - VD: tool.rename_views(pattern="M-{Level}-{Zone}", scope="Level 1")
  - Tat ca tool co dry-run + preview + approval
  - 100% an toan, 0% rui ro

CAP 2: PARAMETERIZED TEMPLATES (V1) — AN TOAN CAO
  - Template script voi placeholder
  - AI chi DIEN tham so, KHONG viet logic
  - Template da duoc review va test truoc
  - VD: template "batch_rename" voi {category, pattern, filter}

CAP 3: REVIEWED SCRIPTS (V2) — AN TOAN CO DIEU KIEN
  - AI sinh C# code bang Roslyn
  - Truoc khi chay:
    a) Static analysis: Block cac API nguy hiem
       Blacklist: Document.Close, Document.Save, 
       SyncWithCentral, FilteredElementCollector.Delete,
       File.Delete, Process.Start, System.Net.*
    b) HIEN THI code cho user doc va xac nhan
    c) Chay trong TransactionGroup (co the rollback)
    d) Timeout 30 giay (tranh infinite loop)
  - User phai bam "Chay" sau khi doc code

CAP 4: DYNAMIC EXECUTION (Future) — CHI CHO DEVELOPER
  - Opt-in, can xac nhan dac biet
  - Full Roslyn scripting voi minimal restrictions
  - Chi danh cho user hieu rui ro
```

---

## 8. OpenClaw

### 8a. OpenClaw la gi va tai sao no viral?

**SU THAT (tu KDnuggets, SimilarLabs, MindStudio):**
- Open-source AI agent chay local, ket noi LLM voi phan mem thuc te
- Ra mat 11/2025 (ten cu: Clawdbot), doi ten OpenClaw 01/2026
- 214,000+ GitHub stars tinh den 02/2026
- Co the: doc/ghi file, chay shell commands, browse web, gui email, control APIs
- Dung "Skills" system (100+ plugins): web browser, messaging, file system
- Creator (Peter Steinberger) da join OpenAI

**TAI SAO VIRAL:**
1. Free + open-source
2. THUC SU LAM VIEC (khong chi chat)
3. Tich hop voi app hien co (Slack, Discord, WhatsApp)
4. Dung dung trend "agentic AI" 2026

### 8b. RUI RO cua OpenClaw (bai hoc cho 765T)

**SU THAT:**
- *"Running the tool without proper precautions can expose sensitive files"*
- *"Some third-party skills have been found to contain malware"*
- *"Reports of agents deleting entire email inboxes"*
- Chi phi: User tra truc tiep cho LLM provider (BYOK)
  *"I sent ~12 messages and it cost $40"* → qua dat neu khong toi uu

### 8c. Bai hoc cho 765T

| OpenClaw lam tot | 765T nen hoc |
|---|---|
| Free + open-source | BYOK model, co free tier |
| Skills/plugin system | 765T Plugin SDK |
| Thuc su execute tasks | 765T Tools + Dynamic scripts |
| Local-first | 765T Hub tai %APPDATA%\\BIM765T.Revit.Agent\\workspaces |

| OpenClaw lam chua tot | 765T nen lam tot hon |
|---|---|
| Khong co domain expertise | 765T chuyen sau BIM/Revit |
| Chi phi khong kiem soat | 765T hien thi chi phi uoc tinh |
| Bao mat lo hong | 765T 4-cap an toan |
| Khong co preview/dry-run | 765T safe mutation pattern |
| Khong nho context qua phien | 765T Hub + session handoff |

---

## 9. Revit.Async

### 9a. Thu vien Revit.Async (KennanChan/Revit.Async)

**SU THAT:**
- Thu vien wrap ExternalEvent thanh async/await pattern
- Cho phep viet code kieu:
  ```csharp
  var result = await RevitTask.RunAsync(app => {
      // Code nay chay tren UI thread cua Revit
      return new FilteredElementCollector(doc)
          .OfCategory(BuiltInCategory.OST_Walls)
          .GetElementCount();
  });
  // Code tiep tuc sau khi co ket qua
  ```
- Giai quyet van de: giao tiep tu background thread vao Revit UI thread
- Hoat dong tren ca .NET FW 4.8 va .NET 8

**NGUON:**
- GitHub KennanChan/Revit.Async: 500+ stars
- Building Coder blog: Recommended solution cho async Revit API

**QUYET DINH cho 765T:**
- SU DUNG Revit.Async lam core pattern giao tiep Chat UI → Revit API
- Flow: User prompt → LLM → Tool call → RevitTask.RunAsync() → Revit API
- Giu code Revit API ngan gon (chi doc/ghi), xu ly phuc tap o ngoai

---

## 10. Quyet dinh ky thuat tong hop

### 10a. Technology Stack cho MVP

| Thanh phan | Lua chon | Ly do |
|---|---|---|
| **Target Revit** | 2025+ (.NET 8) | Unified stack, modern APIs |
| **UI** | WPF UserControl (XAML) | Khong bi WebView2/CEF crash |
| **Chat UI** | WPF + MVVM | Don gian, stable, native look |
| **LLM** | Gemini 2.5 Flash-Lite (default) | Re nhat, 1M context, BYOK |
| **LLM SDK** | Google.GenerativeAI NuGet | Official .NET SDK |
| **Async pattern** | Revit.Async | Proven, async/await cho Revit |
| **Data storage** | JSON files trong 765T Hub | Don gian, khong can DB |
| **Knowledge** | Context stuffing | Nhet text vao prompt, 0 infra |
| **Tool execution** | Pre-built C# tools | An toan tuyet doi |
| **Mutation safety** | TransactionGroup + Preview | Rollback khi can |
| **Streaming** | IProgress<T> callback | 765T Flow realtime |

### 10b. Technology Stack cho V1

| Thanh phan | Nang cap | Ly do |
|---|---|---|
| **UI** | WPF + WebView2 (neu Revit 2026) | UI dep hon, React-based |
| **LLM** | + Gemini 2.5 Flash (khi can reasoning) | Chat luong cao hon |
| **Knowledge** | + SQLite FTS5 | Tim kiem nhanh trong tai lieu lon |
| **Tool execution** | + Parameterized Templates | Linh hoat hon |
| **External** | + Excel read/write | Connector dau tien |

### 10c. Technology Stack cho V2

| Thanh phan | Nang cap | Ly do |
|---|---|---|
| **LLM** | + Claude Sonnet 4.5 (code gen) | Code quality tot nhat |
| **Router** | Intent classifier | Toi uu chi phi |
| **Knowledge** | + sqlite-vec (hybrid search) | Semantic + keyword |
| **Tool execution** | + Roslyn reviewed scripts | AI sinh code, user review |
| **External** | + ACC connector | Demand tu enterprise user |
| **Audit** | Multi-agent (sequential) | Health check + report |

### 10d. KHONG lam trong MVP

| Khong lam | Ly do |
|---|---|
| Vector DB (Qdrant/Chroma) | Context stuffing du cho MVP |
| LLM Router | Chi can 1 model |
| Multi-agent parallel | Revit single-thread |
| Dynamic code execution | Rui ro qua cao |
| Script marketplace | Chua co cong dong |
| ACC/Unifi connector | Chua can, phuc tap |
| WebView2 Dockable Panel | CEF Sharp crash tren Revit 2024/2025 |
| Custom Persona editor | 3 preset la du |
| Revit 2024 support | .NET FW 4.8 phuc tap hoa stack |

---

## Phu luc: Nguon tham khao

| # | Nguon | URL / Reference |
|---|---|---|
| 1 | Gemini API Pricing | ai.google.dev/gemini-api/docs/pricing |
| 2 | Claude API Pricing | costgoat.com/pricing/claude-api |
| 3 | MiniMax Pricing | pricepertoken.com/pricing-page/provider/minimax |
| 4 | WebView2 + Revit | archi-lab.net/webview2-and-revits-dockable-panel/ |
| 5 | Revit CEF Sharp 2025 | revitapidocs.com/2025/news |
| 6 | .NET 8 Migration | Autodesk Forum #13301211 |
| 7 | ExternalEvent issues | Autodesk Forum #8643136 |
| 8 | Revit.Async | github.com/KennanChan/Revit.Async |
| 9 | OpenClaw explained | kdnuggets.com/openclaw-explained |
| 10 | Roslyn sandbox | github.com/dotnet/roslyn #10830 |
| 11 | SQLite RAG | blog.sqlite.ai/building-a-rag-on-sqlite |
| 12 | sqlite-vec perf | zeroclaws.io/blog/zeroclaw-hybrid-memory |
| 13 | ricaun CefSharp | github.com/ricaun-io/ricaun.Revit.CefSharp |
| 14 | Building Coder async | jeremytammik.github.io/tbc/a/1817_async_await |
| 15 | Roslyn scripting | weblog.west-wind.com/posts/2022/Jun/07 |
