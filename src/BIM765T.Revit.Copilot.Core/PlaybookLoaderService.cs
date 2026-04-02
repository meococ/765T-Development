using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

/// <summary>
/// Loads playbook JSON files using product-owned resolution:
/// 1. %APPDATA%\BIM765T.Revit.Agent\playbooks\
/// 2. packs/playbooks/*/assets/ (repo)
/// 3. Built-in defaults (empty)
/// </summary>
public sealed class PlaybookLoaderService
{
    private readonly IReadOnlyDictionary<string, PlaybookDefinition> _playbooks;

    public PlaybookLoaderService()
        : this(LoadAll(null))
    {
    }

    public PlaybookLoaderService(IEnumerable<PlaybookDefinition> playbooks)
    {
        _playbooks = (playbooks ?? Array.Empty<PlaybookDefinition>())
            .Where(p => !string.IsNullOrWhiteSpace(p.PlaybookId))
            .GroupBy(p => p.PlaybookId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string playbookId, out PlaybookDefinition playbook)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            playbook = new PlaybookDefinition();
            return false;
        }

        return _playbooks.TryGetValue(playbookId, out playbook!);
    }

    public IReadOnlyList<PlaybookDefinition> GetAll()
    {
        return _playbooks.Values
            .OrderBy(p => p.PlaybookId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public PlaybookRecommendation Recommend(
        string taskDescription,
        string currentDocumentContext,
        IReadOnlyList<string> availableToolNames)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
        {
            return new PlaybookRecommendation { DontUseReason = "Task description is empty." };
        }

        var candidates = new List<ScoredPlaybook>();
        var taskLower = taskDescription.ToLowerInvariant();

        foreach (var playbook in _playbooks.Values)
        {
            var score = ScorePlaybook(playbook, taskLower, currentDocumentContext, availableToolNames);
            if (score > 0)
            {
                candidates.Add(new ScoredPlaybook { Playbook = playbook, Score = score });
            }
        }

        if (candidates.Count == 0)
        {
            return new PlaybookRecommendation { DontUseReason = "Khong tim thay playbook phu hop cho task nay." };
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = candidates[0];

        return new PlaybookRecommendation
        {
            PlaybookId = best.Playbook.PlaybookId,
            Description = best.Playbook.Description,
            Confidence = Math.Min(1.0, best.Score / 100.0),
            Steps = best.Playbook.Steps.Select(s => new PlaybookStepSummary
            {
                StepName = s.StepName,
                Tool = s.Tool,
                Purpose = s.Purpose,
                Condition = s.Condition
            }).ToList(),
            AlternativePlaybooks = candidates.Skip(1).Take(2)
                .Select(c => c.Playbook.PlaybookId).ToList(),
            PackId = best.Playbook.PackId,
            StandardsRefs = best.Playbook.StandardsRefs?.ToList() ?? new List<string>(),
            RequiredInputs = best.Playbook.RequiredInputs?.ToList() ?? new List<string>(),
            RecommendedSpecialists = best.Playbook.RecommendedSpecialists?.ToList() ?? new List<string>(),
            CapabilityDomain = best.Playbook.CapabilityDomain,
            DeterminismLevel = best.Playbook.DeterminismLevel,
            VerificationMode = best.Playbook.VerificationMode,
            SupportedDisciplines = best.Playbook.SupportedDisciplines?.ToList() ?? new List<string>(),
            IssueKinds = best.Playbook.IssueKinds?.ToList() ?? new List<string>(),
            PolicyPackIds = best.Playbook.PolicyPackIds?.ToList() ?? new List<string>()
        };
    }

    private static int ScorePlaybook(
        PlaybookDefinition playbook,
        string taskLower,
        string currentContext,
        IReadOnlyList<string> availableTools)
    {
        var score = 0;

        foreach (var phrase in playbook.TriggerPhrases ?? new List<string>())
        {
            if (ContainsKeywords(taskLower, phrase.ToLowerInvariant()))
            {
                score += 30;
            }
        }

        // Context match: required context must match current
        if (!string.IsNullOrWhiteSpace(playbook.RequiredContext)
            && !string.IsNullOrWhiteSpace(currentContext)
            && !string.Equals(playbook.RequiredContext, currentContext, StringComparison.OrdinalIgnoreCase))
        {
            return 0; // Hard filter
        }

        // DecisionGate.Use_When matching
        if (playbook.DecisionGate != null)
        {
            var useWhen = playbook.DecisionGate.UseWhen != null ? playbook.DecisionGate.UseWhen : new List<string>();
            foreach (var cond in useWhen)
            {
                if (ContainsKeywords(taskLower, cond.ToLowerInvariant()))
                {
                    score += 25;
                }
            }

            var dontUseWhen = playbook.DecisionGate.DontUseWhen != null ? playbook.DecisionGate.DontUseWhen : new List<string>();
            foreach (var cond in dontUseWhen)
            {
                if (ContainsKeywords(taskLower, cond.ToLowerInvariant()))
                {
                    score -= 30;
                }
            }
        }

        // Description keyword matching
        if (!string.IsNullOrWhiteSpace(playbook.Description))
        {
            var descWords = playbook.Description.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in descWords)
            {
                if (word.Length > 3 && taskLower.Contains(word))
                {
                    score += 5;
                }
            }
        }

        foreach (var issueKind in playbook.IssueKinds ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(issueKind) && taskLower.Contains(issueKind.Replace('_', ' ')))
            {
                score += 12;
            }
        }

        if (!string.IsNullOrWhiteSpace(playbook.CapabilityDomain) && taskLower.Contains(playbook.CapabilityDomain.Replace('_', ' ')))
        {
            score += 10;
        }

        // PlaybookId keyword matching
        var idParts = playbook.PlaybookId.ToLowerInvariant().Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in idParts)
        {
            if (part.Length > 2 && taskLower.Contains(part))
            {
                score += 10;
            }
        }

        // Tool availability: penalize if required tools not available
        if (availableTools != null && availableTools.Count > 0)
        {
            var available = new HashSet<string>(availableTools, StringComparer.OrdinalIgnoreCase);
            var steps = playbook.Steps != null ? playbook.Steps : new List<PlaybookStepDefinition>();
            foreach (var step in steps)
            {
                if (!string.IsNullOrWhiteSpace(step.Tool) && !available.Contains(step.Tool))
                {
                    score -= 10;
                }
            }
        }

        return Math.Max(0, score);
    }

    private static bool ContainsKeywords(string text, string condition)
    {
        // Split condition into keywords (3+ chars) and check if majority present
        var keywords = condition.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToList();

        if (keywords.Count == 0)
        {
            return false;
        }

        var matchCount = keywords.Count(k => text.Contains(k));
        return matchCount >= Math.Max(1, keywords.Count / 2);
    }

    public static IReadOnlyList<PlaybookDefinition> LoadAll(string? baseDirectory)
    {
        var results = new List<PlaybookDefinition>();
        foreach (var dir in ResolveCandidateDirectories(baseDirectory))
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var definition = JsonUtil.DeserializeRequired<PlaybookDefinition>(File.ReadAllText(file));
                    if (!string.IsNullOrWhiteSpace(definition.PlaybookId))
                    {
                        results.Add(definition);
                    }
                }
                catch
                {
                    // Skip malformed playbook files; guidance still works from overlay + manifests.
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> ResolveCandidateDirectories(string? baseDirectory)
    {
        var dirs = new List<string>();

        // Tier 1: %APPDATA%
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            BridgeConstants.AppDataFolderName,
            "playbooks");
        AddDistinct(dirs, appData);

        // Tier 2: repo packs
        var repoRoot = FindRepoRoot(baseDirectory ?? AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var packsRoot = Path.Combine(repoRoot, "packs", "playbooks");
            if (Directory.Exists(packsRoot))
            {
                foreach (var assetDir in Directory.GetDirectories(packsRoot, "assets", SearchOption.AllDirectories))
                {
                    AddDistinct(dirs, assetDir);
                }
            }
        }

        return dirs;
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

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
        {
            values.Add(value);
        }
    }

    private sealed class ScoredPlaybook
    {
        public PlaybookDefinition Playbook { get; set; } = new PlaybookDefinition();
        public int Score { get; set; }
    }
}
