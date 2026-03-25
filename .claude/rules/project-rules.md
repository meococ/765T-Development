# BIM765T — Project Rules

> Tận dụng AI + Tool để build chất lượng. Quy tắc tồn tại vì craft, không phải vì sợ.

## Architecture Boundaries — Clean Separation cho Scale

Mỗi layer có trách nhiệm rõ, giữ clean để AI agent và human đều navigate dễ:

- **Contracts** (netstandard2.0): DTOs only — shared language giữa mọi layer
- **Agent** (net48): Nơi DUY NHẤT gọi Revit API — execution kernel
- **WorkerHost** (net8.0): Orchestration, memory, routing — bộ não
- **McpHost** (net8.0): stdio JSON-RPC bridge — external interface
- **Copilot.Core** (netstandard2.0): AI/pack services — intelligence layer

## Contract Craft

- `DataMember(Order=N)` append-only → backward compatibility chuyên nghiệp
- String default `""`, List default `new()` → null-safe by design
- `DataContractJsonSerializer` → consistent serialization
- Composition only → flexible, testable contracts

## Transaction Flow — UX tốt, không phải bottleneck

- Mutation qua `preview -> approve -> execute` → user thấy trước, tin tưởng
- Transaction dùng `AgentFailureHandling.Configure()` → graceful recovery
- Non-UI thread mutation qua ExternalEvent → thread-safe by design
- Không `.Result` trên UI thread → tránh deadlock (kỹ thuật, không phải policy)

## Naming Conventions

- Tool modules: `*ToolModule.cs`
- Services: `*Service.cs`
- DTOs: `*Dtos.cs` hoặc `*Dto.cs`
- Tests: `*Tests.cs`

## Git Workflow — Ship Nhanh, Ship Đúng

- Conventional Commits: `<type>(<scope>): <description>`
- Types: feat, fix, docs, style, refactor, perf, test, build, ci, chore
- Scopes: contracts, agent-core, agent, workerhost, bridge, mcp, copilot, tools, docs, ui, packs
- Tests pass trước merge
- Không rewrite shared branch history (professionalism, không phải fear)
