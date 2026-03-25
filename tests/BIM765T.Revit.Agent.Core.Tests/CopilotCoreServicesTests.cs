using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class CopilotCoreServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T-CopilotCoreTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void CopilotTaskRunStore_Saves_And_Lists_DurableRuns()
    {
        var store = new CopilotTaskRunStore(new CopilotStatePaths(_root));
        store.Save(new TaskRun
        {
            RunId = "task-1",
            TaskKind = "fix_loop",
            TaskName = "parameter_hygiene",
            DocumentKey = "path:a",
            Status = "planned",
            IntentSummary = "Fix comments"
        });

        var listed = store.List(new TaskListRunsRequest { MaxResults = 10 });

        Assert.Single(listed);
        Assert.Equal("task-1", listed[0].RunId);
        Assert.Equal(1, store.CountRuns());
    }

    [Fact]
    public void CopilotTaskRunStore_Normalizes_LegacySparse_TaskRuns()
    {
        var paths = new CopilotStatePaths(_root);
        paths.EnsureCreated();
        File.WriteAllText(
            paths.GetRunPath("legacy-1"),
            "{\"RunId\":\"legacy-1\",\"TaskKind\":\"fix_loop\",\"TaskName\":\"parameter_hygiene\",\"DocumentKey\":\"path:a\",\"Status\":\"planned\"}");

        var store = new CopilotTaskRunStore(paths);
        var run = store.TryGet("legacy-1");

        Assert.NotNull(run);
        Assert.NotNull(run!.ChangedIds);
        Assert.NotNull(run.ArtifactKeys);
        Assert.NotNull(run.Checkpoints);
        Assert.NotNull(run.RecoveryBranches);
        Assert.NotNull(run.Steps);
        Assert.NotNull(run.TaskSpec);
        Assert.NotNull(run.WorkerProfile);
        Assert.NotNull(run.RunReport);
        Assert.Empty(run.Checkpoints);
        Assert.Empty(run.RecoveryBranches);
    }

    [Fact]
    public void ToolCapabilitySearchService_Finds_By_Query_And_Risk()
    {
        var service = new ToolCapabilitySearchService();
        var response = service.Search(new[]
        {
            new ToolManifest { ToolName = "export.ifc_safe", Description = "Export IFC safely.", RiskTags = new List<string> { "mutation" }, CapabilityPack = WorkerCapabilityPacks.CoreWorker, SkillGroup = WorkerSkillGroups.Documentation },
            new ToolManifest { ToolName = "review.model_health", Description = "Review health.", RiskTags = new List<string> { "qc" }, CapabilityPack = WorkerCapabilityPacks.CoreWorker, SkillGroup = WorkerSkillGroups.QualityControl }
        }, new ToolCapabilityLookupRequest
        {
            Query = "export ifc",
            MaxResults = 5
        });

        Assert.NotEmpty(response.Matches);
        Assert.Equal("export.ifc_safe", response.Matches[0].Manifest.ToolName);
    }

    [Fact]
    public void ToolCapabilitySearchService_Finds_By_CapabilityPack_And_SkillGroup()
    {
        var service = new ToolCapabilitySearchService();
        var response = service.Search(new[]
        {
            new ToolManifest { ToolName = "task.plan", Description = "Plan durable task.", CapabilityPack = WorkerCapabilityPacks.MemoryAndSoul, SkillGroup = WorkerSkillGroups.Orchestration },
            new ToolManifest { ToolName = "family.add_parameter_safe", Description = "Automation lab family mutation.", CapabilityPack = WorkerCapabilityPacks.AutomationLab, SkillGroup = WorkerSkillGroups.Automation }
        }, new ToolCapabilityLookupRequest
        {
            Query = "automation_lab automation",
            MaxResults = 5
        });

        Assert.NotEmpty(response.Matches);
        Assert.Equal("family.add_parameter_safe", response.Matches[0].Manifest.ToolName);
    }

    [Fact]
    public void ArtifactSummaryService_Summarizes_Json_File()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "artifact.json");
        File.WriteAllText(path, "{\"RunId\":\"abc\",\"Status\":\"pass\",\"IssueCount\":0}");

        var service = new ArtifactSummaryService();
        var summary = service.Summarize(new ArtifactSummarizeRequest
        {
            ArtifactPath = path,
            MaxChars = 500,
            MaxLines = 10
        });

        Assert.True(summary.Exists);
        Assert.Equal("json", summary.DetectedFormat);
        Assert.Contains("RunId", summary.TopLevelKeys);
    }

    [Fact]
    public void ContextAnchorService_Resolves_Hot_And_Warm_Items()
    {
        var service = new ContextAnchorService();
        var bundle = service.ResolveBundle(
            new[]
            {
                new ContextBundleItem { AnchorId = "hot:runtime", Tier = "hot", Title = "Runtime", Summary = "Hot state", Score = 100 }
            },
            new[]
            {
                new TaskRun { RunId = "task-1", TaskKind = "fix_loop", TaskName = "parameter_hygiene", Status = "verified", PlanSummary = "Comments fixed", Tags = new List<string> { "comments" } }
            },
            Array.Empty<TaskMemoryPromotionRecord>(),
            new ContextResolveBundleRequest
            {
                Query = "comments",
                IncludeHot = true,
                IncludeWarm = true,
                MaxAnchors = 5
            });

        Assert.True(bundle.Items.Count >= 2);
        Assert.Equal("hot:runtime", bundle.Items[0].AnchorId);
    }

    [Fact]
    public void ContextAnchorService_Deduplicates_Bundle_By_AnchorId()
    {
        var service = new ContextAnchorService();
        var bundle = service.ResolveBundle(
            new[]
            {
                new ContextBundleItem { AnchorId = "task:run-1", Tier = "hot", Title = "Runtime copy", Summary = "Hot", Score = 100 }
            },
            new[]
            {
                new TaskRun { RunId = "run-1", TaskKind = "fix_loop", TaskName = "parameter_hygiene", Status = "planned", PlanSummary = "Warm copy" }
            },
            Array.Empty<TaskMemoryPromotionRecord>(),
            new ContextResolveBundleRequest
            {
                Query = "parameter",
                IncludeHot = true,
                IncludeWarm = true,
                MaxAnchors = 10
            });

        Assert.Single(bundle.Items.FindAll(x => string.Equals(x.AnchorId, "task:run-1", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ToolGuidanceService_Builds_Risk_Recovery_And_FollowUps()
    {
        var service = new ToolGuidanceService();
        var response = service.Build(new[]
        {
            new ToolManifest
            {
                ToolName = ToolNames.ElementDeleteSafe,
                Description = "Delete elements with preview.",
                PermissionLevel = PermissionLevel.Mutate,
                ApprovalRequirement = ApprovalRequirement.HighRiskToken,
                SupportsDryRun = true,
                MutatesModel = true,
                RequiresExpectedContext = true,
                BatchMode = "chunked",
                RiskTags = new List<string> { "mutation", "high_risk" }
            },
            new ToolManifest { ToolName = ToolNames.ContextGetHotState, Description = "Hot state", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.DocumentGetContextFingerprint, Description = "Fingerprint", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.SessionGetRecentOperations, Description = "Recent ops", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.ToolGetGuidance, Description = "Guidance", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.SessionGetTaskContext, Description = "Task context", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.TaskResume, Description = "Resume", PermissionLevel = PermissionLevel.Read },
            new ToolManifest { ToolName = ToolNames.TaskGetResiduals, Description = "Residuals", PermissionLevel = PermissionLevel.Read }
        }, new ToolGuidanceRequest
        {
            ToolNames = new List<string> { ToolNames.ElementDeleteSafe },
            MaxResults = 3
        });

        var guidance = Assert.Single(response.Guidance);
        Assert.Equal(ToolNames.ElementDeleteSafe, guidance.ToolName);
        Assert.True(guidance.RiskScore >= 9);
        Assert.Contains("approval_token", guidance.Prerequisites);
        Assert.Contains(ToolNames.ContextGetHotState, guidance.FollowUps);
        Assert.Contains(StatusCodes.ContextMismatch, guidance.CommonFailureCodes);
        Assert.Contains(ToolNames.DocumentGetContextFingerprint, guidance.RecommendedRecoveryTools);
    }

    [Fact]
    public void ContextDeltaSummaryService_Suggests_Context_Refresh_After_ContextMismatch()
    {
        var service = new ContextDeltaSummaryService();
        var response = service.Build(
            new TaskContextResponse
            {
                Document = new DocumentSummaryDto { DocumentKey = "path:a" },
                RecentOperations = new List<OperationJournalEntry>
                {
                    new OperationJournalEntry
                    {
                        ToolName = ToolNames.ElementMoveSafe,
                        StatusCode = StatusCodes.ContextMismatch,
                        Succeeded = false,
                        StartedUtc = new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc)
                    }
                },
                RecentEvents = new List<EventRecord>
                {
                    new EventRecord
                    {
                        EventKind = "DocumentChanged",
                        TimestampUtc = new DateTime(2026, 3, 19, 8, 1, 0, DateTimeKind.Utc),
                        ElementIds = new List<int> { 1, 2 }
                    }
                }
            },
            new QueueStateResponse { PendingCount = 1 },
            new[]
            {
                new ToolManifest { ToolName = ToolNames.ElementMoveSafe, MutatesModel = true, PermissionLevel = PermissionLevel.Mutate },
                new ToolManifest { ToolName = ToolNames.SessionGetQueueState, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.DocumentGetContextFingerprint, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.ContextGetHotState, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.SessionGetRecentOperations, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.ReviewCaptureSnapshot, PermissionLevel = PermissionLevel.Read }
            },
            new ContextDeltaSummaryRequest
            {
                DocumentKey = "path:a",
                MaxRecentOperations = 5,
                MaxRecentEvents = 5,
                MaxRecommendations = 5
            });

        Assert.Equal("path:a", response.DocumentKey);
        Assert.Equal(ToolNames.ElementMoveSafe, response.LastMutationTool);
        Assert.Equal(StatusCodes.ContextMismatch, response.LastFailureCode);
        Assert.Contains(ToolNames.DocumentGetContextFingerprint, response.SuggestedNextTools);
        Assert.Contains(ToolNames.SessionGetQueueState, response.SuggestedNextTools);
    }

    [Fact]
    public void ContextDeltaSummaryService_Merges_Category_And_Discipline_Insight()
    {
        var service = new ContextDeltaSummaryService();
        var response = service.Build(
            new TaskContextResponse
            {
                Document = new DocumentSummaryDto { DocumentKey = "path:a" },
                RecentOperations = new List<OperationJournalEntry>
                {
                    new OperationJournalEntry
                    {
                        ToolName = ToolNames.SheetCreateSafe,
                        StatusCode = StatusCodes.ExecuteSucceeded,
                        Succeeded = true,
                        ChangedIds = new List<int> { 10, 11 },
                        StartedUtc = new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc)
                    }
                }
            },
            new QueueStateResponse(),
            new[]
            {
                new ToolManifest { ToolName = ToolNames.SheetCreateSafe, MutatesModel = true, PermissionLevel = PermissionLevel.Mutate },
                new ToolManifest { ToolName = ToolNames.ContextGetHotState, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.ToolGetGuidance, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.SessionGetRecentOperations, PermissionLevel = PermissionLevel.Read },
                new ToolManifest { ToolName = ToolNames.ReviewCaptureSnapshot, PermissionLevel = PermissionLevel.Read }
            },
            new ContextDeltaSummaryRequest
            {
                DocumentKey = "path:a",
                MaxRecentOperations = 5,
                MaxRecentEvents = 5,
                MaxRecommendations = 5
            },
            new ContextDeltaInsight
            {
                AddedElementEstimate = 2,
                RemovedElementEstimate = 0,
                ModifiedElementEstimate = 0,
                TopCategories = new List<CountByNameDto> { new CountByNameDto { Name = "Sheets", Count = 2 } },
                DisciplineHints = new List<CountByNameDto> { new CountByNameDto { Name = "Architecture", Count = 2 } },
                RecentMutationKinds = new List<string> { "create" }
            });

        Assert.Equal(2, response.AddedElementEstimate);
        Assert.Single(response.TopCategories);
        Assert.Equal("Sheets", response.TopCategories[0].Name);
        Assert.Single(response.DisciplineHints);
        Assert.Contains("create", response.RecentMutationKinds);
    }

    [Fact]
    public void ExternalTaskIntakeService_Normalizes_Connector_Task_Into_TaskPlanRequest()
    {
        var service = new ExternalTaskIntakeService();
        var normalized = service.Normalize(new ExternalTaskIntakeRequest
        {
            DocumentKey = "path:model",
            TaskKind = "workflow",
            Envelope = new ConnectorTaskEnvelope
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105",
                ProjectRef = "proj-1",
                CallbackMode = "report_back_queue",
                Title = "Issue 105",
                Description = "Review and close issue 105."
            }
        }, new WorkerProfile
        {
            PersonaId = WorkerPersonas.StrictQaFirm,
            AllowedSkillGroups = new List<string> { WorkerSkillGroups.Documentation }
        });

        Assert.Equal("acc", normalized.TaskSpec.Source);
        Assert.Equal("proj-1", normalized.TaskSpec.ProjectScope);
        Assert.True(normalized.TaskSpec.ApprovalPolicy.AllowQueuedExecution);
        Assert.Equal("Issue 105", normalized.TaskName);
        Assert.Equal("acc", normalized.TaskSpec.CallbackTarget.System);
        Assert.Equal(WorkerPersonas.StrictQaFirm, normalized.WorkerProfile.PersonaId);
    }

    [Fact]
    public void TaskQueueCoordinator_Enqueues_Claims_And_Completes_Approved_Run()
    {
        var queueStore = new CopilotTaskQueueStore(new CopilotStatePaths(_root));
        var coordinator = new TaskQueueCoordinator(queueStore);
        var run = new TaskRun
        {
            RunId = "run-1",
            TaskKind = "workflow",
            TaskName = "issue_105",
            Status = "approved",
            QueueEligible = true,
            ApprovalToken = "token-1",
            PreviewRunId = "preview-1",
            TaskSpec = new TaskSpec
            {
                ApprovalPolicy = new TaskApprovalPolicy
                {
                    RequiresOperatorApproval = true,
                    AllowQueuedExecution = true
                },
                CallbackTarget = new TaskCallbackTarget
                {
                    System = "acc",
                    Reference = "issue-105",
                    Mode = "report_back"
                }
            },
            ConnectorTask = new ConnectorTaskEnvelope
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105",
                CallbackMode = "report_back"
            }
        };

        var queued = coordinator.Enqueue(run, new TaskQueueEnqueueRequest { Note = "night run" }, "tester");
        var leased = coordinator.ClaimNext(new TaskQueueClaimRequest { LeaseOwner = "worker-1" });
        var completed = coordinator.Complete(new TaskQueueCompleteRequest
        {
            QueueItemId = leased.QueueItemId,
            Status = "completed",
            Message = "done"
        }, run);

        Assert.Equal("pending", queued.Status);
        Assert.Equal("leased", leased.Status);
        Assert.Equal("worker-1", leased.LeaseOwner);
        Assert.Equal("completed", completed.Status);
        Assert.Equal("done", completed.LastStatusMessage);
    }

    [Fact]
    public void TaskQueueCoordinator_Can_Claim_Specific_Queue_Item_And_Reject_Mismatched_Lease()
    {
        var queueStore = new CopilotTaskQueueStore(new CopilotStatePaths(_root));
        var coordinator = new TaskQueueCoordinator(queueStore);
        var run = new TaskRun
        {
            RunId = "run-claim-1",
            TaskKind = "workflow",
            TaskName = "issue_107",
            Status = "approved",
            QueueEligible = true,
            ApprovalToken = "token-107",
            TaskSpec = new TaskSpec
            {
                ApprovalPolicy = new TaskApprovalPolicy
                {
                    RequiresOperatorApproval = true,
                    AllowQueuedExecution = true
                }
            }
        };

        var queued = coordinator.Enqueue(run, new TaskQueueEnqueueRequest(), "tester");
        var leased = coordinator.Claim(queued.QueueItemId, "worker-a");

        Assert.Equal("leased", leased.Status);
        Assert.Throws<InvalidOperationException>(() => coordinator.Claim(queued.QueueItemId, "worker-b"));
    }

    [Fact]
    public void TaskQueueCoordinator_Reconciles_Blocked_Run_To_Failed_Queue_Item()
    {
        var queueStore = new CopilotTaskQueueStore(new CopilotStatePaths(_root));
        var coordinator = new TaskQueueCoordinator(queueStore);
        var run = new TaskRun
        {
            RunId = "run-2",
            TaskKind = "workflow",
            TaskName = "issue_106",
            Status = "approved",
            QueueEligible = true,
            ApprovalToken = "token-2",
            TaskSpec = new TaskSpec
            {
                ApprovalPolicy = new TaskApprovalPolicy
                {
                    RequiresOperatorApproval = true,
                    AllowQueuedExecution = true
                }
            }
        };

        var queued = coordinator.Enqueue(run, new TaskQueueEnqueueRequest(), "tester");
        run.LastQueueItemId = queued.QueueItemId;
        run.Status = "blocked";
        run.LastErrorCode = StatusCodes.ContextMismatch;
        run.LastErrorMessage = "Context drifted.";

        var reconciled = coordinator.Reconcile(run);

        Assert.NotNull(reconciled);
        Assert.Equal("failed", reconciled!.Status);
        Assert.Contains(StatusCodes.ContextMismatch, reconciled.LastStatusMessage);
    }

    [Fact]
    public void ConnectorCallbackPreviewService_Maps_Status_And_Preserves_Run_Metadata()
    {
        var service = new ConnectorCallbackPreviewService();
        var response = service.Build(new TaskRun
        {
            RunId = "run-1",
            TaskKind = "workflow",
            TaskName = "issue_105",
            Status = "verified",
            ArtifactKeys = new List<string> { "artifact:1" },
            RunReport = new RunReport
            {
                TaskSummary = "Issue 105 resolved.",
                NextRecommendedAction = "report_back",
                ResidualRisks = new List<string> { "None" }
            },
            TaskSpec = new TaskSpec
            {
                CallbackTarget = new TaskCallbackTarget
                {
                    System = "acc",
                    Reference = "issue-105",
                    Mode = "report_back"
                }
            },
            ConnectorTask = new ConnectorTaskEnvelope
            {
                ExternalSystem = "acc",
                ExternalTaskRef = "issue-105",
                StatusMapping = new Dictionary<string, string> { ["done"] = "closed" }
            }
        }, new TaskQueueItem
        {
            QueueItemId = "queue-1",
            Status = "completed"
        });

        Assert.Equal("acc", response.System);
        Assert.Equal("closed", response.SuggestedStatus);
        Assert.Equal("queue-1", response.Payload.QueueItemId);
        Assert.Contains("Issue 105 resolved.", response.PayloadJson);
    }
}
