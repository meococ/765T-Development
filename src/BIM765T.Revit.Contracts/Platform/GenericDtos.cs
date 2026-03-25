// =============================================================================
// GenericDtos.cs has been split into focused domain files:
//
//   ToolManifestDtos.cs         - PermissionLevel, ApprovalRequirement, ToolManifest, ToolCatalogResponse, BridgeCapabilities
//   DocumentViewDtos.cs         - ScopeDescriptor, ContextFingerprint, DocumentSummaryDto, DocumentListResponse, ViewSummaryDto, SelectionSummaryDto
//   ElementDtos.cs              - BoundingBoxDto, ParameterValueDto, ElementSummaryDto, ElementQueryRequest, ElementQueryResponse
//   ReviewDtos.cs               - ReviewIssue, ReviewReport, ActiveViewSummary*, ModelHealth*, WorksetHealth*, LinksStatus*, SheetSummary*, CountByNameDto
//   MutationDtos.cs             - ParameterUpdateItem, SetParametersRequest, DeleteElementsRequest, PlaceFamilyInstanceRequest, MoveElementsRequest, DiffSummary, ExecutionResult
//   FileLifecycleDtos.cs        - SaveDocumentRequest, SaveAsDocumentRequest, OpenBackgroundDocumentRequest, CloseDocumentRequest, SynchronizeRequest
//   SnapshotDtos.cs             - SnapshotElementState, ModelSnapshotSummary, CaptureSnapshotRequest, SnapshotCaptureResponse
//   SessionObservabilityDtos.cs - OperationJournalEntry, EventRecord, RecentEventsResponse, RecentOperationsResponse, TaskContextRequest, TaskContextResponse
//   ViewAutomationDtos.cs       - Create3DViewRequest, ViewFilterRuleRequest, CreateOrUpdateViewFilterRequest, ApplyViewFilterRequest
//   InspectorDtos.cs            - element.explain / element.graph / parameter.trace / view.usage / sheet.dependencies
//   WorkflowDtos.cs             - workflow runtime contracts (plan/apply/resume/evidence)
//
// All types remain in namespace BIM765T.Revit.Contracts.Platform.
// This file is intentionally left empty — it can be deleted once all consumers are verified.
// =============================================================================
