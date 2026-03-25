using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolExecutionTimeoutPolicyTests
{
    [Fact]
    public void ToolExecutionTimeoutPolicy_DefaultTools_Use_BaseTimeout()
    {
        var settings = new AgentSettings
        {
            RequestTimeoutSeconds = 60
        };

        var timeoutMs = ToolExecutionTimeoutPolicy.GetRecommendedTimeoutMs(settings, ToolNames.ElementQuery);

        Assert.Equal(60_000, timeoutMs);
    }

    [Fact]
    public void ToolExecutionTimeoutPolicy_LongRunningTools_Get_ExtendedTimeout()
    {
        var settings = new AgentSettings
        {
            RequestTimeoutSeconds = 60
        };

        var timeoutMs = ToolExecutionTimeoutPolicy.GetRecommendedTimeoutMs(settings, ToolNames.ExportIfcSafe);

        Assert.Equal(300_000, timeoutMs);
    }

    [Fact]
    public void ToolExecutionTimeoutPolicy_ManifestOverride_Wins()
    {
        var settings = new AgentSettings
        {
            RequestTimeoutSeconds = 60
        };
        var manifest = new ToolManifest
        {
            ToolName = ToolNames.ExportIfcSafe,
            ExecutionTimeoutMs = 45_000
        };

        var timeoutMs = ToolExecutionTimeoutPolicy.ResolveExecutionTimeoutMs(settings, manifest);

        Assert.Equal(45_000, timeoutMs);
    }
}
