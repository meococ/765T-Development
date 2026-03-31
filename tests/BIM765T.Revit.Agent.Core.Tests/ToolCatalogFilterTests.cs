using System.Collections.Generic;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolCatalogFilterTests
{
    [Fact]
    public void FilterForMcp_Hides_Internal_And_Hidden_Tools()
    {
        var manifests = new List<ToolManifest>
        {
            BuildManifest("commercial.visible", WorkerAudience.Commercial, WorkerVisibility.Visible, ToolPrimaryPersonas.ProductionBimer),
            BuildManifest("connector.visible", WorkerAudience.Connector, WorkerVisibility.Visible, ToolPrimaryPersonas.MepSpecialist),
            BuildManifest("internal.beta", WorkerAudience.Internal, WorkerVisibility.BetaInternal, ToolPrimaryPersonas.PlatformAuthor),
            BuildManifest("commercial.hidden", WorkerAudience.Commercial, WorkerVisibility.Hidden, ToolPrimaryPersonas.ProductionBimer),
            BuildManifest("internal.visible", WorkerAudience.Internal, WorkerVisibility.Visible, ToolPrimaryPersonas.PlatformAuthor)
        };

        var filtered = ToolCatalogFilter.FilterForMcp(manifests);

        Assert.Collection(filtered,
            item => Assert.Equal("commercial.visible", item.ToolName),
            item => Assert.Equal("connector.visible", item.ToolName));
    }

    [Fact]
    public void FilterForPublicCatalog_Only_Exposes_Commercial_Visible_Tools()
    {
        var manifests = new List<ToolManifest>
        {
            BuildManifest("commercial.visible", WorkerAudience.Commercial, WorkerVisibility.Visible, ToolPrimaryPersonas.ProductionBimer),
            BuildManifest("connector.visible", WorkerAudience.Connector, WorkerVisibility.Visible, ToolPrimaryPersonas.MepSpecialist),
            BuildManifest("internal.beta", WorkerAudience.Internal, WorkerVisibility.BetaInternal, ToolPrimaryPersonas.PlatformAuthor),
            BuildManifest("internal.visible", WorkerAudience.Internal, WorkerVisibility.Visible, ToolPrimaryPersonas.PlatformAuthor)
        };

        var filtered = ToolCatalogFilter.FilterForPublicCatalog(manifests);

        var manifest = Assert.Single(filtered);
        Assert.Equal("commercial.visible", manifest.ToolName);
    }

    [Fact]
    public void FilterForSessionList_Keeps_Internal_But_Still_Hides_Hidden_Tools()
    {
        var manifests = new List<ToolManifest>
        {
            BuildManifest("commercial.visible", WorkerAudience.Commercial, WorkerVisibility.Visible, ToolPrimaryPersonas.ProductionBimer),
            BuildManifest("internal.beta", WorkerAudience.Internal, WorkerVisibility.BetaInternal, ToolPrimaryPersonas.PlatformAuthor),
            BuildManifest("internal.hidden", WorkerAudience.Internal, WorkerVisibility.Hidden, ToolPrimaryPersonas.PlatformAuthor)
        };

        var filtered = ToolCatalogFilter.FilterForSessionList(manifests);

        Assert.Collection(filtered,
            item => Assert.Equal("commercial.visible", item.ToolName),
            item => Assert.Equal("internal.beta", item.ToolName));
    }

    private static ToolManifest BuildManifest(string toolName, string audience, string visibility, string primaryPersona)
    {
        return new ToolManifest
        {
            ToolName = toolName,
            Description = toolName,
            Audience = audience,
            Visibility = visibility,
            PrimaryPersona = primaryPersona,
            PermissionLevel = PermissionLevel.Read,
            ApprovalRequirement = ApprovalRequirement.None,
            Enabled = true,
            SupportsDryRun = false
        };
    }
}
