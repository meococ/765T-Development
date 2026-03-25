using System;
using BIM765T.Revit.Contracts.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Threading;

namespace BIM765T.Revit.WorkerHost.Capabilities;

internal static class CapabilityHttpEndpoints
{
    public static void MapCapabilityEndpoints(this WebApplication app)
    {
        app.MapPost("/api/capabilities/policy/resolve", (PolicyResolutionRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.ResolvePolicy(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/capabilities/specialists/resolve", (CapabilitySpecialistRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.ResolveSpecialists(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/capabilities/intent/compile", (IntentCompileRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.Compile(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/capabilities/intent/validate", (IntentValidateRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.Validate(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/commands/search", (CommandAtlasSearchRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.SearchCommands(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/commands/describe", (CommandDescribeRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.DescribeCommand(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/commands/coverage", (CoverageReportRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.GetCoverageReport(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/commands/quick-plan", (QuickActionRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.QuickPlan(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/commands/execute", async (CommandExecuteRequest request, CapabilityHostService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ExecuteCommandAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/scripts/verify-source", (ScriptSourceVerifyRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.VerifyScriptSource(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/scripts/import-manifest", (ScriptImportManifestRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.ImportScriptManifest(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/scripts/install-pack", (ScriptInstallPackRequest request, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.InstallScriptPack(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapGet("/api/scripts/catalog", (string? workspaceId, CapabilityHostService service) =>
        {
            try
            {
                return Results.Ok(service.GetScriptCatalog(workspaceId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/memory/search-scoped", async (MemoryScopedSearchRequest request, CapabilityHostService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.SearchScopedMemoryAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/evidence/bundles/lookup", async (MemoryScopedSearchRequest request, CapabilityHostService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.LookupEvidenceBundlesAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });
    }
}
