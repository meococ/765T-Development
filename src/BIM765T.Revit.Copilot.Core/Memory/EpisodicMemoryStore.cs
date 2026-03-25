using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core.Memory;

public sealed class EpisodicMemoryStore
{
    private readonly CopilotStatePaths _paths;
    private readonly object _gate = new object();

    public EpisodicMemoryStore(CopilotStatePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string RootPath => _paths.WorkerEpisodesPath;

    public EpisodicRecord Save(EpisodicRecord record)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.EpisodeId))
        {
            record.EpisodeId = Guid.NewGuid().ToString("N");
        }

        record.CreatedUtc = record.CreatedUtc == default ? DateTime.UtcNow : record.CreatedUtc;
        var path = _paths.GetWorkerEpisodePath(record.EpisodeId);
        var tempPath = path + ".tmp";
        lock (_gate)
        {
            File.WriteAllText(tempPath, JsonUtil.Serialize(record));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }

        return Clone(record);
    }

    public bool TryGet(string episodeId, out EpisodicRecord? record)
    {
        var path = _paths.GetWorkerEpisodePath(episodeId ?? string.Empty);
        if (!File.Exists(path))
        {
            record = null;
            return false;
        }

        record = JsonUtil.DeserializeRequired<EpisodicRecord>(File.ReadAllText(path));
        return true;
    }

    public IReadOnlyList<EpisodicRecord> Search(string query, string documentKey, string missionType, int maxResults = 5)
    {
        var terms = Tokenize(query);
        return Directory.Exists(_paths.WorkerEpisodesPath)
            ? Directory.GetFiles(_paths.WorkerEpisodesPath, "*.json", SearchOption.TopDirectoryOnly)
                .Select(file => SafeRead(file))
                .Where(x => x != null)
                .Select(x => new { Record = x!, Score = Score(x!, terms, documentKey, missionType) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Record.CreatedUtc)
                .Take(Math.Max(1, maxResults))
                .Select(x => Clone(x.Record))
                .ToList()
            : Array.Empty<EpisodicRecord>();
    }

    private static EpisodicRecord? SafeRead(string path)
    {
        try
        {
            return JsonUtil.DeserializeRequired<EpisodicRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static double Score(EpisodicRecord record, HashSet<string> queryTerms, string documentKey, string missionType)
    {
        var score = 0d;
        var haystack = string.Join(
            " ",
            record.MissionType,
            record.Outcome,
            string.Join(" ", record.KeyObservations),
            string.Join(" ", record.KeyDecisions),
            string.Join(" ", record.ToolSequence)).ToLowerInvariant();

        foreach (var term in queryTerms)
        {
            if (haystack.IndexOf(term, StringComparison.Ordinal) >= 0)
            {
                score += 1d;
            }
        }

        if (!string.IsNullOrWhiteSpace(documentKey)
            && string.Equals(record.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.75d;
        }

        if (!string.IsNullOrWhiteSpace(missionType)
            && string.Equals(record.MissionType, missionType, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.5d;
        }

        score += Math.Max(0d, 0.2d - (DateTime.UtcNow - record.CreatedUtc).TotalDays / 90d);
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

    private static EpisodicRecord Clone(EpisodicRecord record)
    {
        return new EpisodicRecord
        {
            EpisodeId = record.EpisodeId,
            RunId = record.RunId,
            MissionType = record.MissionType,
            Outcome = record.Outcome,
            KeyObservations = record.KeyObservations.ToList(),
            KeyDecisions = record.KeyDecisions.ToList(),
            ToolSequence = record.ToolSequence.ToList(),
            ArtifactRefs = record.ArtifactRefs.ToList(),
            DocumentKey = record.DocumentKey,
            CreatedUtc = record.CreatedUtc
        };
    }
}
