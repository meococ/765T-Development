using System.Collections.Generic;

namespace BIM765T.Revit.WorkerHost.Migration;

internal sealed class LegacyMigrationReport
{
    public string GeneratedUtc { get; set; } = string.Empty;

    public string SourceRootPath { get; set; } = string.Empty;

    public bool DryRun { get; set; }

    public bool ForceRequested { get; set; }

    public ImportBucket TaskRuns { get; set; } = new ImportBucket();

    public ImportBucket Promotions { get; set; } = new ImportBucket();

    public ImportBucket Episodes { get; set; } = new ImportBucket();

    public ImportBucket QueueItems { get; set; } = new ImportBucket();

    public List<string> Errors { get; set; } = new List<string>();

    public bool Succeeded => Errors.Count == 0;
}

internal sealed class ImportBucket
{
    public int Scanned { get; set; }

    public int WouldImport { get; set; }

    public int Imported { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public List<string> SampleIds { get; set; } = new List<string>();
}
