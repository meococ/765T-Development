using System;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class PipeRequestProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Returns_InvalidRequest_For_Invalid_Json()
    {
        var processor = CreateProcessor();

        var response = await processor.ProcessAsync("{", "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.InvalidRequest, response.StatusCode);
        Assert.Equal(BridgeProtocol.PipeV1, response.ProtocolVersion);
        Assert.Contains(response.Diagnostics, x => x.Code == "INVALID_JSON");
    }

    [Fact]
    public async Task ProcessAsync_Returns_ProtocolUnsupported_For_Unsupported_Version()
    {
        var processor = CreateProcessor();
        var request = CreateRequest();
        request.ProtocolVersion = "pipe/2";

        var response = await processor.ProcessAsync(JsonUtil.Serialize(request), "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.ProtocolUnsupported, response.StatusCode);
        Assert.Contains(response.Diagnostics, x => x.Code == "PROTOCOL_UNSUPPORTED");
    }

    [Fact]
    public async Task ProcessAsync_Returns_CallerNotAllowed_When_Authorizer_Rejects()
    {
        var processor = CreateProcessor(authorizer: new FakeAuthorizer(false, "blocked"));

        var response = await processor.ProcessAsync(JsonUtil.Serialize(CreateRequest()), "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.CallerNotAllowed, response.StatusCode);
        Assert.Contains(response.Diagnostics, x => x.Code == "PIPE_CALLER_REJECTED");
    }

    [Fact]
    public async Task ProcessAsync_Returns_CallerNotAllowed_When_Mcp_Caller_Invokes_InternalOnly_Tool()
    {
        var processor = CreateProcessor(
            scheduler: new FakeScheduler(new ToolResponseEnvelope
            {
                RequestId = "req-internal-block",
                ToolName = "internal.tool",
                CorrelationId = "corr-internal-block",
                ProtocolVersion = BridgeProtocol.PipeV1,
                Succeeded = true,
                StatusCode = StatusCodes.ReadSucceeded
            }),
            manifestResolver: _ => new ToolManifest
            {
                ToolName = "internal.tool",
                Audience = WorkerAudience.Internal,
                Visibility = WorkerVisibility.BetaInternal,
                PrimaryPersona = ToolPrimaryPersonas.PlatformAuthor,
                ExecutionTimeoutMs = 5000
            });

        var request = CreateRequest();
        request.ToolName = "internal.tool";
        request.Caller = "BIM765T.Revit.McpHost";

        var response = await processor.ProcessAsync(JsonUtil.Serialize(request), "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.CallerNotAllowed, response.StatusCode);
        Assert.Contains(response.Diagnostics, x => x.Code == "PIPE_CALLER_POLICY_BLOCKED");
    }

    [Fact]
    public async Task ProcessAsync_Returns_RateLimited_When_Caller_Exceeds_Limit()
    {
        var settings = new AgentSettings
        {
            MaxRequestsPerMinute = 1,
            MaxHighRiskRequestsPerMinute = 1,
            RequestRateLimitWindowSeconds = 60
        };
        var limiter = new RequestRateLimiter(settings, new FakeClock(new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc)));
        var processor = CreateProcessor(
            settings: settings,
            limiter: limiter,
            scheduler: new FakeScheduler(new ToolResponseEnvelope
            {
                RequestId = "req-rate-limit",
                ToolName = ToolNames.DocumentGetActive,
                CorrelationId = "corr-rate-limit",
                ProtocolVersion = BridgeProtocol.PipeV1,
                Succeeded = false,
                StatusCode = StatusCodes.InternalError
            }));

        var request = CreateRequest();
        var json = JsonUtil.Serialize(request);

        var first = await processor.ProcessAsync(json, "DOMAIN\\user", lookupFailed: false, CancellationToken.None);
        var second = await processor.ProcessAsync(json, "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.Equal(StatusCodes.InternalError, first.StatusCode);
        Assert.Equal(StatusCodes.RateLimited, second.StatusCode);
    }

    [Fact]
    public async Task ProcessAsync_Defaults_Correlation_And_Protocol_And_Passes_Request_To_Scheduler()
    {
        var captured = default(ToolRequestEnvelope);
        var scheduler = new FakeScheduler(request =>
        {
            captured = request;
            return new ToolResponseEnvelope
            {
                RequestId = request.RequestId,
                ToolName = request.ToolName,
                CorrelationId = request.CorrelationId,
                Succeeded = true,
                StatusCode = StatusCodes.ReadSucceeded
            };
        });
        var processor = CreateProcessor(scheduler: scheduler);
        var request = CreateRequest();
        request.CorrelationId = string.Empty;
        request.ProtocolVersion = string.Empty;

        var response = await processor.ProcessAsync(JsonUtil.Serialize(request), "DOMAIN\\user", lookupFailed: false, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(request.RequestId, captured!.CorrelationId);
        Assert.Equal(BridgeProtocol.PipeV1, captured.ProtocolVersion);
        Assert.True(response.Succeeded);
        Assert.Equal(BridgeProtocol.PipeV1, response.ProtocolVersion);
    }

    private static PipeRequestProcessor CreateProcessor(
        AgentSettings? settings = null,
        RequestRateLimiter? limiter = null,
        IPipeCallerAuthorizer? authorizer = null,
        IPipeRequestScheduler? scheduler = null,
        Func<string, ToolManifest?>? manifestResolver = null)
    {
        settings ??= new AgentSettings
        {
            MaxRequestsPerMinute = 10,
            MaxHighRiskRequestsPerMinute = 5,
            RequestRateLimitWindowSeconds = 60,
            RequestTimeoutSeconds = 30
        };
        limiter ??= new RequestRateLimiter(settings, new FakeClock(new DateTime(2026, 3, 19, 10, 0, 0, DateTimeKind.Utc)));
        authorizer ??= new FakeAuthorizer(true, string.Empty);
        scheduler ??= new FakeScheduler(request => new ToolResponseEnvelope
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            CorrelationId = request.CorrelationId,
            ProtocolVersion = request.ProtocolVersion,
            Succeeded = true,
            StatusCode = StatusCodes.ReadSucceeded
        });

        return new PipeRequestProcessor(
            settings,
            manifestResolver ?? (_ => new ToolManifest { ToolName = ToolNames.DocumentGetActive, ExecutionTimeoutMs = 5000 }),
            limiter,
            new TestLogger(),
            authorizer,
            scheduler);
    }

    private static ToolRequestEnvelope CreateRequest()
    {
        return new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = ToolNames.DocumentGetActive,
            PayloadJson = "{}",
            Caller = "test-caller",
            SessionId = "session-a",
            DryRun = true,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = BridgeProtocol.PipeV1
        };
    }

    private sealed class FakeAuthorizer : IPipeCallerAuthorizer
    {
        private readonly bool _allowed;
        private readonly string _reason;

        internal FakeAuthorizer(bool allowed, string reason)
        {
            _allowed = allowed;
            _reason = reason;
        }

        public bool TryAuthorize(string clientIdentity, bool lookupFailed, out string rejectReason)
        {
            rejectReason = _reason;
            return _allowed;
        }
    }

    private sealed class FakeScheduler : IPipeRequestScheduler
    {
        private readonly Func<ToolRequestEnvelope, ToolResponseEnvelope> _factory;

        internal FakeScheduler(Func<ToolRequestEnvelope, ToolResponseEnvelope> factory)
        {
            _factory = factory;
        }

        internal FakeScheduler(ToolResponseEnvelope response)
        {
            _factory = _ => response;
        }

        public Task<ToolResponseEnvelope> ScheduleAsync(ToolRequestEnvelope request, int timeoutMs, CancellationToken token)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
