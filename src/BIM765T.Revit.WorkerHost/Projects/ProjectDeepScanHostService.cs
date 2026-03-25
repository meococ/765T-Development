using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

internal sealed class ProjectDeepScanHostService
{
    private static readonly Action<ILogger, string, string, Exception?> ToolFailureWarningLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(5411, "ProjectDeepScanToolFailed"),
            "Project deep scan tool failed for {ToolName}. Status={StatusCode}");
    private static readonly Action<ILogger, string, Exception?> ScanDocumentFailedWarningLog =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(5412, "ProjectDeepScanDocumentFailed"),
            "Project deep scan failed for document {FilePath}");
    private static readonly Action<ILogger, string, Exception?> CloseNonActiveDocumentDebugLog =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(5413, "ProjectDeepScanCloseNonActiveFailed"),
            "Project deep scan close non-active doc failed for {DocumentKey}");

    private readonly ProjectDeepScanService _deepScan;
    private readonly ProjectContextComposer _projectContextComposer;
    private readonly IKernelClient _kernelClient;
    private readonly ILogger<ProjectDeepScanHostService> _logger;

    public ProjectDeepScanHostService(ProjectDeepScanService deepScan, ProjectContextComposer projectContextComposer, IKernelClient kernelClient, ILogger<ProjectDeepScanHostService> logger)
    {
        _deepScan = deepScan;
        _projectContextComposer = projectContextComposer;
        _kernelClient = kernelClient;
        _logger = logger;
    }

    public async Task<ProjectDeepScanResponse> RunAsync(ProjectDeepScanRequest request, CancellationToken cancellationToken)
    {
        ToolPayloadValidator.Validate(request);
        var existing = _deepScan.GetReport(new ProjectDeepScanGetRequest { WorkspaceId = request.WorkspaceId });
        if (existing.Exists && !request.ForceRescan)
        {
            return BuildResponseFromExisting(existing);
        }

        var plan = _deepScan.Prepare(request);
        var report = new ProjectDeepScanReport
        {
            WorkspaceId = plan.WorkspaceId,
            WorkspaceRootPath = plan.WorkspaceRoot,
            GeneratedUtc = DateTime.UtcNow,
            PrimaryRevitFilePath = plan.PrimaryRevitFilePath,
            ManifestStats = plan.ManifestStats,
            Stats = new ProjectDeepScanStats
            {
                DocumentsRequested = plan.TargetFiles.Count
            },
            PendingUnknowns = new List<string>()
        };

        foreach (var filePath in plan.TargetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.Documents.Add(await ScanDocumentAsync(filePath, request, cancellationToken).ConfigureAwait(false));
        }

        report.Findings = report.Documents
            .SelectMany(BuildFindings)
            .OrderByDescending(x => GetSeverityRank(x.Severity))
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxFindings))
            .ToList();
        report.EvidenceRefs = BuildEvidenceRefs(report, request.MaxFindings);
        report.Strengths = BuildStrengths(report);
        report.Weaknesses = BuildWeaknesses(report);
        report.PendingUnknowns = BuildPendingUnknowns(report, plan);

        var response = _deepScan.Save(request, report);
        response.ContextBundle = _projectContextComposer.GetContextBundle(new ProjectContextBundleRequest
        {
            WorkspaceId = response.WorkspaceId,
            Query = "project deep scan",
            MaxSourceRefs = 8,
            MaxStandardsRefs = 6
        });
        response.OnboardingStatus = response.ContextBundle.OnboardingStatus ?? response.OnboardingStatus;
        return response;
    }

    public ProjectDeepScanReportResponse GetReport(ProjectDeepScanGetRequest request)
    {
        ToolPayloadValidator.Validate(request);
        return _deepScan.GetReport(request);
    }

    private ProjectDeepScanResponse BuildResponseFromExisting(ProjectDeepScanReportResponse existing)
    {
        return new ProjectDeepScanResponse
        {
            StatusCode = existing.StatusCode,
            WorkspaceId = existing.WorkspaceId,
            WorkspaceRootPath = existing.WorkspaceRootPath,
            ReportPath = existing.ReportPath,
            SummaryReportPath = existing.SummaryReportPath,
            Report = existing.Report,
            Summary = existing.Summary,
            OnboardingStatus = existing.OnboardingStatus,
            ContextBundle = _projectContextComposer.GetContextBundle(new ProjectContextBundleRequest
            {
                WorkspaceId = existing.WorkspaceId,
                Query = "project deep scan",
                MaxSourceRefs = 8,
                MaxStandardsRefs = 6
            })
        };
    }

    private async Task<ProjectDeepScanDocumentReport> ScanDocumentAsync(string filePath, ProjectDeepScanRequest request, CancellationToken cancellationToken)
    {
        var documentReport = new ProjectDeepScanDocumentReport
        {
            FilePath = filePath ?? string.Empty,
            FileName = Path.GetFileName(filePath ?? string.Empty),
            Status = ProjectDeepScanStatuses.Failed,
            Summary = "Deep scan chua bat dau."
        };

        string documentKey = string.Empty;
        try
        {
            var open = await InvokeToolAsync<OpenBackgroundDocumentRequest, DocumentSummaryDto>(
                ToolNames.DocumentOpenBackgroundRead,
                new OpenBackgroundDocumentRequest { FilePath = filePath ?? string.Empty },
                cancellationToken,
                timeoutMs: 180_000).ConfigureAwait(false);

            if (!open.Success || open.Payload == null)
            {
                documentReport.Summary = $"Khong mo duoc document: {open.StatusCode}";
                return documentReport;
            }

            documentReport.DocumentSummary = open.Payload;
            documentReport.DocumentKey = open.Payload.DocumentKey ?? string.Empty;
            documentKey = documentReport.DocumentKey;

            documentReport.ModelHealth = (await InvokeToolAsync<string, ModelHealthResponse>(
                ToolNames.ReviewModelHealth,
                string.Empty,
                cancellationToken,
                targetDocument: documentKey).ConfigureAwait(false)).Payload ?? new ModelHealthResponse();

            documentReport.LinksStatus = (await InvokeToolAsync<string, LinksStatusResponse>(
                ToolNames.ReviewLinksStatus,
                string.Empty,
                cancellationToken,
                targetDocument: documentKey).ConfigureAwait(false)).Payload ?? new LinksStatusResponse();

            documentReport.WorksetHealth = (await InvokeToolAsync<string, WorksetHealthResponse>(
                ToolNames.ReviewWorksetHealth,
                string.Empty,
                cancellationToken,
                targetDocument: documentKey).ConfigureAwait(false)).Payload ?? new WorksetHealthResponse();

            var sheetList = await InvokeToolAsync<SheetListRequest, SheetListResponse>(
                ToolNames.SheetListAll,
                new SheetListRequest
                {
                    DocumentKey = documentKey,
                    IncludeViewports = false,
                    MaxResults = Math.Max(1, request.MaxSheets)
                },
                cancellationToken,
                targetDocument: documentKey).ConfigureAwait(false);

            var sheets = sheetList.Payload?.Sheets ?? new List<SheetItem>();
            documentReport.SheetCountDiscovered = sheetList.Payload?.Count ?? 0;

            var smartQc = await InvokeToolAsync<SmartQcRequest, SmartQcResponse>(
                ToolNames.ReviewSmartQc,
                new SmartQcRequest
                {
                    DocumentKey = documentKey,
                    RulesetName = string.IsNullOrWhiteSpace(request.SmartQcRulesetName) ? "base-rules" : request.SmartQcRulesetName,
                    MaxFindings = Math.Max(1, request.MaxFindings),
                    MaxSheets = Math.Max(1, request.MaxSheets)
                },
                cancellationToken,
                targetDocument: documentKey,
                timeoutMs: 180_000).ConfigureAwait(false);
            documentReport.SmartQc = smartQc.Payload ?? new SmartQcResponse();

            foreach (var sheet in sheets.Take(Math.Max(1, request.MaxSheetIntelligence)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                documentReport.Sheets.Add(await ScanSheetAsync(documentKey, sheet, request, cancellationToken).ConfigureAwait(false));
            }

            documentReport.Status = (sheetList.Success && smartQc.Success)
                ? ProjectDeepScanStatuses.Completed
                : ProjectDeepScanStatuses.Partial;
            documentReport.Summary = BuildDocumentSummary(documentReport);
            return documentReport;
        }
        catch (Exception ex)
        {
            ScanDocumentFailedWarningLog(_logger, filePath ?? string.Empty, ex);
            documentReport.Status = ProjectDeepScanStatuses.Failed;
            documentReport.Summary = $"Deep scan that bai: {ex.Message}";
            return documentReport;
        }
        finally
        {
            await TryCloseDocumentAsync(documentKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ProjectDeepScanSheetReport> ScanSheetAsync(string documentKey, SheetItem sheet, ProjectDeepScanRequest request, CancellationToken cancellationToken)
    {
        var summary = await InvokeToolAsync<SheetSummaryRequest, SheetSummaryResponse>(
            ToolNames.ReviewSheetSummary,
            new SheetSummaryRequest
            {
                DocumentKey = documentKey,
                SheetId = sheet.Id,
                SheetNumber = sheet.SheetNumber,
                SheetName = sheet.SheetName,
                MaxPlacedViews = 10
            },
            cancellationToken,
            targetDocument: documentKey).ConfigureAwait(false);

        var intelligence = await InvokeToolAsync<SheetCaptureIntelligenceRequest, SheetCaptureIntelligenceResponse>(
            ToolNames.SheetCaptureIntelligence,
            new SheetCaptureIntelligenceRequest
            {
                DocumentKey = documentKey,
                SheetId = sheet.Id,
                SheetNumber = sheet.SheetNumber,
                IncludeViewportDetails = true,
                IncludeScheduleData = request.IncludeScheduleData,
                MaxViewports = 10,
                MaxSchedules = Math.Max(1, request.MaxSchedulesPerSheet),
                MaxSheetTextNotes = 20,
                MaxViewportTextNotes = 10,
                WriteArtifacts = false
            },
            cancellationToken,
            targetDocument: documentKey,
            timeoutMs: 180_000).ConfigureAwait(false);

        var report = new ProjectDeepScanSheetReport
        {
            SheetId = sheet.Id,
            SheetNumber = sheet.SheetNumber ?? string.Empty,
            SheetName = sheet.SheetName ?? string.Empty,
            Summary = summary.Payload ?? new SheetSummaryResponse(),
            Intelligence = intelligence.Payload ?? new SheetCaptureIntelligenceResponse()
        };

        if (request.IncludeScheduleData)
        {
            foreach (var schedule in (report.Intelligence.Schedules ?? new List<SheetScheduleIntelligence>()).Take(Math.Max(1, request.MaxSchedulesPerSheet)))
            {
                var extraction = await InvokeToolAsync<ScheduleExtractionRequest, ScheduleExtractionResponse>(
                    ToolNames.DataExtractScheduleStructured,
                    new ScheduleExtractionRequest
                    {
                        DocumentKey = documentKey,
                        ScheduleId = schedule.ScheduleViewId,
                        ScheduleName = schedule.ScheduleName,
                        MaxRows = Math.Max(1, request.MaxScheduleRows),
                        IncludeColumnMetadata = true
                    },
                    cancellationToken,
                    targetDocument: documentKey,
                    timeoutMs: 180_000).ConfigureAwait(false);

                if (extraction.Payload != null)
                {
                    report.ScheduleSamples.Add(new ProjectDeepScanScheduleReport
                    {
                        SourceSheetNumber = report.SheetNumber,
                        ScheduleId = extraction.Payload.ScheduleId,
                        ScheduleName = extraction.Payload.ScheduleName,
                        Extraction = extraction.Payload
                    });
                }
            }
        }

        return report;
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
                Caller = "BIM765T.Revit.WorkerHost.ProjectDeepScanHostService",
                TimeoutMs = 60_000,
                RequestedAtUtc = DateTime.UtcNow.ToString("O")
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CloseNonActiveDocumentDebugLog(_logger, documentKey, ex);
        }
    }

    private async Task<(bool Success, string StatusCode, TResponse? Payload)> InvokeToolAsync<TRequest, TResponse>(
        string toolName,
        TRequest payload,
        CancellationToken cancellationToken,
        string? targetDocument = null,
        int timeoutMs = 120_000)
        where TResponse : class
    {
        var result = await _kernelClient.InvokeAsync(new KernelToolRequest
        {
            ToolName = toolName,
            PayloadJson = JsonUtil.Serialize(payload),
            Caller = "BIM765T.Revit.WorkerHost.ProjectDeepScanHostService",
            TimeoutMs = timeoutMs,
            RequestedAtUtc = DateTime.UtcNow.ToString("O"),
            TargetDocument = targetDocument ?? string.Empty
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.PayloadJson))
        {
            ToolFailureWarningLog(_logger, toolName, result.StatusCode ?? string.Empty, null);
            return (false, result.StatusCode ?? string.Empty, null);
        }

        return (true, result.StatusCode ?? string.Empty, JsonUtil.DeserializePayloadOrDefault<TResponse>(result.PayloadJson));
    }

    private static string BuildDocumentSummary(ProjectDeepScanDocumentReport report)
    {
        var warnings = report.ModelHealth?.TotalWarnings ?? 0;
        var links = report.LinksStatus?.TotalLinks ?? 0;
        var findings = report.SmartQc?.FindingCount ?? 0;
        return $"{report.FileName}: warnings={warnings}, links={links}, qcFindings={findings}, sheetsSampled={(report.Sheets ?? new List<ProjectDeepScanSheetReport>()).Count}.";
    }

    private static List<ProjectDeepScanFinding> BuildFindings(ProjectDeepScanDocumentReport document)
    {
        var results = new List<ProjectDeepScanFinding>();
        foreach (var issue in document.ModelHealth?.Review?.Issues ?? new List<ReviewIssue>())
        {
            results.Add(new ProjectDeepScanFinding
            {
                FindingId = $"model:{document.FileName}:{issue.Code}:{issue.ElementId}",
                Category = "model_health",
                Title = issue.Code,
                Message = issue.Message,
                Severity = issue.Severity,
                SourceTool = ToolNames.ReviewModelHealth,
                FilePath = document.FilePath,
                ElementId = issue.ElementId,
                EvidenceRef = document.DocumentKey
            });
        }

        foreach (var issue in document.WorksetHealth?.Review?.Issues ?? new List<ReviewIssue>())
        {
            results.Add(new ProjectDeepScanFinding
            {
                FindingId = $"workset:{document.FileName}:{issue.Code}:{issue.ElementId}",
                Category = "workset_health",
                Title = issue.Code,
                Message = issue.Message,
                Severity = issue.Severity,
                SourceTool = ToolNames.ReviewWorksetHealth,
                FilePath = document.FilePath,
                ElementId = issue.ElementId,
                EvidenceRef = document.DocumentKey
            });
        }

        foreach (var finding in document.SmartQc?.Findings ?? new List<SmartQcFinding>())
        {
            results.Add(new ProjectDeepScanFinding
            {
                FindingId = $"smartqc:{document.FileName}:{finding.RuleId}:{finding.ElementId}:{finding.SheetId}",
                Category = finding.Category,
                Title = string.IsNullOrWhiteSpace(finding.Title) ? finding.RuleId : finding.Title,
                Message = finding.Message,
                Severity = finding.Severity,
                SourceTool = string.IsNullOrWhiteSpace(finding.SourceTool) ? ToolNames.ReviewSmartQc : finding.SourceTool,
                FilePath = document.FilePath,
                SheetNumber = ResolveSheetNumber(document, finding.SheetId),
                ElementId = finding.ElementId,
                EvidenceRef = finding.EvidenceRef,
                SuggestedAction = finding.SuggestedAction
            });
        }

        foreach (var sheet in document.Sheets ?? new List<ProjectDeepScanSheetReport>())
        {
            foreach (var issue in sheet.Summary?.Review?.Issues ?? new List<ReviewIssue>())
            {
                results.Add(new ProjectDeepScanFinding
                {
                    FindingId = $"sheet:{document.FileName}:{sheet.SheetNumber}:{issue.Code}:{issue.ElementId}",
                    Category = "sheet_summary",
                    Title = issue.Code,
                    Message = issue.Message,
                    Severity = issue.Severity,
                    SourceTool = ToolNames.ReviewSheetSummary,
                    FilePath = document.FilePath,
                    SheetNumber = sheet.SheetNumber,
                    ElementId = issue.ElementId,
                    EvidenceRef = $"{document.DocumentKey}:{sheet.SheetNumber}"
                });
            }
        }

        return results;
    }

    private static string ResolveSheetNumber(ProjectDeepScanDocumentReport document, int? sheetId)
    {
        if (!sheetId.HasValue)
        {
            return string.Empty;
        }

        var match = (document.Sheets ?? new List<ProjectDeepScanSheetReport>())
            .FirstOrDefault(x => x.SheetId == sheetId.Value);
        return match?.SheetNumber ?? string.Empty;
    }

    private static List<ProjectContextRef> BuildEvidenceRefs(ProjectDeepScanReport report, int maxRefs)
    {
        var refs = new List<ProjectContextRef>();
        foreach (var finding in (report.Findings ?? new List<ProjectDeepScanFinding>())
                     .OrderByDescending(x => GetSeverityRank(x.Severity))
                     .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Max(1, maxRefs)))
        {
            refs.Add(new ProjectContextRef
            {
                RefId = finding.FindingId,
                Title = string.IsNullOrWhiteSpace(finding.Title) ? finding.Category : finding.Title,
                RefKind = "deep_scan_finding",
                SourcePath = finding.FilePath,
                RelativePath = string.IsNullOrWhiteSpace(finding.SheetNumber) ? string.Empty : finding.SheetNumber,
                Summary = finding.Message
            });
        }

        return refs;
    }

    private static List<string> BuildStrengths(ProjectDeepScanReport report)
    {
        var strengths = new List<string>();
        var stats = report.Stats ?? new ProjectDeepScanStats();
        if (stats.DocumentsScanned > 0)
        {
            strengths.Add($"Da deep scan {stats.DocumentsScanned} document read-only qua project brain lane.");
        }

        if (stats.TotalLinks > 0)
        {
            strengths.Add($"Da capture link topology: loaded {stats.LoadedLinks}/{stats.TotalLinks} link(s).");
        }

        if (stats.SheetsScanned > 0)
        {
            strengths.Add($"Da lay intelligence cho {stats.SheetsScanned} sheet(s) mau.");
        }

        if (stats.ScheduleSamples > 0)
        {
            strengths.Add($"Da extract {stats.ScheduleSamples} structured schedule sample(s).");
        }

        return strengths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildWeaknesses(ProjectDeepScanReport report)
    {
        var weaknesses = new List<string>();
        var stats = report.Stats ?? new ProjectDeepScanStats();
        if (stats.WarningCount > 0)
        {
            weaknesses.Add($"Model warnings van con {stats.WarningCount} issue(s).");
        }

        if (stats.FindingCount > 0)
        {
            weaknesses.Add($"Smart QC / review dang co {stats.FindingCount} finding(s) can xem xet.");
        }

        if (stats.DocumentsFailed > 0)
        {
            weaknesses.Add($"Co {stats.DocumentsFailed} document khong the deep scan hoan tat.");
        }

        if (stats.SheetsScanned == 0)
        {
            weaknesses.Add("Chua capture duoc sheet intelligence nao.");
        }

        return weaknesses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildPendingUnknowns(ProjectDeepScanReport report, ProjectDeepScanPlan plan)
    {
        var pending = new List<string>();
        if ((report.Documents ?? new List<ProjectDeepScanDocumentReport>()).Count < plan.TargetFiles.Count)
        {
            pending.Add("Chua scan het target documents da chon.");
        }

        if ((report.Documents ?? new List<ProjectDeepScanDocumentReport>())
            .Any(x => string.Equals(x.Status, ProjectDeepScanStatuses.Failed, StringComparison.OrdinalIgnoreCase)))
        {
            pending.Add("Mot hoac nhieu document deep scan that bai.");
        }

        if ((report.Stats?.ScheduleSamples ?? 0) == 0)
        {
            pending.Add("Structured schedule samples chua co hoac khong tim thay tren cac sheet da quet.");
        }

        return pending
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetSeverityRank(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => 3,
            DiagnosticSeverity.Warning => 2,
            DiagnosticSeverity.Info => 1,
            _ => 0
        };
    }
}
