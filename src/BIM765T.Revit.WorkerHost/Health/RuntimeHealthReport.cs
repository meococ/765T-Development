using System.Collections.Generic;
using BIM765T.Revit.WorkerHost.Eventing;

namespace BIM765T.Revit.WorkerHost.Health;

internal sealed class RuntimeHealthReport
{
    public string GeneratedUtc { get; set; } = string.Empty;

    public bool Ready { get; set; }

    public bool StandaloneChatReady { get; set; }

    public bool LiveRevitReady { get; set; }

    public bool Degraded { get; set; }

    public string ReadinessSummary { get; set; } = string.Empty;

    public string RuntimeTopology { get; set; } = string.Empty;

    public string PublicPipeName { get; set; } = string.Empty;

    public string KernelPipeName { get; set; } = string.Empty;

    public string StateRootPath { get; set; } = string.Empty;

    public string EventStorePath { get; set; } = string.Empty;

    public string LegacyStateRootPath { get; set; } = string.Empty;

    public StoreStatistics Store { get; set; } = new StoreStatistics();

    public LegacyStateInventory LegacyState { get; set; } = new LegacyStateInventory();

    public DependencyHealth PublicControlPlane { get; set; } = new DependencyHealth();

    public DependencyHealth Kernel { get; set; } = new DependencyHealth();

    public DependencyHealth Qdrant { get; set; } = new DependencyHealth();

    public string SemanticMode { get; set; } = string.Empty;

    public List<string> SemanticNamespaces { get; set; } = new List<string>();

    public int RuntimeToolCount { get; set; }

    public int SourceToolCount { get; set; }

    public bool RuntimeLooksStale { get; set; }

    public List<string> StaleReasons { get; set; } = new List<string>();

    public List<string> Diagnostics { get; set; } = new List<string>();
}

internal sealed class LegacyStateInventory
{
    public string RootPath { get; set; } = string.Empty;

    public int TaskRunFileCount { get; set; }

    public int PromotionFileCount { get; set; }

    public int EpisodeFileCount { get; set; }

    public int QueueItemFileCount { get; set; }
}

internal sealed class DependencyHealth
{
    public string Name { get; set; } = string.Empty;

    public bool Reachable { get; set; }

    public string StatusCode { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public List<string> Diagnostics { get; set; } = new List<string>();
}
