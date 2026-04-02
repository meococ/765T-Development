using System;
using System.IO;

namespace BIM765T.Revit.Bridge.Tests;

public sealed class BridgeTopologyMessagingTests
{
    [Fact]
    public void BridgeProgram_Labels_WorkerHost_As_Canonical_Public_Ingress()
    {
        var repoRoot = FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(repoRoot, "src", "BIM765T.Revit.Bridge", "Program.cs"));

        Assert.Contains("BRIDGE_CANONICAL_PUBLIC_INGRESS", content, StringComparison.Ordinal);
        Assert.Contains("WorkerHost", content, StringComparison.Ordinal);
        Assert.Contains("transitional adapter lanes", content, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeProgram_Labels_Kernel_And_Legacy_Fallbacks_As_NonCanonical()
    {
        var repoRoot = FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(repoRoot, "src", "BIM765T.Revit.Bridge", "Program.cs"));

        Assert.Contains("BRIDGE_TOPOLOGY_TRANSITIONAL", content, StringComparison.Ordinal);
        Assert.Contains("BRIDGE_TOPOLOGY_LEGACY", content, StringComparison.Ordinal);
        Assert.Contains("private Revit kernel pipe", content, StringComparison.Ordinal);
        Assert.Contains("legacy and should be considered sunset-bound", content, StringComparison.Ordinal);
        Assert.Contains("--allow-kernel-fallback", content, StringComparison.Ordinal);
        Assert.Contains("--allow-legacy-fallback", content, StringComparison.Ordinal);
        Assert.Contains("--allow-transitional-fallback", content, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "BIM765T.Revit.Bridge", "Program.cs");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for bridge topology tests.");
    }
}
