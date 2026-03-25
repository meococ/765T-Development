using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class ApprovalTokenStore
{
    private static readonly byte[] ProtectedHeader = Encoding.ASCII.GetBytes("BIM765T_APPR_DPAPI_V1\n");
    private readonly string _path;
    private readonly IAgentLogger _logger;
    private readonly object _gate = new object();

    internal ApprovalTokenStore(IAgentLogger logger, string? storageDirectoryOverride = null)
    {
        _logger = logger;
        var dir = !string.IsNullOrWhiteSpace(storageDirectoryOverride)
            ? storageDirectoryOverride
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BridgeConstants.AppDataFolderName);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "pending-approvals.dat");
    }

    internal IReadOnlyList<PersistedApprovalRecord> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return Array.Empty<PersistedApprovalRecord>();
                }

                var bytes = File.ReadAllBytes(_path);
                if (bytes.Length == 0)
                {
                    return Array.Empty<PersistedApprovalRecord>();
                }

                if (!TryReadProtectedJson(bytes, out var protectedJson))
                {
                    _logger.Warn("[APPROVAL_STORE] Ignoring legacy or unprotected approval token cache on disk.");
                    return Array.Empty<PersistedApprovalRecord>();
                }

                var json = protectedJson;

                var envelope = JsonUtil.DeserializeOrDefault<PersistedApprovalEnvelope>(json);
                return (envelope.Records ?? new List<PersistedApprovalRecord>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error("[APPROVAL_STORE] Failed to load persisted approval tokens. Falling back to empty store.", ex);
                return Array.Empty<PersistedApprovalRecord>();
            }
        }
    }

    internal void Save(IEnumerable<PersistedApprovalRecord> records)
    {
        lock (_gate)
        {
            try
            {
                var envelope = new PersistedApprovalEnvelope
                {
                    Records = records
                        .Where(x => !string.IsNullOrWhiteSpace(x.Token))
                        .OrderBy(x => x.ExpiresUtc)
                        .ToList()
                };

                var json = JsonUtil.Serialize(envelope);
                var payloadBytes = BuildPersistedPayload(Encoding.UTF8.GetBytes(json));
                if (payloadBytes == null || payloadBytes.Length == 0)
                {
                    _logger.Warn("[APPROVAL_STORE] Approval token cache was not persisted because DPAPI protection is unavailable.");
                    return;
                }

                var tempPath = _path + ".tmp";
                File.WriteAllBytes(tempPath, payloadBytes);
                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, null);
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("[APPROVAL_STORE] Failed to persist approval tokens.", ex);
            }
        }
    }

    private byte[]? BuildPersistedPayload(byte[] jsonBytes)
    {
        try
        {
            var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
            var output = new byte[ProtectedHeader.Length + protectedBytes.Length];
            Buffer.BlockCopy(ProtectedHeader, 0, output, 0, ProtectedHeader.Length);
            Buffer.BlockCopy(protectedBytes, 0, output, ProtectedHeader.Length, protectedBytes.Length);
            return output;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool TryReadProtectedJson(byte[] bytes, out string json)
    {
        json = string.Empty;
        if (bytes.Length <= ProtectedHeader.Length)
        {
            return false;
        }

        for (var index = 0; index < ProtectedHeader.Length; index++)
        {
            if (bytes[index] != ProtectedHeader[index])
            {
                return false;
            }
        }

        try
        {
            var protectedBytes = new byte[bytes.Length - ProtectedHeader.Length];
            Buffer.BlockCopy(bytes, ProtectedHeader.Length, protectedBytes, 0, protectedBytes.Length);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            json = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

[DataContract]
internal sealed class PersistedApprovalEnvelope
{
    [DataMember(Order = 1)]
    public List<PersistedApprovalRecord> Records { get; set; } = new List<PersistedApprovalRecord>();
}

[DataContract]
internal sealed class PersistedApprovalRecord
{
    [DataMember(Order = 1)]
    public string Token { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RequestFingerprint { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SelectionHash { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string Caller { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public DateTime ExpiresUtc { get; set; }
}
