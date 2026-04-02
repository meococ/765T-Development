using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Capabilities;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class CapabilityHostServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BIM765T.CapabilityHostTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup failures from transient file handles.
        }
    }

    [Fact]
    public void GetScriptCatalog_Is_Empty_When_No_Mvp_Script_Entries_Are_Available()
    {
        var service = CreateHostService();

        var catalog = service.GetScriptCatalog("default");

        Assert.Empty(catalog.Scripts);
        Assert.Equal(_root, Path.GetDirectoryName(catalog.CatalogPath));
    }

    [Theory]
    [InlineData("revit.sheet.create")]
    [InlineData("revit.sheet.renumber")]
    [InlineData("revit.view.duplicate")]
    public async Task ExecuteCommandAsync_Keeps_Mutation_DryRun_For_Previewable_Mvp_Commands(string commandId)
    {
        var kernel = new CapturingKernelClient();
        var service = CreateHostService(kernel, enableDirectCommandExecute: true);

        var response = await service.ExecuteCommandAsync(new CommandExecuteRequest
        {
            WorkspaceId = "default",
            CommandId = commandId,
            Query = commandId,
            PayloadJson = BuildExplicitPayload(commandId),
            AllowAutoExecute = true
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Ok, response.StatusCode);
        var request = Assert.Single(kernel.Requests);
        Assert.True(request.DryRun);
    }

    [Fact]
    public async Task ExecuteCommandAsync_IsBlocked_WhenDirectHttpExecute_IsDisabled()
    {
        var kernel = new CapturingKernelClient();
        var service = CreateHostService(kernel, enableDirectCommandExecute: false);

        var response = await service.ExecuteCommandAsync(new CommandExecuteRequest
        {
            WorkspaceId = "default",
            CommandId = "revit.sheet.create",
            Query = "revit.sheet.create",
            PayloadJson = BuildExplicitPayload("revit.sheet.create"),
            AllowAutoExecute = true
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.CommandExecutionBlocked, response.StatusCode);
        Assert.Empty(kernel.Requests);
    }

    [Fact]
    public async Task LookupEvidenceBundlesAsync_Returns_Evidence_Namespace_Hits()
    {
        Directory.CreateDirectory(_root);
        var store = new SqliteMissionEventStore(Path.Combine(_root, "workerhost.sqlite"));
        var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
        await memory.UpsertAsync(new PromotedMemoryRecord
        {
            MemoryId = "evidence:001",
            Kind = "evidence_bundle",
            Title = "Coordination evidence bundle",
            Snippet = "Opening packet for level 3",
            SourceRef = "artifacts/openings/packet-001.json",
            DocumentKey = "doc-01",
            EventType = "MemoryPromoted",
            RunId = "run-01",
            Promoted = true,
            PayloadJson = "{\"kind\":\"evidence_bundle\"}",
            CreatedUtc = DateTime.UtcNow.ToString("O")
        }, CancellationToken.None);

        var service = CreateHostService(memorySearch: memory);
        var response = await service.LookupEvidenceBundlesAsync(new MemoryScopedSearchRequest
        {
            Query = "opening packet",
            DocumentKey = "doc-01",
            MaxResults = 5
        }, CancellationToken.None);

        var hit = Assert.Single(response.Hits);
        Assert.Equal(MemoryNamespaces.EvidenceLessons, hit.Namespace);
        Assert.Equal("artifacts/openings/packet-001.json", hit.SourceRef);
    }

    private CapabilityHostService CreateHostService(IKernelClient? kernel = null, MemorySearchService? memorySearch = null, bool enableDirectCommandExecute = false)
    {
        Directory.CreateDirectory(_root);
        var settings = new WorkerHostSettings
        {
            StateRootPath = Path.Combine(_root, "state"),
            LegacyStateRootPath = Path.Combine(_root, "legacy"),
            EnableDirectCommandExecuteHttp = enableDirectCommandExecute
        };
        settings.EnsureCreated();
        var packs = new PackCatalogService();
        var workspaces = new WorkspaceCatalogService();
        var standards = new StandardsCatalogService(packs, workspaces);
        var playbooks = new PlaybookOrchestrationService(
            new PlaybookLoaderService(PlaybookLoaderService.LoadAll(AppContext.BaseDirectory)),
            packs,
            workspaces,
            standards);
        var policies = new PolicyResolutionService(packs, workspaces);
        var specialists = new SpecialistRegistryService(packs, workspaces);
        var compiler = new CapabilityTaskCompilerService(
            new ToolCapabilitySearchService(),
            playbooks,
            policies,
            specialists);
        var curated = new CuratedScriptRegistryService(_root);
        var memory = memorySearch ?? new MemorySearchService(new SqliteMissionEventStore(Path.Combine(_root, "workerhost.sqlite")), new ThrowingSemanticMemoryClient());

        return new CapabilityHostService(
            policies,
            specialists,
            compiler,
            new CommandAtlasService(packs, workspaces, curated),
            curated,
            memory,
            kernel ?? new StubKernelClient(),
            settings);
    }

    private static string BuildExplicitPayload(string commandId)
    {
        return commandId switch
        {
            "revit.sheet.create" => JsonUtil.Serialize(new CreateSheetRequest
            {
                SheetNumber = "A101",
                SheetName = "General Notes"
            }),
            "revit.sheet.renumber" => JsonUtil.Serialize(new RenumberSheetRequest
            {
                SheetId = 101,
                OldSheetNumber = "A101",
                NewSheetNumber = "A201"
            }),
            "revit.view.duplicate" => JsonUtil.Serialize(new DuplicateViewRequest
            {
                ViewId = 42,
                DuplicateMode = "AsDependent",
                NewName = "Dependent Copy",
                ActivateAfterCreate = true
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "Unsupported MVP command id.")
        };
    }

    private sealed class ThrowingSemanticMemoryClient : ISemanticMemoryClient
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory intentionally unavailable for lexical fallback test.");
        }

        public Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory intentionally unavailable for lexical fallback test.");
        }
    }

    private sealed class StubKernelClient : IKernelClient
    {
        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new KernelInvocationResult
            {
                Succeeded = true,
                StatusCode = StatusCodes.Ok,
                PayloadJson = "{}"
            });
        }
    }

    private sealed class CapturingKernelClient : IKernelClient
    {
        public List<KernelToolRequest> Requests { get; } = new();

        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new KernelInvocationResult
            {
                Succeeded = true,
                StatusCode = StatusCodes.Ok,
                PayloadJson = "{}"
            });
        }
    }
}
