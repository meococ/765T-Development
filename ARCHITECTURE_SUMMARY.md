# BIM765T Revit Bridge - Architecture Analysis Summary

## Quick Overview

**3 Main Processes:**
- **Agent** (Revit Add-in) - UI thread, ExternalEvent handler for mutations
- **WorkerHost** (Service) - gRPC/HTTP server, mission orchestration, LLM planning
- **Bridge** (CLI) - Stateless command-line tool for tool invocation

**Key Pattern:** Dry-run-first mutations with approval gates

## Entry Points

### 1. AgentApplication.cs (Revit Add-in)
- Implements `IExternalApplication`
- OnStartup: Initialize AgentHost, register ribbon & dockable pane
- OnShutdown: Graceful cleanup
- Commands: Chat, Context, Settings, Health, Warnings, Snapshot

### 2. WorkerHost Program.cs (Service)
- ASP.NET Core WebApplication
- gRPC services + HTTP endpoints on named pipes + localhost
- 50+ singletons: Embedding, LLM, Planning, Mission Orchestration, Project Services
- Hosted services: Memory bootstrapper, WAL checkpoint
- Rate limiting: 100 req/min global, 10 req/min for chat

### 3. Bridge Program.cs (CLI)
- Stateless tool invocation
- Fallback chain: WorkerHost gRPC → Kernel Pipe (ProtoBuf) → Legacy Pipe (JSON)
- Parameters: --pipe, --kernel-pipe, --dry-run, --approval-token, --session-id

## Tool Modules (21 Concrete)

```
WorkerToolModule              → worker.message (5-step async workflow)
ElementAndReviewToolModule    → element.query, element.inspect, element.explain
MutationFileAndDomainToolModule → file.save_as_document, document operations
ParameterToolModule           → parameter.set_safe, batch operations
AnnotationToolModule          → annotation.add_text_note_safe
ViewAnnotationAndTypeToolModule → view.create_3d_safe, filter operations
SheetViewToolModule           → Sheet intelligence
FamilyAuthoringToolModule     → Family operations
SpatialIntelligenceToolModule → 3D spatial queries
QueryPerformanceToolModule    → Performance metrics
CommandAtlasToolModule        → Command registry
ScriptOrchestrationToolModule → Dynamic scripts
IntelligenceToolModule        → QC, audits
FixLoopToolModule             → Iterative fixing
CopilotTaskToolModule         → Long-running tasks
PenetrationWorkflowToolModule → MEP penetrations
DataLifecycleToolModule       → Data export
DeliveryOpsToolModule         → Delivery workflows
AuditCenterToolModule         → Centralized audit
WorkflowInspectorToolModule   → workflow operations
```

## IPC Architecture (3 Layers)

### Layer 1: WorkerHost gRPC (Primary)
- Protocol: HTTP/2 over NamedPipe
- Service: `CompatibilityGrpcService.InvokeToolAsync()`
- Type-safe, streaming support

### Layer 2: Kernel Pipe (Fallback)
- Protocol: ProtoBuf (Google.Protobuf)
- Server: `KernelPipeHostedService` (in Agent)
- Compact binary protocol
- 30-second read timeout (prevents resource exhaustion)
- Windows identity verification

### Layer 3: Legacy Pipe (Fallback)
- Protocol: JSON over text
- Backward compatibility

## ExternalEvent & Mutations

### Scheduling Pattern

```
Named Pipe Request
  ↓
ExternalEventPipeRequestScheduler
  • Creates PendingToolInvocation
  • Enqueues to ToolInvocationQueue (3-tier: High/Normal/Low)
  • Raises ExternalEvent
  ↓
ToolExternalEventHandler.Execute()
  • Runs on Revit UI idle
  • 15ms time budget per cycle (60fps target)
  • 3 items per batch
  • Phase A: Workflow continuations
  • Phase B: Queue items
  ↓
Execution or Continuation
  • Returns next workflow step or response
```

### Dry-Run Flow

1. Client sends DryRun=true (default)
2. Mutation service runs in preview mode (no changes)
3. Response: StatusCode=DRYRUN_SUCCEEDED, ApprovalToken=<guid>, ConfirmationRequired=true
4. User approves
5. Client sends DryRun=false, ApprovalToken=<token>
6. ApprovalService validates token
7. Mutation service commits changes
8. Response: StatusCode=EXECUTE_SUCCEEDED, ChangedIds=[101, 102, 103]

## Worker Message Pipeline (5 Steps)

```
USER INPUT: "kiểm tra model health"
  ↓
GatherContextStep (UI, ~100ms)
  • Resolve document, session, context
  • Intent classification (instant)
  → Decision: Conversational OR Full pipeline
  ↓
IF CONVERSATIONAL (greeting, identity, help, context_query):
  ConversationalStep (Async, ~1s)
    • 1 LLM call for natural response
  BuildResponseStep (UI)
    • Return response
ELSE (action intent: qc, mutation, sheet, ...):
  PlanStep (Async, ~500ms)
    • LLM planner (if configured)
    • Decision: {Intent, Goal, SuggestedActions}
  ExecuteIntentStep (UI)
    • Call handler (audit, sheet, family, etc.)
    • Populate ToolCards, ActionCards
  EnhanceStep (Async, ~1-2s, optional)
    • LLM narration (if enhancer configured)
  BuildResponseStep (UI)
    • Assemble final WorkerResponse
  ↓
TOTAL: Conversational ~1-2s, Full ~2-3s
```

## LLM Integration

### AnthropicLlmClient
- Auto-detects protocol from URL
  - `/v1/messages` → Anthropic Messages API
  - Others → OpenAI Chat Completions
- Models: claude-sonnet-4-20250514 (Anthropic), claude-sonnet-4.6 (OpenAI-compatible)
- Timeout: 20 seconds
- Graceful degradation: Empty string on failure

### LlmResponseEnhancer (Seam Layer)
- Falls back to rule-based text if:
  - LLM unconfigured
  - LLM call fails
  - LLM timeout
  - LLM returns empty
- Used for: UI text narration only
- NOT used for: Tool selection, intent routing

### System Prompt (Vietnamese)
```
You are 765T Assistant, a high-quality BIM copilot embedded inside Autodesk Revit.
Always answer in natural Vietnamese with full diacritics.
Address user as 'anh', refer to yourself as 'em'.
Be concrete, useful, concise: 2-5 sentences.
Do not mention internal terms (lane, intent, mission, orchestration, etc.)
unless explicitly asked.
```

## WorkerHost Services

**Core:**
- SqliteMissionEventStore, InMemoryMissionEventBus
- IKernelClient → KernelPipeClient (connects to Agent)

**Embedding & Memory:**
- IEmbeddingClient → HashEmbeddingClient (non-semantic, WARNING)
- ISemanticMemoryClient → QdrantSemanticMemoryClient
- MemorySearchService

**LLM:**
- ILlmPlanner → OpenAiCompatibleLlmClient or NullLlmPlanner

**Mission Orchestration:**
- PlannerAgent, RetrieverAgent, SafetyAgent, VerifierAgent
- MissionOrchestrator

**Catalog & Capability:**
- PackCatalogService, WorkspaceCatalogService, StandardsCatalogService
- PlaybookLoaderService, ToolCapabilitySearchService, PolicyResolutionService

**Project Services:**
- ProjectInitService, ProjectDeepScanService, ProjectContextComposer

**Other:**
- CapabilityHostService, ExternalAiGatewayService, RuntimeHealthService

## Contracts & DTOs

### ToolRequestEnvelope
```json
{
  "RequestId": "abc123",
  "ToolName": "element.query",
  "PayloadJson": "{...}",
  "DryRun": true,
  "ApprovalToken": "token-xyz",
  "RequestedPriority": "normal"
}
```

### ToolResponseEnvelope
```json
{
  "RequestId": "abc123",
  "Succeeded": true,
  "StatusCode": "DRYRUN_SUCCEEDED",
  "ApprovalToken": "token-xyz",
  "ConfirmationRequired": true,
  "ChangedIds": [101, 102, 103],
  "DurationMs": 250
}
```

### Key Status Codes
- `OK`, `READ_SUCCEEDED`, `DRYRUN_SUCCEEDED`, `EXECUTE_SUCCEEDED`
- `CONFIRMATION_REQUIRED` ← Mutation preview
- `TIMEOUT`, `RATE_LIMITED`, `POLICY_BLOCKED`
- `APPROVAL_INVALID`, `APPROVAL_EXPIRED`, `APPROVAL_MISMATCH`

## Key Design Principles

1. **Non-blocking UI** - ExternalEvent + 15ms budget/cycle
2. **Dry-run first** - All mutations preview before execution
3. **Multi-layer IPC** - gRPC → ProtoBuf → JSON fallback
4. **LLM graceful degradation** - Rule-based fallback if LLM fails
5. **Thread pool for async** - Planning & narration off UI thread
6. **Modular tools** - 21 tool modules with permission/risk/latency metadata
7. **Session aware** - Persona, conversation history, approval state
8. **Rate limited** - 100 req/min global, 10 req/min for chat
9. **Named pipe security** - Windows identity verification, per-user ACL
10. **Event sourcing** - SQLite mission events for recovery

