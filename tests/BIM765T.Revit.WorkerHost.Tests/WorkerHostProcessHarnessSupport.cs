using System;
using System.IO;

namespace BIM765T.Revit.WorkerHost.Tests;

internal static class WorkerHostProcessHarnessSupport
{
    internal static string PrepareIsolatedExePath(string repoRoot)
    {
        var sourceDirectory = Path.Combine(repoRoot, "src", "BIM765T.Revit.WorkerHost", "bin", "Release", "net8.0");
        var sourceExe = Path.Combine(sourceDirectory, "BIM765T.Revit.WorkerHost.exe");
        if (!File.Exists(sourceExe))
        {
            throw new FileNotFoundException("WorkerHost executable not found.", sourceExe);
        }

        var isolatedRoot = Path.Combine(Path.GetTempPath(), "BIM765T.WorkerHost.Harness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);
        CopyDirectory(sourceDirectory, isolatedRoot);
        return Path.Combine(isolatedRoot, "BIM765T.Revit.WorkerHost.exe");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
