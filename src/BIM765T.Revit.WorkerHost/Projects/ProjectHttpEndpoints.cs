using System;
using System.Threading;
using BIM765T.Revit.Contracts.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BIM765T.Revit.WorkerHost.Projects;

internal static class ProjectHttpEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        app.MapPost("/api/projects/init/preview", (ProjectInitPreviewRequest request, ProjectInitHostService service) =>
        {
            try
            {
                return Results.Ok(service.Preview(request));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapPost("/api/projects/init/apply", async (ProjectInitApplyRequest request, ProjectInitHostService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ApplyAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapGet("/api/projects/{workspaceId}/manifest", (string workspaceId, ProjectInitHostService service) =>
            Results.Ok(service.GetManifest(new ProjectManifestRequest { WorkspaceId = workspaceId })));

        app.MapGet("/api/projects/{workspaceId}/context", (string workspaceId, string? query, int? maxSourceRefs, int? maxStandardsRefs, ProjectInitHostService service) =>
            Results.Ok(service.GetContextBundle(new ProjectContextBundleRequest
            {
                WorkspaceId = workspaceId,
                Query = query ?? string.Empty,
                MaxSourceRefs = maxSourceRefs.GetValueOrDefault(8) <= 0 ? 8 : maxSourceRefs.GetValueOrDefault(8),
                MaxStandardsRefs = maxStandardsRefs.GetValueOrDefault(6) <= 0 ? 6 : maxStandardsRefs.GetValueOrDefault(6)
            })));

        app.MapPost("/api/projects/{workspaceId}/deep-scan", async (string workspaceId, ProjectDeepScanRequest request, ProjectDeepScanHostService service, CancellationToken cancellationToken) =>
        {
            try
            {
                request.WorkspaceId = workspaceId;
                return Results.Ok(await service.RunAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        });

        app.MapGet("/api/projects/{workspaceId}/deep-scan", (string workspaceId, ProjectDeepScanHostService service) =>
            Results.Ok(service.GetReport(new ProjectDeepScanGetRequest
            {
                WorkspaceId = workspaceId
            })));
    }
}
