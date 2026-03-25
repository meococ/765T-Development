using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Memory;

public sealed class SessionMemoryStore
{
    private const int MaxEntriesPerSession = 150;
    private readonly object _gate = new object();
    private readonly Dictionary<string, LinkedList<SessionMemoryEntry>> _entries = new Dictionary<string, LinkedList<SessionMemoryEntry>>(StringComparer.OrdinalIgnoreCase);

    public void Add(string sessionId, SessionMemoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || entry == null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(sessionId, out var list))
            {
                list = new LinkedList<SessionMemoryEntry>();
                _entries[sessionId] = list;
            }

            list.AddLast(entry);
            while (list.Count > MaxEntriesPerSession)
            {
                list.RemoveFirst();
            }
        }
    }

    public IReadOnlyList<SessionMemoryEntry> List(string sessionId, int maxResults = 50)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(sessionId ?? string.Empty, out var list))
            {
                return Array.Empty<SessionMemoryEntry>();
            }

            return list.Reverse().Take(Math.Max(1, maxResults)).Select(Clone).ToList();
        }
    }

    public IReadOnlyList<SessionMemoryEntry> Search(string sessionId, string query, string documentKey, int maxResults = 5)
    {
        var terms = Tokenize(query);
        lock (_gate)
        {
            if (!_entries.TryGetValue(sessionId ?? string.Empty, out var list))
            {
                return Array.Empty<SessionMemoryEntry>();
            }

            return list
                .Select(entry => new { Entry = entry, Score = Score(entry, terms, documentKey) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Entry.CreatedUtc)
                .Take(Math.Max(1, maxResults))
                .Select(x => Clone(x.Entry))
                .ToList();
        }
    }

    public void Clear(string sessionId)
    {
        lock (_gate)
        {
            _entries.Remove(sessionId ?? string.Empty);
        }
    }

    private static double Score(SessionMemoryEntry entry, HashSet<string> queryTerms, string documentKey)
    {
        var score = 0d;
        if (queryTerms.Count == 0)
        {
            score += 0.1d;
        }

        var haystack = string.Join(" ", entry.Kind, entry.ToolName, entry.Content, string.Join(" ", entry.Tags)).ToLowerInvariant();
        foreach (var term in queryTerms)
        {
            if (haystack.IndexOf(term, StringComparison.Ordinal) >= 0)
            {
                score += 1d;
            }
        }

        if (!string.IsNullOrWhiteSpace(documentKey)
            && string.Equals(entry.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.5d;
        }

        score += Math.Max(0d, 0.25d - (DateTime.UtcNow - entry.CreatedUtc).TotalHours / 168d);
        return score;
    }

    private static HashSet<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var source = value!;
        return new HashSet<string>(
            source.Split(new[] { ' ', ',', '.', ':', ';', '-', '_', '/', '\\', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length > 1)
                .Select(x => x.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    private static SessionMemoryEntry Clone(SessionMemoryEntry entry)
    {
        return new SessionMemoryEntry
        {
            EntryId = entry.EntryId,
            Kind = entry.Kind,
            Content = entry.Content,
            Tags = entry.Tags.ToList(),
            DocumentKey = entry.DocumentKey,
            ViewKey = entry.ViewKey,
            MissionId = entry.MissionId,
            ToolName = entry.ToolName,
            CreatedUtc = entry.CreatedUtc
        };
    }
}
