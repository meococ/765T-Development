using System;
using System.IO;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Copilot.Core;

public sealed class CopilotStatePaths
{
    public CopilotStatePaths(string? rootPath = null)
    {
        var resolvedRootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BridgeConstants.AppDataFolderName, "state")
            : rootPath!;
        RootPath = resolvedRootPath;
        TaskRunsPath = Path.Combine(RootPath, "tasks");
        TaskQueuePath = Path.Combine(RootPath, "task-queue");
        MemoryPromotionsPath = Path.Combine(RootPath, "memory");
        ArtifactSummariesPath = Path.Combine(RootPath, "artifact-summaries");
        WorkerRootPath = Path.Combine(RootPath, "worker");
        WorkerEpisodesPath = Path.Combine(WorkerRootPath, "episodes");
        EnsureCreated();
    }

    public string RootPath { get; }

    public string TaskRunsPath { get; }

    public string TaskQueuePath { get; }

    public string MemoryPromotionsPath { get; }

    public string ArtifactSummariesPath { get; }

    public string WorkerRootPath { get; }

    public string WorkerEpisodesPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(TaskRunsPath);
        Directory.CreateDirectory(TaskQueuePath);
        Directory.CreateDirectory(MemoryPromotionsPath);
        Directory.CreateDirectory(ArtifactSummariesPath);
        Directory.CreateDirectory(WorkerRootPath);
        Directory.CreateDirectory(WorkerEpisodesPath);
    }

    public string GetRunPath(string runId)
    {
        return Path.Combine(TaskRunsPath, $"{SanitizeFileName(runId)}.json");
    }

    public string GetPromotionPath(string promotionId)
    {
        return Path.Combine(MemoryPromotionsPath, $"{SanitizeFileName(promotionId)}.json");
    }

    public string GetTaskQueueItemPath(string queueItemId)
    {
        return Path.Combine(TaskQueuePath, $"{SanitizeFileName(queueItemId)}.json");
    }

    public string GetArtifactSummaryPath(string artifactPath)
    {
        return Path.Combine(ArtifactSummariesPath, $"{SanitizeFileName(artifactPath)}.json");
    }

    public string GetWorkerEpisodePath(string episodeId)
    {
        return Path.Combine(WorkerEpisodesPath, $"{SanitizeFileName(episodeId)}.json");
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        var sanitized = value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return sanitized.Length <= 120 ? sanitized : sanitized.Substring(0, 120);
    }
}
