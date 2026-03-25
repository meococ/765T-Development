# Lessons Learned

> Historical lessons only. If a lesson conflicts with `docs/assistant/*`, `docs/ARCHITECTURE.md`, or `docs/PATTERNS.md`, treat this file as historical context and follow the canonical docs.

## Cách dùng file này

- Chỉ ghi các bài học có khả năng tái sử dụng.
- Mỗi note nên ngắn và có đủ 4 ý: triệu chứng, nguyên nhân, cách fix, cách phòng ngừa.
- Nếu một bài học đã trở thành kiến thức nền ổn định của project, promote thêm sang `PROJECT_MEMORY.md`.

## Notes

### 2026-03-21 - God file phai tach partial class theo domain, khong de vuot 400 dong/file

- **Triệu chứng:** `ToolPayloadValidator.cs` dat 87KB/1775 dong, dev mat 30+ giay de tim method can sua; merge conflict xay ra thuong xuyen khi 2 agent cung touch file.
- **Nguyên nhân:** tat ca validator methods deu nam trong 1 file vi ban dau chi co 10-20 methods; khi scale len 100+ methods thi file tro thanh bottleneck.
- **Cách fix:** tach thanh 7 partial class files theo domain (`Session`, `Worker`, `CopilotTask`, `Mutation`, `Penetration`, `FamilyAuthoring` + Core). `CreateValidators()` registry van nam trong Core file va reference duoc tat ca methods tu partial files.
- **Phòng ngừa:** bat ky file nao vuot 400 dong hoac chua 15+ methods nen chia partial class. Khi them domain moi, tao partial file rieng ngay tu dau.

### 2026-03-21 - Constructor DI qua 10 params phai dung service bundles

- **Triệu chứng:** `ToolRegistry`, `ToolModuleContext`, va `AgentHost.Initialize()` co 26+ constructor params; them 1 service moi phai sua 3 files, rat de quen.
- **Nguyên nhân:** moi service duoc inject rieng le; khi so luong tang thi constructor tro nen unmanageable.
- **Cách fix:** tao 5 domain bundles (`PlatformBundle`, `InspectionBundle`, `HullBundle`, `WorkflowBundle`, `CopilotBundle`); constructor chi nhan 5 bundles. Properties cua `ToolModuleContext` giu nguyen ten de backward compatible.
- **Phòng ngừa:** khi them service moi, them vao bundle phu hop; chi tao bundle moi khi domain that su rieng biet (>3 services).

### 2026-03-21 - TreatWarningsAsErrors phai condition-based, khong hardcode true/false

- **Triệu chứng:** `Directory.Build.props` co `TreatWarningsAsErrors=false` nhung CI dung `-warnaserror` flag; dev local khong thay warning nao, CI bat ngo fail.
- **Nguyên nhân:** 2 nguon truth khac nhau cho cung 1 policy (MSBuild property vs CLI flag).
- **Cách fix:** dung `Condition="'$(CI)' == 'true' OR '$(Configuration)' == 'Release'"` de enable cho CI/Release, disable cho local Debug.
- **Phòng ngừa:** bat ky build policy nao (analyzer, code style, warnings) nen co 1 nguon truth duy nhat trong MSBuild, khong duplicate o CLI.

### 2026-03-21 - Test project dung Compile Include links phai them moi file moi dependency

- **Triệu chứng:** Agent.Core.Tests fail vi `ExpertPackCatalogItem` not found, nhung type da duoc define trong Agent project.
- **Nguyên nhân:** test project dung `<Compile Include="..\..\src\BIM765T.Revit.Agent\...">` links (vi Agent target net48 khong the ProjectReference truc tiep). Khi extract types ra file moi (`ExpertLabCatalogModels.cs`), file moi chua duoc link.
- **Cách fix:** them `<Compile Include>` cho file moi trong .csproj cua test project.
- **Phòng ngừa:** bat ky refactoring nao move/extract code ra file moi trong Agent project, **LUON check va update** `BIM765T.Revit.Agent.Core.Tests.csproj` Compile Include list.

### 2026-03-21 - Duplicate document-key lambda phai extract thanh method chung

- **Triệu chứng:** cung 1 lambda `doc => { var path = doc.PathName ... }` xuat hien o 2 cho trong `AgentHost.Initialize()` (cho `DocumentCacheService` va `EventIndexService`).
- **Nguyên nhân:** copy-paste khi them EventIndexService; de lau se drift.
- **Cách fix:** extract thanh `private static string ResolveDocumentKey(Document doc)` method, reference tu 2 cho.
- **Phòng ngừa:** khi thay lambda > 3 dong duoc dung lai, extract ra method ngay.

### 2026-03-20 - Worker shell phai di chung mot orchestration path cho UI va MCP

- **Triệu chứng:** neu UI tu goi domain tool truc tiep con MCP di qua orchestration tool rieng, behavior se drift: mission state, memory, approval cards, va tool summaries khong con dong nhat.
- **Nguyên nhân:** de product shell lon nhanh, rat de tao mot path rieng cho UI va mot path rieng cho external AI callers.
- **Cách fix:**
  1. dua orchestration public surface ve `worker.*`
  2. de `WorkerTab` goi `InternalToolClient -> worker.message`
  3. de MCP caller cung goi chinh `worker.message`
  4. giu UI chi la shell render `WorkerResponse`, khong tu so huu mutation logic
- **Phòng ngừa:** bat ky nang cap nao cho mission, approval, persona, hay memory deu phai vao chung `worker.*` lane truoc; UI khong duoc tao private execution path.

### 2026-03-20 - Worker memory v1 nen dung session + episodic, chua nen mo semantic/vector qua som

- **Triệu chứng:** de bi hut vao y tuong embeddings/vector search/auto-learning truoc khi co du pilot sessions that su.
- **Nguyên nhân:** memory system nghe co ve thong minh hon neu co semantic/vector ngay tu dau, nhung retrieval quality, promotion policy, va deploy complexity chua du chin.
- **Cách fix:**
  1. session memory trong RAM cho ngữ cảnh ngắn hạn
  2. episodic mission memory persist JSON cho continuity qua restart
  3. search v1 theo lexical/tag/document/recency scoring
  4. de semantic memory, embeddings, ONNX, va sidecar vector retrieval sang phase sau
- **Phòng ngừa:** chi mo phase semantic khi co metric cho thay episodic retrieval khong du, va phai co `candidate -> review -> promote`, khong auto-self-learning production.

### 2026-03-20 - Worker tool cards khong duoc serialize payload qua `object`

- **Triệu chứng:** `worker.message` len live catalog thanh cong, nhung run QC bi `INTERNAL_ERROR` voi loi `ModelHealthResponse ... is not expected`.
- **Nguyên nhân:** helper tao `WorkerToolCard` goi `JsonUtil.Serialize(payload)` khi `payload` co static type `object`; `DataContractSerializer` luc do chi biet `object`, khong biet runtime type that su.
- **Cách fix:** serialize tool-card payload theo **runtime type** thay vi generic `object`, vi du dong generic method qua reflection hoac helper serialize untyped.
- **Phòng ngừa:** bat ky payload nao di qua `DataContractSerializer` ma bi box thanh `object` deu co nguy co crash tuong tu; neu muon luu payload as JSON string thi phai serialize theo runtime type ngay tai helper trung tam.

### 2026-03-18 - Agent cold-start: uu tien live task context, khong dung cache path cu lam startup truth

- **Trieu chung:** Moi task moi truoc day de goi nhieu tool context roi moi bat dau lam viec, gay cham va verbose.
- **Nguyen nhan:** `session.get_task_context` da gom du context nhung team de bi hut ve helper cache path cu `_session_state.json`.
- **Cach fix:**
  1. Uu tien `session.get_task_context` hoac `worker.get_context` lam live bootstrap.
  2. `update_session_state.ps1` va `.assistant/context/_session_state.json` chi duoc xem la helper cache lich su / convenience cache, khong phai startup truth canonical.
  3. Neu cache cu/thieu/stale, phai goi lai live context tool thay vi tin cache.
- **Phong ngua:** Khong dua old session-state cache path vao read order chinh. Moi startup flow phai uu tien live context truoc, cache chi la optional convenience artifact sau health-check.

### 2026-03-16 - Shadow-copy deploy an toàn hơn manifest trỏ thẳng vào bin

- Triệu chứng: build/install dễ vướng assembly path cũ hoặc bị ảnh hưởng khi Revit đang giữ file trong `bin/Release`.
- Nguyên nhân: `.addin` manifest trỏ thẳng vào output path đang bị thay đổi trong lúc phát triển.
- Cách fix: dùng `install-addin.ps1` theo hướng shadow-copy deployment.
- Cách phòng ngừa: khi có dấu hiệu Revit vẫn load build cũ hoặc build bị lock, ưu tiên cài lại qua script thay vì sửa manifest tay.

### 2026-03-16 - Mutation flow chỉ an toàn khi preview và execute cùng context

- Triệu chứng: execute bị fail do mismatch hoặc, tệ hơn, có nguy cơ chạy sai scope nếu thiếu guard.
- Nguyên nhân: selection/view/document/caller thay đổi giữa `dry-run` và `execute`.
- Cách fix: bind approval token với payload + caller + session + resolved context + `preview_run_id`.
- Cách phòng ngừa: luôn coi `expected_context` và fingerprint là bắt buộc cho write path.

### 2026-03-16 - Debug bridge nên bắt đầu từ health và capabilities trước khi đi sâu

- Triệu chứng: tưởng lỗi business logic nhưng thực tế là pipe, add-in, hoặc active document chưa sẵn sàng.
- Nguyên nhân: nhảy thẳng vào code-level debug khi môi trường chưa được xác nhận.
- Cách fix: chạy `check_bridge_health.ps1`, sau đó kiểm tra `session.list_tools`, `session.get_capabilities`, và active document/view.
- Cách phòng ngừa: giữ debug flow theo tầng: environment -> bridge -> context -> business logic.

### 2026-03-16 - Curated memory phải tách khỏi run history

- Triệu chứng: mỗi phiên phải nạp lại quá nhiều text, nhiều run logs, dễ lẫn giữa kiến thức bền và dữ liệu nhất thời.
- Nguyên nhân: dùng transcript/run artifacts làm nguồn nhớ chính.
- Cách fix: dùng `docs/agent/PROJECT_MEMORY.md` cho stable knowledge và `LESSONS_LEARNED.md` cho kinh nghiệm lặp lại; chỉ mở `.assistant/runs/` khi cần handoff/debug.
- Cách phòng ngừa: sau mỗi discovery có giá trị, tóm tắt thành note ngắn thay vì giữ nguyên transcript dài.

### 2026-03-16 - Rebrand agent phải đổi đồng bộ solution, namespace, pipe, AppData, manifest, và scripts

- Triệu chứng: đổi tên assembly hoặc UI xong nhưng build script, named pipe, config path, add-in manifest, hoặc tool scripts vẫn trỏ brand cũ nên runtime bị lệch context.
- Nguyên nhân: brand của project này đi xuyên suốt từ solution/project names tới `%APPDATA%`, `%LOCALAPPDATA%`, pipe name, docs, package path, và smoke scripts.
- Cách fix: rebrand đồng bộ `BIM7654T → BIM765T` trên solution, project folders, namespaces, manifests, deploy scripts, docs, workflow/schedule prefixes, và build/test/package commands; đổi cả `AddInId` GUID nếu brand cũ có thể còn song song; sau đó build lại và cài lại add-in bằng `install-addin.ps1`.
- Cách phòng ngừa: lần sau nếu đổi brand hoặc product name, luôn chạy checklist theo 6 lớp: source path → namespace/assembly → runtime pipe/config → deploy manifest/AddInId → docs/scripts/smoke tests → Revit addins folder cleanup.

### 2026-03-16 - Security defaults phải fail-closed, không fail-open

- Triệu chứng: caller validation trong PipeServerHostedService return `true` khi identity rỗng → bất kỳ process nào cũng connect được.
- Nguyên nhân: logic guard check empty string rồi return true thay vì false.
- Cách fix: đổi `IsCallerAllowed()` return `false` khi `clientIdentity` là null/empty. Thêm bounds check cho MCP Content-Length (cap 10MB). Thêm path traversal protection cho data import.
- Cách phòng ngừa: mọi security gate mặc định phải deny (fail-closed). Review tất cả input validation paths khi thêm tool mới.

### 2026-03-16 - Deploy/package/smoke scripts de drift neu duplicate executable resolution

- Trieu chung: smoke script co the chay nham packaged build cu, fail theo working directory, hoac package moi van con file cu do target folder khong duoc clear.
- Nguyen nhan: nhieu script tu copy-paste logic resolve `Bridge.exe`/`McpHost.exe`, sort version theo chuoi (`v9` > `v10`) va hardcode path theo `D:\` hoac current directory.
- Cach fix: dua logic resolve executable/package root vao `tools/Assistant.Common.ps1`, sort build theo version so + timestamp, resolve repo path theo project root, va reset package target folders truoc khi copy.
- Cach phong ngua: moi script deploy/package/smoke moi phai dung resolver chung thay vi tu viet lai path logic.

### 2026-03-16 - MCP smoke parser phai doc theo byte va wait dung response id

- Trieu chung: MCP smoke co the treo hoac doc sai body khi response co UTF-8 hoac server gui notification xen giua cac responses.
- Nguyen nhan: `Content-Length` la byte length nhung parser doc theo `char`, dong thoi script gia dinh response se tra ve dung thu tu request.
- Cach fix: doc body bang raw byte stream, decode UTF-8 sau khi doc du byte, va bo qua notifications cho den khi gap response co `id` khop request.
- Cach phong ngua: bat ky MCP smoke/manual client nao trong repo deu phai coi framing la byte-oriented protocol, khong doc body bang char count.

### 2026-03-16 - Mode C scripts phai chon dung Mode C run, khong lay latest run mo ho

- Trieu chung: `create_mode_c_handoff`, `execute_mode_c_tool_plan`, `compare_mode_c_execution`, va `update_mode_c_memory` co the nhat nham `ask-claude` run hoac run prompt-only, dan den thieu `response.parsed.json`/`bridge-execution.json` du run co moi hon.
- Nguyen nhan: script lay thu muc moi nhat bang `Sort-Object Name -Descending` tren toan bo `.assistant/runs/` thay vi filter theo `mode-c-` va artifact bat buoc.
- Cach fix: them resolver chung trong `tools/Assistant.Common.ps1` de filter theo prefix run + required files, va chi index cac run `mode-c-*` trong `index_mode_c_runs.ps1`.
- Cach phong ngua: moi script xu ly run history phai xac dinh ro loai run va file toi thieu truoc khi tu dong chon latest.

### 2026-03-16 - Resolve-ProjectRoot khong duoc coi moi `ASSISTANT.md` la repo marker hop le

- Trieu chung: khi chay script tu `D:\Development`, project root co the bi resolve thanh workspace root thay vi repo `BIM765T-Revit-Agent`, dan den docs/context/relay chay sai cho.
- Nguyen nhan: helper root resolution chap nhan bat ky thu muc nao co `ASSISTANT.md`, trong khi workspace root cung co file nay de boot context.
- Cach fix: uu tien solution file `BIM765T.Revit.Agent.sln`; neu khong co thi chi chap nhan thu muc co du `ASSISTANT.md` + `src\BIM765T.Revit.Agent` + `tools`.
- Cach phong ngua: root resolver phai dua tren marker dac thu repo, khong dua tren file docs chung cua workspace.

### 2026-03-16 - Relay mailbox can write atomic va session mac dinh phai unique

- Trieu chung: relay reader co the skip message malformed hoac thread trong ngay bi tron nhieu task khong lien quan; single-item arrays nhu `risks`/`nextActions` co the bi serialize thanh scalar.
- Nguyen nhan: ghi file thang vao `active/`, dung session mac dinh `session-YYYYMMDD`, va gan array thong qua bieu thuc PowerShell bi unroll mat mang 1 phan tu.
- Cach fix: ghi JSON atomic qua temp file + move, sinh `session-YYYYMMDD-HHmmss-<suffix>` khi user khong truyen `SessionId`, va cast cac field array ve `[object[]]` truoc khi `ConvertTo-Json`.
- Cach phong ngua: moi mailbox/file-based protocol trong repo phai tranh partial-write, tranh session collision, va verify schema voi ca truong hop array 1 phan tu.

### 2026-03-16 - Trong PowerShell array literal, command invocation phai bo trong `$()`

- Trieu chung: helper nhu `Resolve-BridgeExe`/`Resolve-McpExe` fail ngay tu `Join-Path` voi loi `Cannot convert 'System.Object[]' to the type 'System.String' required by parameter 'ChildPath'`.
- Nguyen nhan: dat command invocation truc tiep lam phan tu dau tien trong `@(...)`, khien PowerShell parse phan tu tiep theo thanh them argument cua lenh truoc.
- Cach fix: bo command invocation vao subexpression, vi du `$(Join-Path ...)`, `$(Resolve-LatestPackagedPath ...)`.
- Cach phong ngua: bat ky khi nao nhung command vao array literal, nhat la phan tu dau tien, phai dung `$()` thay vi viet troi.

### 2026-03-16 - Wrapper family suffix helper phai idempotent de tranh double-suffix

- Trieu chung: verify wrapper size-specific bao `FoundCount=0` du family/type da load trong project; ten bi thanh dang `_EXACTA_EXACTA`.
- Nguyen nhan: helper append suffix cho wrapper family name duoc goi lai tren base name da co suffix.
- Cach fix: trong helper `Resolve-WrapperFamilyName`, neu base name da ket thuc bang suffix thi tra ve nguyen trang thay vi append them.
- Cach phong ngua: moi helper compose family/type name theo suffix/token phai idempotent, de co the reuse an toan o ca plan, build, verify, va execute path.

### 2026-03-16 - Size-specific wrapper type names phai tach base placement mode truoc khi suy ra geometry/host face

- Trieu chung: wrapper unified family load dung TypeName nhu `ELEV_XUP__L...__D...` nhung geometry va axis audit lai bi xem nhu `PLAN_ZUP` hoac host face sai.
- Nguyen nhan: code build wrapper so sanh exact `TypeName == "ELEV_XUP"/"ELEV_YUP"`; sau khi them suffix size, so sanh exact fail va logic roi vao default branch.
- Cach fix: moi cho suy ra placement mode tu `TypeName` phai cat prefix truoc `__` roi moi map sang `ELEV_XUP` / `ELEV_YUP` / `PLAN_ZUP`.
- Cach phong ngua: bat ky naming scheme nao co suffix/token (`__L...__D...`, `_EXACTA`, version tag...) deu phai co helper resolve `base mode` trung tam; khong hardcode so sanh exact voi full TypeName trong geometry/orientation logic.

### 2026-03-16 - Unified wrapper family co nhieu embedded variants se lam bbox QC bi phinh to, nen size QC khong duoc dua thuong vao family instance bounding box

- Trieu chung: TypeName dung, position gan dung, nhung `LengthDeltaFeet`/`ExtentDeltaFeet` deu OFF hang loat va gan nhu cung mot gia tri lon cho moi instance.
- Nguyen nhan: mot family gom nhieu embedded variant/nested geometry co the lam family instance bounding box phan anh envelope cua ca family, khong chi geometry dang visible cho current type.
- Cach fix: voi unified-family multi-type wrapper, uu tien QC theo `ExpectedTypeName == ActualTypeName` va mode/position; chi dung bbox QC neu da xac nhan family architecture khong bi envelope inflation.
- Cach phong ngua: khi doi tu multi-family sang single-family multi-type, phai re-validate lai toan bo heuristic QC (bbox, extents, axis, schedule fields), khong duoc mang nguyen rule QC cua multi-family sang.

### 2026-03-16 - Sau khi deploy add-in moi, phai verify runtime bang execute diagnostics chu khong chi tin vao build/deploy artifact

- Trieu chung: `dotnet build` + `install-addin.ps1` deu thanh cong, nhung Revit van chay code cu; build wrapper van tra diagnostic `ROUND_WRAPPER_NESTED_CREATED` thay vi `ROUND_WRAPPER_NATIVE_GEOMETRY_CREATED`.
- Nguyen nhan: Revit giu DLL add-in trong memory theo session startup; deploy shadow folder moi khong tu reload code da load.
- Cach fix: sau moi deploy quan trong, chay 1 tool nho de doc execute diagnostics/runtime behavior. Neu diagnostics van la code cu thi bat buoc restart Revit roi moi tiep tuc mutation workflow.
- Cach phong ngua: coi `install-addin.ps1` chi la buoc cap nhat manifest/shadow. Acceptance gate phai dua tren runtime diagnostics cua session dang mo, khong dua tren file DLL moi tren disk.

### 2026-03-16 - Size-specific wrapper build khong duoc rely vao default target type names; phai pass-through AXIS mapping vao internal planning

- Trieu chung: `build_round_project_wrappers.ps1 -GenerateSizeSpecificVariants` build ra native geometry dung, nhung mot nhom type bi sai axis (`mode=ELEV_YUP` nhung `type=AXIS_X...`), dan den externalize bao thieu type nhu `AXIS_Y__...`.
- Nguyen nhan: `BuildRoundWrapperVariantSpecs()` tao `RoundExternalizationPlanRequest` moi nhung khong copy `Plan/ElevX/ElevY WrapperTypeName` tu request build vao request planning, nen runtime co the roi ve mapping mac dinh khong mong muon.
- Cach fix: khi build size-specific variants, luon pass-through day du `PlanWrapperFamilyName`, `PlanWrapperTypeName`, `ElevXWrapperFamilyName`, `ElevXWrapperTypeName`, `ElevYWrapperFamilyName`, `ElevYWrapperTypeName` vao `PlanRoundExternalization(...)`.
- Cach phong ngua: bat ky tool nao chain `plan -> build -> execute` deu phai forward toan bo naming/axis contract, khong duoc tao request moi chi voi mot phan field va hy vong default se trung.

### 2026-03-16 - Clean project family de externalize IFC khong nen bat dau tu face-based template, va wrapper final nen giu non-work-plane-based neu muc tieu la hostless point placement

- Trieu chung: build clean native family thanh cong, nhung externalize `element.place_family_instance_safe` fail 95/95 voi `Failed to create family instance on face` du da truyen sketch-plane style payload (point + FaceNormal + ReferenceDirection, khong co host).
- Nguyen nhan: `ResolveRoundWrapperTemplatePath()` uu tien `Metric Generic Model face based.rft`, nen family tao ra van mang hosting behavior phu thuoc face. Khi doi sang `Metric Generic Model.rft` nhung van force `FAMILY_WORK_PLANE_BASED = 1`, workflow placement lai tiep tuc di vao nhom host/sketch-plane khong can thiet.
- Cach fix: uu tien `Metric Generic Model.rft` truoc, fallback face-based chi khi can; giu wrapper final o trang thai non-face, tat `Always Vertical`, va khong force `Work Plane-Based` cho clean project family can place hostless theo project axes.
- Cach phong ngua: neu muc tieu la clean project-level family cho IFC/axis alignment, phai chot family placement strategy ngay tu template + family parameter level (non-face, non-work-plane-based neu co the) truoc khi debug placement script.

### 2026-03-16 - OneLevelBased placement trong workflow externalize phai normalize `Elevation from Level`, neu khong Z se bi cong them cao do level

- Trieu chung: family `Round_Project` build dung native geometry va project-axis alignment, nhung mot nhom instance externalize van lech vi tri theo Z mot khoang bang dung elevation cua level gan nhat; QC bao `ProjectAxis=OK` nhung `Position=OFF`.
- Nguyen nhan: khi workflow place `OneLevelBased` family bang `NewFamilyInstance(point, symbol, level, ...)`, Z cua insertion point khong don gian la absolute project Z trong moi case; Revit giu/derives them `Elevation from Level`, nen origin co the bi doi thanh `targetZ + levelElevation`.
- Cach fix: sau khi place instance, doc `LocationPoint` + parameter `Elevation from Level`, tinh lai `desiredOffset = targetZ - levelElevation`, set lai `Elevation from Level` ve gia tri mong muon, roi moi fallback sang `element.move_safe` neu van con delta.
- Cach phong ngua: voi bat ky workflow nao dat `OneLevelBased` family bang absolute coordinates, phai coi `Level` + `Elevation from Level` la mot cap rang buoc; QC vi tri phai kiem tra sau khi da normalize offset, khong chi sau buoc place ban dau.

### 2026-03-16 - Revit 2024 ViewDiscipline enum không có member `Undefined`

- Triệu chứng: build fail với CS0117 `'ViewDiscipline' does not contain a definition for 'Undefined'`.
- Nguyên nhân: Revit API enum members thay đổi giữa versions. `ViewDiscipline` trong Revit 2024 chỉ có `Architectural`, `Structural`, `Mechanical`, `Electrical`, `Coordination` — không có `Undefined`.
- Cách fix: bỏ check `!= ViewDiscipline.Undefined`, dùng `disc.ToString()` trực tiếp. Wrap access `View.Discipline` trong try-catch vì không phải mọi view type đều expose property này.
- Cách phòng ngừa: **không bao giờ assume enum member tồn tại** khi code Revit API. Luôn wrap trong try-catch. Check API docs trước khi dùng enum values.

### 2026-03-16 - Trước khi tạo DTO class mới, phải grep kiểm tra trùng tên trong Contracts

- Triệu chứng: build fail với CS0101 `The namespace already contains a definition for 'ComplianceSectionResult'`.
- Nguyên nhân: tạo `ComplianceSectionResult` trong `TemplateSheetAnalysisDtos.cs` nhưng class này đã tồn tại trong `AuditDtos.cs`. Codebase có 8+ DTO files trong cùng namespace.
- Cách fix: xóa duplicate definition, thêm missing fields vào class gốc ở `AuditDtos.cs`.
- Cách phòng ngừa: **LUÔN grep toàn bộ `src/BIM765T.Revit.Contracts/`** trước khi tạo DTO class mới. Đặc biệt cẩn thận với tên generic như `*Result`, `*Item`, `*Response`.

### 2026-03-16 - BUILD_LOG.md — nơi ghi detailed task log cho agent kế tiếp

- Triệu chứng: agent mới vào dự án lặp lại lỗi đã fix, không biết pattern đã chọn, không hiểu wiring checklist.
- Nguyên nhân: LESSONS_LEARNED chỉ ghi bài học ngắn, không ghi chi tiết task-level (files touched, API gotchas, pattern decisions).
- Cách fix: tạo `docs/agent/BUILD_LOG.md` — append-only, mỗi session ghi 1 entry structured gồm: task title, module, files touched, problems & fixes, Revit API gotchas, pattern decisions, reusable for.
- Cách phòng ngừa: mỗi khi hoàn thành task có giá trị, ghi vào BUILD_LOG trước khi kết session. Wiring checklist ở cuối file dùng cho mọi new-service task.

### 2026-03-16 - Externalization QC phai bind theo exact old-new pair, khong duoc ket luan dua tren count hoac schedule row count

- Trieu chung: batch moi co the tao du 95 instance va schedule nhin "co ve dung", nhung van sai vi tri/kich thuoc/truc o tung cap old-new.
- Nguyen nhan: chi nhin tong so instance, row count, hoac audit tong hop ma khong co mapping exact giua old Round va new Round_Project.
- Cach fix: pair deterministic tung instance, ghi `PAIR#`, `OLD=<id>`, `MODE`, `P/SZ/AX/PS` vao comment, va chi pass task khi pair-QC dat 95/95.
- Cach phong ngua: moi workflow replace/externalize ve sau phai co exact reconciliation surface, khong duoc dung count-based acceptance.

### 2026-03-16 - Với nested opening family, anchor đúng phải là midpoint thật của LocationCurve, không phải transform origin

- Trieu chung: instance moi co axis dung nhung van lech vi tri mot khoang kho hieu, nhat la voi nested family chain.
- Nguyen nhan: transform origin cua nested family khong dam bao trung tam opening that trong model space.
- Cach fix: lay midpoint that cua `LocationCurve` (hoac anchor hinh hoc tuong duong) cua old instance de plan va place new wrapper.
- Cach phong ngua: bat ky bai toan migrate opening/penetration nao cung phai xac dinh geometric anchor that truoc, khong duoc mac dinh dung family origin.

### 2026-03-16 - Neu source family la void, material cua wrapper phai theo explicit project policy, khong co gi de "copy" tu source

- Trieu chung: audit/copy material tu source Round cho ket qua rong hoac khong on dinh, dan den confusion ve "material mapping".
- Nguyen nhan: source `Round` la void nen khong co explicit geometry material de ke thua nhu family solid thong thuong.
- Cach fix: dat ro target material policy cho wrapper (`Mii_Penetration`) va log diagnostic rang source chi cung cap subcategory, khong cung cap reusable material.
- Cach phong ngua: truoc khi viet logic copy material, phai phan loai source family la solid hay void; voi void phai chot project-standard material/graphics strategy ngay tu dau.

### 2026-03-16 - Sync hardening phai dua tren preflight va explicit worksharing options, khong duoc "ve an toan" bang TransactionGroup neu ban chat API khong cho rollback nhu model edit thuong

- Trieu chung: de xuat nghe co ve dep la "wrap SynchronizeWithCentral trong TransactionGroup" de rollback neu co van de.
- Nguyen nhan: nham lan giua model mutation transaction va worksharing/file lifecycle operation cua Revit.
- Cach fix: dung preflight, strict approval/context, conservative defaults, va chi cho `relinquish all` khi payload request explicit.
- Cach phong ngua: voi moi API high-risk, phai check ban chat operation truoc; khong duoc dung ngon ngu transaction cho operation ma thuc te khong rollback theo cach do.

### 2026-03-16 - Rate limit phai chan tu dau vao pipe, khong doi toi UI thread moi fail request

- Trieu chung: du tool execute cuoi cung co fail nhanh, flood request van co the day hang doi len UI thread va lam Revit lag/ach.
- Nguyen nhan: control dat qua muon trong pipeline, sau khi request da vao queue/ExternalEvent.
- Cach fix: them sliding-window rate limiter ngay tai pipe ingress, truoc enqueue.
- Cach phong ngua: bat ky control ve tai nguyen/heavy workload nao cung uu tien chan som nhat co the trong pipeline.

### 2026-03-16 - Preview delete an toan nhat la simulate `doc.Delete()` trong transaction rollback de lay exact blast radius

- Trieu chung: dependency scan bang heuristic de sot hoac danh gia sai so element bi xoa kem.
- Nguyen nhan: Revit tu quyet dinh mot phan delete cascade qua internal dependency rules; suy luan ben ngoai de sai.
- Cach fix: thuc hien `doc.Delete(requestedIds)` trong transaction tam, doc danh sach ids bi tac dong, roi rollback va dung ket qua do cho preview/block execute.
- Cach phong ngua: voi cac operation co implicit cascade trong Revit, uu tien preview bang rollback simulation thay vi tu suy luan dependency tree.

### 2026-03-17 - Them intelligence theo lop song song, khong rewrite workflow on dinh

- Trieu chung: muon them auto-fix/fix-loop nhanh nhung neu chen thang vao workflow cu thi rat de gay regression cho 5 workflow dang on dinh.
- Nguyen nhan: workflow runtime cu da duoc su dung nhu mot state machine an toan; intelligence moi lai can iterate nhanh va co logic ra quyet dinh rieng.
- Cach fix: tao `FixLoopService` + tool surface moi song song (`review.fix_candidates`, `workflow.fix_loop_*`) thay vi thay the built-in workflows.
- Cach phong ngua: voi moi dot "nang cap thong minh", uu tien them orchestration layer moi truoc; chi merge vao runtime cu khi pattern da duoc chung minh on dinh.

### 2026-03-17 - Preview schedule fields an toan nhat la tao schedule tam va rollback

- Trieu chung: validate field/filter/sort cho `schedule.create_safe` de sai neu chi dua vao ten parameter thuan text.
- Nguyen nhan: danh sach `SchedulableField` hop le phu thuoc category va context cua chinh `ViewSchedule`.
- Cach fix: tao model schedule tam, lay `GetSchedulableFields()`, map field names/parameter ids, roi rollback transaction.
- Cach phong ngua: bat ky tooling nao can inspect schedule field surface deu nen resolve qua schedule tam thay vi tu doan bang heuristic.

### 2026-03-17 - Export/load/print phai bi rang buoc boi allowlist va preset, khong nhan payload tu do

- Trieu chung: file operation de tro thanh duong thoat khoi policy neu cho payload raw path/raw option tu do.
- Nguyen nhan: export/load/print la boundary giua Revit model va filesystem; neu khong rao lai, AI co the tao output sai cho, ghi de, hoac load asset ngoai policy.
- Cach fix: them `family roots`, `output roots`, va named presets; validate path containment truoc preview/execute.
- Cach phong ngua: bat ky delivery-op moi nao (IFC, DWG, PDF, family load, CSV export...) deu phai di theo pattern `preset-governed + allowlisted root + dry-run`.

### 2026-03-17 - Khong duoc claim live verification neu bridge van offline

- Trieu chung: build pass, install pass, tool da wire xong nhung Revit khong mo hoac bridge chua online.
- Nguyen nhan: de dong nhat "code da ship" voi "runtime da xac minh".
- Cach fix: chay `check_bridge_health.ps1` sau install va ghi ro status thuc te. Neu `BRIDGE_UNAVAILABLE` thi chi duoc claim build/install complete, khong duoc claim live-tested.
- Cach phong ngua: acceptance cho moi tool/runtime wave phai tach lam 2 cot moc: `build/test/install` va `live bridge verify`.

### 2026-03-17 - Trong PowerShell, single-item array trong hashtable de ConvertTo-Json co the bi roi thanh scalar

- Trieu chung: payload preview cho DTO co List<int> / List<string> fail voi INVALID_PAYLOAD_JSON du nhin bang mat thuong thay "co ve dung".
- Nguyen nhan: bieu thuc dang SheetIds = if (<cond>) { @(<value>) } else { @() } trong hashtable co the output scalar neu chi co 1 item, nen JSON ra "SheetIds": 375396 thay vi array.
- Cach fix: ep kieu array truoc khi serialize, vi du [int[]]$(...) hoac [string[]]$(...).
- Cach phong ngua: bat ky script PowerShell nao build payload bridge cho DTO list fields deu phai force array shape truoc ConvertTo-Json, nhat la voi smoke cases chi co 1 sheet / 1 view / 1 element.

### 2026-03-17 - Batch mutation qua bridge co the partial-create roi moi fail; phai xu ly nhu resumable workflow, khong duoc coi la rollback-toan-bo

- Trieu chung: `externalize_round_from_plan.ps1` run live tao duoc `69` wrapper, sau do `26` item fail `RATE_LIMITED`, va con lai 1 orphan wrapper do create thanh cong truoc khi batch vo vao `parameter.set_safe` context mismatch.
- Nguyen nhan: bridge/runtime rate limit va execute theo request don le, nen mot batch dai co the mutate model tung phan truoc khi script ket luan fail. Revit model khong tu rollback toan bo chi vi PowerShell batch bi dung o giua.
- Cach fix: them retry parser cho thong diep `retry after Ns` trong preview/execute, rerun **chi** danh sach source ids that bai, so sanh live inventory voi created-id map, va xoa orphan partial create truoc khi QC.
- Cach phong ngua: voi moi workflow create/update hang loat qua bridge, phai luon coi artifact success map + live inventory la source of truth. Tuyet doi khong rerun lai toan bo batch neu chua reconcile state that cua model.

### 2026-03-17 - `ConvertFrom-Json` doc JSON array root trong PowerShell de bi nest thanh `System.Object[]` khi boc them bang `@(...)`

- Trieu chung: doc `results.json` bang `@(Get-Content ... | ConvertFrom-Json)` cho ra `Count = 1`, phan tu dau la `System.Object[]`, dan den cast loi khi code nghi moi phan tu la 1 row object.
- Nguyen nhan: file JSON root da la array; `ConvertFrom-Json` tra ve object array roi, nen boc them `@(...)` tao thanh mang 1 phan tu chua chinh cai object array do.
- Cach fix: normalize ngay sau khi read: `if ($rows.Count -eq 1 -and $rows[0] -is [System.Array]) { $rows = @($rows[0]) }`.
- Cach phong ngua: bat ky script nao merge/phan tich artifact JSON root-array deu phai flatten ngay sau `ConvertFrom-Json`, nhat la `results.json`, `pairs.json`, `issues.json`.

### 2026-03-17 - Neu externalize da dung family/type/position/size nhung van `ROTATED_IN_VIEW`, cach sua an toan nhat la xoa dung wrapper loi va rerun dung source ids, khong rerun full batch

- Trieu chung: sau batch externalize chinh, QC co the cho `Position=OK`, `Size=OK`, `Type=OK` nhung van con mot nhom nho `ProjectAxis=OFF` do transform cua wrapper instance sai.
- Nguyen nhan: placement fallback co the tao `FamilyInstance` co local BasisX/BasisY lech truc project du geometry/type van dung tren giay.
- Cach fix: lay danh sach wrapper loi tu artifact QC, xoa **chi** nhung wrapper do, rerun `externalize_round_from_plan.ps1` voi **chi** cac `RoundElementId` tuong ung, merge lai execute artifacts, roi QC lai.
- Cach phong ngua: voi workflow family migration co old source element van con ton tai, uu tien pattern `selective delete + selective recreate + re-merge artifact` thay vi co gang "chua chay" bang rerun toan batch hoac chinh tay instance da loi.

### 2026-03-17 - Goi script PowerShell co tham so `[int[]]` tu process ngoai de bi edge case bind array; an toan hon la splat parameter trong cung session

- Trieu chung: rerun selective batch qua `powershell -File script.ps1 -RoundElementIds $array ...` co the cho ket qua bind kho tin hoac message loi nhiu, nhat la khi chen cung switch/argument khac.
- Nguyen nhan: command-line argument passing giua process cha/con cua PowerShell khong ro rang bang parameter binding trong cung session cho array types.
- Cach fix: tao hashtable params va goi script bang `& .\\script.ps1 @params`.
- Cach phong ngua: bat ky helper script nao rerun theo danh sach `ElementId` / `SheetId` / `ViewId` deu nen dung splatting trong cung PowerShell session neu can do tin cay cao.

### 2026-03-17 - Voi Round IFC -> MiTek, `ProjectAxis=OK` van co the export sai orientation downstream

- Trieu chung: `Round_Project` trong Revit QC xanh `95/95`, nhin dung trong model, nhung khi xuat IFC qua MiTek Structure / converter downstream thi cac con dung co the bi convert thanh ngang.
- Nguyen nhan: downstream consumer khong nhat thiet doc orientation theo cung contract voi audit truc trong Revit; `AXIS_Z` co the dung theo project-axis nhung van la export-risk tren path IFC -> MiTek.
- Cach fix: tach **model truth** va **export truth**. Giu `Round_Project` cho Revit/model QC, con export path dung mapping/export-shadow/export metadata rieng; tam thoi coi `AXIS_Z` la export-risk va uu tien canonical horizontal type + pose downstream kieu `STAND_UP`.
- Cach phong ngua: bat ky task Round nao co dich la IFC / MiTek / converter downstream deu phai co acceptance 2 tang: (1) Revit/model QC, (2) sample export downstream dung orientation. Khong duoc dung lai o `ProjectAxisOkCount`.

### 2026-03-17 - `Document.Export(..., IFCExportOptions)` co the doi transaction wrapper du la file export

- Trieu chung: `export.ifc_safe` preview xanh, nhung execute crash voi `ModificationOutsideTransactionException: Modifying is forbidden because the document has no open transaction.`
- Nguyen nhan: trong runtime/Revit setup nay, path IFC export co the cham vao document state noi bo; goi `doc.Export(..., IFCExportOptions)` ngoai transaction khong on dinh nhu assumption ban dau.
- Cach fix: wrap IFC export trong transaction tam, goi `doc.Export(...)`, sau do `RollBack()` de giu model khong bi dirty; live verify execute sau khi deploy lai add-in.
- Cach phong ngua: voi moi delivery-op file lifecycle, khong chi verify preview. Phai live-test execute that va neu thay exception transaction thi uu tien rollback-wrapper thay vi de tool crash.

### 2026-03-17 - Spike IFC cho Round cho thay exporter van giu dung AXIS_X / AXIS_Y / AXIS_Z

- Trieu chung: user nghi van raw `AXIS_Z` co the da bi Revit IFC exporter xoay sai truoc khi vao MiTek.
- Nguyen nhan: chua co bang chung tu file IFC that, nen de nham exporter va downstream converter vao cung mot nho lo nguyen nhan.
- Cach fix: export live 1 file IFC spike 6 mau va doc truc tiep entity/type definition trong IFC. Ket qua: `AXIS_X -> global X`, `AXIS_Y -> global Y`, `AXIS_Z -> global Z`.
- Cach phong ngua: neu downstream convert sai orientation, truoc khi sua family/model, phai tach 2 cau hoi: (1) IFC file luu orientation ra sao, (2) consumer doc IFC ra sao. Neu IFC da giu dung `AXIS_Z` ma consumer van convert ngang, thi do la downstream-risk contract, khong phai exporter flatten.

### 2026-03-17 - Voi MiTek Structure, red axis / local X co ve moi la contract downstream that cho Round

- Trieu chung: team structure convert that cho ket qua chi nhom `AXIS_X` dung; `AXIS_Y` va `AXIS_Z` deu sai, du spike IFC cho thay geometry trong IFC van duoc xuat dung theo Y/Z.
- Nguyen nhan: MiTek co ve dang doc penetration axis theo **instance local X / red axis**, khong doc day du orientation chain cua mapped geometry/swept solid trong IFC.
- Cach fix: doi mental model tu `AXIS_X/Y/Z` sang `PENETRATION_AXIS = LOCAL_X` cho export path MiTek. Nghia la export geometry/local contract phai canonical theo X; `Y` va `Z` deu la downstream-risk neu chi duoc encode trong shape orientation.
- Cach phong ngua: bat ky export shadow/family/surrogate nao cho MiTek deu phai duoc kiem theo 1 cau hoi duy nhat: "red axis cua object sau IFC co trung tam truc penetration that khong?" Neu khong, dung xem la export-safe.

### 2026-03-17 - `AXIS_X` co the canonicalize sang `Y` bang rotate instance trong IFC, nhung khong canonicalize sang `Z` tren family `OneLevelBased` hien tai

- Trieu chung: can kiem chung xem duong `dung 1 type AXIS_X roi quay instance` co giup MiTek doc local X/red axis dung hon khong.
- Nguyen nhan: chua ro Revit IFC encode rotate instance o lop nao, va co phan biet duoc `Y`/`Z` hay khong.
- Cach fix / spike: tao 3 sample `Round_Project` cung type `AXIS_X__L1792__D1088`:
  - `LOCALX_GLOBALX` = khong quay
  - `LOCALX_GLOBALY` = quay `+90°` quanh global `Z`
  - `LOCALX_GLOBALZ` = quay `-90°` quanh global `Y`
  Sau do export IFC that va doc `IFCLOCALPLACEMENT`.
- Ket qua that:
  - `LOCALX_GLOBALY` xuat ra `RefDirection=(0,1,0)` -> local X cua object trong IFC da theo global `Y`.
  - `LOCALX_GLOBALZ` quay ve cung placement/transform contract nhu case `X` -> rotate 3D len `Z` **khong survive thanh contract instance ro rang** trong IFC.
- Cach phong ngua:
  - case `Y` con co cua di theo huong `AXIS_X canonical + in-plane rotate`
  - case `Z` khong duoc ky vong se xong neu chi dung `AXIS_X + rotate 3D`; can surrogate/export shadow/placement contract khac

### 2026-03-17 - Sau khi co spike local-X, phai chot report tach `X/Y` va `Z` o cap pair-level, neu khong session sau rat de loay hoay lai

- Trieu chung: da co bang chung ky thuat cho X/Y/Z nhung neu khong formalize thanh report pair-level thi chat moi van de "ca batch 95" nhu mot khoi duy nhat.
- Nguyen nhan: evidence spike thuong nam roi rac trong handoff/IFC artifact, chua bien thanh work-splitting surface de giao viec tiep theo.
- Cach fix: sinh artifact export-contract tu final QC pairs, gan moi pair vao 1 bucket ro rang (`READY_X_AS_IS`, `READY_Y_CANONICAL_X`, `BLOCKED_Z_RESEARCH`).
- Cach phong ngua: sau moi spike contract/export, phai co them 1 lop report operational hoa evidence thanh danh sach item co the lam ngay vs item bi block.

### 2026-03-17 - Khong duoc de intermediate IFC heuristic override downstream consumer truth

- Trieu chung: phan tich entity-level trong IFC cho case `LOCALX_GLOBALZ` nhin giong case X, dan den ket luan tam la `Z` bi block.
- Nguyen nhan: qua tin vao lop evidence trung gian (`IFCLOCALPLACEMENT` / `IFCMAPPEDITEM`) thay vi doi downstream consumer check that.
- Cach fix: dung representative spike roi lay feedback truc tiep tu consumer cuoi (MiTek). Ket qua thuc te cho thay ca `LOCALX_GLOBALX/Y/Z` deu dung.
- Cach phong ngua: voi bai toan export-contract, neu IFC heuristic va consumer behavior mau thuan nhau, phai uu tien consumer truth va cap nhat ngay operational contract/report.

---

## [2026-03-17] FilteredElementCollector NET 4.8 — không reusable sau enumerate

- **Triệu chứng:**  nhận đúng  nhưng luôn trả  — trong khi  cùng collector thì tìm được.
- **Nguyên nhân:**  trả  lazy. Khi gọi  lần 1 (check FilterId) → collector bị consumed/advanced. Gọi  lần 2 (check FilterName) trên cùng biến → enumerate trên iterator đã hết → không tìm được gì.
- **Cách fix:** Materialize ngay sau khi tạo collector: . Sau đó dùng  thoải mái.
- **Áp dụng cho:** Bất kỳ chỗ nào dùng  mà cần query nhiều lần (filter by Id rồi fallback by Name).
- **Bonus fix:**  không reliable với  trên workshared docs — luôn dùng  thay thế.

---

## [2026-03-17] Bridge CLI — payload phải truyền qua  flag, không phải positional arg

- **Triệu chứng:** Possible reasons for this include:
  * You misspelled a built-in dotnet command.
  * You intended to execute a .NET program, but dotnet-Bridge.dll does not exist.
  * You intended to run a global tool, but a dotnet-prefixed executable with this name could not be found on the PATH. → agent nhận  — serialize/deserialize bị đổ tội oan.
- **Nguyên nhân:** Bridge  chỉ đọc payload qua . Arg positional thứ 2 bị bỏ qua hoàn toàn →  → default DTO.
- **Cách gọi đúng:** Possible reasons for this include:
  * You misspelled a built-in dotnet command.
  * You intended to execute a .NET program, but dotnet-Bridge.dll does not exist.
  * You intended to run a global tool, but a dotnet-prefixed executable with this name could not be found on the PATH. hoặc .
- **Lưu ý:** Các tools không cần input (list, get) vẫn work vì DTO default đủ dùng — che giấu bug này.

---

## [2026-03-17] FilteredElementCollector NET 4.8 — không reusable sau enumerate

- Triệu chứng: InspectFilter nhận đúng FilterName nhưng luôn trả "Không tìm thấy filter" — trong khi ListViewFilters cùng collector thì tìm được.
- Nguyên nhân: FilteredElementCollector.Cast() trả IEnumerable lazy. Gọi .FirstOrDefault() lần 1 (check FilterId) → collector bị consumed. Gọi .FirstOrDefault() lần 2 (check FilterName) trên cùng biến → enumerate hết → không tìm được.
- Cách fix: Materialize ngay sau khi tạo: .Cast<ParameterFilterElement>().ToList(). Sau đó dùng List thoải mái.
- Áp dụng cho: Bất kỳ chỗ nào dùng FilteredElementCollector mà cần query nhiều lần.
- Bonus fix: GetElement(ElementId) không reliable với ParameterFilterElement trên workshared docs — luôn dùng FilteredElementCollector.OfClass(typeof(ParameterFilterElement)) thay thế.

---

## [2026-03-17] Bridge CLI — payload phải truyền qua --payload flag, không phải positional arg

- Triệu chứng: dotnet Bridge.dll view.inspect_filter '{"FilterName":"..."}' → agent nhận FilterName='' — serialize/deserialize bị đổ tội oan.
- Nguyên nhân: Bridge Program.cs chỉ đọc payload qua GetOption(args, "--payload"). Arg positional thứ 2 bị bỏ qua → payloadJson = string.Empty → default DTO.
- Cách gọi đúng: dotnet Bridge.dll tool.name --payload '{"key":"value"}' hoặc --payload path/to/file.json
- Lưu ý: Tools không cần input (list, get) vẫn work vì DTO default đủ dùng — che giấu bug này lâu nay.

### 2026-03-17 - Hai Revit process cung dang nghe mot named pipe se lam bridge tra loi lan document

- Trieu chung: `session.list_open_documents` bao model `SR_QQ...` nhung `document.get_active` lai tra `SR_ST-R...`; mutation/export co nguy co dong nham file.
- Nguyen nhan: tren may dang mo 2 process Revit cung load add-in voi cung `PipeName = BIM765T.Revit.Agent`, bridge ket noi vao process khong on dinh.
- Cach fix: dong het process Revit khong lien quan, chi de lai dung model can thao tac, roi verify lai bang `check_bridge_health.ps1`, `session.list_open_documents`, `document.get_active`.
- Cach phong ngua: truoc moi live mutation/export quan trong, phai xem so process `Revit.exe`. Neu hon 1, coi session la unsafe cho toi khi isolate ve 1 process.

### 2026-03-17 - `@($genericList)` khong phai cach an toan de serialize DTO array cho bridge PowerShell scripts

- Trieu chung: script export batch fail `Argument types do not match` du tung item trong `System.Collections.Generic.List[object]` nhin co ve dung.
- Nguyen nhan: `@($changes)` wrap ca generic list thanh 1 phan tu, khong materialize thanh mang object rows thuc su de serialize JSON dung cho DTO list field.
- Cach fix: dung `@($changes.ToArray())` hoac xay dung mang `@()`/`[pscustomobject]` ngay tu dau truoc `ConvertTo-Json`.
- Cach phong ngua: bat ky bridge payload nao co field list (`Changes`, `Rules`, `ElementIds`, ...), neu dang dung generic list thi phai `.ToArray()` truoc khi dua vao payload hashtable.

### 2026-03-17 - Query type catalog qua `NameContains` qua hep se lam sot alias canonical types

- Trieu chung: export logic Round chi query `AXIS_X__` nen khong thay duoc `AXIS_XY__...` va `AXIS_XZ__...`, dan toi mapping type thieu du alias da load vao project.
- Nguyen nhan: export workflow da doi sang alias type names de giu local-X canonical geometry, nhung filter catalog van theo assumption cu.
- Cach fix: doi `NameContains` sang `AXIS_` va loc exact type names trong code.
- Cach phong ngua: moi khi doi naming contract/type alias cho family export, phai soat lai toan bo script query type catalog de tranh hardcode prefix cu.

### 2026-03-17 - Neu da co persisted approval tokens thi khong duoc doi sang token crypto kieu stateless bang key random theo process

- Trieu chung: hardening security nghe hop ly nhung neu key ky token doi sau moi lan restart thi token preview/execute cu se vo hieu hoa ngay.
- Nguyen nhan: nham lan giua bai toan "chong forgery" va bai toan "survive restart".
- Cach fix: giu token server-side, harden store-at-rest bang protected persistence thay vi HMAC ngau nhien theo process.
- Cach phong ngua: truoc moi de xuat security change, phai check no co pha vo behavioral contract hien co hay khong.

### 2026-03-17 - Timeout hardening phai xay tren timeout infra dang co, khong duoc rewrite tu dau neu pipe/external-event boundary da on

- Trieu chung: de xuat ban dau muon them timeout moi nhung repo da co `RequestTimeoutSeconds` va pending cancellation.
- Nguyen nhan: doc plan ma chua map day du vao code that.
- Cach fix: them `ExecutionTimeoutMs` vao manifest va de `PipeServerHostedService` dung policy do khi cho ket qua.
- Cach phong ngua: production hardening truoc het la giam complexity, khong phai tang so he thong song song.

### 2026-03-17 - Logging context cho bridge co queue + external event phai dung scope co the restore, khong dung static string toan cuc

- Trieu chung: plain text log du de doc bang mat nhung rat kho trace 1 request qua nhieu lop va rat de leak context giua cac request.
- Nguyen nhan: khong co scope mechanism cho `correlation/tool/source`.
- Cach fix: `IAgentLogger.BeginScope(...)` + `AsyncLocal` state trong `FileLogger`.
- Cach phong ngua: bat ky telemetry nao di qua async boundary deu phai co scoped context ro rang.

### 2026-03-17 - Fix-loop verification phai bam theo tap action duoc chon, khong phai toan bo candidate universe

- Trieu chung: planner de xuat 10 actions, operator chi approve 2 action nhung verify van so voi expected delta cua ca 10 -> report partial/pass de bi sai su that.
- Nguyen nhan: `ExpectedIssueDelta` duoc tinh o phase plan tu toan bo candidate set thay vi selected set.
- Cach fix: luu `RecommendedActionIds` va `SelectedActionIds`, sau do recompute expected delta tu selected actions trong `VerifyCore()`.
- Cach phong ngua: bat ky supervised workflow nao co selective approval thi verification phai theo approved intent, khong theo superset proposal.

### 2026-03-17 - Playbook intelligence muon mo rong ma van nhanh thi phai tach decision logic thanh pure helper + cache override file

- Trieu chung: service fix-loop de bi phinh va moi lan review/plan deu deserialize playbook lai tu disk.
- Nguyen nhan: rule resolution va path resolution dang nam tan man trong service code.
- Cach fix: tach `FixLoopDecisionEngine` (rule match, sort, recommendation, path candidates) va cache playbook theo `fullPath + lastWriteUtc`.
- Cach phong ngua: voi rule-driven workflow, uu tien pure helper testable truoc khi nghiem trong hoa bang facade/refactor lon.

### 2026-03-17 - Rule fix-loop cho parameter/view nen co do dac hieu theo project truoc khi them them scenario moi

- Trieu chung: rule chi dua theo parameter name hoac view type thuan tuy se tao candidate dung ky thuat nhung kem thong minh o model that.
- Nguyen nhan: thieu context category/family/current-template/sheet-prefix trong rule match.
- Cach fix: mo rong playbook rule voi `CategoryName`, `FamilyName`, `ElementNameContains`, `CurrentTemplateNameContains`, `SheetNumberPrefix`, `Priority`, `Recommendation`.
- Cach phong ngua: Phase 2 intelligence nen nang chat luong quyet dinh truoc, khong nen them qua nhieu scenario khi rule context con ngheo.

### 2026-03-17 - Live fix-loop execute phai luon truyen `ExpectedContextJson` tu plan sang execute neu tool bi danh dau high-risk

- Trieu chung: preview/plan hop le nhung execute bi chan boi `HIGH_RISK_REQUIRES_CONTEXT` / `CONTEXT_MISMATCH`.
- Nguyen nhan: script chi dua `ApprovalToken` va `PreviewRunId`, nhung bo qua `ExpectedContextJson` ma runtime can de khoa scope thuc thi.
- Cach fix: lay `ExpectedContextJson` tu plan payload/run va truyen nguyen van vao execute request.
- Cach phong ngua: bat ky script mutation nao dung high-risk lane deu phai coi `ExpectedContextJson` la thanh phan bat buoc cua handoff preview -> execute, giong nhu approval token.

### 2026-03-17 - Khi wrapper script hong sau buoc execute, artifact JSON van la source-of-truth; dung do de ket luan thay vi rerun vo i vang

- Trieu chung: script PowerShell fail o buoc tong hop cuoi cung (`property Summary not found`) lam de tuong nhu ca workflow that bai.
- Nguyen nhan: buoc doc ket qua o script sai shape DTO, trong khi runtime da execute va verify xong truoc do.
- Cach fix: luu tung response/payload thanh file artifact roi doc lai cac file `apply-execute`, `verify`, `post-check` de ket luan su that.
- Cach phong ngua: voi live mutation scripts, luon persist moi milestone response truoc khi lam reporting logic, va neu script fail muon thi uu tien doc artifact thay vi chay lai ngay.

### 2026-03-17 - Neu roadmap qua lon thi phai cat mot foundation slice co the ship truthfully, khong duoc gia vo da xong toan bo vNext

- Trieu chung: plan tam nhin copilot rat dung nhung neu implement ca cụm trong mot turn thi de roi vao tinh trang "tool count tang" nhung khong co durable seam that.
- Nguyen nhan: gop qua nhieu muc tieu (runtime split, graph, app, memory, planner, verifier) vao cung mot lan thay doi.
- Cach fix: ship foundation slice truoc: durable task runs + context broker primitives + runtime health + hot graph snapshot.
- Cach phong ngua: voi roadmap lon, moi dot ship phai co mot lop gia tri hoan chinh, build/test duoc, va mo duong ro rang cho buoc tiep theo.

### 2026-03-17 - Context budget cho AI khong duoc giai bang cach nap them docs; phai co anchor, bundle, summary, va tool lookup

- Trieu chung: khi he thong co nhieu docs/playbooks/artifacts/tool manifests, AI de bi tran context hoac phai mo file dai de tim thong tin co ban.
- Nguyen nhan: thieu lop retrieval nho gon phia truoc Revit/tool platform.
- Cach fix: them `context.resolve_bundle`, `context.search_anchors`, `artifact.summarize`, `memory.find_similar_runs`, `tool.find_by_capability` va hot-state surface.
- Cach phong ngua: bat ky capability moi nao cung nen duoc nghi kem retrieval surface va compact summary surface, khong chi tool execute.

### 2026-03-17 - Task-level API co gia tri hon viec tiep tuc nhan tool raw khi muc tieu la copilot-grade task completion

- Trieu chung: raw tools va workflows co the manh, nhung AI van phai tu nho state, tu chain call, va rat kho resume/handoff.
- Nguyen nhan: thieu durable `task.*` abstraction bao quanh plan -> preview -> approve -> execute -> verify -> summarize.
- Cach fix: them `task.*` API va durable `TaskRun` store de bao state, selected actions, verifier, artifacts, va next action.
- Cach phong ngua: voi bounded BIM jobs, mac dinh di qua task surfaces; raw tools chi dung cho debug, special-case, hoac layer substrate.

### 2026-03-17 - Resume an toan khong phai la "retry lai"; phai chon recovery branch dua tren checkpoint truth

- Trieu chung: task da preview/approve mot phan nhung `resume` van de nhay thang vao execute, de sai lane khi context da drift hoac approval dang cho user.
- Nguyen nhan: runtime chi biet run status chung, chua co checkpoint/recovery model ro rang.
- Cach fix: persist `TaskCheckpointRecord`, suy ra `TaskRecoveryBranch`, va de `task.resume` chon branch preview/approve/execute/verify/summary thay vi retry mu quang.
- Cach phong ngua: moi durable workflow/task runtime sau nay deu phai xem resume la bai toan decision, khong phai bai toan loop lai cung mot API call.

### 2026-03-17 - Neu tool call that bai ma khong persist last error vao durable run thi copilot state se noi doi sau khi tra exception

- Trieu chung: bridge nhan `CONTEXT_MISMATCH` hoac `APPROVAL_EXPIRED`, nhung `task.get_run` sau do van trong nhu task chi dang pending binh thuong.
- Nguyen nhan: loi chi song trong response exception, khong duoc ghi vao state store.
- Cach fix: `PersistFailure(...)` cap nhat `LastErrorCode`, `LastErrorMessage`, checkpoint blocked, va recovery branches truoc khi throw.
- Cach phong ngua: voi bat ky task runtime nao co durable state, failure path phai co gia tri persistence ngang voi success path.

### 2026-03-17 - Runtime tool-count parity khong du de ket luan behavior moi da live neu ta thay doi semantics ben trong tool cu

- Trieu chung: `RuntimeToolCount = SourceToolCount` va required tools deu du, nhung smoke van cho thay `SupportsCheckpointRecovery = false`, checkpoint/recovery fields bang 0.
- Nguyen nhan: phase nay thay doi behavior va payload cua `task.*` hien co, khong them tool name moi; count parity vi vay khong bat duoc runtime stale.
- Cach fix: live smoke phai verify field-level truth (`SupportsCheckpointRecovery`, `CheckpointCount`, `RecoveryBranchCount`, v.v.) va neu can thi restart Revit sau moi deploy co thay doi behavior.
- Cach phong ngua: voi moi wave ma tool surface khong doi nhung semantics doi, dinh nghia bo smoke assertions theo payload shape/meaning, khong chi theo tool count.

### 2026-03-18 - Durable schema additive thay doi ma khong normalize state cu thi hot-state/runtime summary co the crash du semantic moi da live

- Trieu chung: sau restart, `session.get_runtime_health` va `task.*` cho thay checkpoint/recovery da live, nhung `context.get_hot_state` lai crash `NullReferenceException`.
- Nguyen nhan: mot so `TaskRun` JSON cu khong co cac collection moi (`Checkpoints`, `RecoveryBranches`, `ChangedIds`, ...), va summary path doc thang state cu khong normalize.
- Cach fix: normalize `TaskRun` tren store read/save/list va bo sung defensive normalization truoc cac summary/recovery operations.
- Cach phong ngua: bat ky durable DTO nao duoc mo rong additively deu phai co read-normalization strategy cho state cu; khong duoc tin vao property initializer sau deserialization.

### 2026-03-18 - Hot-state final chỉ nên coi là pass khi cả runtime health lẫn graph/pending-task payload đều đọc được trong cùng session

- Triệu chứng: `SupportsCheckpointRecovery=true` có thể đã live, nhưng nếu `context.get_hot_state` vẫn crash thì copilot context lane chưa thật sự sẵn sàng.
- Nguyên nhân: checkpoint/recovery và hot-state đi qua các đường code khác nhau; một bên có thể pass trong khi bên kia vẫn vướng legacy durable-state hoặc summary crash.
- Cách fix: luôn verify cùng lúc `session.get_runtime_health` và `context.get_hot_state`, rồi lưu artifact live cuối cùng làm source of truth.
- Cách phòng ngừa: mọi wave copilot sau này phải có smoke summary gộp cả runtime, hot-state, durable run, context bundle, và capability lookup; không kết luận theo một tool đơn lẻ.

### 2026-03-18 - Khi old/new penetration family co catalog type rat lech nhau, phai pair instance live thay vi copy theo family type

- Trieu chung: user can copy parameter tu Penetration Alpha M sang Penetration Alpha, nhung lane type copy bi tac vi source co 59 type con target chi co 3 type.
- Nguyen nhan: day la bai toan migrate data tren instance sau replace, khong phai bai toan type-to-type parity.
- Cach fix: inventory live hai family, pair 95 -> 95 instance theo signature penetration + vi tri, roi batch copy Comments, Mark, va writable Mii_* qua parameter.set_safe.
- Cach phong ngua: trong penetration replace jobs, check source/target type count ngay tu dau; neu catalog lech manh thi doi mental model sang instance pairing de tranh debug sai lane.

### 2026-03-18 - Nested family type mapping theo tung parent family type khong duoc lam bang ChangeTypeId don thuan

- Trieu chung: tool tao du child type theo ten parent type (`59/59`) trong `Penetration Alpha M`, nhung verify sau execute van bao `AssignCount = 58`; nested child type khong thay doi dung theo tung parent type.
- Nguyen nhan: `nestedInstance.ChangeTypeId(...)` trong family editor chi doi type cua nested instance hien hanh, nhung khong tao co che luu rieng theo tung `FamilyManager.CurrentType` nhu mong doi.
- Cach fix: tao/reuse mot **family type control parameter** cho nested family, associate parameter `Type` cua nested instance vao family parameter do, roi `FamilyManager.Set(controlParameter, targetSymbol.Id)` cho moi parent type.
- Cach phong ngua: bat ky bai toan "nested child family phai theo type cua family me" nao cung phai uu tien pattern `associate nested Type -> family type parameter -> set per parent type`, khong di thang bang `ChangeTypeId` neu chua co bang chung live cho thay no persist theo type.

### 2026-03-18 - Family doc mo tu Edit Family co the khong co OwnerFamily.Name, nen check target document phai co fallback theo title/path

- Trieu chung: tool dang target dung `Penetration Alpha M.rfa`, nhung van fail `REVIT_CONTEXT_MISSING` vi he thong doc duoc ten family rong.
- Nguyen nhan: voi mot so family document mo qua `Edit Family`, `doc.OwnerFamily?.Name` co the rong hoac khong du on dinh de lam identity check.
- Cach fix: check family doc theo fallback chain: `OwnerFamily.Name` -> `Path.GetFileNameWithoutExtension(doc.Title)` -> `Path.GetFileNameWithoutExtension(doc.PathName)`.
- Cach phong ngua: trong family-doc workflow, neu target la file `.rfa` dang mo, khong duoc hardcode identity chi bang `OwnerFamily`; phai co fallback theo title/path de tranh false negative context gate.

### 2026-03-18 - Đừng promote kết luận trung gian của Round export thành current truth nếu chưa sync lại với downstream check cuối và handoff mới nhất

- Triệu chứng: memory/docs dễ tự mâu thuẫn, vì một số note cũ còn ghi `Z blocked` hoặc contract alias `AXIS_XY__...` / `AXIS_XZ__...`, trong khi downstream check sau đó đã xác nhận local-X spike đúng cho cả X/Y/Z.
- Nguyên nhân: lịch sử điều tra export đi qua nhiều hypothesis trung gian; nếu chỉ append log mà không cập nhật `PROJECT_MEMORY.md`, handoff, và task blueprint thì chat mới rất dễ bám nhầm conclusion cũ.
- Cách fix: current truth cho Round phải chốt ở 3 nơi cùng lúc: `PROJECT_MEMORY.md`, `ROUND_TASK_HANDOFF.md`, và `TASK_JOBDIRECTION_ROUND_EXPORT_IFC_MITEK_CASE.md`.
- Cách phòng ngừa: với mọi task downstream-heavy, coi `BUILD_LOG.md` là chronology; current truth phải nằm ở memory/handoff mới nhất, và nếu hướng cũ đã bị supersede thì phải ghi rõ là superseded.
### 2026-03-19 - Tool metadata phai song tai ToolRegistry, khong infer theo ten tool

- Trieu chung: `session.list_tools`, `session.get_capabilities`, `tool.find_by_capability`, `context.get_hot_state`, va MCP `tools/list` co the drift metadata khi tool moi duoc them hoac rename.
- Nguyen nhan: metadata bi suy doan tu naming convention o nhieu lop khac nhau thay vi khai bao tai diem register tool.
- Cach fix: dua `RequiredContext`, `TouchesActiveView`, `BatchMode`, `Idempotency`, `PreviewArtifacts`, `RiskTags`, `RulePackTags`, va timeout override ve `ToolRegistry` qua metadata/preset explicit; chi cho phep derive cac invariant tu `PermissionLevel`/`ApprovalRequirement`.
- Cach phong ngua: `ToolNames` chi nen la string constants; MCP `tools/list` phai doc live `session.list_tools` va fail-closed neu bridge khong tra catalog hop le, khong fallback heuristic/reflection.
### 2026-03-19 - Root repo config nen dat o 1 cho de giam diff noise va bat analyzer dong bo

- Trieu chung: file line ending va format de drift giua may/agent, build quality phu thuoc IDE tung nguoi, va them analyzer theo tung project rat de lech nhau.
- Nguyen nhan: thieu repo-level `.editorconfig`, `.gitattributes`, va `Directory.Build.props` cho quality defaults.
- Cach fix: them `.editorconfig` de chot formatting co ban, `.gitattributes` de normalize line endings/binary assets, va bat `Microsoft.CodeAnalysis.NetAnalyzers` trong `Directory.Build.props` cho toan solution.
- Cach phong ngua: moi rule formatting/analyzer chung phai di qua repo root; khong lap lai config tung csproj tru khi co ly do rat dac biet.
### 2026-03-19 - Analyzer cleanup wave 1 nen uu tien fix semantic thuc te truoc: culture explicit, string compare explicit, async stream overload dung, roi moi tinh den versioning

- Trieu chung: bat NetAnalyzers xong warning no lon nhat khong nam o business logic ma nam o `ToString(...)`, `ToUpper()`, `StartsWith(...)`, `ReadAsync/WriteAsync`, va command signature.
- Nguyen nhan: code cu chay duoc nhung duoc viet theo default locale/default overload/default interface naming, nen analyzer coi la co rui ro behavior drift hoac readability drift.
- Cach fix: dung `CultureInfo.InvariantCulture` cho log/artifact/id/timestamp, dung `StringComparison`/direct numeric compare cho string checks, dung `ToUpperInvariant()`, dung `Stream.ReadAsync/WriteAsync(Memory<byte>)`, va dat ten parameter `commandData` dung theo `IExternalCommand`.
- Cach phong ngua: wave dau tien sau khi bat analyzer nen d?n cac warning semantic-relevant va low-risk truoc; de lai warning can policy nhu assembly versioning cho wave rieng de tranh chot version vo i.

### 2026-03-19 - Versioning baseline cho SDK solution phai di kem generated assembly info, neu khong analyzer CA1016 van noi dung du root props da co version

- Trieu chung: da set `Version`, `AssemblyVersion`, `FileVersion` o `Directory.Build.props` nhung `CA1016` van bao thieu assembly version; build artifact net8/netstandard van co xu huong tro ve `0.0.0.0`.
- Nguyen nhan: mot so csproj dang tat `GenerateAssemblyInfo`, nen SDK khong phat sinh assembly attributes tu version properties o repo root.
- Cach fix: chot baseline `1.0.0 / 1.0.0.0` o `Directory.Build.props`, bat lai `GenerateAssemblyInfo` cho cac SDK projects khong co manual assembly attributes, va sua them warning test nho de dat build `0 Warning(s)`.
- Cach phong ngua: chi tat `GenerateAssemblyInfo` khi project thuc su co `AssemblyInfo.cs` rieng; neu khong, repo-level versioning + generated assembly info la pattern on dinh va de bao tri nhat.

### 2026-03-19 - Default DTO restore khong duoc dung string search tren raw JSON

- Trieu chung: field default nhu `MaxResults=200` hoac `ViewScopeOnly=true` bi mat khi ten field xuat hien trong string value, du caller khong gui property do.
- Nguyen nhan: `JsonContainsField()` truoc day chi search substring tren raw JSON, nen false-positive neu value text trung ten field.
- Cach fix: doi sang parser top-level property names trong `JsonUtil`, chi xet property that cua object va bo qua nested values/strings.
- Cach phong ngua: moi logic contract compatibility/phuc hoi default phai dua tren parse co cau truc, khong dua tren substring search.

### 2026-03-19 - Pipe ingress nen tach processor doc lap de test auth/rate-limit/protocol

- Trieu chung: khi parse request, auth caller, rate limit, timeout, va protocol nam chung trong `PipeServerHostedService`, rat kho test ma khong dung pipe that.
- Nguyen nhan: networking concerns va policy concerns bi tron trong transport implementation.
- Cach fix: tach `PipeRequestProcessor` + `IPipeCallerAuthorizer` + `IPipeRequestScheduler`, de test duoc invalid JSON, reject caller, rate limit, protocol mismatch, va happy path bang fake scheduler.
- Cach phong ngua: bat ky boundary IO nao trong repo cung nen co lop processor/policy testable truoc khi vao transport implementation.

### 2026-03-19 - Coverage gate trong repo nay nen gate theo package line-rate, khong gate theo whole-report

- Trieu chung: report coverage tu `BIM765T.Revit.Agent.Core.Tests` co the gom nhieu package (`Contracts`, `Copilot.Core`, `Agent.Core`), nen overall line-rate rat de gay hieu sai.
- Nguyen nhan: 1 test project dang cover nhieu assembly/project tham chieu khac nhau, trong khi maturity cua tung package khac nhau.
- Cach fix: parse Cobertura theo `package name` va gate rieng cho `BIM765T.Revit.Contracts`, `BIM765T.Revit.Copilot.Core`, va `BIM765T.Revit.Agent.Core` bang `tools/testing/check_coverage_thresholds.ps1`.
- Cach phong ngua: moi khi them pure-core seam moi co the runner-test duoc, nen gate theo package/assembly do thay vi ep threshold tren report tong.

### 2026-03-19 - ToolExecutor nen co pure-core library rieng, wrapper Revit-bound chi giu UI-thread va journal enrichment

- Trieu chung: `ToolExecutor` vua check policy high-risk, vua map exception/status, vua format journal summary, vua doc active doc/view; unit test rat kho vi dinh `UIApplication`.
- Nguyen nhan: pure orchestration va Revit-bound context handling bi tron trong cung 1 class.
- Cach fix: tach core execution vao `BIM765T.Revit.Agent.Core` (`ToolExecutionCore`), de test unsupported/disabled/high-risk/exception-mapping/finalize-summary ma khong can Revit; wrapper trong Agent chi con invoke handler tren UI thread va enrich document/view key.
- Cach phong ngua: bat ky logic nao khong can `UIApplication`, `Document`, `View`, `Transaction`, hoac `ExternalEvent` thi uu tien dat vao runner-capable pure-core assembly.

### 2026-03-19 - Repo-local skill docs phai duoc coi la knowledge packs, khong duoc danh dong voi runtime-installed skills

- Trieu chung: de lap luan nham rang chi can tao `skills/*` trong repo la runtime AI se tu load/use nhu mot he skill system that.
- Nguyen nhan: repo docs va runtime skill infrastructure la 2 lop khac nhau; neu khong noi ro thi docs dep nhung drift voi behavior that.
- Cach fix: dat intelligence docs o `docs/agent/skills/`, viet `SKILL.md` theo kieu router/index, va de runtime chi doc phan that su can qua file curated nhu `TOOL_GRAPH.overlay.json` va rulesets copy sang output.
- Cach phong ngua: moi artifact tri thuc moi phai xac dinh ro no la human-curated knowledge pack, runtime config, hay public contract.

### 2026-03-19 - Structured-first la cach mo intelligence an toan nhat cho BIM agent

- Trieu chung: de bi hap dan boi huong visual/base64/compare-view som, nhung token cost va do tin cay thuc te rat kho kiem soat.
- Nguyen nhan: sheet/schedule/view/family trong Revit rat giau visual complexity, neu nhay thang vao image-heavy payload se lam runtime nang va kho debug.
- Cach fix: uu tien `data.extract_schedule_structured` truoc, sau do `review.smart_qc` aggregate tu data/read surfaces da co; chi can artifact path cho visual layer, khong inline base64 o v1.
- Cach phong ngua: voi intelligence tool moi, mac dinh ship structured payload va machine-readable finding truoc khi mo rong sang visual reasoning.

### 2026-03-19 - Structured sheet intelligence nên bắt đầu từ sheet surface + viewport metrics, không cố nhảy thẳng vào image-first workflow

- Triệu chứng: dễ bị cuốn vào hướng export PNG/base64 để AI “nhìn sheet”, nhưng payload phình rất nhanh và khó test.
- Nguyên nhân: sheet là bài toán visual, nhưng phần có giá trị cho QC thực ra đến từ metadata, viewport layout, schedule placement, sheet notes, và annotation counts.
- Cách fix: `sheet.capture_intelligence` v1 chỉ trả structured metadata + layout map + optional artifact path; không inline image/blob vào response.
- Cách phòng ngừa: với tool intelligence mới, luôn ưu tiên structured-first, artifact-reference-second, visual-inline-last.

### 2026-03-19 - Family X-Ray v1 phải degrade an toàn khi family không editable hoặc là in-place

- Triệu chứng: nhiều family trong project không mở sâu được bằng `EditFamily`, hoặc anatomy trả thiếu nếu family là in-place/non-editable.
- Nguyên nhân: Revit family ecosystem không đồng nhất; không phải mọi family đều cho deep inspection theo cùng một đường.
- Cách fix: `family.xray` v1 luôn trả project-level truth trước (family/type/category), rồi mới thêm deep data nếu mở được family document; nếu không, trả issue rõ ràng thay vì giả vờ đủ dữ liệu.
- Cách phòng ngừa: tool read-only dạng diagnostic phải nói rõ phạm vi truth của nó; không over-claim khi API context không hỗ trợ.

### 2026-03-20 - Hardening family authoring nên bắt đầu bằng benchmark deterministic, không nhảy thẳng vào family “quái vật”

- Triệu chứng: khi trộn geometry phức tạp, connector, alignment, nested family và type logic vào cùng một pass, rất khó xác định lane nào của backend đang gãy thật.
- Nguyên nhân: family authoring trong Revit có nhiều boundary khác nhau (document lifecycle, reference planes, forms, visibility, materials, connectors, alignment); nếu benchmark không tách lớp, diagnostics sẽ nhiễu.
- Cách fix: dựng benchmark deterministic `ME_Benchmark_Parametric_ServiceBox_v1` và chạy theo thứ tự cố định `create doc -> params/formulas -> planes -> subcats -> forms -> visibility/material -> type catalog -> xray/list_geometry/save`.
- Cách phòng ngừa: mọi benchmark family backend mới phải có core pass rõ ràng và stretch scenarios tách riêng (alignment, connector, blend/revolution). Đừng biến benchmark đầu tiên thành production family hoàn chỉnh rồi tự làm mờ source of failure.

### 2026-03-20 - Boundary vao Revit `net48` khong nen bi ep thanh public gRPC endpoint

- Trieu chung: neu tiep tuc day public protocol thang vao add-in thi phai tu giai quyet versioning, timeout, streaming, va compatibility o boundary kho thay doi nhat cua he thong.
- Nguyen nhan: Revit add-in bi khoa boi `net48`, trong khi public IPC hien dai lai hop hon o `.NET 8`; ep gRPC truc tiep vao add-in se lam boundary Revit phinh to va kho harden.
- Cach fix: tach `BIM765T.Revit.WorkerHost` lam public control plane gRPC, giu `BIM765T.Revit.Agent` la private execution kernel, va noi 2 ben bang protobuf-delimited kernel pipe.
- Cach phong ngua: moi nang cap protocol/public client surface sau nay phai uu tien WorkerHost; add-in chi nhan contract nho, private, audit duoc, va ton trong `ExternalEvent` + approval invariants.

### 2026-03-20 - Event sourcing local nen dung SQLite WAL + snapshot, khong tiep tuc file-backed JSON lam source of truth

- Trieu chung: file state JSON de bi ghi de, de corrupt khi crash giua chung, va kho replay/forensics cho mission runtime.
- Nguyen nhan: luu current state thay vi append-only history khong phu hop cho lane approval/mutation co nhieu checkpoint.
- Cach fix: dung `events + snapshots + outbox + memory_projection` tren SQLite, append event cung transaction voi snapshot, va chay background WAL checkpoint.
- Cach phong ngua: moi state runtime co tinh durable/recovery cao deu nen uu tien append-only stream + projection, khong luu mot JSON state duy nhat roi ghi de.

### 2026-03-20 - WorkerHost health CLI phai bound timeout va xuat pure JSON

- Trieu chung: `BIM765T.Revit.WorkerHost.exe --health-json` co the treo khi kernel pipe khong ton tai, hoac in them HttpClient info log lam script `ConvertFrom-Json` fail.
- Nguyen nhan: probe kernel/public pipe khong co timeout rieng, va logging default cua `System.Net.Http.HttpClient` van o muc `Information`.
- Cach fix: bat timeout trong `KernelPipeClient` va `RuntimeHealthService`, dong thoi filter `System.Net.Http.HttpClient` xuong `Warning` de CLI chi xuat JSON sach.
- Cach phong ngua: moi helper CLI machine-readable trong repo phai co timeout bounded cho IO probes va khong duoc chen log text vao stdout neu contract dau ra la JSON.

### 2026-03-20 - WorkerHost exe nen roll forward de chay duoc tren runtime moi hon

- Trieu chung: may local co .NET SDK moi hon nhung thieu `Microsoft.AspNetCore.App 8.x`, khi chay `BIM765T.Revit.WorkerHost.exe` thi bi app-launch failed du solution van build duoc.
- Nguyen nhan: apphost mac dinh doi dung major runtime, trong khi may dev chi co shared runtime `10.x`.
- Cach fix: dat `RollForward=Major` cho `BIM765T.Revit.WorkerHost.csproj` de WorkerHost van chay duoc tren runtime ASP.NET Core moi hon neu khong co 8.x.
- Cach phong ngua: moi sidecar/tool `net8.0` duoc ship nhu executable nen co policy roll-forward ro rang hoac publish self-contained neu muon giam phu thuoc runtime tren may BIM user.

### 2026-03-20 - WorkerHost service mode can harden public pipe, nhung van phai coi state path va service account la explicit operational decision

- Trieu chung: khi sidecar duoc nang len thanh Windows Service, operational team de mac dinh chay duoi account he thong ma khong de y state root va named-pipe access pattern thay doi theo account.
- Nguyen nhan: service mode va foreground dev mode khong dung cung user profile; `%APPDATA%` cua LocalSystem/LocalService khac voi user BIM dang mo Revit.
- Cach fix: harden WorkerHost theo huong service-aware (`UseWindowsService`, named-pipe ACL config, installer script rieng), nhung deployment script phai expose ro `ServiceAccount`, startup type, va khong giau operational choice.
- Cach phong ngua: moi sidecar chay duoi Windows Service trong repo phai co script install/uninstall ro rang va phai document ro profile/state impact truoc khi default cho BIM user.

### 2026-03-20 - Qdrant local companion nen bootstrap bang startup task, khong ep thanh classic Windows Service khi chua co wrapper service rieng

- Trieu chung: de bi hap dan dung `sc.exe create qdrant.exe` cho nhanh, nhung raw `qdrant.exe` khong co service wrapper behavior/observability toi uu cho desktop companion use case.
- Nguyen nhan: Qdrant local trong lane nay la companion process theo user session, can config path/storage path ro rang hon la service semantics cua Windows.
- Cach fix: dung scheduled task per-user luc logon de start `tools/infra/start_qdrant_local.ps1 -Foreground`, giu config bootstrap va logs trong companion root.
- Cach phong ngua: binary ben thu 3 nao chua service-aware thi uu tien startup task hoac wrapper service rieng, khong cai truc tiep thanh classic Windows Service roi moi harden sau.

### 2026-03-20 - Outbox projector khong duoc de status `processing` vo thoi han sau crash

- Trieu chung: neu projector process bi chet giua luc da lease outbox item nhung chua complete, row bi mac ket o `processing` va khong bao gio duoc lease lai.
- Nguyen nhan: Wave 3 moi chi co `pending -> processing -> completed/ignored`, chua co co che reclaim lease stale.
- Cach fix: them `leased_utc`, `attempt_count`, `next_attempt_utc`, `last_error`, va logic `RequeueStaleProcessingAsync(...)`; projector reclaim stale lease truoc khi lease batch moi.
- Cach phong ngua: bat ky outbox/queue local nao co trang thai in-flight deu phai co lease timeout + reclaim path, neu khong crash recovery chi dung o event stream ma gay ket projection layer.

### 2026-03-20 - Retry/backoff can tach khoi dead-letter de operator phan biet loi tam thoi va loi can can thiep

- Trieu chung: neu tat ca projector failure deu bi ghi `failed` ngay, runtime health khong phan biet duoc item dang cho retry hay item da can operator review.
- Nguyen nhan: state model cua outbox qua phang, thieu `attempt_count` va `next_attempt_utc`.
- Cach fix: them retry/backoff bounded, luu `attempt_count + next_attempt_utc + last_error`, va chi move sang `dead_letter` khi qua `OutboxProjectorMaxAttempts`.
- Cach phong ngua: health/report cua runtime nen surface rieng `BackoffPendingOutboxCount` va `DeadLetterOutboxCount`, tranh danh dong transient issue voi terminal issue.

### 2026-03-20 - Companion install lane nen co package manifest va installer script duy nhat

- Trieu chung: sau khi package `Agent/Bridge/McpHost/WorkerHost`, operator van phai tu nho duong dan script nao can copy va script nao can chay.
- Nguyen nhan: package build moi copy binaries, chua co companion manifest va installer orchestration.
- Cach fix: `tools/infra/package_revit_bridge_build.ps1` gio package them companion scripts/manifest, va `tools/infra/install_enterprise_companion.ps1` gom lane copy WorkerHost + scripts + optional qdrant + optional service/task install.
- Cach phong ngua: moi lane deploy sidecar/companion trong repo nen co 1 entrypoint install ro rang thay vi bat operator ghap tay nhieu script rieng le.

### 2026-03-21 - Bridge hotfix cho benchmark family không thể chỉ giả định legacy JSON pipe còn sống

- Triệu chứng: benchmark runner và health probe có lúc timeout hoặc fail dù Revit add-in đã lên, vì bridge chỉ thử public WorkerHost hoặc legacy JSON pipe.
- Nguyên nhân: runtime hiện tại có thể chỉ mở private kernel pipe `BIM765T.Revit.Agent.Kernel`; public WorkerHost control plane chưa chắc reachable và legacy JSON pipe không còn là nguồn sự thật ổn định.
- Cách fix: `BIM765T.Revit.Bridge` được harden theo thứ tự fallback `WorkerHost -> kernel pipe protobuf -> legacy JSON pipe`, và khi đi qua kernel pipe thì gắn diagnostic `BRIDGE_FALLBACK_KERNEL_PIPE`.
- Cách phòng ngừa: với mọi tool vận hành/benchmark sau này, đừng assume boundary IPC cũ còn tồn tại; phải support boundary private hiện hành của add-in trước rồi mới coi legacy path là fallback cuối.

### 2026-03-21 - Benchmark family batched có thể false-fail vì `ActiveDocEpoch` biến động giữa preview và execute

- Triệu chứng: benchmark đi qua được nhiều bước nhưng đột ngột fail `CONTEXT_MISMATCH` ở các mutation muộn như `family.set_parameter_formula_safe`, dù document/view/selection vẫn đúng.
- Nguyên nhân: expected context hiện so cả `ActiveDocEpoch`; trong family authoring benchmark dài, `DocumentChanged` có thể làm epoch tăng giữa lúc preview và execute, tạo mismatch giả.
- Cách fix: `tools/testing/run_family_authoring_benchmark.ps1` hiện normalize `ResolvedContext.ActiveDocEpoch = 0` trước execute cho lane benchmark. Việc này vẫn giữ check `DocumentKey`, `ViewKey`, selection và approval token, nhưng bỏ volatile epoch khỏi comparison.
- Cách phòng ngừa: với playbook mutation dài nhiều bước trong cùng family doc, chỉ dùng `ActiveDocEpoch` khi thật sự cần bắt thay đổi runtime cấp phiên; đừng để epoch race làm benchmark fail giả rồi che mờ bug authoring thật.

### 2026-03-21 - Family dimension authoring không nên phụ thuộc một view/line duy nhất

- Triệu chứng: benchmark fail ở `family.add_dimension_safe` với lỗi `The direction of dimension is invalid.`
- Nguyên nhân: dimension lane cũ chọn một view và một dimension line cố định; cách này brittle khi cặp reference plane thay đổi giữa width/depth/height trong family doc.
- Cách fix: `ExecuteAddDimension(...)` giờ thử nhiều candidate view, score view phù hợp hơn, và thử nhiều offset line khác nhau trước khi fail; khi pass sẽ log rõ `FAMILY_DIMENSION_VIEW_USED`.
- Cách phòng ngừa: với family authoring backend, dimension/alignment phải là lane retry-aware; đừng giả định có một universal view chiến thắng cho mọi cặp reference plane.

### 2026-03-21 - Agent stack cho Revit can co mot constitution chung + shell XAML/MVVM, neu khong docs va UI se drift rat nhanh

- Trieu chung: Claude/Codex bi phan manh read order, command docs stale model/path, UI dockable pane code-built kho them progress/evidence va tao cam giac vo hon.
- Nguyen nhan: boot files/workspace/global giu logic rieng, trong khi shell cu khong co view-model center va queue/progress/risk state khong duoc surface ro.
- Cach fix: chot `AGENTS.md` + `docs/ARCHITECTURE.md` + `docs/PATTERNS.md` lam canonical constitution, bien repo/workspace/global instructions thanh adapter mong, va doi shell sang XAML/MVVM mission-first.
- Cach phong ngua: moi thay doi startup/model/path/handoff hay UX shell phai di kem audit script + architecture test + memory update; khong de command generic hay stale cache path song song voi current truth.

### 2026-03-21 - Project init phai luu curated refs, khong copy source binaries vao workspace

- **Trieu chung:** workspace/project state rat de bi phinh neu `/init` copy `.rvt`, `.rfa`, `.pdf` hoac dump full extracted text vao repo-local artifacts.
- **Nguyen nhan:** coi project bootstrap nhu mot import pipeline thay vi mot context bootstrap pipeline.
- **Cach fix:** chi luu manifest metadata, fingerprint, summary markdown, project brief, va refs/evidence paths; giu binary va heavy text o source goc cho wave sau.
- **Cach phong ngua:** bat ky flow bootstrap/context nao ve sau deu phai bat dau tu curated metadata + refs, khong tu source copying.

### 2026-03-21 - Thu tu precedence cua context phai duoc chot ro va reused o moi consumer

- **Trieu chung:** neu web, worker, va future MCP retrieval moi noi merge context mot kieu, agent se tra loi drift va kho debug.
- **Nguyen nhan:** layer `core`, `firm`, `project`, `session` rat de bi merge ngau hung theo feature.
- **Cach fix:** chot 1 thu tu bat bien `core safety > firm doctrine > project overlay > session/run memory`, enforce trong `ProjectContextComposer`, va surface summary cung thu tu do cho worker/web.
- **Cach phong ngua:** moi context consumer moi phai tai su dung composer/chuan bundle, khong tu merge tay.

### 2026-03-21 - Deep scan nen la orchestration tu cac read tools nho, khong phai mot mega scanner trong kernel

- **Trieu chung:** y tuong quet ca project de bi truot thanh 1 service Revit-bound rat lon, kho test va de vo boundary.
- **Nguyen nhan:** thay vi tai dung tool read/QC san co, de xu huong "viet scanner tong" trong Agent.
- **Cach fix:** de WorkerHost goi cac tool read-only da co (`review.*`, `sheet.*`, `data.extract_schedule_structured`, `document.*`) va chi own orchestration/report persistence.
- **Cach phong ngua:** truoc khi them Revit service lon moi, kiem tra xem co the compose tu manifest/tool hien co hay khong. Neu co, uu tien compose.

### 2026-03-21 - Web workspace nen an context bundle/summary truoc, khong feed raw report vao UI va prompt

- **Trieu chung:** raw project scan report co the rat dai, khien UI roi, prompt to, va kho tim source refs quan trong.
- **Nguyen nhan:** consumer muon doc truc tiep JSON day du thay vi thong qua curated bundle.
- **Cach fix:** surface summary, counts, strengths/weaknesses, pending unknowns, va top refs trong context bundle; de raw report o artifact path cho explorer khi can.
- **Cach phong ngua:** bat ky surface chat/web moi nao cho Project Brain deu phai co summary contract rieng truoc khi mo raw artifact explorer.
