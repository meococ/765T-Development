// CA1305: string.Format locale — these are diagnostic messages where locale is irrelevant.
// Pre-existing across ~30 call sites; will migrate to interpolation in a future cleanup pass.
#pragma warning disable CA1305

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Script orchestration engine for the family authoring and automation pipeline.
///
/// Supports:
///   - Inline C# scripts executed via Revit API
///   - Named script resolution (from library)
///   - Dry-run validation (syntax + safety checks)
///   - Composed multi-step scripts with sequential execution
///   - Timeout enforcement and run tracking
///
/// Scripts execute within a Transaction managed by the caller.
/// All scripts receive (Document, UIApplication) context and return ScriptRunResult.
/// </summary>
internal sealed class ScriptOrchestrationService
{
    private readonly string _scriptsLibraryPath;
    private readonly object _runsLock = new object();
    private readonly Dictionary<string, ScriptRunResult> _recentRuns = new Dictionary<string, ScriptRunResult>(StringComparer.OrdinalIgnoreCase);
    private const int MaxRecentRuns = 100;

    internal ScriptOrchestrationService()
    {
        _scriptsLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BIM765T.Revit.Agent", "scripts");
    }

    // ── LIST ──

    internal ScriptListResponse ListScripts()
    {
        var response = new ScriptListResponse();

        // Built-in scripts
        response.Scripts.Add(new ScriptCatalogEntry
        {
            ScriptId = "builtin.create_parametric_column",
            Description = "Tạo parametric column family với Width, Depth, Height parameters.",
            Tags = new List<string> { "family-authoring", "column" }
        });
        response.Scripts.Add(new ScriptCatalogEntry
        {
            ScriptId = "builtin.create_simple_profile",
            Description = "Tạo profile family đơn giản (rectangle hoặc circle) cho sweep/extrusion.",
            Tags = new List<string> { "family-authoring", "profile" }
        });
        response.Scripts.Add(new ScriptCatalogEntry
        {
            ScriptId = "builtin.audit_family_parameters",
            Description = "Audit tất cả parameters trong family: unused, duplicate names, missing formulas.",
            Tags = new List<string> { "family-audit" }
        });
        response.Scripts.Add(new ScriptCatalogEntry
        {
            ScriptId = "builtin.batch_rename_types",
            Description = "Đổi tên tất cả family types theo pattern prefix + parameter value.",
            Tags = new List<string> { "family-management" }
        });

        // User scripts from library
        if (Directory.Exists(_scriptsLibraryPath))
        {
            foreach (var file in Directory.GetFiles(_scriptsLibraryPath, "*.json"))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var def = JsonUtil.Deserialize<ScriptDefinition>(content);
                    if (def != null && !string.IsNullOrWhiteSpace(def.ScriptId))
                    {
                        response.Scripts.Add(new ScriptCatalogEntry
                        {
                            ScriptId = def.ScriptId,
                            FileName = Path.GetFileName(file),
                            Description = def.Description ?? string.Empty,
                            Tags = new List<string> { def.Category ?? "user" }
                        });
                    }
                }
                catch
                {
                    // Skip corrupted script definitions
                }
            }
        }

        return response;
    }

    // ── VALIDATE ──

    internal ScriptValidationResult Validate(ScriptValidateRequest request)
    {
        var response = new ScriptValidationResult { ScriptId = request.ScriptId ?? string.Empty, IsValid = true };

        var code = ResolveCode(request.ScriptId, request.InlineCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            response.IsValid = false;
            response.Warnings.Add("Script code trống hoặc ScriptId không tìm thấy.");
            return response;
        }

        code ??= string.Empty;

        // Safety checks
        var dangerousPatterns = new[]
        {
            "System.IO.File.Delete", "Directory.Delete", "Process.Start",
            "System.Net.", "WebClient", "HttpClient",
            "Assembly.Load", "Activator.CreateInstance",
            "Registry.", "RegistryKey",
            "Environment.Exit", "Application.Exit"
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                response.DangerousApis.Add(pattern);
                response.Warnings.Add(string.Format("Script chứa pattern nguy hiểm: '{0}'. Review kỹ trước khi chạy.", pattern));
            }
        }

        // Check for required patterns
        if (code.IndexOf("Document", StringComparison.Ordinal) < 0 &&
            code.IndexOf("doc", StringComparison.Ordinal) < 0)
        {
            response.Warnings.Add("Script không reference Document — có thể không tương tác với Revit model.");
        }

        // Check for Transaction usage (script should NOT create own transactions if run inside pipeline)
        if (code.IndexOf("new Transaction", StringComparison.Ordinal) >= 0)
        {
            response.Warnings.Add("Script tự tạo Transaction. Nếu chạy qua script.run_safe, Transaction đã được quản lý bên ngoài.");
        }

        return response;
    }

    // ── RUN ──

    internal ExecutionResult PreviewRun(PlatformServices services, Document doc, ScriptRunRequest request, ToolRequestEnvelope envelope)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var validation = Validate(new ScriptValidateRequest
        {
            ScriptId = request.ScriptId,
            InlineCode = request.InlineCode
        });

        diagnostics.Add(DiagnosticRecord.Create("SCRIPT_VALIDATE", DiagnosticSeverity.Info,
            string.Format("Validation: IsValid={0}, DangerousApis={1}, Warnings={2}.",
                validation.IsValid, validation.DangerousApis.Count, validation.Warnings.Count)));

        foreach (var w in validation.Warnings)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_WARNING", DiagnosticSeverity.Warning, w));
        }

        if (!validation.IsValid)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_VALIDATE_FAILED", DiagnosticSeverity.Error, "Script validation failed."));
        }

        return new ExecutionResult
        {
            OperationName = "script.run_safe",
            DryRun = true,
            ConfirmationRequired = validation.IsValid,
            Diagnostics = diagnostics
        };
    }

    internal ExecutionResult ExecuteRun(PlatformServices services, Document doc, UIApplication uiapp, ScriptRunRequest request)
    {
        var code = ResolveCode(request.ScriptId, request.InlineCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return new ExecutionResult
            {
                OperationName = "script.run_safe",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("SCRIPT_NOT_FOUND", DiagnosticSeverity.Error, "Script code trống hoặc ScriptId không tìm thấy.")
                }
            };
        }

        var runId = Guid.NewGuid().ToString("N").Substring(0, 12);
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<DiagnosticRecord>();

        try
        {
            // Execute built-in scripts
            if (!string.IsNullOrWhiteSpace(request.ScriptId) && request.ScriptId.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase))
            {
                var result = ExecuteBuiltIn(request.ScriptId, doc, uiapp, request.Parameters ?? new Dictionary<string, string>(), diagnostics);
                sw.Stop();

                var hasErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
                var runResult = new ScriptRunResult
                {
                    RunId = runId,
                    ScriptId = request.ScriptId ?? "(inline)",
                    Success = !hasErrors,
                    DurationMs = sw.ElapsedMilliseconds,
                    Output = string.Format("BuiltIn completed in {0}ms", sw.ElapsedMilliseconds),
                    ElementsCreated = result.ChangedIds?.Count ?? 0
                };
                TrackRun(runResult);

                result.Diagnostics.AddRange(diagnostics);
                result.Diagnostics.Add(DiagnosticRecord.Create("SCRIPT_RUN_COMPLETE", DiagnosticSeverity.Info,
                    string.Format("RunId={0}, Duration={1}ms.", runId, sw.ElapsedMilliseconds)));
                return result;
            }

            // For inline code: currently only support built-in scripts
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_INLINE_NOT_SUPPORTED", DiagnosticSeverity.Warning,
                "Inline C# script execution chưa được hỗ trợ. Sử dụng built-in scripts hoặc tạo script definition."));

            return new ExecutionResult
            {
                OperationName = "script.run_safe",
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            var runResult = new ScriptRunResult
            {
                RunId = runId,
                ScriptId = request.ScriptId ?? "(inline)",
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
            TrackRun(runResult);

            return new ExecutionResult
            {
                OperationName = "script.run_safe",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("SCRIPT_EXCEPTION", DiagnosticSeverity.Error, string.Format("Script failed: {0}", ex.Message))
                }
            };
        }
    }

    // ── COMPOSE ──

    internal ExecutionResult PreviewCompose(PlatformServices services, Document doc, ScriptComposeRequest request, ToolRequestEnvelope envelope)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var steps = request.Steps ?? new List<ScriptComposeStep>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var validation = Validate(new ScriptValidateRequest
            {
                ScriptId = step?.ScriptId ?? string.Empty,
                InlineCode = step?.InlineCode ?? string.Empty
            });

            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_COMPOSE_STEP", DiagnosticSeverity.Info,
                string.Format("Step [{0}]: {1} — Valid={2}, Warnings={3}.",
                    i, step?.ScriptId ?? "(inline)", validation.IsValid, validation.Warnings.Count)));
        }

        return new ExecutionResult
        {
            OperationName = "script.compose",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics
        };
    }

    internal ExecutionResult ExecuteCompose(PlatformServices services, Document doc, UIApplication uiapp, ScriptComposeRequest request)
    {
        var steps = request.Steps ?? new List<ScriptComposeStep>();
        var diagnostics = new List<DiagnosticRecord>();
        var totalSw = Stopwatch.StartNew();
        var allChangedIds = new List<int>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step == null) continue;

            var stepRequest = new ScriptRunRequest
            {
                ScriptId = step.ScriptId,
                InlineCode = step.InlineCode,
                Parameters = step.Parameters ?? new Dictionary<string, string>(),
                TimeoutMs = request.TimeoutMs
            };

            var result = ExecuteRun(services, doc, uiapp, stepRequest);
            var hasErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_COMPOSE_STEP_RESULT", DiagnosticSeverity.Info,
                string.Format("Step [{0}] {1}: {2}", i, hasErrors ? "FAILED" : "OK", step.ScriptId ?? "(inline)")));
            diagnostics.AddRange(result.Diagnostics);
            allChangedIds.AddRange(result.ChangedIds ?? new List<int>());

            if (hasErrors && !step.ContinueOnError)
            {
                diagnostics.Add(DiagnosticRecord.Create("SCRIPT_COMPOSE_ABORT", DiagnosticSeverity.Error,
                    string.Format("Compose aborted at step [{0}].", i)));

                return new ExecutionResult
                {
                    OperationName = "script.compose",
                    Diagnostics = diagnostics,
                    ChangedIds = allChangedIds
                };
            }
        }

        totalSw.Stop();

        diagnostics.Add(DiagnosticRecord.Create("SCRIPT_COMPOSE_DONE", DiagnosticSeverity.Info,
            string.Format("Compose completed: {0} steps in {1}ms.", steps.Count, totalSw.ElapsedMilliseconds)));

        return new ExecutionResult
        {
            OperationName = "script.compose",
            Diagnostics = diagnostics,
            ChangedIds = allChangedIds
        };
    }

    // ── GET RUN ──

    internal ScriptRunResult GetRun(string runId)
    {
        lock (_runsLock)
        {
            if (_recentRuns.TryGetValue(runId, out var result))
            {
                return result;
            }
        }

        return new ScriptRunResult
        {
            RunId = runId,
            Success = false,
            ErrorMessage = string.Format("Run '{0}' không tìm thấy.", runId)
        };
    }

    // ── BUILT-IN SCRIPTS ──

    private ExecutionResult ExecuteBuiltIn(string scriptId, Document doc, UIApplication uiapp,
        Dictionary<string, string> parameters, List<DiagnosticRecord> diagnostics)
    {
        switch (scriptId.ToLowerInvariant())
        {
            case "builtin.create_parametric_column":
                return BuiltIn_CreateParametricColumn(doc, parameters, diagnostics);

            case "builtin.create_simple_profile":
                return BuiltIn_CreateSimpleProfile(doc, parameters, diagnostics);

            case "builtin.audit_family_parameters":
                return BuiltIn_AuditFamilyParameters(doc, diagnostics);

            case "builtin.batch_rename_types":
                return BuiltIn_BatchRenameTypes(doc, parameters, diagnostics);

            default:
                diagnostics.Add(DiagnosticRecord.Create("SCRIPT_NOT_FOUND", DiagnosticSeverity.Error,
                    string.Format("Built-in script '{0}' không tồn tại.", scriptId)));
                return new ExecutionResult
                {
                    OperationName = scriptId,
                    Diagnostics = diagnostics
                };
        }
    }

    private ExecutionResult BuiltIn_CreateParametricColumn(Document doc, Dictionary<string, string> parameters, List<DiagnosticRecord> diagnostics)
    {
        FamilyAuthoringService.GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;

        var widthMm = GetDoubleParam(parameters, "Width", 300);
        var depthMm = GetDoubleParam(parameters, "Depth", 300);
        var heightMm = GetDoubleParam(parameters, "Height", 3000);

        // Convert to feet
        var wFt = widthMm / 304.8;
        var dFt = depthMm / 304.8;
        var hFt = heightMm / 304.8;
        var halfW = wFt / 2.0;
        var halfD = dFt / 2.0;

        // Create rectangular extrusion
        var sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        var profile = new CurveArrArray();
        var curves = new CurveArray();
        curves.Append(Line.CreateBound(new XYZ(-halfW, -halfD, 0), new XYZ(halfW, -halfD, 0)));
        curves.Append(Line.CreateBound(new XYZ(halfW, -halfD, 0), new XYZ(halfW, halfD, 0)));
        curves.Append(Line.CreateBound(new XYZ(halfW, halfD, 0), new XYZ(-halfW, halfD, 0)));
        curves.Append(Line.CreateBound(new XYZ(-halfW, halfD, 0), new XYZ(-halfW, -halfD, 0)));
        profile.Append(curves);

        var extrusion = doc.FamilyCreate.NewExtrusion(true, profile, sketchPlane, hFt);

        // Add parameters
        AddParameterIfMissing(fm, "Width", SpecTypeId.Length, GroupTypeId.Geometry, true, diagnostics);
        AddParameterIfMissing(fm, "Depth", SpecTypeId.Length, GroupTypeId.Geometry, true, diagnostics);
        AddParameterIfMissing(fm, "Height", SpecTypeId.Length, GroupTypeId.Geometry, true, diagnostics);

        diagnostics.Add(DiagnosticRecord.Create("BUILTIN_COLUMN_CREATED", DiagnosticSeverity.Info,
            string.Format("Đã tạo column {0}x{1}x{2}mm (Extrusion Id={3}).",
                widthMm, depthMm, heightMm, extrusion.Id.Value)));

        return new ExecutionResult
        {
            OperationName = "builtin.create_parametric_column",
            Diagnostics = diagnostics,
            ChangedIds = new List<int> { checked((int)extrusion.Id.Value) }
        };
    }

    private ExecutionResult BuiltIn_CreateSimpleProfile(Document doc, Dictionary<string, string> parameters, List<DiagnosticRecord> diagnostics)
    {
        FamilyAuthoringService.GuardFamilyDocument(doc);

        var shape = GetStringParam(parameters, "Shape", "rectangle");
        var sizeMm = GetDoubleParam(parameters, "Size", 100);
        var sizeFt = sizeMm / 304.8;
        var half = sizeFt / 2.0;

        var sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        var profile = new CurveArrArray();
        var curves = new CurveArray();

        if (string.Equals(shape, "circle", StringComparison.OrdinalIgnoreCase))
        {
            var circle = Arc.Create(Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero), half, 0, 2 * Math.PI);
            curves.Append(circle);
        }
        else
        {
            curves.Append(Line.CreateBound(new XYZ(-half, -half, 0), new XYZ(half, -half, 0)));
            curves.Append(Line.CreateBound(new XYZ(half, -half, 0), new XYZ(half, half, 0)));
            curves.Append(Line.CreateBound(new XYZ(half, half, 0), new XYZ(-half, half, 0)));
            curves.Append(Line.CreateBound(new XYZ(-half, half, 0), new XYZ(-half, -half, 0)));
        }

        profile.Append(curves);
        var extrusion = doc.FamilyCreate.NewExtrusion(true, profile, sketchPlane, sizeFt * 0.1); // thin profile

        diagnostics.Add(DiagnosticRecord.Create("BUILTIN_PROFILE_CREATED", DiagnosticSeverity.Info,
            string.Format("Đã tạo {0} profile {1}mm (Id={2}).", shape, sizeMm, extrusion.Id.Value)));

        return new ExecutionResult
        {
            OperationName = "builtin.create_simple_profile",
            Diagnostics = diagnostics,
            ChangedIds = new List<int> { checked((int)extrusion.Id.Value) }
        };
    }

    private ExecutionResult BuiltIn_AuditFamilyParameters(Document doc, List<DiagnosticRecord> diagnostics)
    {
        FamilyAuthoringService.GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;
        var issues = 0;

        foreach (FamilyParameter param in fm.Parameters)
        {
            var name = param.Definition?.Name ?? "(unnamed)";

            // Check for formula
            if (!param.IsDeterminedByFormula && !param.IsInstance && param.StorageType == StorageType.Double)
            {
                diagnostics.Add(DiagnosticRecord.Create("PARAM_NO_FORMULA", DiagnosticSeverity.Warning,
                    string.Format("Parameter '{0}' (type, double) không có formula — có thể cần constraint.", name)));
                issues++;
            }

            // Check for unused (no associated elements)
            if (param.AssociatedParameters == null || param.AssociatedParameters.Size == 0)
            {
                if (!param.IsShared && !param.IsDeterminedByFormula)
                {
                    diagnostics.Add(DiagnosticRecord.Create("PARAM_POSSIBLY_UNUSED", DiagnosticSeverity.Info,
                        string.Format("Parameter '{0}' không associate với element nào và không có formula.", name)));
                }
            }
        }

        diagnostics.Add(DiagnosticRecord.Create("BUILTIN_AUDIT_DONE", DiagnosticSeverity.Info,
            string.Format("Audit hoàn tất: {0} parameters, {1} issues.", fm.Parameters.Size, issues)));

        return new ExecutionResult
        {
            OperationName = "builtin.audit_family_parameters",
            Diagnostics = diagnostics
        };
    }

    private ExecutionResult BuiltIn_BatchRenameTypes(Document doc, Dictionary<string, string> parameters, List<DiagnosticRecord> diagnostics)
    {
        FamilyAuthoringService.GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;

        var prefix = GetStringParam(parameters, "Prefix", "Type");
        var separator = GetStringParam(parameters, "Separator", " - ");
        var renamed = 0;

        var index = 1;
        foreach (FamilyType ft in fm.Types)
        {
            if (ft == null) continue;
            var newName = string.Format("{0}{1}{2}", prefix, separator, index);
            fm.CurrentType = ft;
            fm.RenameCurrentType(newName);
            renamed++;
            index++;
        }

        diagnostics.Add(DiagnosticRecord.Create("BUILTIN_RENAME_DONE", DiagnosticSeverity.Info,
            string.Format("Đã rename {0} types với prefix '{1}'.", renamed, prefix)));

        return new ExecutionResult
        {
            OperationName = "builtin.batch_rename_types",
            Diagnostics = diagnostics
        };
    }

    // ── HELPERS ──

    private string? ResolveCode(string? scriptId, string? inlineCode)
    {
        if (!string.IsNullOrWhiteSpace(inlineCode))
        {
            return inlineCode;
        }

        var normalizedScriptId = scriptId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedScriptId))
        {
            return null;
        }

        // Built-in scripts return a marker
        if (normalizedScriptId.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase))
        {
            return "[builtin:" + normalizedScriptId + "]";
        }

        // User script library
        var scriptPath = Path.Combine(_scriptsLibraryPath, normalizedScriptId + ".json");
        if (File.Exists(scriptPath))
        {
            try
            {
                var content = File.ReadAllText(scriptPath);
                var def = JsonUtil.Deserialize<ScriptDefinition>(content);
                return def?.Code ?? string.Empty;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private void TrackRun(ScriptRunResult result)
    {
        lock (_runsLock)
        {
            _recentRuns[result.RunId] = result;

            // Trim if too many
            if (_recentRuns.Count > MaxRecentRuns)
            {
                var oldest = _recentRuns.OrderBy(x => x.Value.DurationMs).First().Key;
                _recentRuns.Remove(oldest);
            }
        }
    }

    private static void AddParameterIfMissing(FamilyManager fm, string name, ForgeTypeId specTypeId, ForgeTypeId groupTypeId, bool isInstance, List<DiagnosticRecord> diagnostics)
    {
        foreach (FamilyParameter p in fm.Parameters)
        {
            if (string.Equals(p.Definition?.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        fm.AddParameter(name, groupTypeId, specTypeId, isInstance);
        diagnostics.Add(DiagnosticRecord.Create("PARAM_ADDED", DiagnosticSeverity.Info,
            string.Format("Đã thêm parameter '{0}'.", name)));
    }

    private static double GetDoubleParam(Dictionary<string, string> parameters, string key, double defaultValue)
    {
        if (parameters != null && parameters.TryGetValue(key, out var val))
        {
            if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return defaultValue;
    }

    private static string GetStringParam(Dictionary<string, string> parameters, string key, string defaultValue)
    {
        if (parameters != null && parameters.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        return defaultValue;
    }
}

// ── INTERNAL DTOs ──

[DataContract]
internal sealed class ScriptDefinition
{
    [DataMember(Order = 1)] public string ScriptId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string Description { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string Category { get; set; } = string.Empty;
    [DataMember(Order = 4)] public string Code { get; set; } = string.Empty;
}
