using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateScriptSourceVerify(ScriptSourceVerifyRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateScriptSourceManifest(request.Manifest, diagnostics);
    }

    private static void ValidateScriptImportManifest(ScriptImportManifestRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateScriptSourceManifest(request.Manifest, diagnostics);
    }

    private static void ValidateScriptInstallPack(ScriptInstallPackRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.PackId))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_PACK_ID_REQUIRED", DiagnosticSeverity.Error, "PackId khong duoc rong."));
        }

        RequireNonEmpty(request.Scripts, "SCRIPT_PACK_EMPTY", "Scripts khong duoc rong.", diagnostics);
        foreach (var manifest in request.Scripts ?? Enumerable.Empty<ScriptSourceManifest>())
        {
            ValidateScriptSourceManifest(manifest, diagnostics);
        }
    }

    private static void ValidateScriptSourceManifest(ScriptSourceManifest manifest, ICollection<DiagnosticRecord> diagnostics)
    {
        if (manifest == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_MANIFEST_REQUIRED", DiagnosticSeverity.Error, "Manifest khong duoc null."));
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.ScriptId))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_ID_REQUIRED", DiagnosticSeverity.Error, "ScriptId khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_DISPLAY_NAME_REQUIRED", DiagnosticSeverity.Error, "DisplayName khong duoc rong."));
        }

        if (!IsAllowedValue(
                manifest.SourceKind,
                CommandSourceKinds.Repo,
                CommandSourceKinds.Internal,
                CommandSourceKinds.PyRevit,
                CommandSourceKinds.DynamoOrchid,
                CommandSourceKinds.ApprovedVendor))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_SOURCE_KIND_INVALID", DiagnosticSeverity.Error, "SourceKind khong hop le."));
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceRef))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_SOURCE_REF_REQUIRED", DiagnosticSeverity.Error, "SourceRef khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(manifest.VerificationMode))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_VERIFICATION_MODE_REQUIRED", DiagnosticSeverity.Error, "VerificationMode khong duoc rong."));
        }

        if (!string.Equals(manifest.SafetyClass, CommandSafetyClasses.ReadOnly, System.StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(manifest.VerificationRecipe))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_VERIFICATION_RECIPE_REQUIRED", DiagnosticSeverity.Error, "Mutation-capable script phai co VerificationRecipe."));
        }

        if (!IsAllowedValue(
                manifest.ImportMode,
                SourceImportModes.BehaviorOnly,
                SourceImportModes.WrapperOnly,
                SourceImportModes.CodeReuseAllowed))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_IMPORT_MODE_INVALID", DiagnosticSeverity.Error, "ImportMode khong hop le."));
        }

        if (string.Equals(manifest.SourceKind, CommandSourceKinds.PyRevit, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(manifest.ImportMode, SourceImportModes.CodeReuseAllowed, System.StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_IMPORT_MODE_LICENSE_BLOCKED", DiagnosticSeverity.Error, "pyRevit chi duoc dung o mode behavior_only hoac wrapper_only cho core product."));
        }
    }
}
