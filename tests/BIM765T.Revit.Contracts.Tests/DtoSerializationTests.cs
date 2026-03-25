using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class DtoSerializationTests
{
    [Fact]
    public void DocumentSummaryDto_RoundTrips()
    {
        var dto = new DocumentSummaryDto
        {
            DocumentKey = "path:c:\\test.rvt",
            Title = "Test",
            PathName = "c:\\test.rvt",
            IsActive = true,
            IsModified = false,
            IsWorkshared = true,
            CanSave = true,
            CanSynchronize = true
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<DocumentSummaryDto>(json);

        Assert.Equal("path:c:\\test.rvt", result.DocumentKey);
        Assert.Equal("Test", result.Title);
        Assert.True(result.IsActive);
        Assert.True(result.IsWorkshared);
    }

    [Fact]
    public void ElementSummaryDto_Default_Has_Empty_Collections()
    {
        var dto = new ElementSummaryDto();
        Assert.NotNull(dto.Parameters);
        Assert.Empty(dto.Parameters);
        Assert.Equal(string.Empty, dto.UniqueId);
        Assert.Equal(string.Empty, dto.CategoryName);
    }

    [Fact]
    public void InspectFilterRequest_RoundTrip_PreservesFilterNameAndId()
    {
        var req = new InspectFilterRequest
        {
            FilterName = "00i. CAS",
            FilterId = 35514583,
            IncludeViewUsage = false,
            IncludeTemplateUsage = false
        };

        var json = JsonUtil.Serialize(req);
        // Ghi ra để debug xem format thật
        Console.WriteLine("Serialized InspectFilterRequest: " + json);

        var back = JsonUtil.DeserializeRequired<InspectFilterRequest>(json);
        Assert.Equal("00i. CAS", back.FilterName);
        Assert.Equal(35514583, back.FilterId);
    }

    [Fact]
    public void InspectFilterRequest_DeserializeFromExternalJson_PreservesFilterName()
    {
        // Simulate JSON từ Bridge CLI gửi vào — flat JSON, không phải DataContract format
        var externalJson = "{\"FilterName\":\"00i. CAS\",\"FilterId\":35514583,\"IncludeViewUsage\":false,\"IncludeTemplateUsage\":false}";
        Console.WriteLine("External JSON input: " + externalJson);

        var back = JsonUtil.DeserializeRequired<InspectFilterRequest>(externalJson);
        Console.WriteLine("FilterName: '" + back.FilterName + "' | FilterId: " + back.FilterId);
        Assert.Equal("00i. CAS", back.FilterName);
        Assert.Equal(35514583, back.FilterId);
    }

    [Fact]
    public void ToolManifest_RoundTrips()
    {
        var manifest = new ToolManifest
        {
            ToolName = "element.query",
            Description = "Query elements",
            PermissionLevel = PermissionLevel.Read,
            SupportsDryRun = true,
            Enabled = true,
            RequiredContext = new System.Collections.Generic.List<string> { "document", "selection" },
            RequiresExpectedContext = true,
            MutatesModel = false,
            PreviewArtifacts = new System.Collections.Generic.List<string> { "snapshot", "diff" },
            RiskTags = new System.Collections.Generic.List<string> { "qc" },
            RulePackTags = new System.Collections.Generic.List<string> { "document_health_v1" },
            InputSchemaJson = "{\"type\":\"object\"}",
            ExecutionTimeoutMs = 180000,
            CapabilityPack = WorkerCapabilityPacks.CoreWorker,
            SkillGroup = WorkerSkillGroups.QualityControl,
            Audience = WorkerAudience.Commercial,
            Visibility = WorkerVisibility.Visible,
            PrimaryPersona = ToolPrimaryPersonas.ProductionBimer,
            UserValueClass = ToolUserValueClasses.DailyRoi,
            RepeatabilityClass = ToolRepeatabilityClasses.Repeatable,
            AutomationStage = ToolAutomationStages.CoreSkill,
            CanTeachBack = true,
            FallbackArtifactKinds = new System.Collections.Generic.List<string> { FallbackArtifactKinds.Playbook },
            CommercialTier = CommercialTiers.PersonalPro,
            CacheValueClass = CacheValueClasses.TeachBack
        };

        var json = JsonUtil.Serialize(manifest);
        var result = JsonUtil.DeserializeRequired<ToolManifest>(json);

        Assert.Equal("element.query", result.ToolName);
        Assert.Equal(PermissionLevel.Read, result.PermissionLevel);
        Assert.True(result.SupportsDryRun);
        Assert.Equal(2, result.RequiredContext.Count);
        Assert.True(result.RequiresExpectedContext);
        Assert.Contains("snapshot", result.PreviewArtifacts);
        Assert.Contains("document_health_v1", result.RulePackTags);
        Assert.Equal("{\"type\":\"object\"}", result.InputSchemaJson);
        Assert.Equal(180000, result.ExecutionTimeoutMs);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, result.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.QualityControl, result.SkillGroup);
        Assert.Equal(CapabilityDomains.General, result.CapabilityDomain);
        Assert.Equal(ToolPrimaryPersonas.ProductionBimer, result.PrimaryPersona);
        Assert.Equal(ToolUserValueClasses.DailyRoi, result.UserValueClass);
        Assert.Equal(ToolRepeatabilityClasses.Repeatable, result.RepeatabilityClass);
        Assert.Equal(ToolAutomationStages.CoreSkill, result.AutomationStage);
        Assert.True(result.CanTeachBack);
        Assert.Contains(FallbackArtifactKinds.Playbook, result.FallbackArtifactKinds);
        Assert.Equal(CommercialTiers.PersonalPro, result.CommercialTier);
        Assert.Equal(CacheValueClasses.TeachBack, result.CacheValueClass);
    }

    [Fact]
    public void SkillLibraryStrategyDtos_RoundTrip()
    {
        var quick = new QuickActionResponse
        {
            Query = "import sheet metadata from excel",
            WorkspaceId = "default",
            ExecutionDisposition = "mapped_only",
            StrategySummary = "tool -> playbook -> fallback_artifact (mapped_only)",
            FallbackProposal = new FallbackArtifactProposal
            {
                ProposalId = "proposal-1",
                WorkspaceId = "default",
                Reason = "mapped_only",
                Summary = "Fallback proposal",
                PreviewSummary = "Review artifact before save.",
                ArtifactKinds = new System.Collections.Generic.List<string>
                {
                    FallbackArtifactKinds.Playbook,
                    FallbackArtifactKinds.CsvMapping,
                    FallbackArtifactKinds.OpenXmlRecipe
                },
                ArtifactPaths = new System.Collections.Generic.List<string>
                {
                    "artifacts/fallback/default/import_sheet_metadata.playbook.json"
                }
            }
        };

        var script = new ScriptSourceManifest
        {
            ScriptId = "builtin.sheet_profile",
            DisplayName = "Sheet profile",
            SourceKind = CommandSourceKinds.Internal,
            SourceRef = "builtin",
            EntryPoint = "builtin.sheet_profile",
            CapabilityDomain = CapabilityDomains.Governance,
            ImportMode = SourceImportModes.BehaviorOnly,
            PrimaryPersona = ToolPrimaryPersonas.ProductionBimer,
            UserValueClass = ToolUserValueClasses.DailyRoi,
            RepeatabilityClass = ToolRepeatabilityClasses.Teachable,
            AutomationStage = ToolAutomationStages.ArtifactFallback,
            FallbackArtifactKinds = new System.Collections.Generic.List<string> { FallbackArtifactKinds.Playbook },
            CommercialTier = CommercialTiers.PersonalPro,
            CacheValueClass = CacheValueClasses.ArtifactReuse,
            SourceLogicIds = new System.Collections.Generic.List<string> { "revitlookup.inspect" }
        };

        var result = new CommandExecuteResponse
        {
            StatusCode = StatusCodes.CommandExecutionBlocked,
            Summary = "Blocked for fallback review.",
            ToolName = ToolNames.CommandExecuteSafe,
            FallbackProposal = quick.FallbackProposal
        };

        var quickJson = JsonUtil.Serialize(quick);
        var scriptJson = JsonUtil.Serialize(script);
        var executeJson = JsonUtil.Serialize(result);

        var quickRoundTrip = JsonUtil.DeserializeRequired<QuickActionResponse>(quickJson);
        var scriptRoundTrip = JsonUtil.DeserializeRequired<ScriptSourceManifest>(scriptJson);
        var executeRoundTrip = JsonUtil.DeserializeRequired<CommandExecuteResponse>(executeJson);

        Assert.Equal(FallbackArtifactKinds.CsvMapping, quickRoundTrip.FallbackProposal.ArtifactKinds[1]);
        Assert.Equal("proposal-1", quickRoundTrip.FallbackProposal.ProposalId);
        Assert.Equal(SourceImportModes.BehaviorOnly, scriptRoundTrip.ImportMode);
        Assert.Equal(ToolAutomationStages.ArtifactFallback, scriptRoundTrip.AutomationStage);
        Assert.Equal(CacheValueClasses.ArtifactReuse, scriptRoundTrip.CacheValueClass);
        Assert.Equal("proposal-1", executeRoundTrip.FallbackProposal.ProposalId);
    }

    [Fact]
    public void CapabilityOrchestrationDtos_RoundTrip()
    {
        var dto = new CompiledTaskPlan
        {
            CapabilityDomain = CapabilityDomains.Coordination,
            DeterminismLevel = ToolDeterminismLevels.PolicyBacked,
            VerificationMode = ToolVerificationModes.GeometryCheck,
            Task = new IntentTask
            {
                Query = "resolve clash",
                WorkspaceId = "default",
                Discipline = CapabilityDisciplines.Mep,
                IssueKinds = new System.Collections.Generic.List<string> { CapabilityIssueKinds.HardClash }
            },
            PolicyResolution = new PolicyResolution
            {
                WorkspaceId = "default",
                CapabilityDomain = CapabilityDomains.Coordination,
                ResolvedPackIds = new System.Collections.Generic.List<string> { "bim765t.standards.mep.clearance" }
            },
            RecommendedSpecialists = new System.Collections.Generic.List<CapabilitySpecialistDescriptor>
            {
                new CapabilitySpecialistDescriptor
                {
                    SpecialistId = "coordination-specialist",
                    PackId = "bim765t.agents.specialist.coordination",
                    CapabilityDomains = new System.Collections.Generic.List<string> { CapabilityDomains.Coordination }
                }
            },
            CandidateToolNames = new System.Collections.Generic.List<string> { ToolNames.SpatialClashDetect, ToolNames.IntentCompile },
            VerifyTools = new System.Collections.Generic.List<string> { ToolNames.SpatialClashDetect },
            Summary = "compiled"
        };

        var json = JsonUtil.Serialize(dto);
        var roundTrip = JsonUtil.DeserializeRequired<CompiledTaskPlan>(json);

        Assert.Equal(CapabilityDomains.Coordination, roundTrip.CapabilityDomain);
        Assert.Equal(ToolVerificationModes.GeometryCheck, roundTrip.VerificationMode);
        Assert.Equal("default", roundTrip.PolicyResolution.WorkspaceId);
        Assert.Single(roundTrip.RecommendedSpecialists);
        Assert.Contains(ToolNames.SpatialClashDetect, roundTrip.CandidateToolNames);
    }

    [Fact]
    public void OperationJournalEntry_RoundTrips_CorrelationId()
    {
        var entry = new OperationJournalEntry
        {
            JournalId = "journal-001",
            RequestId = "request-001",
            CorrelationId = "corr-001",
            SessionId = "session-001",
            PreviewRunId = "preview-001",
            ToolName = "review.model_health",
            StatusCode = StatusCodes.ReadSucceeded
        };

        var json = JsonUtil.Serialize(entry);
        var result = JsonUtil.DeserializeRequired<OperationJournalEntry>(json);

        Assert.Equal("journal-001", result.JournalId);
        Assert.Equal("request-001", result.RequestId);
        Assert.Equal("corr-001", result.CorrelationId);
        Assert.Equal("session-001", result.SessionId);
        Assert.Equal("preview-001", result.PreviewRunId);
        Assert.Equal("review.model_health", result.ToolName);
    }

    [Fact]
    public void BridgeCapabilities_Default_Has_BIM765T_Platform()
    {
        var caps = new BridgeCapabilities();
        Assert.Equal("765T Revit Bridge", caps.PlatformName);
        Assert.Equal("2024", caps.RevitYear);
        Assert.True(caps.SupportsDryRun);
        Assert.True(caps.SupportsApprovalTokens);
        Assert.True(caps.SupportsMcpHost);
        Assert.True(caps.SupportsWorkflowRuntime);
        Assert.True(caps.SupportsInspectorLane);
        Assert.Equal(BIM765T.Revit.Contracts.Common.BridgeProtocol.PipeV1, caps.BridgeProtocolVersion);
        Assert.Equal(BIM765T.Revit.Contracts.Common.BridgeConstants.McpDefaultProtocolVersion, caps.McpProtocolVersion);
        Assert.Equal(WorkerShellModes.Worker, caps.VisibleShellMode);
        Assert.NotNull(caps.DefaultWorkerProfile);
    }

    [Fact]
    public void ReviewReport_RoundTrips_With_Issues()
    {
        var report = new ReviewReport
        {
            Name = "test_review",
            DocumentKey = "path:test",
            IssueCount = 1,
            Issues = new System.Collections.Generic.List<ReviewIssue>
            {
                new ReviewIssue
                {
                    Code = "TEST_ISSUE",
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Test issue",
                    ElementId = 123
                }
            }
        };

        var json = JsonUtil.Serialize(report);
        var result = JsonUtil.DeserializeRequired<ReviewReport>(json);

        Assert.Equal("test_review", result.Name);
        Assert.Equal(1, result.IssueCount);
        Assert.Single(result.Issues);
        Assert.Equal("TEST_ISSUE", result.Issues[0].Code);
        Assert.Equal(123, result.Issues[0].ElementId);
    }

    [Fact]
    public void DiffSummary_Default_Has_Empty_Collections()
    {
        var diff = new DiffSummary();
        Assert.NotNull(diff.CreatedIds);
        Assert.NotNull(diff.ModifiedIds);
        Assert.NotNull(diff.DeletedIds);
        Assert.NotNull(diff.ParameterChanges);
        Assert.Equal(0, diff.WarningDelta);
    }

    [Fact]
    public void ContextFingerprint_RoundTrips()
    {
        var fp = new ContextFingerprint
        {
            DocumentKey = "path:c:\\test.rvt",
            ViewKey = "view:12345",
            SelectionCount = 5,
            SelectionHash = "abc123",
            SelectedElementIds = new System.Collections.Generic.List<int> { 1, 2, 3, 4, 5 },
            ActiveDocEpoch = 99
        };

        var json = JsonUtil.Serialize(fp);
        var result = JsonUtil.DeserializeRequired<ContextFingerprint>(json);

        Assert.Equal("path:c:\\test.rvt", result.DocumentKey);
        Assert.Equal("view:12345", result.ViewKey);
        Assert.Equal(5, result.SelectionCount);
        Assert.Equal("abc123", result.SelectionHash);
        Assert.Equal(5, result.SelectedElementIds.Count);
        Assert.Equal(99, result.ActiveDocEpoch);
    }

    [Fact]
    public void PermissionLevel_Enum_Has_Five_Levels()
    {
        var values = Enum.GetValues<PermissionLevel>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void ApprovalRequirement_Enum_Has_Three_Values()
    {
        var values = Enum.GetValues<ApprovalRequirement>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void ExecutionResult_Default_Has_Empty_State()
    {
        var result = new ExecutionResult();
        Assert.Equal(string.Empty, result.OperationName);
        Assert.False(result.DryRun);
        Assert.False(result.ConfirmationRequired);
        Assert.NotNull(result.ChangedIds);
        Assert.NotNull(result.DiffSummary);
        Assert.NotNull(result.Diagnostics);
    }

    [Fact]
    public void FamilyAxisAlignmentRequest_RoundTrips_UseActiveViewOnly()
    {
        var dto = new FamilyAxisAlignmentRequest
        {
            ViewName = "{3D}",
            MaxElements = 5000,
            UseActiveViewOnly = false
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<FamilyAxisAlignmentRequest>(json);

        Assert.Equal("{3D}", result.ViewName);
        Assert.Equal(5000, result.MaxElements);
        Assert.False(result.UseActiveViewOnly);
    }

    [Fact]
    public void RoundExternalizationPlanRequest_RoundTrips()
    {
        var dto = new RoundExternalizationPlanRequest
        {
            ParentFamilyName = "Penetration Alpha",
            RoundFamilyName = "Round",
            MaxResults = 10000,
            PlanWrapperFamilyName = "Round_Project",
            ElevXWrapperFamilyName = "Round_Project",
            ElevYWrapperFamilyName = "Round_Project"
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<RoundExternalizationPlanRequest>(json);

        Assert.Equal("Penetration Alpha", result.ParentFamilyName);
        Assert.Equal("Round", result.RoundFamilyName);
        Assert.Equal(10000, result.MaxResults);
        Assert.Equal("Round_Project", result.PlanWrapperFamilyName);
        Assert.Equal("Round_Project", result.ElevXWrapperFamilyName);
        Assert.Equal("Round_Project", result.ElevYWrapperFamilyName);
    }

    [Fact]
    public void BuildRoundProjectWrappersRequest_RoundTrips()
    {
        var dto = new BuildRoundProjectWrappersRequest
        {
            SourceFamilyName = "Round",
            OutputDirectory = @"C:\temp\round_wrappers",
            PlanWrapperFamilyName = "Round_Project",
            PlanWrapperTypeName = "AXIS_X",
            ElevXWrapperFamilyName = "Round_Project",
            ElevXWrapperTypeName = "AXIS_Z",
            ElevYWrapperFamilyName = "Round_Project",
            ElevYWrapperTypeName = "AXIS_Y",
            OverwriteFamilyFiles = true,
            LoadIntoProject = true,
            GenerateSizeSpecificVariants = true
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<BuildRoundProjectWrappersRequest>(json);

        Assert.Equal("Round", result.SourceFamilyName);
        Assert.Equal(@"C:\temp\round_wrappers", result.OutputDirectory);
        Assert.Equal("Round_Project", result.PlanWrapperFamilyName);
        Assert.Equal("AXIS_X", result.PlanWrapperTypeName);
        Assert.Equal("Round_Project", result.ElevXWrapperFamilyName);
        Assert.Equal("AXIS_Z", result.ElevXWrapperTypeName);
        Assert.Equal("Round_Project", result.ElevYWrapperFamilyName);
        Assert.Equal("AXIS_Y", result.ElevYWrapperTypeName);
        Assert.True(result.OverwriteFamilyFiles);
        Assert.True(result.GenerateSizeSpecificVariants);
        Assert.True(result.LoadIntoProject);
    }

    [Fact]
    public void RoundPenetrationCutRequests_RoundTrip()
    {
        var plan = new RoundPenetrationCutPlanRequest
        {
            TargetFamilyName = "Mii_Pen-Round_Project",
            SourceElementClasses = new System.Collections.Generic.List<string> { "PIP", "PPF" },
            HostElementClasses = new System.Collections.Generic.List<string> { "GYB", "WFR" },
            SourceElementIds = new System.Collections.Generic.List<int> { 10, 11 },
            GybClearancePerSideInches = 0.25,
            WfrClearancePerSideInches = 0.125,
            TraceCommentPrefix = "BIM765T_PEN_ROUND"
        };

        var execute = new CreateRoundPenetrationCutBatchRequest
        {
            TargetFamilyName = "Mii_Pen-Round_Project",
            OutputDirectory = @"C:\temp\pen_round",
            MaxCutRetries = 2,
            RetryBackoffMs = 150,
            ForceRebuildFamilies = true
        };

        var qc = new RoundPenetrationCutQcRequest
        {
            TargetFamilyName = "Mii_Pen-Round_Project",
            MaxResults = 200
        };

        var planJson = JsonUtil.Serialize(plan);
        var executeJson = JsonUtil.Serialize(execute);
        var qcJson = JsonUtil.Serialize(qc);

        var planResult = JsonUtil.DeserializeRequired<RoundPenetrationCutPlanRequest>(planJson);
        var executeResult = JsonUtil.DeserializeRequired<CreateRoundPenetrationCutBatchRequest>(executeJson);
        var qcResult = JsonUtil.DeserializeRequired<RoundPenetrationCutQcRequest>(qcJson);

        Assert.Equal("Mii_Pen-Round_Project", planResult.TargetFamilyName);
        Assert.Equal(2, planResult.SourceElementIds.Count);
        Assert.Equal(@"C:\temp\pen_round", executeResult.OutputDirectory);
        Assert.True(executeResult.ForceRebuildFamilies);
        Assert.Equal(2, executeResult.MaxCutRetries);
        Assert.Equal(150, executeResult.RetryBackoffMs);
        Assert.Equal("Mii_Pen-Round_Project", qcResult.TargetFamilyName);
        Assert.Equal(200, qcResult.MaxResults);
    }

    [Fact]
    public void FixLoopRun_RoundTrips()
    {
        var dto = new FixLoopRun
        {
            RunId = "run-1",
            ScenarioName = "parameter_hygiene",
            PlaybookName = "default.fix_loop_v1",
            Status = "planned",
            DocumentKey = "path:c:\\test.rvt",
            RecommendedActionIds = new System.Collections.Generic.List<string> { "a1" },
            Issues = new System.Collections.Generic.List<FixLoopIssue>
            {
                new FixLoopIssue { IssueId = "i1", IssueClass = "parameter_missing_or_empty", Code = "PARAMETER_EMPTY", Message = "Missing", ElementId = 10 }
            },
            CandidateActions = new System.Collections.Generic.List<FixLoopCandidateAction>
            {
                new FixLoopCandidateAction
                {
                    ActionId = "a1",
                    ToolName = "parameter.batch_fill_safe",
                    Title = "Fill",
                    ElementIds = new System.Collections.Generic.List<int> { 10 },
                    DecisionReason = "Matched category rule.",
                    Priority = 30,
                    IsRecommended = true,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ExpectedIssueDelta = 1,
                        ExpectedRemainingMax = 0
                    }
                }
            },
            Evidence = new FixLoopEvidenceBundle
            {
                RecommendedActionIds = new System.Collections.Generic.List<string> { "a1" },
                SelectedActionIds = new System.Collections.Generic.List<string> { "a1" },
                ExpectedIssueDelta = 1,
                ActualIssueDelta = 1,
                VerificationStatus = "pass"
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<FixLoopRun>(json);

        Assert.Equal("run-1", result.RunId);
        Assert.Single(result.Issues);
        Assert.Single(result.CandidateActions);
        Assert.Single(result.RecommendedActionIds);
        Assert.Equal("Matched category rule.", result.CandidateActions[0].DecisionReason);
        Assert.Equal(30, result.CandidateActions[0].Priority);
        Assert.True(result.CandidateActions[0].IsRecommended);
        Assert.Single(result.Evidence.RecommendedActionIds);
        Assert.Single(result.Evidence.SelectedActionIds);
        Assert.Equal(1, result.Evidence.ExpectedIssueDelta);
        Assert.Equal(1, result.Evidence.ActualIssueDelta);
        Assert.Equal("pass", result.Evidence.VerificationStatus);
    }

    [Fact]
    public void DeliveryOpsRequests_RoundTrip()
    {
        var familyLoad = new FamilyLoadRequest
        {
            DocumentKey = "path:c:\\test.rvt",
            LibraryRootName = "repo_testing_families",
            RelativeFamilyPath = "Openings\\Round_Project.rfa",
            TypeNames = new System.Collections.Generic.List<string> { "AXIS_Z__L1792__D896" },
            OverwriteExisting = true
        };

        var schedule = new ScheduleCreateRequest
        {
            DocumentKey = "path:c:\\test.rvt",
            ScheduleName = "Round Review",
            CategoryName = "Generic Models",
            Fields = new System.Collections.Generic.List<ScheduleFieldSpec>
            {
                new ScheduleFieldSpec { ParameterName = "Comments", ColumnHeading = "Comments" }
            }
        };

        var ifc = new IfcExportRequest { DocumentKey = "path:c:\\test.rvt", PresetName = "coordination_ifc", OutputRootName = "documents_exports", RelativeOutputPath = "ifc", FileName = "test.ifc" };
        var dwg = new DwgExportRequest { DocumentKey = "path:c:\\test.rvt", PresetName = "default_dwg", OutputRootName = "documents_exports", RelativeOutputPath = "dwg", ViewIds = new System.Collections.Generic.List<int> { 1 } };
        var pdf = new PdfPrintRequest { DocumentKey = "path:c:\\test.rvt", PresetName = "sheet_issue_pdf", OutputRootName = "documents_exports", RelativeOutputPath = "pdf", SheetIds = new System.Collections.Generic.List<int> { 2 } };

        Assert.Equal("repo_testing_families", JsonUtil.DeserializeRequired<FamilyLoadRequest>(JsonUtil.Serialize(familyLoad)).LibraryRootName);
        Assert.Equal("Round Review", JsonUtil.DeserializeRequired<ScheduleCreateRequest>(JsonUtil.Serialize(schedule)).ScheduleName);
        Assert.Equal("coordination_ifc", JsonUtil.DeserializeRequired<IfcExportRequest>(JsonUtil.Serialize(ifc)).PresetName);
        Assert.Equal("default_dwg", JsonUtil.DeserializeRequired<DwgExportRequest>(JsonUtil.Serialize(dwg)).PresetName);
        Assert.Equal("sheet_issue_pdf", JsonUtil.DeserializeRequired<PdfPrintRequest>(JsonUtil.Serialize(pdf)).PresetName);
    }

    [Fact]
    public void FixLoopCandidateAction_RoundTrips_IntelligenceFields()
    {
        var dto = new FixLoopCandidateAction
        {
            ActionId = "action-1",
            ToolName = "view.set_template_safe",
            Title = "Apply template",
            DecisionReason = "Matched sheet prefix rule.",
            Priority = 40,
            IsRecommended = false,
            Verification = new FixLoopVerificationCriteria
            {
                ExpectedIssueDelta = 2,
                ExpectedRemainingMax = 1
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<FixLoopCandidateAction>(json);

        Assert.Equal("Matched sheet prefix rule.", result.DecisionReason);
        Assert.Equal(40, result.Priority);
        Assert.False(result.IsRecommended);
        Assert.Equal(2, result.Verification.ExpectedIssueDelta);
    }

    [Fact]
    public void FixLoopEvidenceBundle_RoundTrips_IntelligenceFields()
    {
        var dto = new FixLoopEvidenceBundle
        {
            RecommendedActionIds = new System.Collections.Generic.List<string> { "a1", "a2" },
            SelectedActionIds = new System.Collections.Generic.List<string> { "a1" },
            ExpectedIssueDelta = 2,
            ActualIssueDelta = 1,
            VerificationStatus = "partial"
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<FixLoopEvidenceBundle>(json);

        Assert.Equal(2, result.RecommendedActionIds.Count);
        Assert.Single(result.SelectedActionIds);
        Assert.Equal(2, result.ExpectedIssueDelta);
        Assert.Equal(1, result.ActualIssueDelta);
        Assert.Equal("partial", result.VerificationStatus);
    }

    [Fact]
    public void RoundPenetrationReviewPacketRequest_RoundTrips()
    {
        var dto = new RoundPenetrationReviewPacketRequest
        {
            TargetFamilyName = "Mii_Pen-Round_Project",
            SourceElementIds = new System.Collections.Generic.List<int> { 1001, 1002 },
            PenetrationElementIds = new System.Collections.Generic.List<int> { 2001 },
            MaxItems = 4,
            ViewNamePrefix = "BIM765T_RoundPen_Review",
            SheetNumber = "BIM765T-RP-01",
            SheetName = "Round Penetration Review",
            SectionBoxPaddingFeet = 0.75,
            ExportSheetImage = true
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<RoundPenetrationReviewPacketRequest>(json);

        Assert.Equal("Mii_Pen-Round_Project", result.TargetFamilyName);
        Assert.Equal(2, result.SourceElementIds.Count);
        Assert.Single(result.PenetrationElementIds);
        Assert.Equal(4, result.MaxItems);
        Assert.Equal("BIM765T-RP-01", result.SheetNumber);
        Assert.True(result.ExportSheetImage);
    }
}
