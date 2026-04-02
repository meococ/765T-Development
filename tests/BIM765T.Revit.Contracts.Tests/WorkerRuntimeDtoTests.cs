using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class WorkerRuntimeDtoTests
{
    [Fact]
    public void WorkerMessageRequest_RoundTrips()
    {
        var request = new WorkerMessageRequest
        {
            SessionId = "session-1",
            Message = "kiem tra model health",
            PersonaId = WorkerPersonas.RevitWorker,
            ClientSurface = WorkerClientSurfaces.Mcp,
            ContinueMission = false
        };

        var json = JsonUtil.Serialize(request);
        var restored = JsonUtil.DeserializeRequired<WorkerMessageRequest>(json);

        Assert.Equal("session-1", restored.SessionId);
        Assert.Equal("kiem tra model health", restored.Message);
        Assert.Equal(WorkerPersonas.RevitWorker, restored.PersonaId);
        Assert.Equal(WorkerClientSurfaces.Mcp, restored.ClientSurface);
        Assert.False(restored.ContinueMission);
    }

    [Fact]
    public void WorkerResponse_RoundTrips_With_Cards_And_Approval()
    {
        var response = new WorkerResponse
        {
            SessionId = "session-1",
            MissionId = "mission-1",
            MissionStatus = WorkerMissionStates.AwaitingApproval,
            Messages = new List<WorkerChatMessage>
            {
                new WorkerChatMessage { Role = WorkerMessageRoles.User, Content = "preview purge unused" },
                new WorkerChatMessage { Role = WorkerMessageRoles.Worker, Content = "Da preview xong." }
            },
            ActionCards = new List<WorkerActionCard>
            {
                new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Approve,
                    Title = "Dong y execute purge",
                    Summary = "Go dong y de execute.",
                    ExecutionTier = WorkerExecutionTiers.Tier2,
                    WhyThisAction = "Token/context da preview xong.",
                    Confidence = 0.9d,
                    RecoveryHint = "Neu token het han thi preview lai.",
                    AutoExecutionEligible = false
                }
            },
            PendingApproval = new PendingApprovalRef
            {
                PendingActionId = "pending-1",
                ToolName = "audit.purge_unused_safe",
                Summary = "Preview purge unused",
                ExpiresUtc = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc),
                ExecutionTier = WorkerExecutionTiers.Tier2,
                RecoveryHint = "Preview lai neu can.",
                AutoExecutionEligible = false,
                ExpectedContextJson = """{"doc":"Model.rvt","view":"Level 1"}"""
            },
            ToolCards = new List<WorkerToolCard>
            {
                new WorkerToolCard
                {
                    ToolName = "review.smart_qc",
                    StatusCode = "READ_SUCCEEDED",
                    Succeeded = true,
                    Summary = "QC pass",
                    Stage = WorkerStages.Verification,
                    Progress = 100,
                    WhyThisTool = "Doc model health va summary.",
                    Confidence = 0.93d,
                    RecoveryHints = new List<string> { "Neu can, chay context.get_delta_summary." },
                    ExecutionTier = WorkerExecutionTiers.Tier0,
                    AutoExecutionEligible = true
                }
            },
            ArtifactRefs = new List<string> { "artifacts/qc/report.json" },
            ContextSummary = new WorkerContextSummary
            {
                DocumentKey = "path:a",
                DocumentTitle = "Model.rvt",
                ActiveViewKey = "view:1",
                ActiveViewName = "Level 1",
                SelectionCount = 2,
                Summary = "2 warning(s)",
                SuggestedNextTools = new List<string> { "review.smart_qc" },
                SimilarEpisodeHints = new List<string> { "Lan truoc da chay QC." },
                QueueSummary = "Queue dang ranh.",
                WorkspaceId = "default",
                PackSummary = "Pack bim765t.playbooks.core; specialists=sheet,audit."
            },
            ReasoningSummary = "Rule-first routing.",
            PlanSummary = "Preview -> approve -> execute",
            Stage = WorkerStages.Approval,
            Progress = 60,
            Confidence = 0.88d,
            RecoveryHints = new List<string> { "Neu token drift thi preview lai." },
            ExecutionTier = WorkerExecutionTiers.Tier2,
            AutoExecutionEligible = false,
            QueueState = new QueueStateResponse
            {
                PendingCount = 2,
                PendingHighPriorityCount = 1,
                PendingNormalPriorityCount = 1,
                PendingLowPriorityCount = 0,
                ActiveStage = WorkerStages.Approval,
                ActiveExecutionTier = WorkerExecutionTiers.Tier2,
                ActiveRiskTier = ToolRiskTiers.Tier2,
                ActiveLatencyClass = ToolLatencyClasses.Standard,
                ActiveElapsedMs = 1500,
                CanCancelPending = true
            },
            WorkspaceId = "default",
            SelectedPlaybook = new PlaybookRecommendation
            {
                PlaybookId = "sheet_create_arch_package.v1",
                PackId = "bim765t.playbooks.core",
                StandardsRefs = new List<string> { "templates.json#view_templates.architectural_plan" },
                RequiredInputs = new List<string> { "sheet_name", "levels" }
            },
            PlaybookPreview = new PlaybookPreviewResponse
            {
                WorkspaceId = "default",
                PlaybookId = "sheet_create_arch_package.v1",
                Summary = "Tool chain san sang.",
                Steps = new List<PlaybookPreviewStep>
                {
                    new PlaybookPreviewStep { StepName = "Resolve standards", Tool = ToolNames.StandardsResolve }
                },
                Standards = new StandardsResolution
                {
                    WorkspaceId = "default",
                    Summary = "Resolved 1/1 key(s).",
                    Values = new List<StandardsResolvedValue>
                    {
                        new StandardsResolvedValue
                        {
                            RequestedKey = "templates.json#view_templates.architectural_plan",
                            Value = "BIM765T_Arch_Plan_v2",
                            SourcePackId = "bim765t.standards.core",
                            SourceFile = "templates.json",
                            Matched = true
                        }
                    }
                }
            },
            StandardsSummary = "Resolved 1/1 key(s)."
            ,
            ContextPills = new List<WorkerContextPill>
            {
                new WorkerContextPill
                {
                    Key = "document",
                    Label = "Document",
                    Value = "Model.rvt",
                    Icon = "document",
                    Tone = "neutral",
                    IsPrimary = true
                }
            },
            ExecutionItems = new List<WorkerExecutionItem>
            {
                new WorkerExecutionItem
                {
                    Title = "review.smart_qc",
                    Summary = "QC pass",
                    Status = WorkerExecutionItemStates.Verified,
                    Stage = WorkerStages.Verification,
                    ToolName = ToolNames.ReviewSmartQc
                }
            },
            EvidenceItems = new List<WorkerEvidenceItem>
            {
                new WorkerEvidenceItem
                {
                    ArtifactRef = "artifacts/qc/report.json",
                    Title = "report.json",
                    Summary = "QC evidence",
                    Status = WorkerExecutionItemStates.Verified,
                    SourceToolName = ToolNames.ReviewSmartQc,
                    VerificationMode = ToolVerificationModes.ReportOnly,
                    Verified = true
                }
            },
            SuggestedCommands = new List<WorkerCommandSuggestion>
            {
                new WorkerCommandSuggestion
                {
                    CommandId = ToolNames.CommandSearch,
                    Label = "Tim command",
                    Summary = "Quick command palette",
                    ToolName = ToolNames.CommandSearch,
                    SurfaceId = WorkerSurfaceIds.Commands
                }
            },
            PrimaryRiskSummary = new WorkerRiskSummary
            {
                RiskLevel = WorkerRiskLevels.High,
                Label = "Approval required",
                Summary = "Can phe duyet",
                RequiresApproval = true,
                VerificationMode = ToolVerificationModes.ReportOnly,
                ExecutionTier = WorkerExecutionTiers.Tier2
            },
            SurfaceHint = new WorkerSurfaceHint
            {
                SurfaceId = WorkerSurfaceIds.Assistant,
                Reason = "Dang cho approval",
                Emphasis = "approval"
            },
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "default",
                WorkspaceRootPath = @"D:\repo\workspaces\default",
                InitStatus = ProjectOnboardingStatuses.Initialized,
                DeepScanStatus = ProjectDeepScanStatuses.Partial,
                ResumeEligible = true,
                SessionId = "session-1",
                MissionId = "mission-1",
                PendingApproval = new PendingApprovalRef
                {
                    PendingActionId = "pending-1",
                    ToolName = "audit.purge_unused_safe"
                },
                PrimaryModelStatus = ProjectPrimaryModelStatuses.PendingLiveSummary,
                Summary = "Resume available."
            },
            FallbackProposal = new FallbackArtifactProposal
            {
                ProposalId = "fallback-1",
                WorkspaceId = "default",
                Reason = "atlas_miss",
                Summary = "Review fallback artifact.",
                ArtifactKinds = new List<string> { FallbackArtifactKinds.Playbook, FallbackArtifactKinds.ExportProfile }
            },
            SkillCaptureProposal = new SkillCaptureProposal
            {
                CaptureId = "capture-1",
                SourceRunId = "mission-1",
                WorkspaceId = "default",
                CandidateSkillId = "skill.default.sheet_create_arch_package.v1.20260322",
                PlaybookId = "sheet_create_arch_package.v1",
                Summary = "Save reusable skill.",
                CanPromoteToFreeReplay = true,
                Confidence = 0.84d
            },
            ProjectPatternSnapshot = new ProjectPatternSnapshot
            {
                SnapshotId = "snapshot-1",
                WorkspaceId = "default",
                RecommendedPlaybooks = new List<string> { "sheet_create_arch_package.v1" },
                RecommendedToolNames = new List<string> { ToolNames.SheetCreateSafe, ToolNames.SheetPlaceViewsSafe },
                Summary = "Pattern snapshot ready.",
                Confidence = 0.77d,
                SourceWorkspaceId = "default"
            },
            TemplateSynthesisProposal = new TemplateSynthesisProposal
            {
                ProposalId = "template-1",
                WorkspaceId = "default",
                SourceProjectWorkspaceId = "default",
                ProposedWorkspacePackId = "bim765t.generated.default.starter",
                ProposedArtifactPaths = new List<string> { "artifacts/templates/default.starter.playbook.json" },
                Summary = "Starter pack proposal.",
                Confidence = 0.8d
            },
            ConfiguredProvider = "OPENROUTER",
            PlannerModel = "openai/gpt-5.2",
            ResponseModel = "openai/gpt-5-mini",
            ReasoningMode = WorkerReasoningModes.LlmValidated,
            DeltaSuggestions = new List<DeltaSuggestion>
            {
                new DeltaSuggestion
                {
                    SuggestionId = "delta-1",
                    WorkspaceId = "default",
                    Summary = "Run deep scan next.",
                    Reason = "deep_scan_pending",
                    Stage = WorkerFlowStages.Scan,
                    CandidateToolNames = new List<string> { ToolNames.ProjectDeepScan },
                    Confidence = 0.9d
                }
            }
        };

        var json = JsonUtil.Serialize(response);
        var restored = JsonUtil.DeserializeRequired<WorkerResponse>(json);

        Assert.Equal("session-1", restored.SessionId);
        Assert.Equal("mission-1", restored.MissionId);
        Assert.Equal(WorkerMissionStates.AwaitingApproval, restored.MissionStatus);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Single(restored.ActionCards);
        Assert.Equal("pending-1", restored.PendingApproval.PendingActionId);
        Assert.Equal("""{"doc":"Model.rvt","view":"Level 1"}""", restored.PendingApproval.ExpectedContextJson);
        Assert.Single(restored.ToolCards);
        Assert.Single(restored.ArtifactRefs);
        Assert.Equal("Model.rvt", restored.ContextSummary.DocumentTitle);
        Assert.Equal("Rule-first routing.", restored.ReasoningSummary);
        Assert.Equal("Preview -> approve -> execute", restored.PlanSummary);
        Assert.Equal(WorkerStages.Approval, restored.Stage);
        Assert.Equal(WorkerExecutionTiers.Tier2, restored.ExecutionTier);
        Assert.Equal("Queue dang ranh.", restored.ContextSummary.QueueSummary);
        Assert.Equal(2, restored.QueueState.PendingCount);
        Assert.Single(restored.RecoveryHints);
        Assert.Equal("default", restored.WorkspaceId);
        Assert.Equal("sheet_create_arch_package.v1", restored.SelectedPlaybook.PlaybookId);
        Assert.Equal("BIM765T_Arch_Plan_v2", restored.PlaybookPreview.Standards.Values[0].Value);
        Assert.Equal("Resolved 1/1 key(s).", restored.StandardsSummary);
        Assert.Single(restored.ContextPills);
        Assert.Single(restored.ExecutionItems);
        Assert.Single(restored.EvidenceItems);
        Assert.Single(restored.SuggestedCommands);
        Assert.Equal(WorkerRiskLevels.High, restored.PrimaryRiskSummary.RiskLevel);
        Assert.Equal(WorkerSurfaceIds.Assistant, restored.SurfaceHint.SurfaceId);
        Assert.Equal(ProjectOnboardingStatuses.Initialized, restored.OnboardingStatus.InitStatus);
        Assert.Equal(ProjectDeepScanStatuses.Partial, restored.OnboardingStatus.DeepScanStatus);
        Assert.True(restored.OnboardingStatus.ResumeEligible);
        Assert.Equal("pending-1", restored.OnboardingStatus.PendingApproval.PendingActionId);
        Assert.Equal("fallback-1", restored.FallbackProposal.ProposalId);
        Assert.Equal("skill.default.sheet_create_arch_package.v1.20260322", restored.SkillCaptureProposal.CandidateSkillId);
        Assert.Equal("snapshot-1", restored.ProjectPatternSnapshot.SnapshotId);
        Assert.Equal("template-1", restored.TemplateSynthesisProposal.ProposalId);
        Assert.Single(restored.DeltaSuggestions);
        Assert.Equal(WorkerFlowStages.Scan, restored.DeltaSuggestions[0].Stage);
        Assert.Equal("OPENROUTER", restored.ConfiguredProvider);
        Assert.Equal("openai/gpt-5.2", restored.PlannerModel);
        Assert.Equal("openai/gpt-5-mini", restored.ResponseModel);
        Assert.Equal(WorkerReasoningModes.LlmValidated, restored.ReasoningMode);
    }

    [Fact]
    public void WorkerFlowStages_Normalize_Maps_Legacy_Stages()
    {
        Assert.Equal(WorkerFlowStages.Thinking, WorkerFlowStages.Normalize(WorkerStages.Intake));
        Assert.Equal(WorkerFlowStages.Thinking, WorkerFlowStages.Normalize(WorkerStages.Context));
        Assert.Equal(WorkerFlowStages.Plan, WorkerFlowStages.Normalize(WorkerStages.Planning));
        Assert.Equal(WorkerFlowStages.Approval, WorkerFlowStages.Normalize(WorkerStages.Approval));
        Assert.Equal(WorkerFlowStages.Run, WorkerFlowStages.Normalize(WorkerStages.Execution));
        Assert.Equal(WorkerFlowStages.Verify, WorkerFlowStages.Normalize(WorkerStages.Verification));
        Assert.Equal(WorkerFlowStages.Error, WorkerFlowStages.Normalize(WorkerStages.Recovery));
        Assert.Equal(WorkerFlowStages.Done, WorkerFlowStages.Normalize(WorkerStages.Done));
    }

    [Fact]
    public void SessionMemoryEntry_And_EpisodicRecord_RoundTrip()
    {
        var memory = new SessionMemoryEntry
        {
            EntryId = "entry-1",
            Kind = WorkerMemoryKinds.ToolResult,
            Content = "QC xong",
            Tags = new List<string> { "qc", "model" },
            DocumentKey = "path:a",
            ViewKey = "view:1",
            MissionId = "mission-1",
            ToolName = "review.smart_qc",
            CreatedUtc = new DateTime(2026, 3, 20, 10, 30, 0, DateTimeKind.Utc)
        };

        var episode = new EpisodicRecord
        {
            EpisodeId = "episode-1",
            RunId = "run-1",
            MissionType = "qc_request",
            Outcome = "completed",
            KeyObservations = new List<string> { "3 warnings" },
            KeyDecisions = new List<string> { "giu read-only" },
            ToolSequence = new List<string> { "review.model_health", "review.smart_qc" },
            ArtifactRefs = new List<string> { "artifacts/qc/report.json" },
            DocumentKey = "path:a",
            CreatedUtc = new DateTime(2026, 3, 20, 10, 45, 0, DateTimeKind.Utc)
        };

        var memoryJson = JsonUtil.Serialize(memory);
        var episodeJson = JsonUtil.Serialize(episode);

        var restoredMemory = JsonUtil.DeserializeRequired<SessionMemoryEntry>(memoryJson);
        var restoredEpisode = JsonUtil.DeserializeRequired<EpisodicRecord>(episodeJson);

        Assert.Equal("entry-1", restoredMemory.EntryId);
        Assert.Equal(WorkerMemoryKinds.ToolResult, restoredMemory.Kind);
        Assert.Equal("review.smart_qc", restoredMemory.ToolName);
        Assert.Equal("episode-1", restoredEpisode.EpisodeId);
        Assert.Equal("qc_request", restoredEpisode.MissionType);
        Assert.Equal(2, restoredEpisode.ToolSequence.Count);
    }
}
