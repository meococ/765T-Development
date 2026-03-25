using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class CuratedScriptRegistryService
{
    private static readonly HashSet<string> AllowedSourceKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CommandSourceKinds.Internal,
        CommandSourceKinds.Repo,
        CommandSourceKinds.PyRevit,
        CommandSourceKinds.DynamoOrchid,
        CommandSourceKinds.ApprovedVendor
    };

    public string RegistryRootPath { get; }

    public string ManifestRootPath { get; }

    public string ScriptLibraryRootPath { get; }

    public CuratedScriptRegistryService(string? rootPath = null)
    {
        RegistryRootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIM765T.Revit.Agent", "curated-scripts")
            : rootPath!;
        ManifestRootPath = Path.Combine(RegistryRootPath, "manifests");
        ScriptLibraryRootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIM765T.Revit.Agent", "scripts");
        Directory.CreateDirectory(ManifestRootPath);
        Directory.CreateDirectory(ScriptLibraryRootPath);
    }

    public IReadOnlyList<ScriptSourceManifest> LoadAll()
    {
        var results = new List<ScriptSourceManifest>();
        if (!Directory.Exists(ManifestRootPath))
        {
            return results;
        }

        foreach (var file in Directory.GetFiles(ManifestRootPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var manifest = JsonUtil.DeserializeRequired<ScriptSourceManifest>(File.ReadAllText(file));
                if (!string.IsNullOrWhiteSpace(manifest.ScriptId))
                {
                    results.Add(manifest);
                }
            }
            catch
            {
            }
        }

        return results
            .GroupBy(x => x.ScriptId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.ScriptId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ScriptSourceVerifyResponse Verify(ScriptSourceManifest? manifest)
    {
        manifest ??= new ScriptSourceManifest();
        var response = new ScriptSourceVerifyResponse();
        var normalizedSourceKind = (manifest.SourceKind ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(manifest.ScriptId))
        {
            response.Errors.Add("ScriptId is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedSourceKind) || !AllowedSourceKinds.Contains(normalizedSourceKind))
        {
            response.Errors.Add("SourceKind is not approved for the curated registry.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceRef))
        {
            response.Errors.Add("SourceRef is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            response.Errors.Add("EntryPoint is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ImportMode)
            || (!string.Equals(manifest.ImportMode, SourceImportModes.BehaviorOnly, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(manifest.ImportMode, SourceImportModes.WrapperOnly, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(manifest.ImportMode, SourceImportModes.CodeReuseAllowed, StringComparison.OrdinalIgnoreCase)))
        {
            response.Errors.Add("ImportMode must be behavior_only, wrapper_only, or code_reuse_allowed.");
        }

        if (string.Equals(manifest.SourceKind, CommandSourceKinds.PyRevit, StringComparison.OrdinalIgnoreCase)
            && string.Equals(manifest.ImportMode, SourceImportModes.CodeReuseAllowed, StringComparison.OrdinalIgnoreCase))
        {
            response.Errors.Add("pyRevit manifests may not be marked code_reuse_allowed for the core curated registry.");
        }

        if (!string.Equals(manifest.SafetyClass, CommandSafetyClasses.ReadOnly, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(manifest.SafetyClass, CommandSafetyClasses.HarmlessUi, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(manifest.VerificationRecipe))
        {
            response.Errors.Add("Mutation-capable scripts must declare a VerificationRecipe.");
        }

        if (manifest.ApprovalRequirement != ApprovalRequirement.None
            && string.IsNullOrWhiteSpace(manifest.VerificationRecipe))
        {
            response.Errors.Add("Scripts requiring approval must declare a VerificationRecipe.");
        }

        if (manifest.Approved && !HasExecutableWrapper(manifest))
        {
            response.Errors.Add("Approved scripts must resolve to a built-in or installed script wrapper.");
        }
        else if (!manifest.Approved)
        {
            response.Warnings.Add("Manifest is stored as mapped-only until it is explicitly approved.");
        }

        if (string.Equals(manifest.SourceKind, CommandSourceKinds.PyRevit, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.SourceKind, CommandSourceKinds.DynamoOrchid, StringComparison.OrdinalIgnoreCase))
        {
            response.Warnings.Add("External source wrappers stay fail-closed until an approved local wrapper is installed.");
        }

        if (string.Equals(manifest.ImportMode, SourceImportModes.BehaviorOnly, StringComparison.OrdinalIgnoreCase))
        {
            response.Warnings.Add("Behavior-only manifests document logic provenance but still require a local wrapper before execution.");
        }

        response.IsValid = response.Errors.Count == 0;
        response.Summary = response.IsValid
            ? $"Script manifest `{manifest.ScriptId}` is valid with {response.Warnings.Count} warning(s)."
            : $"Script manifest `{manifest.ScriptId}` is invalid with {response.Errors.Count} error(s).";
        return response;
    }

    public ScriptImportManifestResponse Import(ScriptImportManifestRequest? request)
    {
        request ??= new ScriptImportManifestRequest();
        var verification = Verify(request.Manifest);
        var response = new ScriptImportManifestResponse
        {
            Verification = verification
        };

        if (!verification.IsValid)
        {
            response.Imported = false;
            response.Summary = verification.Summary;
            return response;
        }

        var path = BuildManifestPath(request.Manifest.ScriptId);
        if (File.Exists(path) && !request.OverwriteExisting)
        {
            response.Imported = false;
            response.ManifestPath = path;
            response.Summary = "Manifest already exists. Set OverwriteExisting=true to replace it.";
            return response;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ManifestRootPath);
        File.WriteAllText(path, JsonUtil.Serialize(request.Manifest));

        response.Imported = true;
        response.ManifestPath = path;
        response.Summary = $"Imported curated script manifest `{request.Manifest.ScriptId}`.";
        return response;
    }

    public ScriptInstallPackResponse InstallPack(ScriptInstallPackRequest? request)
    {
        request ??= new ScriptInstallPackRequest();
        var response = new ScriptInstallPackResponse
        {
            PackId = string.IsNullOrWhiteSpace(request.PackId) ? "curated-pack" : request.PackId
        };

        foreach (var script in request.Scripts ?? new List<ScriptSourceManifest>())
        {
            var imported = Import(new ScriptImportManifestRequest
            {
                Manifest = script,
                OverwriteExisting = true
            });
            if (imported.Imported)
            {
                response.InstalledCount++;
                if (!string.IsNullOrWhiteSpace(imported.ManifestPath))
                {
                    response.ManifestPaths.Add(imported.ManifestPath);
                }
            }
        }

        var indexPath = Path.Combine(RegistryRootPath, "packs", SanitizeFileName(response.PackId) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? RegistryRootPath);
        File.WriteAllText(indexPath, JsonUtil.Serialize(new ScriptInstallPackRequest
        {
            WorkspaceId = request.WorkspaceId ?? string.Empty,
            PackId = response.PackId,
            Scripts = request.Scripts?.ToList() ?? new List<ScriptSourceManifest>()
        }));

        response.Summary = $"Installed {response.InstalledCount} curated script manifest(s) into pack `{response.PackId}`.";
        return response;
    }

    public string BuildManifestPath(string scriptId)
    {
        return Path.Combine(ManifestRootPath, SanitizeFileName(scriptId) + ".json");
    }

    public bool HasExecutableWrapper(ScriptSourceManifest? manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.ScriptId))
        {
            return false;
        }

        if (manifest.ScriptId.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var libraryPath = Path.Combine(ScriptLibraryRootPath, manifest.ScriptId + ".json");
        if (File.Exists(libraryPath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            if (Path.IsPathRooted(manifest.EntryPoint) && File.Exists(manifest.EntryPoint))
            {
                return true;
            }

            var combined = Path.Combine(ScriptLibraryRootPath, manifest.EntryPoint);
            if (File.Exists(combined))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
