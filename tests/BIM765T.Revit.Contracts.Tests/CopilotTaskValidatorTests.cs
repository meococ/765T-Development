using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class CopilotTaskValidatorTests
{
    [Fact]
    public void Validate_Throws_For_TaskPlan_Without_TaskKind()
    {
        var request = new TaskPlanRequest { TaskKind = "", TaskName = "parameter_hygiene" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_KIND_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_TaskPlan_Without_TaskName()
    {
        var request = new TaskPlanRequest { TaskKind = "fix_loop" };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_NAME_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_TaskExecute_Without_RunId()
    {
        var request = new TaskExecuteStepRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_RUN_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_TaskPlan_With_Invalid_CapabilityPack()
    {
        var request = new TaskPlanRequest
        {
            TaskKind = "workflow",
            TaskName = "sheet_setup",
            PreferredCapabilityPack = "unknown_pack"
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_CAPABILITY_PACK_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_TaskVerify_With_NonPositive_MaxResidualIssues()
    {
        var request = new TaskVerifyRequest { RunId = "run-1", MaxResidualIssues = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_MAX_RESIDUAL_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_ContextResolveBundle_With_NonPositive_MaxAnchors()
    {
        var request = new ContextResolveBundleRequest { MaxAnchors = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "CONTEXT_MAX_ANCHORS_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_TaskResume_With_NonPositive_MaxResidualIssues()
    {
        var request = new TaskResumeRequest { RunId = "run-1", MaxResidualIssues = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_MAX_RESIDUAL_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_ArtifactSummarize_Without_Path()
    {
        var request = new ArtifactSummarizeRequest { ArtifactPath = "", MaxChars = 100, MaxLines = 10 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "ARTIFACT_PATH_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_ToolLookup_With_NonPositive_MaxResults()
    {
        var request = new ToolCapabilityLookupRequest { Query = "ifc", MaxResults = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TOOL_LOOKUP_MAX_RESULTS_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_ToolGuidance_Without_Query_Or_ToolNames()
    {
        var request = new ToolGuidanceRequest { MaxResults = 5 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TOOL_GUIDANCE_QUERY_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_ContextDeltaSummary_With_Invalid_Limits()
    {
        var request = new ContextDeltaSummaryRequest
        {
            MaxRecentOperations = 0,
            MaxRecentEvents = 0,
            MaxRecommendations = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "CONTEXT_DELTA_LIMIT_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "CONTEXT_DELTA_RECOMMENDATIONS_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_ScheduleExtraction_Without_Schedule_Selector()
    {
        var request = new ScheduleExtractionRequest { MaxRows = 20 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SCHEDULE_SCOPE_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_SmartQc_With_Invalid_Limits()
    {
        var request = new SmartQcRequest
        {
            RulesetName = "",
            MaxFindings = 0,
            MaxSheets = 0,
            MaxNamingViolations = 0,
            DuplicateToleranceMm = 0
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SMART_QC_RULESET_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "SMART_QC_MAX_FINDINGS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "SMART_QC_MAX_SHEETS_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "SMART_QC_MAX_NAMING_INVALID");
        Assert.Contains(ex.Diagnostics, d => d.Code == "SMART_QC_DUPLICATE_TOLERANCE_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_FamilyXray_Without_Family_Selector()
    {
        var request = new FamilyXrayRequest { MaxNestedFamilies = 10, MaxParameters = 10, MaxTypeNames = 10 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "FAMILY_XRAY_SCOPE_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_SheetCaptureIntelligence_Without_Sheet_Selector()
    {
        var request = new SheetCaptureIntelligenceRequest
        {
            MaxViewports = 10,
            MaxSchedules = 10,
            MaxSheetTextNotes = 10,
            MaxViewportTextNotes = 10
        };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "SHEET_INTELLIGENCE_SCOPE_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_ExternalTaskIntake_Without_Connector_Metadata()
    {
        var request = new ExternalTaskIntakeRequest
        {
            DocumentKey = "",
            TaskKind = "workflow",
            TaskName = ""
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_DOCUMENT_KEY_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "EXTERNAL_SYSTEM_REQUIRED");
        Assert.Contains(ex.Diagnostics, d => d.Code == "EXTERNAL_TASK_REF_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_TaskQueueClaim_Without_LeaseOwner()
    {
        var request = new TaskQueueClaimRequest();
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_QUEUE_LEASE_OWNER_REQUIRED");
    }

    [Fact]
    public void Validate_Throws_For_TaskQueueList_With_Invalid_MaxResults()
    {
        var request = new TaskQueueListRequest { MaxResults = 0 };
        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_QUEUE_MAX_RESULTS_INVALID");
    }

    [Fact]
    public void Validate_Throws_For_TaskQueueRun_Without_LeaseOwner()
    {
        var request = new TaskQueueRunRequest
        {
            QueueItemId = "queue-1",
            LeaseOwner = "",
            MaxResidualIssues = 10
        };

        var ex = Assert.Throws<ToolPayloadValidationException>(() => ToolPayloadValidator.Validate(request));
        Assert.Contains(ex.Diagnostics, d => d.Code == "TASK_QUEUE_LEASE_OWNER_REQUIRED");
    }

    [Fact]
    public void Validate_Allows_Valid_Copilot_Task_Payloads()
    {
        ToolPayloadValidator.Validate(new TaskPlanRequest
        {
            TaskKind = "fix_loop",
            TaskName = "parameter_hygiene",
            InputJson = "{}"
        });

        ToolPayloadValidator.Validate(new TaskPreviewRequest
        {
            RunId = "run-1",
            StepId = "preview"
        });

        ToolPayloadValidator.Validate(new ContextResolveBundleRequest
        {
            Query = "parameter hygiene",
            MaxAnchors = 8,
            IncludeHot = true,
            IncludeWarm = true,
            IncludeCold = false
        });

        ToolPayloadValidator.Validate(new ArtifactSummarizeRequest
        {
            ArtifactPath = "C:\\temp\\artifact.json",
            MaxChars = 200,
            MaxLines = 20
        });

        ToolPayloadValidator.Validate(new ToolGuidanceRequest
        {
            Query = "export ifc",
            MaxResults = 5
        });

        ToolPayloadValidator.Validate(new ContextDeltaSummaryRequest
        {
            DocumentKey = "path:a",
            MaxRecentOperations = 10,
            MaxRecentEvents = 10,
            MaxRecommendations = 5
        });

        ToolPayloadValidator.Validate(new ScheduleExtractionRequest
        {
            ScheduleName = "Door Schedule",
            MaxRows = 100
        });

        ToolPayloadValidator.Validate(new SmartQcRequest
        {
            RulesetName = "base-rules",
            MaxFindings = 50,
            MaxSheets = 10,
            MaxNamingViolations = 20,
            DuplicateToleranceMm = 1.0
        });

        ToolPayloadValidator.Validate(new FamilyXrayRequest
        {
            FamilyName = "M_Round_Duct_Diffuser",
            MaxNestedFamilies = 10,
            MaxParameters = 50,
            MaxTypeNames = 10
        });

        ToolPayloadValidator.Validate(new SheetCaptureIntelligenceRequest
        {
            SheetNumber = "A101",
            MaxViewports = 10,
            MaxSchedules = 5,
            MaxSheetTextNotes = 20,
            MaxViewportTextNotes = 10
        });

        ToolPayloadValidator.Validate(new ExternalTaskIntakeRequest
        {
            DocumentKey = "path:a",
            TaskKind = "workflow",
            TaskName = "issue_105",
            Envelope = new ConnectorTaskEnvelope
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105"
            }
        });

        ToolPayloadValidator.Validate(new TaskQueueClaimRequest
        {
            LeaseOwner = "worker-1"
        });

        ToolPayloadValidator.Validate(new ConnectorCallbackPreviewRequest
        {
            RunId = "run-1"
        });

        ToolPayloadValidator.Validate(new TaskQueueRunRequest
        {
            QueueItemId = "queue-1",
            LeaseOwner = "worker-1",
            MaxResidualIssues = 10
        });
    }
}
