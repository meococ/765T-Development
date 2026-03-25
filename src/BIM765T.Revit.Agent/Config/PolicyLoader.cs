using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Config;

/// <summary>
/// Load policy.json từ %APPDATA%\BIM765T.Revit.Agent\.
///
/// P1-2 FIX: Không nuốt lỗi im lặng nữa.
/// - JSON lỗi → fallback về default (safe: deny all)
/// - HasLoadError + Diagnostics cho AgentHost log warning
/// </summary>
internal static class PolicyLoader
{
    internal static PolicyLoadResult Load()
    {
        var result = new PolicyLoadResult();
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, BridgeConstants.AppDataFolderName);
            var path = Path.Combine(dir, "policy.json");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtil.Serialize(result.Policy));
                return result;
            }

            result.Policy = JsonUtil.Deserialize<BridgePolicy>(File.ReadAllText(path));
            return result;
        }
        catch (Exception ex)
        {
            result.HasLoadError = true;
            result.LoadError = ex.Message;
            result.Diagnostics.Add(DiagnosticRecord.Create(
                "CONFIG_POLICY_PARSE_ERROR",
                DiagnosticSeverity.Warning,
                $"policy.json parse failed — fallback về defaults. Lỗi: {ex.Message}"));
            return result;
        }
    }
}

internal sealed class PolicyLoadResult
{
    internal BridgePolicy Policy { get; set; } = new BridgePolicy();
    internal bool HasLoadError { get; set; }
    internal string LoadError { get; set; } = string.Empty;
    internal List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}
