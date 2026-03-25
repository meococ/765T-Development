using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

[DataContract]
public sealed class ToolGraphOverlayCatalog
{
    [DataMember(Order = 1)]
    public List<ToolGraphOverlayEntry> Entries { get; set; } = new List<ToolGraphOverlayEntry>();
}

[DataContract]
public sealed class ToolGraphOverlayEntry
{
    [DataMember(Order = 1)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> Prerequisites { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> FollowUps { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> AntiPatterns { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> TypicalChains { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> RecoveryHints { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> RecommendedTemplates { get; set; } = new List<string>();
}

public sealed class ToolGraphOverlayService
{
    private readonly IReadOnlyDictionary<string, ToolGraphOverlayEntry> _entries;

    public ToolGraphOverlayService()
        : this(LoadDefaultEntries())
    {
    }

    public ToolGraphOverlayService(IEnumerable<ToolGraphOverlayEntry> entries)
    {
        _entries = (entries ?? Array.Empty<ToolGraphOverlayEntry>())
            .Where(x => !string.IsNullOrWhiteSpace(x.ToolName))
            .GroupBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => Normalize(x.Last()), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string toolName, out ToolGraphOverlayEntry entry)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            entry = new ToolGraphOverlayEntry();
            return false;
        }

        return _entries.TryGetValue(toolName, out entry!);
    }

    public IReadOnlyList<ToolGraphOverlayEntry> GetAll()
    {
        return _entries.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ToolGraphOverlayService LoadDefault()
    {
        return new ToolGraphOverlayService(LoadDefaultEntries());
    }

    public static IReadOnlyList<ToolGraphOverlayEntry> LoadDefaultEntries()
    {
        foreach (var candidate in ResolveCandidatePaths(AppContext.BaseDirectory))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var catalog = JsonUtil.DeserializeRequired<ToolGraphOverlayCatalog>(File.ReadAllText(candidate));
                if (catalog.Entries.Count > 0)
                {
                    return catalog.Entries.Select(Normalize).ToList();
                }
            }
            catch
            {
                // Fail closed to empty overlay; guidance still works from live manifest.
            }
        }

        return Array.Empty<ToolGraphOverlayEntry>();
    }

    public static IReadOnlyList<string> ResolveCandidatePaths(string? baseDirectory)
    {
        var results = new List<string>();
        AddDistinct(results, Path.Combine(baseDirectory ?? string.Empty, "tool-intelligence", "TOOL_GRAPH.overlay.json"));

        var repoRoot = FindRepoRoot(baseDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            AddDistinct(results, Path.Combine(repoRoot, "docs", "agent", "skills", "tool-intelligence", "TOOL_GRAPH.overlay.json"));
        }

        return results;
    }

    private static string FindRepoRoot(string? baseDirectory)
    {
        var current = new DirectoryInfo(baseDirectory ?? string.Empty);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    private static ToolGraphOverlayEntry Normalize(ToolGraphOverlayEntry entry)
    {
        return new ToolGraphOverlayEntry
        {
            ToolName = (entry.ToolName ?? string.Empty).Trim(),
            Prerequisites = NormalizeList(entry.Prerequisites),
            FollowUps = NormalizeList(entry.FollowUps),
            AntiPatterns = NormalizeList(entry.AntiPatterns),
            TypicalChains = NormalizeList(entry.TypicalChains),
            RecoveryHints = NormalizeList(entry.RecoveryHints),
            RecommendedTemplates = NormalizeList(entry.RecommendedTemplates)
        };
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value))
        {
            values.Add(value);
        }
    }
}
