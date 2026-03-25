using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

/// <summary>
/// Auto-updates PROJECT_MEMORY.md, LESSONS_LEARNED.md, and TOOL_GRAPH.overlay.json
/// after each phase build success. Idempotent — won't duplicate sections.
/// </summary>
public sealed class KnowledgeUpdateService
{
    private readonly string _docsAgentPath;

    public KnowledgeUpdateService(string? repoRootPath = null)
    {
        var repoRoot = !string.IsNullOrWhiteSpace(repoRootPath)
            ? repoRootPath!
            : FindRepoRoot(AppContext.BaseDirectory);
        _docsAgentPath = Path.Combine(repoRoot, "docs", "agent");
    }

    /// <summary>
    /// Append a new section to PROJECT_MEMORY.md if it doesn't already exist.
    /// Idempotent: checks for sectionKey in existing content before appending.
    /// </summary>
    public KnowledgeUpdateResult UpdateProjectMemory(string sectionKey, string markdownContent, string triggerPhase)
    {
        if (string.IsNullOrWhiteSpace(sectionKey) || string.IsNullOrWhiteSpace(markdownContent))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "Empty sectionKey or content." };
        }

        var filePath = Path.Combine(_docsAgentPath, "PROJECT_MEMORY.md");
        if (!File.Exists(filePath))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "PROJECT_MEMORY.md not found." };
        }

        var existing = File.ReadAllText(filePath);
        var sectionMarker = $"## {sectionKey}";

        if (existing.IndexOf(sectionMarker, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new KnowledgeUpdateResult
            {
                Updated = false,
                Reason = $"Section '{sectionKey}' already exists. Skipped (idempotent)."
            };
        }

        var newSection = $"\n\n{sectionMarker}\n\n" +
                         $"<!-- Auto-appended by KnowledgeUpdateService | Phase: {triggerPhase} | {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC -->\n\n" +
                         markdownContent.TrimEnd() + "\n";

        File.AppendAllText(filePath, newSection);

        return new KnowledgeUpdateResult
        {
            Updated = true,
            FilePath = filePath,
            SectionKey = sectionKey,
            Reason = $"Appended section '{sectionKey}' to PROJECT_MEMORY.md."
        };
    }

    /// <summary>
    /// Append a lesson to LESSONS_LEARNED.md.
    /// </summary>
    public KnowledgeUpdateResult AppendLesson(string problem, string rootCause, string fix, string prevention)
    {
        if (string.IsNullOrWhiteSpace(problem))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "Empty problem description." };
        }

        var filePath = Path.Combine(_docsAgentPath, "LESSONS_LEARNED.md");
        if (!File.Exists(filePath))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "LESSONS_LEARNED.md not found." };
        }

        var entry = $"\n\n### {DateTime.UtcNow:yyyy-MM-dd} — {problem.Trim()}\n\n" +
                    $"- **Root cause:** {rootCause?.Trim() ?? "N/A"}\n" +
                    $"- **Fix:** {fix?.Trim() ?? "N/A"}\n" +
                    $"- **Prevention:** {prevention?.Trim() ?? "N/A"}\n";

        File.AppendAllText(filePath, entry);

        return new KnowledgeUpdateResult
        {
            Updated = true,
            FilePath = filePath,
            SectionKey = problem.Trim(),
            Reason = "Appended lesson."
        };
    }

    /// <summary>
    /// Add or update a tool graph overlay entry.
    /// If the tool already exists in the overlay, skip (idempotent).
    /// </summary>
    public KnowledgeUpdateResult UpdateToolGraphOverlay(ToolGraphOverlayEntry newEntry)
    {
        if (newEntry == null || string.IsNullOrWhiteSpace(newEntry.ToolName))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "Empty tool entry." };
        }

        var overlayPath = Path.Combine(_docsAgentPath, "skills", "tool-intelligence", "TOOL_GRAPH.overlay.json");
        if (!File.Exists(overlayPath))
        {
            return new KnowledgeUpdateResult { Updated = false, Reason = "TOOL_GRAPH.overlay.json not found." };
        }

        try
        {
            var catalog = JsonUtil.DeserializeRequired<ToolGraphOverlayCatalog>(File.ReadAllText(overlayPath));

            if (catalog.Entries.Any(e => string.Equals(e.ToolName, newEntry.ToolName, StringComparison.OrdinalIgnoreCase)))
            {
                return new KnowledgeUpdateResult
                {
                    Updated = false,
                    Reason = $"Tool '{newEntry.ToolName}' already in overlay. Skipped (idempotent)."
                };
            }

            catalog.Entries.Add(newEntry);
            File.WriteAllText(overlayPath, JsonUtil.Serialize(catalog));

            return new KnowledgeUpdateResult
            {
                Updated = true,
                FilePath = overlayPath,
                SectionKey = newEntry.ToolName,
                Reason = $"Added tool '{newEntry.ToolName}' to overlay."
            };
        }
        catch (Exception ex)
        {
            return new KnowledgeUpdateResult
            {
                Updated = false,
                Reason = $"Failed to update overlay: {ex.Message}"
            };
        }
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
}
