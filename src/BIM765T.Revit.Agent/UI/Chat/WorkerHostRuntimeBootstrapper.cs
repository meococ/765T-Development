using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.UI.Chat;

internal static class WorkerHostRuntimeBootstrapper
{
    private static readonly SemaphoreSlim StartupGate = new SemaphoreSlim(1, 1);
    private static readonly HttpClient ProbeClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private static readonly HttpClient StatusProbeClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static DateTime _lastStartAttemptUtc = DateTime.MinValue;

    private static string AssemblyDirectory => Path.GetDirectoryName(typeof(WorkerHostRuntimeBootstrapper).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;

    internal static async Task EnsureAvailableAsync(Uri? baseAddress, CancellationToken cancellationToken)
    {
        if (!SupportsAutoStart(baseAddress))
        {
            return;
        }

        if (await IsAvailableAsync(baseAddress!, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await StartupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await IsAvailableAsync(baseAddress!, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now - _lastStartAttemptUtc > TimeSpan.FromSeconds(4))
            {
                var workerHostExe = ResolveWorkerHostExecutablePath();
                StartHidden(workerHostExe);
                _lastStartAttemptUtc = now;
            }

            if (!await WaitUntilAvailableAsync(baseAddress!, cancellationToken).ConfigureAwait(false))
            {
                var status = await TryGetGatewayStatusAsync(baseAddress!, cancellationToken).ConfigureAwait(false);
                throw BuildUnavailableException(baseAddress!, status, null);
            }
        }
        finally
        {
            StartupGate.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "ReadAsStringAsync on the add-in/test target does not expose a CancellationToken overload.")]
    internal static async Task<WorkerHostGatewayStatus?> TryGetGatewayStatusAsync(Uri? baseAddress, CancellationToken cancellationToken)
    {
        if (baseAddress == null)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/api/external-ai/status"));
            using var response = await StatusProbeClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return TryParseGatewayStatus(json);
        }
        catch
        {
            return null;
        }
    }

    private static WorkerHostGatewayStatus TryParseGatewayStatus(string json)
    {
        try
        {
            return JsonUtil.DeserializeRequired<WorkerHostGatewayStatus>(json);
        }
        catch
        {
            return new WorkerHostGatewayStatus
            {
                SupportsTaskRuntime = ExtractJsonBool(json, "supportsTaskRuntime"),
                ConfiguredProvider = ExtractJsonString(json, "configuredProvider"),
                PlannerModel = ExtractJsonString(json, "plannerModel"),
                ResponseModel = ExtractJsonString(json, "responseModel"),
                ReasoningMode = ExtractJsonString(json, "reasoningMode"),
                SecretSourceKind = ExtractJsonString(json, "secretSourceKind"),
                Health = new WorkerHostRuntimeReadiness
                {
                    Ready = ExtractJsonBool(json, "ready"),
                    StandaloneChatReady = ExtractJsonBool(json, "standaloneChatReady"),
                    LiveRevitReady = ExtractJsonBool(json, "liveRevitReady"),
                    Degraded = ExtractJsonBool(json, "degraded"),
                    ReadinessSummary = ExtractJsonString(json, "readinessSummary"),
                    RuntimeTopology = ExtractJsonString(json, "runtimeTopology")
                }
            };
        }
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        var pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"";
        var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Regex.Unescape(match.Groups["value"].Value) : string.Empty;
    }

    private static bool ExtractJsonBool(string json, string propertyName)
    {
        var pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(?<value>true|false)";
        var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && bool.TryParse(match.Groups["value"].Value, out var value) && value;
    }

    private static bool SupportsAutoStart(Uri? baseAddress)
    {
        if (baseAddress == null)
        {
            return false;
        }

        return baseAddress.IsLoopback
            || string.Equals(baseAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(baseAddress.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitUntilAvailableAsync(Uri baseAddress, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsAvailableAsync(baseAddress, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> IsAvailableAsync(Uri baseAddress, CancellationToken cancellationToken)
    {
        var status = await TryGetGatewayStatusAsync(baseAddress, cancellationToken).ConfigureAwait(false);
        if (status?.Health.StandaloneChatReady == true)
        {
            return true;
        }

        return await IsRootAvailableAsync(baseAddress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsRootAvailableAsync(Uri baseAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/"));
            using var response = await ProbeClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void StartHidden(string workerHostExe)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerHostExe,
            WorkingDirectory = Path.GetDirectoryName(workerHostExe) ?? AssemblyDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Khong the khoi dong BIM765T.Revit.WorkerHost.");
        }
    }

    private static string ResolveWorkerHostExecutablePath()
    {
        foreach (var candidate in GetWorkerHostCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "Khong tim thay BIM765T.Revit.WorkerHost.exe de auto-start. Hay build WorkerHost truoc hoac cung cap BIM765T_WORKERHOST_EXE.");
    }

    private static IEnumerable<string> GetWorkerHostCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BIM765T_WORKERHOST_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv.Trim();
        }

        yield return Path.Combine(AssemblyDirectory, "BIM765T.Revit.WorkerHost.exe");

        foreach (var candidate in EnumerateRepoCandidates(AssemblyDirectory, "Release"))
        {
            yield return candidate;
        }

        foreach (var candidate in EnumerateRepoCandidates(Environment.CurrentDirectory, "Release"))
        {
            yield return candidate;
        }

        var deployMetadataPath = Path.Combine(AssemblyDirectory, "deploy-metadata.json");
        if (File.Exists(deployMetadataPath))
        {
            DeployMetadata? metadata = null;
            try
            {
                metadata = JsonUtil.DeserializeRequired<DeployMetadata>(File.ReadAllText(deployMetadataPath));
            }
            catch
            {
                metadata = null;
            }

            if (metadata != null)
            {
                var configuration = string.IsNullOrWhiteSpace(metadata.Configuration) ? "Release" : metadata.Configuration.Trim();
                var sourceDirectory = FirstNonEmpty(metadata.SourceDirectory, Path.GetDirectoryName(metadata.SourceAssemblyPath));
                foreach (var candidate in EnumerateRepoCandidates(sourceDirectory, configuration))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRepoCandidates(string? sourceDirectory, string configuration)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            yield break;
        }

        var repoRoot = FindRepoRoot(sourceDirectory!);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            yield return Path.Combine(repoRoot!, "src", "BIM765T.Revit.WorkerHost", "bin", configuration, "net8.0", "BIM765T.Revit.WorkerHost.exe");
            yield return Path.Combine(repoRoot!, "src", "BIM765T.Revit.WorkerHost", "bin", "Release", "net8.0", "BIM765T.Revit.WorkerHost.exe");
            yield return Path.Combine(repoRoot!, "src", "BIM765T.Revit.WorkerHost", "bin", "Debug", "net8.0", "BIM765T.Revit.WorkerHost.exe");
        }

        var workerHostBin = Path.Combine(sourceDirectory!, "..", "..", "..", "BIM765T.Revit.WorkerHost", "bin", configuration, "net8.0", "BIM765T.Revit.WorkerHost.exe");
        yield return Path.GetFullPath(workerHostBin);
    }

    private static string? FindRepoRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BIM765T.Revit.Agent.sln"))
                || File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static InvalidOperationException BuildUnavailableException(Uri baseAddress, WorkerHostGatewayStatus? status, Exception? probeException)
    {
        var detail = status?.Health.ReadinessSummary;
        if (string.IsNullOrWhiteSpace(detail) && probeException != null)
        {
            detail = probeException.Message;
        }

        if (status?.Health.StandaloneChatReady == true && status.Health.LiveRevitReady == false)
        {
            return new InvalidOperationException(
                "WorkerHost da san sang cho standalone chat nhung kernel Revit chua available. Em van co the tra loi conversational, con thao tac live Revit thi can mo Revit va project."
                + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " Chi tiet: " + detail));
        }

        var builder = new StringBuilder();
        builder.Append("AI runtime dang tat hoac chua san sang. Em da thu tu khoi dong WorkerHost nhung van chua ket noi duoc.");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.Append(" Chi tiet: ");
            builder.Append(detail);
        }

        builder.Append(" WorkerHost expected at ");
        builder.Append(baseAddress);
        builder.Append('.');
        return new InvalidOperationException(builder.ToString());
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    [DataContract]
    private sealed class DeployMetadata
    {
        [DataMember(Order = 1)]
        public string SourceAssemblyPath { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string SourceDirectory { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string InstalledAtUtc { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public string RevitYear { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string Configuration { get; set; } = string.Empty;
    }
}
