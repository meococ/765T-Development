using System;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Contracts.Validation;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Kernel;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost.Projects;

internal sealed class ProjectInitHostService
{
    private static readonly Action<ILogger, string, Exception?> CloseNonActiveDocumentDebugLog =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(5401, "ProjectInitCloseNonActiveFailed"),
            "Close non-active doc after project init summary failed for {DocumentKey}");
    private static readonly Action<ILogger, string, string, Exception?> LiveSummaryFailedWarningLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(5402, "ProjectInitLiveSummaryFailed"),
            "Project init live summary failed for {FilePath}. Status={StatusCode}");
    private static readonly Action<ILogger, string, Exception?> LiveSummaryPendingWarningLog =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(5403, "ProjectInitLiveSummaryPending"),
            "Project init live summary pending for {FilePath}");

    private readonly ProjectInitService _projectInit;
    private readonly ProjectContextComposer _projectContextComposer;
    private readonly IKernelClient _kernelClient;
    private readonly ILogger<ProjectInitHostService> _logger;

    public ProjectInitHostService(ProjectInitService projectInit, ProjectContextComposer projectContextComposer, IKernelClient kernelClient, ILogger<ProjectInitHostService> logger)
    {
        _projectInit = projectInit;
        _projectContextComposer = projectContextComposer;
        _kernelClient = kernelClient;
        _logger = logger;
    }

    public ProjectInitPreviewResponse Preview(ProjectInitPreviewRequest request)
    {
        ToolPayloadValidator.Validate(request);
        return _projectInit.Preview(request);
    }

    public async Task<ProjectInitApplyResponse> ApplyAsync(ProjectInitApplyRequest request, CancellationToken cancellationToken)
    {
        ToolPayloadValidator.Validate(request);
        var liveSummary = request.IncludeLivePrimaryModelSummary
            ? await TryCapturePrimaryModelAsync(request.PrimaryRevitFilePath, cancellationToken).ConfigureAwait(false)
            : null;
        var response = _projectInit.Apply(request, liveSummary);
        response.ContextBundle = _projectContextComposer.GetContextBundle(new ProjectContextBundleRequest
        {
            WorkspaceId = response.WorkspaceId,
            Query = string.IsNullOrWhiteSpace(request.DisplayName) ? "project init" : request.DisplayName,
            MaxSourceRefs = 8,
            MaxStandardsRefs = 6
        });
        response.OnboardingStatus = response.ContextBundle.OnboardingStatus ?? response.OnboardingStatus;
        return response;
    }

    public ProjectManifestResponse GetManifest(ProjectManifestRequest request)
    {
        ToolPayloadValidator.Validate(request);
        return _projectInit.GetManifest(request);
    }

    public ProjectContextBundleResponse GetContextBundle(ProjectContextBundleRequest request)
    {
        ToolPayloadValidator.Validate(request);
        return _projectContextComposer.GetContextBundle(request);
    }

    private async Task<ProjectPrimaryModelReport?> TryCapturePrimaryModelAsync(string? primaryRevitFilePath, CancellationToken cancellationToken)
    {
        var normalizedPath = ProjectInitService.NormalizeExistingPath(primaryRevitFilePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        try
        {
            var openResult = await _kernelClient.InvokeAsync(new KernelToolRequest
            {
                ToolName = ToolNames.DocumentOpenBackgroundRead,
                PayloadJson = JsonUtil.Serialize(new OpenBackgroundDocumentRequest
                {
                    FilePath = normalizedPath
                }),
                Caller = "BIM765T.Revit.WorkerHost.ProjectInitHostService",
                TimeoutMs = 120_000,
                RequestedAtUtc = DateTime.UtcNow.ToString("O")
            }, cancellationToken).ConfigureAwait(false);

            if (!openResult.Succeeded || string.IsNullOrWhiteSpace(openResult.PayloadJson))
            {
                LiveSummaryFailedWarningLog(_logger, normalizedPath, openResult.StatusCode ?? string.Empty, null);
                return new ProjectPrimaryModelReport
                {
                    FilePath = normalizedPath,
                    Status = ProjectPrimaryModelStatuses.PendingLiveSummary,
                    PendingLiveSummary = true,
                    Summary = $"Primary model live summary pending: {openResult.StatusCode}"
                };
            }

            var summary = JsonUtil.DeserializePayloadOrDefault<DocumentSummaryDto>(openResult.PayloadJson) ?? new DocumentSummaryDto();
            await TryCloseDocumentAsync(summary.DocumentKey, cancellationToken).ConfigureAwait(false);
            return new ProjectPrimaryModelReport
            {
                FilePath = normalizedPath,
                Status = ProjectPrimaryModelStatuses.Captured,
                PendingLiveSummary = false,
                CapturedUtc = DateTime.UtcNow,
                Summary = $"Live summary captured for {summary.Title}.",
                DocumentSummary = summary
            };
        }
        catch (Exception ex)
        {
            LiveSummaryPendingWarningLog(_logger, normalizedPath, ex);
            return new ProjectPrimaryModelReport
            {
                FilePath = normalizedPath,
                Status = ProjectPrimaryModelStatuses.PendingLiveSummary,
                PendingLiveSummary = true,
                Summary = "Primary model live summary pending vi WorkerHost khong the goi kernel ngay luc init."
            };
        }
    }

    private async Task TryCloseDocumentAsync(string documentKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            return;
        }

        try
        {
            await _kernelClient.InvokeAsync(new KernelToolRequest
            {
                ToolName = ToolNames.DocumentCloseNonActive,
                PayloadJson = JsonUtil.Serialize(new CloseDocumentRequest
                {
                    DocumentKey = documentKey,
                    SaveModified = false
                }),
                Caller = "BIM765T.Revit.WorkerHost.ProjectInitHostService",
                TimeoutMs = 60_000,
                RequestedAtUtc = DateTime.UtcNow.ToString("O")
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CloseNonActiveDocumentDebugLog(_logger, documentKey, ex);
        }
    }
}
