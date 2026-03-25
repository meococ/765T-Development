using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Config;

/// <summary>
/// Load settings.json từ %APPDATA%\BIM765T.Revit.Agent\.
///
/// P1-2 FIX: Không nuốt lỗi im lặng nữa.
/// - JSON lỗi → log warning + phát sinh DiagnosticRecord
/// - Default fallback vẫn giữ (không crash Revit) nhưng có cờ HasLoadError
/// - Caller (AgentHost) có thể kiểm tra và hiện warning cho user
/// </summary>
internal static class SettingsLoader
{
    internal static SettingsLoadResult Load()
    {
        var result = new SettingsLoadResult();
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, BridgeConstants.AppDataFolderName);
            var path = Path.Combine(dir, "settings.json");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtil.Serialize(result.Settings));
                return result;
            }

            result.Settings = JsonUtil.Deserialize<AgentSettings>(File.ReadAllText(path));
            return result;
        }
        catch (Exception ex)
        {
            result.HasLoadError = true;
            result.LoadError = ex.Message;
            result.Diagnostics.Add(DiagnosticRecord.Create(
                "CONFIG_SETTINGS_PARSE_ERROR",
                DiagnosticSeverity.Warning,
                $"settings.json parse failed — fallback về defaults. Lỗi: {ex.Message}"));
            return result;
        }
    }

    internal static bool TrySave(AgentSettings settings, out string error)
    {
        error = string.Empty;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, BridgeConstants.AppDataFolderName);
            var path = Path.Combine(dir, "settings.json");
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtil.Serialize(settings ?? new AgentSettings()));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>
/// Kết quả load settings — bao gồm error info nếu JSON bị lỗi.
/// AgentHost dùng HasLoadError + Diagnostics để log warning lúc startup.
/// </summary>
internal sealed class SettingsLoadResult
{
    internal AgentSettings Settings { get; set; } = new AgentSettings();
    internal bool HasLoadError { get; set; }
    internal string LoadError { get; set; } = string.Empty;
    internal List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
}
