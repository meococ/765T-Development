using System;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class RequestRateLimiterTests
{
    [Fact]
    public void Evaluate_Denies_When_Standard_Limit_Reached()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc));
        var settings = new AgentSettings
        {
            MaxRequestsPerMinute = 2,
            MaxHighRiskRequestsPerMinute = 1,
            RequestRateLimitWindowSeconds = 60
        };
        var limiter = new RequestRateLimiter(settings, clock);
        var manifest = new ToolManifest { ApprovalRequirement = ApprovalRequirement.None };

        Assert.True(limiter.Evaluate("caller-a", manifest).Allowed);
        Assert.True(limiter.Evaluate("caller-a", manifest).Allowed);

        var denied = limiter.Evaluate("caller-a", manifest);

        Assert.False(denied.Allowed);
        Assert.Equal(2, denied.Limit);
        Assert.Equal(60, denied.WindowSeconds);
        Assert.True(denied.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Evaluate_Uses_Separate_Buckets_For_High_Risk()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc));
        var settings = new AgentSettings
        {
            MaxRequestsPerMinute = 5,
            MaxHighRiskRequestsPerMinute = 1,
            RequestRateLimitWindowSeconds = 60
        };
        var limiter = new RequestRateLimiter(settings, clock);
        var standard = new ToolManifest { ApprovalRequirement = ApprovalRequirement.None };
        var highRisk = new ToolManifest { ApprovalRequirement = ApprovalRequirement.HighRiskToken };

        Assert.True(limiter.Evaluate("caller-a", standard).Allowed);
        Assert.True(limiter.Evaluate("caller-a", highRisk).Allowed);

        var deniedHighRisk = limiter.Evaluate("caller-a", highRisk);
        var allowedStandard = limiter.Evaluate("caller-a", standard);

        Assert.False(deniedHighRisk.Allowed);
        Assert.True(allowedStandard.Allowed);
    }

    [Fact]
    public void Evaluate_Allows_Again_After_Window_Expires()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc));
        var settings = new AgentSettings
        {
            MaxRequestsPerMinute = 1,
            RequestRateLimitWindowSeconds = 60
        };
        var limiter = new RequestRateLimiter(settings, clock);
        var manifest = new ToolManifest();

        Assert.True(limiter.Evaluate("caller-a", manifest).Allowed);
        Assert.False(limiter.Evaluate("caller-a", manifest).Allowed);

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(limiter.Evaluate("caller-a", manifest).Allowed);
    }
}
