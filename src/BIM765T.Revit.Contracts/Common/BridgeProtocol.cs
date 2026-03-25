using System;

namespace BIM765T.Revit.Contracts.Common;

public static class BridgeProtocol
{
    public const string PipeV1 = "pipe/1";

    public static string NormalizeOrDefault(string? protocolVersion)
    {
        var value = protocolVersion;
        if (string.IsNullOrWhiteSpace(value))
        {
            return PipeV1;
        }

        return value?.Trim() ?? PipeV1;
    }

    public static bool IsSupported(string? protocolVersion)
    {
        return TryGetMajorVersion(protocolVersion, out var major) && major == 1;
    }

    public static bool TryGetMajorVersion(string? protocolVersion, out int major)
    {
        major = 0;
        var normalized = NormalizeOrDefault(protocolVersion);
        if (!normalized.StartsWith("pipe/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(normalized.Substring("pipe/".Length), out major);
    }
}
