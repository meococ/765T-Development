using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Configuration;

internal sealed class WorkerHostSettings
{
    public string PublicPipeName { get; set; } = BridgeConstants.DefaultWorkerHostPipeName;

    public string KernelPipeName { get; set; } = BridgeConstants.DefaultKernelPipeName;

    public string StateRootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BridgeConstants.AppDataFolderName,
        "workerhost");

    public string LegacyStateRootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BridgeConstants.AppDataFolderName,
        "state");

    public string ProjectWorkspaceRootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BridgeConstants.AppDataFolderName,
        "workspaces");

    public string EventStorePath => Path.Combine(StateRootPath, "workerhost.sqlite");

    public string CompanionRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BridgeConstants.AppDataFolderName,
        "companion");

    public string QdrantCompanionRootPath => Path.Combine(CompanionRootPath, "qdrant");

    public string QdrantStoragePath => Path.Combine(QdrantCompanionRootPath, "storage");

    public string QdrantSnapshotsPath => Path.Combine(QdrantCompanionRootPath, "snapshots");

    public string QdrantConfigPath => Path.Combine(QdrantCompanionRootPath, "config.local.yaml");

    public string QdrantUrl { get; set; } = "http://127.0.0.1:6333";

    public string QdrantCollectionName { get; set; } = "bim765t";

    public List<string> MemoryNamespaces { get; set; } = new()
    {
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.AtlasNativeCommands,
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.AtlasCustomTools,
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.AtlasCuratedScripts,
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.PlaybooksPolicies,
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.ProjectRuntimeMemory,
        BIM765T.Revit.Contracts.Platform.MemoryNamespaces.EvidenceLessons
    };

    public string WindowsServiceName { get; set; } = "BIM765T.Revit.WorkerHost";

    public string PublicPipeAclMode { get; set; } = "authenticated_users";

    public int QdrantHttpPort { get; set; } = 6333;

    public int QdrantGrpcPort { get; set; } = 6334;

    public int DefaultTopK { get; set; } = 5;

    public int DefaultTimeoutMs { get; set; } = 120_000;

    public int StreamingPollIntervalMs { get; set; } = 250;

    public int StreamingIdleTimeoutMs { get; set; } = 10_000;

    public int EmbeddingDimensions { get; set; } = 64;

    public int HttpApiPort { get; set; } = 50765;

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Ship;

    public double BoundedPlannerConfidenceThreshold { get; set; } = 0.45d;

    public double ShipPlannerConfidenceThreshold { get; set; } = 0.22d;

    public int ShipModePlannerCandidateLimit { get; set; } = 14;

    public int WalCheckpointIntervalSeconds { get; set; } = 30;

    public int OutboxProjectorPollIntervalMs { get; set; } = 2_000;

    public int OutboxProjectorBatchSize { get; set; } = 32;

    public int OutboxProjectorMaxAttempts { get; set; } = 5;

    public int OutboxProjectorBaseBackoffMs { get; set; } = 1_000;

    public int OutboxProjectorMaxBackoffMs { get; set; } = 60_000;

    public int OutboxLeaseTimeoutMs { get; set; } = 120_000;

    public void EnsureCreated()
    {
        Directory.CreateDirectory(StateRootPath);
        Directory.CreateDirectory(QdrantCompanionRootPath);
        Directory.CreateDirectory(QdrantStoragePath);
        Directory.CreateDirectory(QdrantSnapshotsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(QdrantConfigPath) ?? QdrantCompanionRootPath);
    }

    public IReadOnlyList<string> GetSemanticNamespaces()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var value in MemoryNamespaces ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                ordered.Add(trimmed);
            }
        }

        if (ordered.Count == 0)
        {
            ordered.Add(BIM765T.Revit.Contracts.Platform.MemoryNamespaces.ProjectRuntimeMemory);
        }

        return ordered;
    }

    public string ResolveQdrantCollectionName(string? namespaceId)
    {
        var prefix = SanitizeCollectionToken(QdrantCollectionName, "bim765t");
        var suffix = SanitizeCollectionToken(namespaceId, BIM765T.Revit.Contracts.Platform.MemoryNamespaces.ProjectRuntimeMemory);
        return prefix + "-" + suffix;
    }

    public string ResolveAutonomyMode()
    {
        return WorkerAutonomyModes.Normalize(AutonomyMode);
    }

    public double ResolvePlannerConfidenceThreshold()
    {
        return string.Equals(ResolveAutonomyMode(), WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase)
            ? ShipPlannerConfidenceThreshold
            : BoundedPlannerConfidenceThreshold;
    }

    private static string SanitizeCollectionToken(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        var chars = source
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
