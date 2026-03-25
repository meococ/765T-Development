using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIM765T.Revit.Bridge;

internal static class CliFileInputGuard
{
    private static readonly string[] AllowedExtensions = { ".json", ".jsonc", ".txt" };
    private const string UnsafeFileBypassEnvVar = "BIM765T_BRIDGE_ALLOW_UNSAFE_FILE_INPUT";
    private const string ExtraRootsEnvVar = "BIM765T_BRIDGE_ALLOWED_INPUT_ROOTS";

    internal static string ResolveJsonOrFile(string? raw, string optionName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        if (!File.Exists(raw))
        {
            return raw;
        }

        return File.ReadAllText(ValidateAndNormalizePath(raw, optionName));
    }

    internal static string ValidateAndNormalizePath(string rawPath, string optionName)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException($"{optionName} is empty.");
        }

        var fullPath = Path.GetFullPath(rawPath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"{optionName} file not found: {fullPath}");
        }

        if (string.Equals(Environment.GetEnvironmentVariable(UnsafeFileBypassEnvVar), "1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{UnsafeFileBypassEnvVar}=1 is no longer supported because it bypasses file input safety checks. Use inline JSON or extend {ExtraRootsEnvVar} with a trusted root.");
        }

        var extension = Path.GetExtension(fullPath);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{optionName} only accepts {string.Join(", ", AllowedExtensions)} files. Pass inline JSON for other content.");
        }

        if (!IsUnderAllowedRoot(fullPath))
        {
            throw new InvalidOperationException(
                $"{optionName} file path is outside allowed roots. Pass inline JSON, place the file under the current directory/temp/AppData, or extend {ExtraRootsEnvVar}.");
        }

        return fullPath;
    }

    private static bool IsUnderAllowedRoot(string fullPath)
    {
        foreach (var root in GetAllowedRoots())
        {
            if (IsPathUnderRoot(fullPath, root))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetAllowedRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfPresent(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var normalized = Path.GetFullPath(path);
                if (Directory.Exists(normalized))
                {
                    roots.Add(NormalizeForComparison(normalized));
                }
            }
            catch
            {
                // Ignore malformed environment entries.
            }
        }

        AddIfPresent(Directory.GetCurrentDirectory());
        AddIfPresent(Path.GetTempPath());
        AddIfPresent(AppContext.BaseDirectory);
        AddIfPresent(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        var extraRoots = Environment.GetEnvironmentVariable(ExtraRootsEnvVar);
        if (!string.IsNullOrWhiteSpace(extraRoots))
        {
            foreach (var root in extraRoots.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                AddIfPresent(root.Trim());
            }
        }

        return roots;
    }

    private static bool IsPathUnderRoot(string fullPath, string root)
    {
        var normalizedPath = NormalizeForComparison(Path.GetDirectoryName(fullPath) ?? fullPath);
        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeForComparison(fullPath), root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForComparison(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var normalized = path.Normalize(NormalizationForm.FormKC);
        normalized = normalized.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || normalized.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;

        return normalized;
    }
}
