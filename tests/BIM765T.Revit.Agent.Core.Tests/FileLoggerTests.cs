using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public void FileLogger_Writes_JsonLine_With_Correlation_And_Tool()
    {
        var logDirectory = CreateTempDirectory();
        try
        {
            var logger = new FileLogger(verbose: true, jsonLogFormat: true, logDirectoryOverride: logDirectory);
            using (logger.BeginScope("corr-001", "review.model_health", "pipe"))
            {
                logger.Warn("testing structured log");
            }

            var file = Directory.GetFiles(logDirectory, "*.log").Single();
            var line = File.ReadAllLines(file).Single();
            using var json = JsonDocument.Parse(line);
            Assert.Equal("WARN", json.RootElement.GetProperty("Level").GetString());
            Assert.Equal("testing structured log", json.RootElement.GetProperty("Message").GetString());
            Assert.Equal("corr-001", json.RootElement.GetProperty("CorrelationId").GetString());
            Assert.Equal("review.model_health", json.RootElement.GetProperty("ToolName").GetString());
            Assert.Equal("pipe", json.RootElement.GetProperty("Source").GetString());
        }
        finally
        {
            Directory.Delete(logDirectory, true);
        }
    }

    [Fact]
    public void FileLogger_BeginScope_Restores_Previous_Context()
    {
        var logDirectory = CreateTempDirectory();
        try
        {
            var logger = new FileLogger(verbose: true, jsonLogFormat: true, logDirectoryOverride: logDirectory);
            using (logger.BeginScope("outer-corr", "tool.outer", "pipe"))
            {
                logger.Info("outer-1");
                using (logger.BeginScope("inner-corr", "tool.inner", "executor"))
                {
                    logger.Info("inner");
                }

                logger.Info("outer-2");
            }

            var file = Directory.GetFiles(logDirectory, "*.log").Single();
            var lines = File.ReadAllLines(file);
            Assert.Equal(3, lines.Length);

            using var outer1 = JsonDocument.Parse(lines[0]);
            using var inner = JsonDocument.Parse(lines[1]);
            using var outer2 = JsonDocument.Parse(lines[2]);

            Assert.Equal("outer-corr", outer1.RootElement.GetProperty("CorrelationId").GetString());
            Assert.Equal("tool.outer", outer1.RootElement.GetProperty("ToolName").GetString());
            Assert.Equal("inner-corr", inner.RootElement.GetProperty("CorrelationId").GetString());
            Assert.Equal("tool.inner", inner.RootElement.GetProperty("ToolName").GetString());
            Assert.Equal("outer-corr", outer2.RootElement.GetProperty("CorrelationId").GetString());
            Assert.Equal("tool.outer", outer2.RootElement.GetProperty("ToolName").GetString());
        }
        finally
        {
            Directory.Delete(logDirectory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "bim765t-filelogger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
