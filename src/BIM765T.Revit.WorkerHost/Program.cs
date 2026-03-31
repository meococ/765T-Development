using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Capabilities;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.ExternalAi;
using BIM765T.Revit.WorkerHost.Grpc;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Migration;
using BIM765T.Revit.WorkerHost.Projects;
using BIM765T.Revit.WorkerHost.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder, args);

        var runHealthJson = HasArg(args, "--health-json");
        var runMigration = HasArg(args, "--migrate-legacy-state");
        var forceMigrate = HasArg(args, "--force-migrate");
        var dryRun = HasArg(args, "--dry-run");
        var probePublicPipe = HasArg(args, "--probe-public-pipe");

        if (runHealthJson || runMigration)
        {
            await using var app = builder.Build();
            using var scope = app.Services.CreateScope();
            var cancellationToken = CancellationToken.None;

            LegacyMigrationReport? migrationReport = null;
            RuntimeHealthReport? healthReport = null;

            if (runMigration)
            {
                var migrator = scope.ServiceProvider.GetRequiredService<LegacyStateMigrator>();
                migrationReport = await migrator.MigrateAsync(forceMigrate, dryRun, cancellationToken).ConfigureAwait(false);
            }

            if (runHealthJson)
            {
                var runtimeHealth = scope.ServiceProvider.GetRequiredService<RuntimeHealthService>();
                healthReport = await runtimeHealth.CollectAsync(probePublicPipe, cancellationToken).ConfigureAwait(false);
            }

            var payload = runMigration && runHealthJson
                ? JsonSerializer.Serialize(new { migration = migrationReport, health = healthReport })
                : runMigration
                    ? JsonSerializer.Serialize(migrationReport)
                    : JsonSerializer.Serialize(healthReport);

            Console.WriteLine(payload);
            return DetermineExitCode(migrationReport, healthReport);
        }

        var appHost = builder.Build();
        appHost.UseCors("LocalWeb");
        appHost.UseRateLimiter();
        appHost.MapGrpcService<CompatibilityGrpcService>();
        appHost.MapGrpcService<CatalogGrpcService>();
        appHost.MapGrpcService<ContextGrpcService>();
        appHost.MapGrpcService<MissionGrpcService>();
        appHost.MapGrpcService<MissionStreamGrpcService>();
        appHost.MapProjectEndpoints();
        appHost.MapCapabilityEndpoints();
        appHost.MapExternalAiEndpoints();
        appHost.MapGet("/", () => "BIM765T.Revit.WorkerHost is running.");
        await appHost.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static void ConfigureServices(WebApplicationBuilder builder, string[] args)
    {
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        var settings = new WorkerHostSettings();
        builder.Configuration.GetSection("WorkerHost").Bind(settings);
        settings.EnsureCreated();

        if (OperatingSystem.IsWindows())
        {
            builder.Host.UseWindowsService(options =>
            {
                options.ServiceName = settings.WindowsServiceName;
            });
        }

        builder.Services.AddSingleton(settings);
        builder.Services.AddGrpc();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("LocalWeb", policy => policy
                .SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
                })
                .AllowAnyHeader()
                .AllowAnyMethod());
        });

        // Rate limiting for HTTP endpoints - prevent flooding
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global per-IP limiter
            options.AddPolicy("PerIpLimit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.RateLimitPerIpPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = settings.RateLimitPerIpQueueLimit
                    }));

            // Chat endpoint limiter (expensive operation)
            options.AddPolicy("ChatLimit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.RateLimitChatPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = settings.RateLimitChatQueueLimit
                    }));
        });

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(new SqliteMissionEventStore(settings.EventStorePath));
        builder.Services.AddSingleton<IMissionEventBus, InMemoryMissionEventBus>();
        var timeoutProfile = settings.BuildLlmTimeoutProfile();
        builder.Services.AddSingleton(timeoutProfile);
        builder.Services.AddSingleton<IKernelClient>(_ => new KernelPipeClient(
            settings.KernelPipeName,
            initialConnectTimeoutMs: settings.KernelInitialConnectTimeoutMs,
            maxConnectTimeoutMs: settings.KernelMaxConnectTimeoutMs,
            maxRetries: settings.KernelMaxRetries));
        builder.Services.AddSingleton<ICopilotLogger>(sp =>
            new MsLoggerCopilotAdapter(sp.GetRequiredService<ILoggerFactory>().CreateLogger("BIM765T.Copilot")));
        builder.Services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EmbeddingProviderFactory>>();
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var copilotLogger = sp.GetRequiredService<ICopilotLogger>();
            return EmbeddingProviderFactory.Create(settings, httpFactory, logger, copilotLogger, timeoutProfile);
        });
        builder.Services.AddSingleton<IEmbeddingClient>(sp =>
            sp.GetRequiredService<EmbeddingProviderResult>().Client);
        builder.Services.AddSingleton<IEmbeddingProvider>(sp =>
            sp.GetRequiredService<EmbeddingProviderResult>().Provider);
        builder.Services.AddHttpClient<ISemanticMemoryClient, QdrantSemanticMemoryClient>(client =>
        {
            client.BaseAddress = new Uri(settings.QdrantUrl.EndsWith('/') ? settings.QdrantUrl : settings.QdrantUrl + "/");
        });
        builder.Services.AddSingleton<MemorySearchService>();
        builder.Services.AddSingleton<IMemorySearchService>(sp => sp.GetRequiredService<MemorySearchService>());
        builder.Services.AddSingleton<ISecretProvider, EnvSecretProvider>();
        builder.Services.AddSingleton<ILlmProviderConfigResolver, OpenRouterFirstLlmProviderConfigResolver>();
        builder.Services.AddSingleton<ILlmPlanner>(sp => CreatePlanner(
            sp.GetRequiredService<ILlmProviderConfigResolver>().Resolve(),
            sp.GetRequiredService<ICopilotLogger>(),
            sp.GetRequiredService<LlmTimeoutProfile>()));
        builder.Services.AddSingleton<IToolCandidateBuilder>(sp =>
            new MissionToolCandidateBuilder(new IntentClassifier(), new PersonaRegistry(), sp.GetRequiredService<WorkerHostSettings>()));
        builder.Services.AddSingleton<IBoundedMissionPlanner>(sp =>
            new BoundedMissionPlanner(sp.GetRequiredService<ILlmPlanner>()));
        builder.Services.AddSingleton<IExecutionPolicyEvaluator, ExecutionPolicyEvaluator>();
        builder.Services.AddSingleton<IReadOnlyResearchOrchestrator, ReadOnlyResearchOrchestrator>();
        builder.Services.AddSingleton(sp => new PlannerAgent(
            sp.GetRequiredService<IToolCandidateBuilder>(),
            sp.GetRequiredService<IBoundedMissionPlanner>(),
            sp.GetRequiredService<IReadOnlyResearchOrchestrator>()));
        builder.Services.AddSingleton<RetrieverAgent>();
        builder.Services.AddSingleton<SafetyAgent>();
        builder.Services.AddSingleton<VerifierAgent>();
        builder.Services.AddSingleton<MissionOrchestrator>();
        builder.Services.AddSingleton<RuntimeHealthService>();
        builder.Services.AddSingleton<LegacyStateMigrator>();
        builder.Services.AddSingleton(_ => new PackCatalogService(AppContext.BaseDirectory));
        builder.Services.AddSingleton(_ => new WorkspaceCatalogService(AppContext.BaseDirectory));
        builder.Services.AddSingleton<ToolCapabilitySearchService>();
        builder.Services.AddSingleton<StandardsCatalogService>();
        builder.Services.AddSingleton(_ => new PlaybookLoaderService(PlaybookLoaderService.LoadAll(AppContext.BaseDirectory)));
        builder.Services.AddSingleton<PlaybookOrchestrationService>();
        builder.Services.AddSingleton<PolicyResolutionService>();
        builder.Services.AddSingleton<SpecialistRegistryService>();
        builder.Services.AddSingleton<CapabilityTaskCompilerService>();
        builder.Services.AddSingleton<CuratedScriptRegistryService>();
        builder.Services.AddSingleton<CommandAtlasService>();
        builder.Services.AddSingleton(sp => new ProjectInitService(sp.GetRequiredService<PackCatalogService>(), sp.GetRequiredService<WorkspaceCatalogService>(), AppContext.BaseDirectory));
        builder.Services.AddSingleton(sp => new ProjectDeepScanService(sp.GetRequiredService<ProjectInitService>(), AppContext.BaseDirectory));
        builder.Services.AddSingleton(sp => new ProjectContextComposer(sp.GetRequiredService<ProjectInitService>(), sp.GetRequiredService<WorkspaceCatalogService>(), sp.GetRequiredService<StandardsCatalogService>(), sp.GetRequiredService<PlaybookOrchestrationService>(), AppContext.BaseDirectory));
        builder.Services.AddSingleton<ProjectInitHostService>();
        builder.Services.AddSingleton<ProjectDeepScanHostService>();
        builder.Services.AddSingleton<CapabilityHostService>();
        builder.Services.AddSingleton<StandaloneConversationService>();
        builder.Services.AddSingleton<ExternalAiGatewayService>();
        builder.Services.AddHostedService<MarkdownMemoryBootstrapper>();
        builder.Services.AddHostedService<MemoryOutboxProjectorService>();
        builder.Services.AddHostedService<SqliteWalCheckpointService>();

        if (OperatingSystem.IsWindows())
        {
            ConfigureWindowsNamedPipes(builder, settings);
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(settings.HttpApiPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            });
            options.ListenNamedPipe(settings.PublicPipeName, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
    }

    private static bool HasArg(string[] args, string key)
    {
        return Array.Exists(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
    }

    private static int DetermineExitCode(LegacyMigrationReport? migrationReport, RuntimeHealthReport? healthReport)
    {
        if (migrationReport != null && !migrationReport.Succeeded)
        {
            return 1;
        }

        if (healthReport != null && !healthReport.Ready)
        {
            return 1;
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureWindowsNamedPipes(WebApplicationBuilder builder, WorkerHostSettings settings)
    {
        var normalizedAclMode = NormalizePublicPipeAclMode(settings.PublicPipeAclMode);

        builder.WebHost.UseNamedPipes(options =>
        {
            if (string.Equals(normalizedAclMode, "current_user", StringComparison.Ordinal))
            {
                options.CurrentUserOnly = true;
                options.PipeSecurity = null;
                return;
            }

            options.CurrentUserOnly = false;
            options.PipeSecurity = BuildPublicPipeSecurity(normalizedAclMode);
        });
    }

    [SupportedOSPlatform("windows")]
    private static PipeSecurity BuildPublicPipeSecurity(string aclMode)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl);
        AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl);

        var currentIdentity = WindowsIdentity.GetCurrent();
        if (currentIdentity?.User != null)
        {
            AddAccessRule(security, currentIdentity.User, PipeAccessRights.FullControl);
        }

        var normalizedMode = NormalizePublicPipeAclMode(aclMode);

        switch (normalizedMode)
        {
            case "builtin_users":
            case "users":
                AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite);
                break;

            case "current_user":
                if (currentIdentity?.User == null)
                {
                    AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite);
                }
                break;

            case "authenticated_users":
            default:
                AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite);
                break;
        }

        return security;
    }

    private static string NormalizePublicPipeAclMode(string? aclMode)
    {
        return string.IsNullOrWhiteSpace(aclMode)
            ? "authenticated_users"
            : aclMode.Trim().ToLowerInvariant();
    }

    [SupportedOSPlatform("windows")]
    private static void AddAccessRule(PipeSecurity security, IdentityReference identity, PipeAccessRights rights)
    {
        security.AddAccessRule(new PipeAccessRule(identity, rights, AccessControlType.Allow));
    }

    private static ILlmPlanner CreatePlanner(LlmProviderConfiguration profile, ICopilotLogger? logger = null, LlmTimeoutProfile? timeoutProfile = null)
    {
        if (profile == null
            || !profile.IsConfigured
            || !string.Equals(profile.ProviderKind, "openai_compatible", StringComparison.OrdinalIgnoreCase))
        {
            return new NullLlmPlanner();
        }

        var tp = timeoutProfile ?? LlmTimeoutProfile.Default;
        var primary = new OpenAiCompatibleLlmClient(
            new HttpClient(),
            profile.ApiKey,
            model: profile.PlannerPrimaryModel,
            maxTokens: tp.PlannerMaxTokens,
            apiUrl: profile.ApiUrl,
            providerLabel: profile.ConfiguredProvider,
            organization: profile.Organization,
            project: profile.Project,
            httpReferer: profile.HttpReferer,
            xTitle: profile.XTitle,
            logger: logger,
            timeoutProfile: tp);
        OpenAiCompatibleLlmClient? fallback = null;
        if (!string.IsNullOrWhiteSpace(profile.PlannerFallbackModel)
            && !string.Equals(profile.PlannerFallbackModel, profile.PlannerPrimaryModel, StringComparison.OrdinalIgnoreCase))
        {
            fallback = new OpenAiCompatibleLlmClient(
                new HttpClient(),
                profile.ApiKey,
                model: profile.PlannerFallbackModel,
                maxTokens: tp.PlannerMaxTokens,
                apiUrl: profile.ApiUrl,
                providerLabel: profile.ConfiguredProvider,
                organization: profile.Organization,
                project: profile.Project,
                httpReferer: profile.HttpReferer,
                xTitle: profile.XTitle,
                logger: logger,
                timeoutProfile: tp);
        }

        return new LlmPlanningService(profile, primary, fallback, tp);
    }
}
