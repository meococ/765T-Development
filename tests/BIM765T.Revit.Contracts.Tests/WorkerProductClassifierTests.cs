using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class WorkerProductClassifierTests
{
    [Fact]
    public void Classify_FamilyTool_Goes_To_AutomationLab_Internal_Surface()
    {
        var result = WorkerProductClassifier.Classify(
            ToolNames.FamilyAddParameterSafe,
            PermissionLevel.Mutate);

        Assert.Equal(WorkerCapabilityPacks.AutomationLab, result.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Automation, result.SkillGroup);
        Assert.Equal(WorkerAudience.Internal, result.Audience);
        Assert.Equal(WorkerVisibility.BetaInternal, result.Visibility);
    }

    [Fact]
    public void Classify_TaskTool_Goes_To_MemoryAndSoul_Orchestration()
    {
        var result = WorkerProductClassifier.Classify(
            ToolNames.TaskPlan,
            PermissionLevel.Review);

        Assert.Equal(WorkerCapabilityPacks.MemoryAndSoul, result.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Orchestration, result.SkillGroup);
        Assert.Equal(WorkerAudience.Commercial, result.Audience);
        Assert.Equal(WorkerVisibility.Visible, result.Visibility);
    }

    [Fact]
    public void Classify_Review_And_Documentation_Tools_Use_Expected_Skill_Groups()
    {
        var review = WorkerProductClassifier.Classify(ToolNames.ReviewModelHealth, PermissionLevel.Read);
        var documentation = WorkerProductClassifier.Classify(ToolNames.SheetCreateSafe, PermissionLevel.Mutate);

        Assert.Equal(WorkerSkillGroups.QualityControl, review.SkillGroup);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, review.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Documentation, documentation.SkillGroup);
        Assert.Equal(WorkerCapabilityPacks.CoreWorker, documentation.CapabilityPack);
    }

    [Fact]
    public void Classify_Respects_Explicit_Overrides()
    {
        var result = WorkerProductClassifier.Classify(
            ToolNames.ExportIfcSafe,
            PermissionLevel.FileLifecycle,
            WorkerCapabilityPacks.Connector,
            WorkerSkillGroups.Orchestration,
            WorkerAudience.Connector,
            WorkerVisibility.Hidden);

        Assert.Equal(WorkerCapabilityPacks.Connector, result.CapabilityPack);
        Assert.Equal(WorkerSkillGroups.Orchestration, result.SkillGroup);
        Assert.Equal(WorkerAudience.Connector, result.Audience);
        Assert.Equal(WorkerVisibility.Hidden, result.Visibility);
    }

    [Fact]
    public void Classify_New_CapabilityNamespaces_Map_To_New_SkillGroups()
    {
        var annotation = WorkerProductClassifier.Classify(ToolNames.AnnotationAddTextNoteSafe, PermissionLevel.Mutate);
        var systems = WorkerProductClassifier.Classify(ToolNames.SystemCaptureGraph, PermissionLevel.Read);
        var intent = WorkerProductClassifier.Classify(ToolNames.IntentCompile, PermissionLevel.Read);
        var integration = WorkerProductClassifier.Classify(ToolNames.IntegrationPreviewSync, PermissionLevel.Read);

        Assert.Equal(WorkerSkillGroups.Annotation, annotation.SkillGroup);
        Assert.Equal(WorkerSkillGroups.Systems, systems.SkillGroup);
        Assert.Equal(WorkerSkillGroups.Intent, intent.SkillGroup);
        Assert.Equal(WorkerSkillGroups.Integration, integration.SkillGroup);
        Assert.Equal(WorkerCapabilityPacks.Connector, integration.CapabilityPack);
        Assert.Equal(WorkerCapabilityPacks.MemoryAndSoul, intent.CapabilityPack);
    }
}
