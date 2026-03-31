using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.WorkerHost.Eventing;
using Microsoft.Extensions.Hosting;

namespace BIM765T.Revit.WorkerHost.Memory;

internal sealed class MarkdownMemoryBootstrapper : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly SqliteMissionEventStore _store;
    private readonly ISemanticMemoryClient _semanticClient;

    public MarkdownMemoryBootstrapper(IHostEnvironment environment, SqliteMissionEventStore store, ISemanticMemoryClient semanticClient)
    {
        _environment = environment;
        _store = store;
        _semanticClient = semanticClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var semanticBootstrapTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            semanticBootstrapTimeout.CancelAfter(TimeSpan.FromSeconds(1));
            await _semanticClient.EnsureReadyAsync(semanticBootstrapTimeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Ignore Qdrant bootstrap errors/timeouts; WorkerHost still works with lexical fallback.
        }

        foreach (var file in ResolveMarkdownMemoryFiles())
        {
            if (!File.Exists(file.Path))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(file.Path, cancellationToken).ConfigureAwait(false);
            foreach (var chunk in Chunk(content))
            {
                await _store.UpsertMemoryAsync(new PromotedMemoryRecord
                {
                    MemoryId = $"{file.Kind}:{ComputeDeterministicHash(file.Path + chunk.Title)}",
                    Kind = file.Kind,
                    NamespaceId = string.Equals(file.Kind, "lesson", StringComparison.OrdinalIgnoreCase)
                        ? MemoryNamespaces.EvidenceLessons
                        : MemoryNamespaces.PlaybooksPolicies,
                    Title = chunk.Title,
                    Snippet = chunk.Snippet,
                    SourceRef = file.Path,
                    DocumentKey = string.Empty,
                    EventType = "MemoryPromoted",
                    RunId = string.Empty,
                    Promoted = true,
                    PayloadJson = chunk.PayloadJson,
                    CreatedUtc = DateTime.UtcNow.ToString("O")
                }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private IEnumerable<(string Kind, string Path)> ResolveMarkdownMemoryFiles()
    {
        var current = new DirectoryInfo(_environment.ContentRootPath);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BIM765T.Revit.Agent.sln")))
            {
                yield return ("project_memory", Path.Combine(current.FullName, "docs", "agent", "PROJECT_MEMORY.md"));
                yield return ("lesson", Path.Combine(current.FullName, "docs", "agent", "LESSONS_LEARNED.md"));
                yield break;
            }

            current = current.Parent;
        }
    }

    private static string ComputeDeterministicHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static IEnumerable<(string Title, string Snippet, string PayloadJson)> Chunk(string markdown)
    {
        var sections = Regex.Split(markdown ?? string.Empty, @"(?=^##+\s+)", RegexOptions.Multiline);
        foreach (var section in sections.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var lines = section.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var title = lines.FirstOrDefault() ?? "memory";
            var snippet = string.Join(" ", lines.Skip(1).Take(4)).Trim();
            if (string.IsNullOrWhiteSpace(snippet))
            {
                snippet = title;
            }

            yield return (title, snippet, System.Text.Json.JsonSerializer.Serialize(new { title, snippet, section }));
        }
    }
}
