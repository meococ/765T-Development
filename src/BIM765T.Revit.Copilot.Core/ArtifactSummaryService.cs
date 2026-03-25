using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ArtifactSummaryService
{
    public ArtifactSummaryResponse Summarize(ArtifactSummarizeRequest request)
    {
        request ??= new ArtifactSummarizeRequest();
        var path = request.ArtifactPath ?? string.Empty;
        var response = new ArtifactSummaryResponse
        {
            ArtifactPath = path
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            response.Summary = "Artifact not found.";
            return response;
        }

        var fileInfo = new FileInfo(path);
        response.Exists = true;
        response.SizeBytes = fileInfo.Length;
        response.DetectedFormat = DetectFormat(path);

        var maxLines = Math.Max(1, request.MaxLines);
        var maxChars = Math.Max(200, request.MaxChars);
        var lines = new List<string>();
        var totalLineCount = 0;
        using (var reader = new StreamReader(path))
        {
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine() ?? string.Empty;
                totalLineCount++;
                if (lines.Count < maxLines)
                {
                    lines.Add(line);
                }
            }
        }

        var preview = string.Join(Environment.NewLine, lines);
        if (preview.Length > maxChars)
        {
            preview = preview.Substring(0, maxChars);
        }

        response.LineCountEstimate = totalLineCount;
        response.PreviewText = preview;
        response.TopLevelKeys = ExtractTopLevelKeys(preview);
        response.Summary = BuildSummary(fileInfo, response);
        return response;
    }

    private static string DetectFormat(string path)
    {
        var extension = Path.GetExtension(path) ?? string.Empty;
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return "jsonl";
        }

        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            return "markdown";
        }

        return string.IsNullOrWhiteSpace(extension) ? "text" : extension.TrimStart('.').ToLowerInvariant();
    }

    private static List<string> ExtractTopLevelKeys(string preview)
    {
        var matches = Regex.Matches(preview, "\"([A-Za-z0-9_.$ -]+)\"\\s*:");
        return matches
            .Cast<Match>()
            .Select(x => x.Groups[1].Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static string BuildSummary(FileInfo fileInfo, ArtifactSummaryResponse response)
    {
        var parts = new List<string>
        {
            $"{response.DetectedFormat} artifact",
            $"{fileInfo.Length} bytes",
            $"{response.LineCountEstimate} lines"
        };

        if (response.TopLevelKeys.Count > 0)
        {
            parts.Add("keys=" + string.Join(", ", response.TopLevelKeys.Take(5)));
        }

        return string.Join(" | ", parts);
    }
}
