using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BIM765T.Revit.WorkerHost.ExternalAi;

internal static class ExternalAiHttpEndpoints
{
    public static void MapExternalAiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/external-ai/status", async (ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetStatusAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapGet("/api/external-ai/catalog", (string? workspaceId, ExternalAiGatewayService service) =>
        {
            try
            {
                return Results.Ok(service.GetCatalog(workspaceId));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapPost("/api/external-ai/chat", async (ExternalAiChatRequest request, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.SubmitChatAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("ChatLimit");

        app.MapGet("/api/external-ai/missions/{missionId}", async (string missionId, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetMissionAsync(missionId, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapGet("/api/external-ai/missions/{missionId}/events", async Task (string missionId, HttpContext httpContext, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.WriteMissionEventsSseAsync(missionId, httpContext.Response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (httpContext.Response.HasStarted)
                {
                    return;
                }

                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { statusCode = ex.Message, message = ex.Message }, cancellationToken).ConfigureAwait(false);
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapPost("/api/external-ai/missions/{missionId}/approve", async (string missionId, ExternalAiMissionCommandRequest request, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ApproveAsync(missionId, request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapPost("/api/external-ai/missions/{missionId}/reject", async (string missionId, ExternalAiMissionCommandRequest request, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.RejectAsync(missionId, request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapPost("/api/external-ai/missions/{missionId}/cancel", async (string missionId, ExternalAiMissionCommandRequest request, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.CancelAsync(missionId, request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");

        app.MapPost("/api/external-ai/missions/{missionId}/resume", async (string missionId, ExternalAiMissionCommandRequest request, ExternalAiGatewayService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ResumeAsync(missionId, request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { statusCode = ex.Message, message = ex.Message });
            }
        }).RequireRateLimiting("PerIpLimit");
    }
}
