using System;
using System.IO;
using System.Linq;
using System.Text;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ApprovalTokenStoreTests
{
    [Fact]
    public void ApprovalTokenStore_RoundTrips_Protected_Records()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var logger = new TestLogger();
            var store = new ApprovalTokenStore(logger, tempDir);
            var records = new[]
            {
                new PersistedApprovalRecord
                {
                    Token = "token-001",
                    ToolName = "element.move_safe",
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(5)
                }
            };

            store.Save(records);
            var path = Path.Combine(tempDir, "pending-approvals.dat");
            var bytes = File.ReadAllBytes(path);
            var text = Encoding.UTF8.GetString(bytes);

            Assert.DoesNotContain("token-001", text);

            var loaded = store.Load();
            Assert.Single(loaded);
            Assert.Equal("token-001", loaded[0].Token);
            Assert.Equal("element.move_safe", loaded[0].ToolName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ApprovalTokenStore_Rejects_LegacyPlaintext_Format()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var logger = new TestLogger();
            var path = Path.Combine(tempDir, "pending-approvals.dat");
            var envelope = new PersistedApprovalEnvelope
            {
                Records =
                {
                    new PersistedApprovalRecord
                    {
                        Token = "legacy-token",
                        ToolName = "file.save_document",
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
                    }
                }
            };
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(JsonUtil.Serialize(envelope)));

            var store = new ApprovalTokenStore(logger, tempDir);
            var loaded = store.Load();

            Assert.Empty(loaded);
            Assert.Contains(logger.Messages, message => message.Contains("Ignoring legacy or unprotected approval token cache", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "bim765t-approval-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
