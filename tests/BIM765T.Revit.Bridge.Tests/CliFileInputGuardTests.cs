using System;
using System.IO;

namespace BIM765T.Revit.Bridge.Tests;

public sealed class CliFileInputGuardTests : IDisposable
{
    private readonly string _disallowedDirectory;

    public CliFileInputGuardTests()
    {
        _disallowedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "bim765t-bridge-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_disallowedDirectory);
    }

    [Fact]
    public void ResolveJsonOrFile_Returns_Inline_Json_When_File_Does_Not_Exist()
    {
        var json = "{\"Tool\":\"review.model_health\"}";
        var resolved = CliFileInputGuard.ResolveJsonOrFile(json, "--payload");

        Assert.Equal(json, resolved);
    }

    [Fact]
    public void ResolveJsonOrFile_Reads_Allowed_Temp_File()
    {
        var path = Path.Combine(Path.GetTempPath(), "bim765t-bridge-payload-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"Hello\":\"World\"}");

            var resolved = CliFileInputGuard.ResolveJsonOrFile(path, "--payload");

            Assert.Equal("{\"Hello\":\"World\"}", resolved);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ValidateAndNormalizePath_Blocks_Files_Outside_Allowed_Roots()
    {
        var path = Path.Combine(_disallowedDirectory, "payload.json");
        File.WriteAllText(path, "{}");

        var ex = Assert.Throws<InvalidOperationException>(() => CliFileInputGuard.ValidateAndNormalizePath(path, "--payload"));

        Assert.Contains("outside allowed roots", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndNormalizePath_Does_Not_Allow_Deprecated_Unsafe_Bypass()
    {
        var path = Path.Combine(Path.GetTempPath(), "bim765t-bridge-payload-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{}");
            Environment.SetEnvironmentVariable("BIM765T_BRIDGE_ALLOW_UNSAFE_FILE_INPUT", "1");

            var ex = Assert.Throws<InvalidOperationException>(() => CliFileInputGuard.ValidateAndNormalizePath(path, "--payload"));

            Assert.Contains("no longer supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BIM765T_BRIDGE_ALLOW_UNSAFE_FILE_INPUT", null);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BIM765T_BRIDGE_ALLOW_UNSAFE_FILE_INPUT", null);
        if (Directory.Exists(_disallowedDirectory))
        {
            Directory.Delete(_disallowedDirectory, recursive: true);
        }
    }
}
