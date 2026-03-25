using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class CopilotTaskDtoTests
{
    [Fact]
    public void TaskRun_RoundTrips_With_SelectedActions_And_Steps()
    {
        var dto = new TaskRun
        {
            RunId = "task-001",
            TaskKind = "fix_loop",
            TaskName = "parameter_hygiene",
            Status = "preview_ready",
            DocumentKey = "path:c:\\test.rvt",
            IntentSummary = "Fix empty comments",
            PlanSummary = "Plan -> preview -> approve -> execute -> verify",
            UnderlyingRunId = "fix-001",
            ApprovalToken = "token-123",
            PreviewRunId = "preview-123",
            RecommendedActionIds = new List<string> { "a1", "a2" },
            SelectedActionIds = new List<string> { "a1" },
            LastErrorCode = "CONTEXT_MISMATCH",
            LastErrorMessage = "Preview context drifted.",
            Steps = new List<TaskStepState>
            {
                new TaskStepState { StepId = "plan", Status = "completed" },
                new TaskStepState { StepId = "preview", Status = "completed" }
            },
            Checkpoints = new List<TaskCheckpointRecord>
            {
                new TaskCheckpointRecord { CheckpointId = "cp-1", StepId = "plan", Status = "completed", NextAction = "preview", CanResume = true }
            },
            RecoveryBranches = new List<TaskRecoveryBranch>
            {
                new TaskRecoveryBranch { BranchId = "refresh_preview", NextAction = "preview", AutoResumable = true, IsRecommended = true }
            },
            CapabilityPack = WorkerCapabilityPacks.CoreWorker,
            PrimarySkillGroup = WorkerSkillGroups.Documentation,
            QueueEligible = true,
            TaskSpec = new TaskSpec
            {
                Source = "panel",
                Goal = "Fix empty comments safely.",
                DocumentScope = "path:c:\\test.rvt"
            },
            WorkerProfile = new WorkerProfile
            {
                PersonaId = WorkerPersonas.FreelancerDefault,
                AllowedSkillGroups = new List<string> { WorkerSkillGroups.Documentation }
            },
            RunReport = new RunReport
            {
                TaskSummary = "Fix comments",
                PlanExecuted = new List<string> { "plan:completed", "preview:completed" },
                ApprovalCheckpoints = new List<string> { "approve:completed:Operator approval recorded." },
                ActionsPerformed = new List<string> { "a1" },
                ArtifactsGenerated = new List<string> { "artifact:1" },
                ResidualRisks = new List<string> { "CONTEXT_MISMATCH: Preview context drifted." },
                NextRecommendedAction = "verify"
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<TaskRun>(json);

        Assert.Equal("task-001", result.RunId);
        Assert.Equal("fix_loop", result.TaskKind);
        Assert.Single(result.SelectedActionIds);
        Assert.Equal(2, result.RecommendedActionIds.Count);
        Assert.Equal("preview-123", result.PreviewRunId);
        Assert.Equal(2, result.Steps.Count);
        Assert.Single(result.Checkpoints);
        Assert.Single(result.RecoveryBranches);
        Assert.Equal("CONTEXT_MISMATCH", result.LastErrorCode);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, result.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Documentation, result.PrimarySkillGroup);
        Assert.True(result.QueueEligible);
        Assert.Equal("Fix empty comments safely.", result.TaskSpec.Goal);
        Assert.Equal(WorkerPersonas.FreelancerDefault, result.WorkerProfile.PersonaId);
        Assert.Equal("verify", result.RunReport.NextRecommendedAction);
    }

    [Fact]
    public void SessionRuntimeHealthResponse_Defaults_Support_Copilot_Surface()
    {
        var dto = new SessionRuntimeHealthResponse();

        Assert.Equal("embedded_agent", dto.RuntimeMode);
        Assert.True(dto.SupportsTaskRuntime);
        Assert.True(dto.SupportsContextBroker);
        Assert.True(dto.SupportsStateGraph);
        Assert.True(dto.SupportsDurableTaskRuns);
        Assert.True(dto.SupportsCheckpointRecovery);
        Assert.Equal(WorkerReasoningModes.RuleFirst, dto.ReasoningMode);
        Assert.Equal(string.Empty, dto.ConfiguredProvider);
    }

    [Fact]
    public void ContextResolveBundleResponse_RoundTrips_Items()
    {
        var dto = new ContextResolveBundleResponse
        {
            Query = "round export",
            Items = new List<ContextBundleItem>
            {
                new ContextBundleItem
                {
                    AnchorId = "task:123",
                    Tier = "warm",
                    Title = "fix_loop • round_export",
                    Summary = "Verified round export contract.",
                    SourceKind = "task_run",
                    Tags = new List<string> { "round", "export" },
                    Score = 42
                }
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<ContextResolveBundleResponse>(json);

        Assert.Equal("round export", result.Query);
        Assert.Single(result.Items);
        Assert.Equal("task:123", result.Items[0].AnchorId);
        Assert.Equal(42, result.Items[0].Score);
    }

    [Fact]
    public void TaskSummaryResponse_RoundTrips_Checkpoint_And_Resume_Metadata()
    {
        var dto = new TaskSummaryResponse
        {
            RunId = "run-1",
            TaskKind = "fix_loop",
            TaskName = "parameter_hygiene",
            NextAction = "preview",
            CheckpointCount = 3,
            RecoveryBranchCount = 2,
            CanResume = true,
            LastErrorCode = StatusCodes.ContextMismatch,
            CapabilityPack = WorkerCapabilityPacks.CoreWorker,
            PrimarySkillGroup = WorkerSkillGroups.QualityControl,
            WorkerPersonaId = WorkerPersonas.StrictQaFirm,
            RunReport = new RunReport
            {
                TaskSummary = "QC report",
                NextRecommendedAction = "resume"
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<TaskSummaryResponse>(json);

        Assert.Equal(3, result.CheckpointCount);
        Assert.Equal(2, result.RecoveryBranchCount);
        Assert.True(result.CanResume);
        Assert.Equal(StatusCodes.ContextMismatch, result.LastErrorCode);
        Assert.Equal(WorkerSkillGroups.QualityControl, result.PrimarySkillGroup);
        Assert.Equal(WorkerPersonas.StrictQaFirm, result.WorkerPersonaId);
        Assert.Equal("resume", result.RunReport.NextRecommendedAction);
    }

    [Fact]
    public void TaskPlanRequest_And_MemoryPromotion_RoundTrip_Worker_Product_Metadata()
    {
        var request = new TaskPlanRequest
        {
            DocumentKey = "path:a",
            TaskKind = "workflow",
            TaskName = "sheet_setup",
            IntentSummary = "Prepare documentation packet.",
            PreferredCapabilityPack = WorkerCapabilityPacks.CoreWorker,
            TaskSpec = new TaskSpec
            {
                Source = "acc",
                Goal = "Set up sheets and exports.",
                DocumentScope = "path:a",
                Constraints = new List<string> { "no delete", "report required" },
                ApprovalPolicy = new TaskApprovalPolicy { AllowQueuedExecution = true, MaxBatchSize = 25 }
            },
            WorkerProfile = new WorkerProfile
            {
                PersonaId = WorkerPersonas.StrictQaFirm,
                Tone = "concise",
                QaStrictness = "strict",
                AllowedSkillGroups = new List<string> { WorkerSkillGroups.Documentation, WorkerSkillGroups.QualityControl }
            }
        };

        var promotion = new TaskMemoryPromotionRecord
        {
            PromotionId = "mem-1",
            RunId = "run-1",
            Summary = "Document packet pattern.",
            MemoryRecord = new MemoryRecord
            {
                Scope = "workspace",
                Kind = "playbook",
                Source = "task:run-1",
                Summary = "Approved workflow for documentation packet.",
                PromotionStatus = "approved"
            }
        };

        var requestJson = JsonUtil.Serialize(request);
        var requestRoundTrip = JsonUtil.DeserializeRequired<TaskPlanRequest>(requestJson);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, requestRoundTrip.PreferredCapabilityPack);
        Assert.True(requestRoundTrip.TaskSpec.ApprovalPolicy.AllowQueuedExecution);
        Assert.Equal(WorkerPersonas.StrictQaFirm, requestRoundTrip.WorkerProfile.PersonaId);

        var promotionJson = JsonUtil.Serialize(promotion);
        var promotionRoundTrip = JsonUtil.DeserializeRequired<TaskMemoryPromotionRecord>(promotionJson);
        Assert.Equal("workspace", promotionRoundTrip.MemoryRecord.Scope);
        Assert.Equal("approved", promotionRoundTrip.MemoryRecord.PromotionStatus);
    }

    [Fact]
    public void ExternalTaskIntake_Queue_And_CallbackDtos_RoundTrip()
    {
        var intake = new ExternalTaskIntakeRequest
        {
            DocumentKey = "path:model",
            TaskKind = "workflow",
            TaskName = "issue_105",
            IntentSummary = "Review issue 105 safely.",
            Envelope = new ConnectorTaskEnvelope
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105",
                ProjectRef = "project-1",
                CallbackMode = "report_back",
                StatusMapping = new Dictionary<string, string> { ["done"] = "closed" },
                Title = "Issue 105",
                Description = "Check clash and fix sheet output.",
                DocumentHint = "MainModel"
            }
        };

        var queueItem = new TaskQueueItem
        {
            QueueItemId = "queue-1",
            RunId = "run-1",
            TaskKind = "workflow",
            TaskName = "issue_105",
            QueueName = "approved",
            Status = "leased",
            ConnectorSystem = "acc",
            ExternalTaskRef = "issue-105",
            CallbackMode = "report_back",
            LeaseOwner = "worker-1"
        };

        var callback = new ConnectorCallbackPreviewResponse
        {
            System = "acc",
            Reference = "issue-105",
            Mode = "report_back",
            SuggestedStatus = "closed",
            Summary = "Issue 105 reviewed.",
            Payload = new ConnectorCallbackPayload
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105",
                RunId = "run-1",
                QueueItemId = "queue-1",
                Status = "closed"
            },
            PayloadJson = "{\"status\":\"closed\"}"
        };

        var intakeJson = JsonUtil.Serialize(intake);
        var intakeRoundTrip = JsonUtil.DeserializeRequired<ExternalTaskIntakeRequest>(intakeJson);
        Assert.Equal("acc", intakeRoundTrip.Envelope.ExternalSystem);
        Assert.Equal("Issue 105", intakeRoundTrip.Envelope.Title);

        var queueJson = JsonUtil.Serialize(queueItem);
        var queueRoundTrip = JsonUtil.DeserializeRequired<TaskQueueItem>(queueJson);
        Assert.Equal("leased", queueRoundTrip.Status);
        Assert.Equal("worker-1", queueRoundTrip.LeaseOwner);

        var callbackJson = JsonUtil.Serialize(callback);
        var callbackRoundTrip = JsonUtil.DeserializeRequired<ConnectorCallbackPreviewResponse>(callbackJson);
        Assert.Equal("acc", callbackRoundTrip.System);
        Assert.Equal("closed", callbackRoundTrip.Payload.Status);
    }

    [Fact]
    public void TaskQueueRunResponse_RoundTrips_Run_Queue_And_Callback_State()
    {
        var dto = new TaskQueueRunResponse
        {
            QueueItem = new TaskQueueItem
            {
                QueueItemId = "queue-1",
                RunId = "run-1",
                Status = "completed"
            },
            Run = new TaskRun
            {
                RunId = "run-1",
                Status = "verified"
            },
            Summary = new TaskSummaryResponse
            {
                RunId = "run-1",
                Status = "verified",
                NextAction = "report_back"
            },
            CallbackPreview = new ConnectorCallbackPreviewResponse
            {
                System = "acc",
                Reference = "issue-105",
                SuggestedStatus = "closed"
            }
        };

        var json = JsonUtil.Serialize(dto);
        var roundTrip = JsonUtil.DeserializeRequired<TaskQueueRunResponse>(json);

        Assert.Equal("queue-1", roundTrip.QueueItem.QueueItemId);
        Assert.Equal("run-1", roundTrip.Run.RunId);
        Assert.Equal("report_back", roundTrip.Summary.NextAction);
        Assert.Equal("closed", roundTrip.CallbackPreview.SuggestedStatus);
    }

    [Fact]
    public void ToolGuidanceResponse_RoundTrips_Curated_Intelligence()
    {
        var dto = new ToolGuidanceResponse
        {
            Query = "export ifc",
            Guidance = new List<ToolGuidanceRecord>
            {
                new ToolGuidanceRecord
                {
                    ToolName = "export.ifc_safe",
                    GuidanceSummary = "Risk 7/10.",
                    RiskScore = 7,
                    CostScore = 6,
                    Prerequisites = new List<string> { "document", "dry_run preview" },
                    FollowUps = new List<string> { "artifact.summarize" },
                    CommonFailureCodes = new List<string> { StatusCodes.ContextMismatch },
                    RecommendedRecoveryTools = new List<string> { "document.get_context_fingerprint" },
                    AntiPatterns = new List<string> { "Khong skip dry-run." },
                    TypicalChains = new List<string> { "export.ifc_safe -> artifact.summarize" },
                    RecoveryHints = new List<string> { "Lam moi context roi preview lai." },
                    RecommendedTemplates = new List<string> { "MODEL_QC_BASE" }
                }
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<ToolGuidanceResponse>(json);

        Assert.Equal("export ifc", result.Query);
        Assert.Single(result.Guidance);
        Assert.Equal("export.ifc_safe", result.Guidance[0].ToolName);
        Assert.Contains("artifact.summarize", result.Guidance[0].FollowUps);
        Assert.Contains("MODEL_QC_BASE", result.Guidance[0].RecommendedTemplates);
    }

    [Fact]
    public void ScheduleExtractionResponse_RoundTrips_Structured_Rows()
    {
        var dto = new ScheduleExtractionResponse
        {
            DocumentKey = "path:a",
            ScheduleId = 42,
            ScheduleName = "Door Schedule",
            ColumnCount = 2,
            TotalRowCount = 3,
            ReturnedRowCount = 2,
            Columns = new List<ScheduleColumnInfo>
            {
                new ScheduleColumnInfo { Index = 0, Key = "mark", Heading = "Mark" },
                new ScheduleColumnInfo { Index = 1, Key = "width", Heading = "Width" }
            },
            Rows = new List<ScheduleExtractionRow>
            {
                new ScheduleExtractionRow { RowIndex = 1, Cells = new Dictionary<string, string> { ["mark"] = "D01", ["width"] = "900" } }
            },
            Totals = new Dictionary<string, string> { ["count"] = "3" },
            Summary = "Structured schedule."
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<ScheduleExtractionResponse>(json);

        Assert.Equal(42, result.ScheduleId);
        Assert.Single(result.Rows);
        Assert.Equal("D01", result.Rows[0].Cells["mark"]);
    }

    [Fact]
    public void SmartQcResponse_RoundTrips_Findings()
    {
        var dto = new SmartQcResponse
        {
            DocumentKey = "path:a",
            RulesetName = "base-rules",
            RulesetDescription = "Base QC",
            ExecutedCheckCount = 3,
            FindingCount = 1,
            ExecutedChecks = new List<string> { "review.model_health", "audit.naming_convention" },
            RulesEvaluated = new List<string> { "DOC_WARNINGS_LIMIT" },
            Findings = new List<SmartQcFinding>
            {
                new SmartQcFinding
                {
                    RuleId = "DOC_WARNINGS_LIMIT",
                    Title = "Warnings",
                    Category = "document",
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Model co qua nhieu warnings.",
                    SourceTool = "review.model_health",
                    EvidenceRef = "document"
                }
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<SmartQcResponse>(json);

        Assert.Equal("base-rules", result.RulesetName);
        Assert.Single(result.Findings);
        Assert.Equal("DOC_WARNINGS_LIMIT", result.Findings[0].RuleId);
    }

    [Fact]
    public void FamilyXrayResponse_RoundTrips_Deep_Family_Data()
    {
        var dto = new FamilyXrayResponse
        {
            DocumentKey = "path:a",
            FamilyId = 101,
            FamilyName = "M_Round_Duct_Diffuser",
            CategoryName = "Air Terminals",
            SourceDocumentTitle = "M_Round_Duct_Diffuser.rfa",
            TypesCount = 3,
            TypeNames = new List<string> { "Type A", "Type B" },
            NestedFamilies = new List<FamilyNestedFamilyInfo>
            {
                new FamilyNestedFamilyInfo { FamilyName = "Round_Connector", CategoryName = "Duct Fittings", IsShared = true, Count = 2 }
            },
            InstanceParameters = new List<string> { "Offset", "Flow" },
            TypeParameters = new List<string> { "Diameter", "Manufacturer" },
            FormulaParameters = new List<FamilyFormulaInfo>
            {
                new FamilyFormulaInfo { ParameterName = "Area", Formula = "PI() * r * r" }
            },
            ReferencePlanes = new List<string> { "Center (Left/Right)", "Center (Front/Back)" },
            Connectors = new List<FamilyConnectorInfo>
            {
                new FamilyConnectorInfo { ConnectorId = 2001, Name = "Supply", Domain = "DomainHvac", SystemClassification = "SupplyAir", Shape = "Round", DirectionSummary = "(0,0,1)" }
            },
            Issues = new List<string> { "Missing Description parameter." }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<FamilyXrayResponse>(json);

        Assert.Equal(101, result.FamilyId);
        Assert.Single(result.NestedFamilies);
        Assert.Single(result.Connectors);
        Assert.Contains("Offset", result.InstanceParameters);
    }

    [Fact]
    public void SheetCaptureIntelligenceResponse_RoundTrips_Structured_Sheet_Data()
    {
        var dto = new SheetCaptureIntelligenceResponse
        {
            DocumentKey = "path:a",
            SheetId = 501,
            SheetNumber = "A101",
            SheetName = "General Arrangement",
            TitleBlockName = "A1 Titleblock",
            CurrentRevision = "P01",
            TitleBlockParameters = new List<SheetTitleBlockParameterInfo>
            {
                new SheetTitleBlockParameterInfo { Name = "Drawn By", Value = "TH" }
            },
            Viewports = new List<SheetViewportIntelligence>
            {
                new SheetViewportIntelligence { ViewportId = 1, ViewId = 1001, ViewName = "Level 1 Plan", ViewType = "FloorPlan", Scale = 100, TextNoteCount = 5 }
            },
            Schedules = new List<SheetScheduleIntelligence>
            {
                new SheetScheduleIntelligence { ScheduleInstanceId = 2, ScheduleViewId = 2002, ScheduleName = "Door Schedule", RowCount = 24, ColumnCount = 6 }
            },
            SheetTextNotes = new List<SheetTextNoteIntelligence>
            {
                new SheetTextNoteIntelligence { OwnerViewId = 501, OwnerViewName = "General Arrangement", Text = "ISSUED FOR REVIEW", X = 10, Y = 20 }
            },
            AnnotationCounts = new List<CountByNameDto>
            {
                new CountByNameDto { Name = "viewports", Count = 1 }
            },
            LayoutMap = "+--[A101]--+",
            Artifacts = new List<SheetArtifactReference>
            {
                new SheetArtifactReference { ArtifactType = "layout_map", Path = "C:\\temp\\A101.layout.txt", Description = "Layout map" }
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<SheetCaptureIntelligenceResponse>(json);

        Assert.Equal("A101", result.SheetNumber);
        Assert.Single(result.Viewports);
        Assert.Single(result.Schedules);
        Assert.Single(result.Artifacts);
    }

    [Fact]
    public void ContextDeltaSummaryResponse_RoundTrips_Suggested_Tools()
    {
        var dto = new ContextDeltaSummaryResponse
        {
            DocumentKey = "path:a",
            RecentOperationCount = 4,
            RecentEventCount = 2,
            RecentChangedElementCount = 12,
            LastMutationTool = "element.move_safe",
            LastFailureCode = StatusCodes.ContextMismatch,
            Summary = "Queue still has 1 request.",
            SuggestedNextTools = new List<string> { "document.get_context_fingerprint", "context.get_hot_state" },
            AddedElementEstimate = 2,
            RemovedElementEstimate = 1,
            ModifiedElementEstimate = 9,
            TopCategories = new List<CountByNameDto> { new CountByNameDto { Name = "Walls", Count = 6 } },
            DisciplineHints = new List<CountByNameDto> { new CountByNameDto { Name = "Architecture", Count = 6 } },
            RecentMutationKinds = new List<string> { "modify", "delete" }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<ContextDeltaSummaryResponse>(json);

        Assert.Equal("path:a", result.DocumentKey);
        Assert.Equal(12, result.RecentChangedElementCount);
        Assert.Equal(StatusCodes.ContextMismatch, result.LastFailureCode);
        Assert.Contains("context.get_hot_state", result.SuggestedNextTools);
        Assert.Equal(2, result.AddedElementEstimate);
        Assert.Single(result.TopCategories);
        Assert.Contains("modify", result.RecentMutationKinds);
    }
}
