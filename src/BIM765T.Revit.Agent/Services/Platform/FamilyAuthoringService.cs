// CA1305: string.Format locale — these are diagnostic messages where locale is irrelevant.
// Pre-existing across ~150 call sites; will migrate to interpolation in a future cleanup pass.
#pragma warning disable CA1305

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Service xử lý family authoring operations: parameter, formula, type catalog,
/// geometry creation (extrusion, sweep, blend, revolution), reference planes,
/// subcategory assignment, nested family loading, save/save-as.
///
/// Tất cả methods trả về ExecutionResult hoặc DTO response — không gọi Transaction trực tiếp.
/// Transaction handling thuộc về caller (ToolModule) thông qua TransactionHelper hoặc
/// RegisterMutationTool pipeline.
/// </summary>
internal sealed class FamilyAuthoringService
{
    // ── GUARD ──

    /// <summary>
    /// Kiểm tra document hiện tại phải là family document.
    /// Throw InvalidOperationException nếu không phải.
    /// </summary>
    internal static void GuardFamilyDocument(Document doc)
    {
        if (doc == null)
        {
            throw new InvalidOperationException("Document is null.");
        }

        if (!doc.IsFamilyDocument)
        {
            throw new InvalidOperationException(
                "Document phải là Family Document (.rfa) để sử dụng family authoring tools. " +
                "Document hiện tại: " + (doc.Title ?? "(no title)"));
        }
    }

    // ── PARAMETER ──

    internal ExecutionResult PreviewAddParameter(PlatformServices services, Document doc, FamilyAddParameterRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;
        var diagnostics = new List<DiagnosticRecord>();

        // Check duplicate
        var existing = FindParameter(fm, request.ParameterName);
        if (existing != null)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_EXISTS", DiagnosticSeverity.Warning,
                string.Format("Parameter '{0}' đã tồn tại (IsInstance={1}, StorageType={2}).",
                    request.ParameterName, existing.IsInstance, existing.StorageType)));
        }

        var resolved = ResolveParameterType(request.ParameterType);
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ thêm parameter '{0}', type={1}, group={2}, isInstance={3}.",
                request.ParameterName, resolved.typeLabel, request.ParameterGroup ?? "PG_DATA",
                request.IsInstance)));

        return new ExecutionResult
        {
            OperationName = "family.add_parameter",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteAddParameter(PlatformServices services, Document doc, FamilyAddParameterRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = TransactionHelper.RunSafe(doc, "family.add_parameter", (_, txDiagnostics) =>
        {
            var fm = doc.FamilyManager;
            var existing = FindParameter(fm, request.ParameterName);
            if (existing != null)
            {
                txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_EXISTS", DiagnosticSeverity.Error,
                    string.Format("Parameter '{0}' ?? t?n t?i.", request.ParameterName)));
                return;
            }

            var (specTypeId, typeLabel) = ResolveParameterType(request.ParameterType);
            var group = ResolveParameterGroup(request.ParameterGroup);
            var param = fm.AddParameter(request.ParameterName, group, specTypeId, request.IsInstance);

            if (!string.IsNullOrWhiteSpace(request.DefaultValue) && param != null)
            {
                TrySetParameterDefault(fm, param, request.DefaultValue, txDiagnostics);
            }

            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_ADDED", DiagnosticSeverity.Info,
                string.Format("?? th?m parameter '{0}' ({1}, IsInstance={2}).",
                    request.ParameterName, typeLabel, request.IsInstance)));
        });

        return new ExecutionResult
        {
            OperationName = "family.add_parameter",
            Diagnostics = diagnostics
        };
    }

    // ── FORMULA ──

    internal ExecutionResult PreviewSetFormula(PlatformServices services, Document doc, FamilySetFormulaRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;
        var diagnostics = new List<DiagnosticRecord>();

        var param = FindParameter(fm, request.ParameterName);
        if (param == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.set_formula",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("FAMILY_PARAM_NOT_FOUND", DiagnosticSeverity.Error,
                        string.Format("Parameter '{0}' không tồn tại trong family.", request.ParameterName))
                }
            };
        }

        var currentFormula = param.Formula ?? "(none)";
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_FORMULA_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ set formula cho '{0}': {1} → {2}.",
                request.ParameterName, currentFormula, request.Formula)));

        return new ExecutionResult { OperationName = "family.set_formula", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteSetFormula(PlatformServices services, Document doc, FamilySetFormulaRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = TransactionHelper.RunSafe(doc, "family.set_formula", (_, txDiagnostics) =>
        {
            var fm = doc.FamilyManager;
            var param = FindParameter(fm, request.ParameterName);
            if (param == null)
            {
                txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_NOT_FOUND", DiagnosticSeverity.Error,
                    string.Format("Parameter '{0}' kh?ng t?n t?i.", request.ParameterName)));
                return;
            }

            fm.SetFormula(param, request.Formula);
            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_FORMULA_SET", DiagnosticSeverity.Info,
                string.Format("?? set formula cho '{0}': {1}.", request.ParameterName, request.Formula)));
        });

        return new ExecutionResult
        {
            OperationName = "family.set_formula",
            Diagnostics = diagnostics
        };
    }

    // ── TYPE CATALOG ──

    internal ExecutionResult PreviewSetTypeCatalog(PlatformServices services, Document doc, FamilySetTypeCatalogRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var fm = doc.FamilyManager;
        var diagnostics = new List<DiagnosticRecord>();

        // Check current types
        var existingTypes = new List<string>();
        foreach (FamilyType ft in fm.Types)
        {
            existingTypes.Add(ft.Name ?? "(unnamed)");
        }

        diagnostics.Add(DiagnosticRecord.Create("FAMILY_TYPES_CURRENT", DiagnosticSeverity.Info,
            string.Format("Types hiện tại: [{0}]. Sẽ tạo/cập nhật {1} types.",
                string.Join(", ", existingTypes), request.Types?.Count ?? 0)));

        return new ExecutionResult { OperationName = "family.set_type_catalog", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteSetTypeCatalog(PlatformServices services, Document doc, FamilySetTypeCatalogRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = TransactionHelper.RunSafe(doc, "family.set_type_catalog", (_, txDiagnostics) =>
        {
            var fm = doc.FamilyManager;
            var created = 0;
            var updated = 0;

            foreach (var typeEntry in request.Types ?? new List<FamilyTypeCatalogEntry>())
            {
                if (typeEntry == null || string.IsNullOrWhiteSpace(typeEntry.TypeName))
                {
                    continue;
                }

                FamilyType? existingType = null;
                foreach (FamilyType ft in fm.Types)
                {
                    if (string.Equals(ft.Name, typeEntry.TypeName, StringComparison.OrdinalIgnoreCase))
                    {
                        existingType = ft;
                        break;
                    }
                }

                if (existingType != null)
                {
                    fm.CurrentType = existingType;
                    updated++;
                }
                else
                {
                    var newType = fm.NewType(typeEntry.TypeName);
                    fm.CurrentType = newType;
                    created++;
                }

                if (typeEntry.ParameterValues != null)
                {
                    foreach (var pv in typeEntry.ParameterValues)
                    {
                        var param = FindParameter(fm, pv.Key);
                        if (param != null)
                        {
                            TrySetParameterDefault(fm, param, pv.Value, txDiagnostics);
                        }
                        else
                        {
                            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_TYPE_PARAM_NOT_FOUND", DiagnosticSeverity.Warning,
                                string.Format("Type '{0}': parameter '{1}' kh?ng t?n t?i.", typeEntry.TypeName, pv.Key)));
                        }
                    }
                }
            }

            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_TYPE_CATALOG_DONE", DiagnosticSeverity.Info,
                string.Format("Created {0} types, updated {1} types.", created, updated)));
        });

        return new ExecutionResult
        {
            OperationName = "family.set_type_catalog",
            Diagnostics = diagnostics
        };
    }

    // ── REFERENCE PLANE ──

    internal ExecutionResult PreviewAddReferencePlane(PlatformServices services, Document doc, FamilyAddReferencePlaneRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_REFPLANE_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo Reference Plane '{0}' tại origin=({1},{2},{3}), normal=({4},{5},{6}), extent={7}ft.",
                request.Name, request.OriginX, request.OriginY, request.OriginZ,
                request.NormalX, request.NormalY, request.NormalZ, request.ExtentFeet)));

        return new ExecutionResult { OperationName = "family.add_reference_plane", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteAddReferencePlane(PlatformServices services, Document doc, FamilyAddReferencePlaneRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.add_reference_plane", (_, txDiagnostics) =>
        {
            var normal = new XYZ(request.NormalX, request.NormalY, request.NormalZ).Normalize();
            var origin = new XYZ(request.OriginX, request.OriginY, request.OriginZ);
            var perpendicular = GetPerpendicular(normal);
            var bubbleEnd = origin - perpendicular * request.ExtentFeet;
            var freeEnd = origin + perpendicular * request.ExtentFeet;

            var refPlane = doc.FamilyCreate.NewReferencePlane(bubbleEnd, freeEnd, normal, doc.ActiveView);
            refPlane.Name = request.Name;
            changedIds.Add(checked((int)refPlane.Id.Value));

            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_REFPLANE_CREATED", DiagnosticSeverity.Info,
                string.Format("?? t?o Reference Plane '{0}' (Id={1}).", request.Name, refPlane.Id.Value)));
        });

        return new ExecutionResult
        {
            OperationName = "family.add_reference_plane",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── EXTRUSION ──

    internal ExecutionResult PreviewCreateExtrusion(PlatformServices services, Document doc, FamilyCreateExtrusionRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_EXTRUSION_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo Extrusion: {0} profile points, isVoid={1}, start={2:F3}, end={3:F3} (feet).",
                request.Profile?.Count ?? 0, request.IsVoid, request.StartOffset, request.EndOffset)));

        return new ExecutionResult { OperationName = "family.create_extrusion", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteCreateExtrusion(PlatformServices services, Document doc, FamilyCreateExtrusionRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.create_extrusion", (_, txDiagnostics) =>
        {
            var sketchPlane = ResolveOrCreateSketchPlane(doc, request.SketchPlaneName);
            var profileLoops = BuildCurveArrArrayFromProfile(request.Profile);

            var extrusion = doc.FamilyCreate.NewExtrusion(!request.IsVoid, profileLoops, sketchPlane, request.EndOffset);
            if (Math.Abs(request.StartOffset) > 1e-9)
            {
                extrusion.StartOffset = request.StartOffset;
            }

            changedIds.Add(checked((int)extrusion.Id.Value));
            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_EXTRUSION_CREATED", DiagnosticSeverity.Info,
                string.Format("?? t?o Extrusion (Id={0}).", extrusion.Id.Value)));
        });

        return new ExecutionResult
        {
            OperationName = "family.create_extrusion",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── SWEEP ──

    internal ExecutionResult PreviewCreateSweep(PlatformServices services, Document doc, FamilyCreateSweepRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_SWEEP_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo Sweep: {0} profile points, {1} path points, isVoid={2}.",
                request.Profile?.Count ?? 0, request.PathPoints?.Count ?? 0, request.IsVoid)));

        return new ExecutionResult { OperationName = "family.create_sweep", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteCreateSweep(PlatformServices services, Document doc, FamilyCreateSweepRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var sketchPlane = ResolveOrCreateSketchPlane(doc, request.SketchPlaneName);
        var sweepProfile = BuildSweepProfile(request.Profile);
        if (sweepProfile == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyProfileInvalid, DiagnosticSeverity.Error,
                "Kh?ng th? t?o SweepProfile t? profile points ?? cung c?p."));
            return new ExecutionResult
            {
                OperationName = "family.create_sweep",
                Diagnostics = diagnostics
            };
        }
        var pathCurves = BuildPathCurvesFromPathPoints(request.PathPoints);

        // Create sweep using path-based approach
        var path = new CurveArray();
        foreach (var c in pathCurves)
        {
            path.Append(c);
        }

        var sweep = doc.FamilyCreate.NewSweep(!request.IsVoid, path, sketchPlane, sweepProfile, 0, ProfilePlaneLocation.Start);

        return new ExecutionResult
        {
            OperationName = "family.create_sweep",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("FAMILY_SWEEP_CREATED", DiagnosticSeverity.Info,
                    string.Format("Đã tạo Sweep (Id={0}).", sweep.Id.Value))
            },
            ChangedIds = new List<int> { checked((int)sweep.Id.Value) }
        };
    }

    // ── BLEND ──

    internal ExecutionResult PreviewCreateBlend(PlatformServices services, Document doc, FamilyCreateBlendRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_BLEND_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo Blend: bottom {0}pts, top {1}pts, topOffset={2:F3}ft, isVoid={3}.",
                request.BottomProfile?.Count ?? 0, request.TopProfile?.Count ?? 0, request.TopOffset, request.IsVoid)));

        return new ExecutionResult { OperationName = "family.create_blend", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteCreateBlend(PlatformServices services, Document doc, FamilyCreateBlendRequest request)
    {
        GuardFamilyDocument(doc);

        var sketchPlane = ResolveOrCreateSketchPlane(doc, request.SketchPlaneName);
        var bottomProfile = BuildCurveArrArrayFromProfile(request.BottomProfile);
        var topProfile = BuildCurveArrArrayFromProfile(request.TopProfile);

        var blend = doc.FamilyCreate.NewBlend(!request.IsVoid, topProfile.get_Item(0), bottomProfile.get_Item(0), sketchPlane);
        blend.TopOffset = request.TopOffset;

        return new ExecutionResult
        {
            OperationName = "family.create_blend",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("FAMILY_BLEND_CREATED", DiagnosticSeverity.Info,
                    string.Format("Đã tạo Blend (Id={0}).", blend.Id.Value))
            },
            ChangedIds = new List<int> { checked((int)blend.Id.Value) }
        };
    }

    // ── REVOLUTION ──

    internal ExecutionResult PreviewCreateRevolution(PlatformServices services, Document doc, FamilyCreateRevolutionRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_REVOLUTION_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo Revolution: {0} profile points, axis=({1},{2}), angles={3:F1}°→{4:F1}°, isVoid={5}.",
                request.Profile?.Count ?? 0, request.AxisDirectionX, request.AxisDirectionY,
                request.StartAngle, request.EndAngle, request.IsVoid)));

        return new ExecutionResult { OperationName = "family.create_revolution", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteCreateRevolution(PlatformServices services, Document doc, FamilyCreateRevolutionRequest request)
    {
        GuardFamilyDocument(doc);

        var sketchPlane = ResolveOrCreateSketchPlane(doc, request.SketchPlaneName);
        var profileLoops = BuildCurveArrArrayFromProfile(request.Profile);

        // Create axis line
        var axisOrigin = new XYZ(request.AxisOriginX, request.AxisOriginY, 0);
        var axisDir = new XYZ(request.AxisDirectionX, request.AxisDirectionY, 0).Normalize();
        var axisLine = Line.CreateBound(axisOrigin, axisOrigin + axisDir * 10.0);

        var revolution = doc.FamilyCreate.NewRevolution(!request.IsVoid, profileLoops, sketchPlane, axisLine,
            DegToRad(request.StartAngle), DegToRad(request.EndAngle));

        return new ExecutionResult
        {
            OperationName = "family.create_revolution",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("FAMILY_REVOLUTION_CREATED", DiagnosticSeverity.Info,
                    string.Format("Đã tạo Revolution (Id={0}).", revolution.Id.Value))
            },
            ChangedIds = new List<int> { checked((int)revolution.Id.Value) }
        };
    }

    // ── SUBCATEGORY ──

    internal ExecutionResult PreviewSetSubcategory(PlatformServices services, Document doc, FamilySetSubcategoryRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCATEGORY_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ gán subcategory '{0}' cho element Id={1}.", request.SubcategoryName, request.FormElementId)));

        return new ExecutionResult { OperationName = "family.set_subcategory", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteSetSubcategory(PlatformServices services, Document doc, FamilySetSubcategoryRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.set_subcategory", (_, txDiagnostics) =>
        {
            var element = doc.GetElement(new ElementId((long)request.FormElementId));
            if (element == null)
            {
                txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_ELEMENT_NOT_FOUND", DiagnosticSeverity.Error,
                    string.Format("Element Id={0} kh?ng t?n t?i.", request.FormElementId)));
                return;
            }

            var ownerCategory = doc.OwnerFamily.FamilyCategory;
            Category? subcat = null;
            foreach (Category c in ownerCategory.SubCategories)
            {
                if (string.Equals(c.Name, request.SubcategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    subcat = c;
                    break;
                }
            }

            if (subcat == null)
            {
                subcat = doc.Settings.Categories.NewSubcategory(ownerCategory, request.SubcategoryName);
            }

            var subcatParam = element.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
            if (subcatParam != null && !subcatParam.IsReadOnly)
            {
                subcatParam.Set(subcat.Id);
            }

            changedIds.Add(request.FormElementId);
            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCATEGORY_SET", DiagnosticSeverity.Info,
                string.Format("?? g?n subcategory '{0}' cho element Id={1}.", request.SubcategoryName, request.FormElementId)));
        });

        return new ExecutionResult
        {
            OperationName = "family.set_subcategory",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── LOAD NESTED ──

    internal ExecutionResult PreviewLoadNested(PlatformServices services, Document doc, FamilyLoadNestedRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        if (!File.Exists(request.FamilyFilePath))
        {
            return new ExecutionResult
            {
                OperationName = "family.load_nested",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("FAMILY_FILE_NOT_FOUND", DiagnosticSeverity.Error,
                        string.Format("File không tồn tại: {0}", request.FamilyFilePath))
                }
            };
        }

        diagnostics.Add(DiagnosticRecord.Create("FAMILY_LOAD_NESTED_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ load nested family từ: {0}. Overwrite={1}.",
                request.FamilyFilePath, request.OverwriteExisting)));

        return new ExecutionResult { OperationName = "family.load_nested", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteLoadNested(PlatformServices services, Document doc, FamilyLoadNestedRequest request)
    {
        GuardFamilyDocument(doc);

        if (!File.Exists(request.FamilyFilePath))
        {
            return new ExecutionResult
            {
                OperationName = "family.load_nested",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("FAMILY_FILE_NOT_FOUND", DiagnosticSeverity.Error,
                        string.Format("File không tồn tại: {0}", request.FamilyFilePath))
                }
            };
        }

        Family? loadedFamily = null;
        var success = doc.LoadFamily(request.FamilyFilePath, out loadedFamily);

        if (!success && request.OverwriteExisting)
        {
            // Try with overwrite
            success = doc.LoadFamily(request.FamilyFilePath, new OverwriteFamilyLoadOptions(), out loadedFamily);
        }

        if (!success || loadedFamily == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.load_nested",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("FAMILY_NESTED_LOAD_FAILED", DiagnosticSeverity.Error,
                        string.Format("Không thể load family từ: {0}", request.FamilyFilePath))
                }
            };
        }

        return new ExecutionResult
        {
            OperationName = "family.load_nested",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("FAMILY_NESTED_LOADED", DiagnosticSeverity.Info,
                    string.Format("Đã load nested family '{0}' (Id={1}).", loadedFamily.Name, loadedFamily.Id.Value))
            },
            ChangedIds = new List<int> { checked((int)loadedFamily.Id.Value) }
        };
    }

    // ── SAVE ──

    internal ExecutionResult PreviewSave(PlatformServices services, Document doc, FamilySaveRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var action = string.IsNullOrWhiteSpace(request.SaveAsPath) ? "Save" : "Save As → " + request.SaveAsPath;
        diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Action: {0}. CompactFile={1}.", action, request.CompactFile)));

        if (!string.IsNullOrWhiteSpace(request.SaveAsPath) && File.Exists(request.SaveAsPath) && !request.OverwriteExisting)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_FILE_EXISTS", DiagnosticSeverity.Warning,
                "File đã tồn tại và OverwriteExisting=false."));
        }

        return new ExecutionResult { OperationName = "family.save", DryRun = true, ConfirmationRequired = true, Diagnostics = diagnostics };
    }

    internal ExecutionResult ExecuteSave(PlatformServices services, Document doc, FamilySaveRequest request)
    {
        GuardFamilyDocument(doc);

        if (string.IsNullOrWhiteSpace(request.SaveAsPath))
        {
            // Simple save
            doc.Save();
        }
        else
        {
            var options = new SaveAsOptions { OverwriteExistingFile = request.OverwriteExisting };
            if (request.CompactFile)
            {
                options.Compact = true;
            }

            doc.SaveAs(request.SaveAsPath, options);
        }

        var message = string.IsNullOrWhiteSpace(request.SaveAsPath)
            ? "Đã lưu family document."
            : string.Format("Đã lưu family document → {0}.", request.SaveAsPath);

        return new ExecutionResult
        {
            OperationName = "family.save",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("FAMILY_SAVED", DiagnosticSeverity.Info, message)
            }
        };
    }

    // ── LIST GEOMETRY ──

    internal FamilyListGeometryResponse ListGeometry(PlatformServices services, Document doc)
    {
        GuardFamilyDocument(doc);

        var response = new FamilyListGeometryResponse
        {
            FamilyName = doc.Title ?? string.Empty,
            CategoryName = doc.OwnerFamily?.FamilyCategory?.Name ?? string.Empty
        };

        // Collect all GenericForm elements
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(GenericForm));

        foreach (var elem in collector)
        {
            var form = elem as GenericForm;
            if (form == null) continue;

            var info = new FamilyGeometryItem
            {
                ElementId = checked((int)form.Id.Value),
                FormType = GetGeometryType(form),
                Mode = form.IsSolid ? "Solid" : "Void",
                IsVisible = form.Visible
            };

            // Get subcategory
            var subcatParam = form.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
            if (subcatParam != null)
            {
                var subcatElem = doc.GetElement(subcatParam.AsElementId());
                info.SubcategoryName = subcatElem?.Name ?? string.Empty;
            }

            response.Forms.Add(info);
        }

        // Collect reference planes
        var refPlaneCollector = new FilteredElementCollector(doc)
            .OfClass(typeof(ReferencePlane));

        foreach (var elem in refPlaneCollector)
        {
            var rp = elem as ReferencePlane;
            if (rp != null && !string.IsNullOrWhiteSpace(rp.Name))
            {
                response.ReferencePlaneNames.Add(rp.Name);
            }
        }

        // Parameters summary
        var fm = doc.FamilyManager;
        response.ParameterCount = fm.Parameters.Size;
        response.TypeCount = 0;
        foreach (FamilyType ft in fm.Types) { response.TypeCount++; }

        return response;
    }

    // ── HELPERS ──

    private static FamilyParameter? FindParameter(FamilyManager fm, string name)
    {
        foreach (FamilyParameter p in fm.Parameters)
        {
            if (string.Equals(p.Definition?.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }

    private static (ForgeTypeId specTypeId, string typeLabel) ResolveParameterType(string typeString)
    {
        if (string.IsNullOrWhiteSpace(typeString))
        {
            return (SpecTypeId.String.Text, "Text");
        }

        switch (typeString.ToLowerInvariant())
        {
            case "length": return (SpecTypeId.Length, "Length");
            case "area": return (SpecTypeId.Area, "Area");
            case "volume": return (SpecTypeId.Volume, "Volume");
            case "angle": return (SpecTypeId.Angle, "Angle");
            case "integer": return (SpecTypeId.Int.Integer, "Integer");
            case "number": return (SpecTypeId.Number, "Number");
            case "yesno": return (SpecTypeId.Boolean.YesNo, "YesNo");
            case "material": return (SpecTypeId.Reference.Material, "Material");
            case "url": return (SpecTypeId.String.Url, "URL");
            case "image": return (SpecTypeId.Reference.Image, "Image");
            case "text":
            default: return (SpecTypeId.String.Text, "Text");
        }
    }

    private static ForgeTypeId ResolveParameterGroup(string groupString)
    {
        if (string.IsNullOrWhiteSpace(groupString))
        {
            return GroupTypeId.Data;
        }

        switch (groupString.ToLowerInvariant())
        {
            case "geometry": return GroupTypeId.Geometry;
            case "identity": return GroupTypeId.IdentityData;
            case "construction": return GroupTypeId.Construction;
            case "constraints": return GroupTypeId.Constraints;
            case "materials": return GroupTypeId.Materials;
            case "data":
            default: return GroupTypeId.Data;
        }
    }

    private static void TrySetParameterDefault(FamilyManager fm, FamilyParameter param, string value, List<DiagnosticRecord> diagnostics)
    {
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    fm.Set(param, value);
                    break;
                case StorageType.Integer:
                    if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intVal))
                    {
                        fm.Set(param, intVal);
                    }
                    break;
                case StorageType.Double:
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleVal))
                    {
                        fm.Set(param, doubleVal);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_DEFAULT_FAILED", DiagnosticSeverity.Warning,
                string.Format("Không thể set default cho '{0}': {1}",
                    param.Definition?.Name ?? "?", ex.Message)));
        }
    }

    private static SketchPlane ResolveOrCreateSketchPlane(Document doc, string planeName)
    {
        if (!string.IsNullOrWhiteSpace(planeName))
        {
            // Try to find existing sketch plane by reference plane name
            var refPlanes = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => string.Equals(rp.Name, planeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (refPlanes.Count > 0)
            {
                return SketchPlane.Create(doc, refPlanes[0].GetPlane());
            }
        }

        // Default: XY plane at Z=0
        var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        return SketchPlane.Create(doc, plane);
    }

    private static CurveArrArray BuildCurveArrArrayFromProfile(List<FamilyProfilePoint> points)
    {
        var result = new CurveArrArray();
        var curveArr = new CurveArray();

        if (points == null || points.Count < 3)
        {
            return result;
        }

        for (var i = 0; i < points.Count; i++)
        {
            var p1 = points[i];
            var p2 = points[(i + 1) % points.Count];
            var start = new XYZ(p1.X, p1.Y, 0);
            var end = new XYZ(p2.X, p2.Y, 0);

            if (start.DistanceTo(end) > 1e-9)
            {
                curveArr.Append(Line.CreateBound(start, end));
            }
        }

        result.Append(curveArr);
        return result;
    }

    private static SweepProfile? BuildSweepProfile(List<FamilyProfilePoint> points)
    {
        var loops = BuildCurveArrArrayFromProfile(points);
        // SweepProfile from CurveArrArray approach — used by Revit 2024
        return null; // Placeholder: actual implementation needs doc.Application.Create.NewCurveLoopsProfile
    }

    private static List<Curve> BuildPathCurvesFromPathPoints(List<FamilyPathPoint> pathPoints)
    {
        var curves = new List<Curve>();
        if (pathPoints == null || pathPoints.Count < 2) return curves;

        for (var i = 0; i < pathPoints.Count - 1; i++)
        {
            var p1 = pathPoints[i];
            var p2 = pathPoints[i + 1];
            var start = new XYZ(p1.X, p1.Y, p1.Z);
            var end = new XYZ(p2.X, p2.Y, p2.Z);

            if (start.DistanceTo(end) > 1e-9)
            {
                curves.Add(Line.CreateBound(start, end));
            }
        }

        return curves;
    }

    private static XYZ GetPerpendicular(XYZ normal)
    {
        if (Math.Abs(normal.Z) < 0.9)
        {
            return normal.CrossProduct(XYZ.BasisZ).Normalize();
        }

        return normal.CrossProduct(XYZ.BasisX).Normalize();
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    private static string GetGeometryType(GenericForm form)
    {
        if (form is Extrusion) return "Extrusion";
        if (form is Sweep) return "Sweep";
        if (form is Blend) return "Blend";
        if (form is Revolution) return "Revolution";
        if (form is SweptBlend) return "SweptBlend";
        return "Unknown";
    }

    // ══════════════════════════════════════════════════════════════
    // TIER 1: Dimension, Alignment, Connector
    // ══════════════════════════════════════════════════════════════

    // ── DIMENSION ──

    internal ExecutionResult PreviewAddDimension(PlatformServices services, Document doc, FamilyAddDimensionRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var rp1 = FindReferencePlane(doc, request.ReferencePlane1Name);
        var rp2 = FindReferencePlane(doc, request.ReferencePlane2Name);

        if (rp1 == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                string.Format("Reference plane '{0}' không tìm thấy.", request.ReferencePlane1Name)));
        }

        if (rp2 == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                string.Format("Reference plane '{0}' không tìm thấy.", request.ReferencePlane2Name)));
        }

        if (!string.IsNullOrEmpty(request.LabelParameterName))
        {
            var fm = doc.FamilyManager;
            var param = FindParameter(fm, request.LabelParameterName);
            if (param == null)
            {
                diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Warning,
                    string.Format("Parameter '{0}' chưa tồn tại — cần tạo trước hoặc sẽ không bind label.", request.LabelParameterName)));
            }
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_DIMENSION_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ tạo {0} dimension giữa '{1}' và '{2}'{3}.",
                    request.DimensionType,
                    request.ReferencePlane1Name,
                    request.ReferencePlane2Name,
                    string.IsNullOrEmpty(request.LabelParameterName) ? "" : ", label=" + request.LabelParameterName)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_dimension",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteAddDimension(PlatformServices services, Document doc, FamilyAddDimensionRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.add_dimension", (_, txDiagnostics) =>
        {
            var rp1 = FindReferencePlane(doc, request.ReferencePlane1Name);
            var rp2 = FindReferencePlane(doc, request.ReferencePlane2Name);
            if (rp1 == null || rp2 == null)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                    "M?t ho?c c? hai reference planes kh?ng t?m th?y."));
                return;
            }

            var candidateViews = GetDimensionViewCandidates(doc, rp1, rp2);
            if (candidateViews.Count == 0)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                    "Kh?ng t?m th?y view ph? h?p ?? ??t dimension."));
                return;
            }

            var direction = (rp2.GetPlane().Origin - rp1.GetPlane().Origin);
            if (direction.GetLength() < 1e-9)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                    "Hai reference planes tr?ng v? tr?, kh?ng th? t?o dimension."));
                return;
            }

            var refArray = new ReferenceArray();
            refArray.Append(rp1.GetReference());
            refArray.Append(rp2.GetReference());
            Dimension? dim = null;
            View? selectedView = null;
            var dimensionAttemptErrors = new List<string>();
            foreach (var candidateView in candidateViews)
            {
                foreach (var dimLine in BuildDimensionLineCandidates(candidateView, rp1.GetPlane().Origin, rp2.GetPlane().Origin))
                {
                    try
                    {
                        dim = doc.FamilyCreate.NewDimension(candidateView, dimLine, refArray);
                        selectedView = candidateView;
                        break;
                    }
                    catch (Exception ex)
                    {
                        dimensionAttemptErrors.Add(string.Format("{0} [{1}]: {2}",
                            candidateView.Name,
                            candidateView.ViewType,
                            ex.Message));
                    }
                }

                if (dim != null)
                {
                    break;
                }
            }

            if (dim == null)
            {
                var detail = dimensionAttemptErrors.Count > 0
                    ? string.Join(" | ", dimensionAttemptErrors.Take(4))
                    : "Khong co candidate line hop le.";
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                    string.Format("Khong tao duoc dimension giua '{0}' va '{1}'. {2}",
                        request.ReferencePlane1Name,
                        request.ReferencePlane2Name,
                        detail)));
                return;
            }

            changedIds.Add(checked((int)dim.Id.Value));
            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_DIMENSION_VIEW_USED", DiagnosticSeverity.Info,
                string.Format("Da dat dimension trong view '{0}' ({1}).",
                    selectedView?.Name ?? "(unknown)",
                    selectedView?.ViewType.ToString() ?? "(unknown)")));

            if (!string.IsNullOrEmpty(request.LabelParameterName))
            {
                var fm = doc.FamilyManager;
                var param = FindParameter(fm, request.LabelParameterName);
                if (param != null)
                {
                    try
                    {
                        dim.FamilyLabel = param;
                        txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_DIMENSION_LABELED", DiagnosticSeverity.Info,
                            string.Format("?? bind dimension v?o parameter '{0}'.", request.LabelParameterName)));
                    }
                    catch (Exception ex)
                    {
                        txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Warning,
                            string.Format("Kh?ng th? bind label: {0}", ex.Message)));
                    }
                }
                else
                {
                    txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Warning,
                        string.Format("Parameter '{0}' kh?ng t?m th?y ? dimension t?o nh?ng kh?ng bind label.", request.LabelParameterName)));
                }
            }

            if (request.IsLocked)
            {
                try { dim.IsLocked = true; }
                catch (Exception ex) { Trace.TraceWarning($"FamilyAuthoring: Failed to lock dimension: {ex.Message}"); }
            }

            txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_DIMENSION_CREATED", DiagnosticSeverity.Info,
                string.Format("?? t?o dimension gi?a '{0}' v? '{1}'.", request.ReferencePlane1Name, request.ReferencePlane2Name)));
        });

        return new ExecutionResult
        {
            OperationName = "family.add_dimension",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── ALIGNMENT ──

    internal ExecutionResult PreviewAddAlignment(PlatformServices services, Document doc, FamilyAddAlignmentRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var rp = FindReferencePlane(doc, request.ReferencePlaneName);
        if (rp == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                string.Format("Reference plane '{0}' không tìm thấy.", request.ReferencePlaneName)));
        }

        var form = doc.GetElement(new ElementId((long)request.GeometryElementId));
        if (form == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                string.Format("Geometry element Id={0} không tìm thấy.", request.GeometryElementId)));
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_ALIGNMENT_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ tạo alignment constraint giữa ref plane '{0}' và geometry Id={1} (face={2}), locked={3}.",
                    request.ReferencePlaneName, request.GeometryElementId, request.GeometryFaceIndex, request.IsLocked)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_alignment",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteAddAlignment(PlatformServices services, Document doc, FamilyAddAlignmentRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var rp = FindReferencePlane(doc, request.ReferencePlaneName);
        if (rp == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.add_alignment",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                        string.Format("Reference plane '{0}' không tìm thấy.", request.ReferencePlaneName))
                }
            };
        }

        var element = doc.GetElement(new ElementId((long)request.GeometryElementId));
        if (element == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.add_alignment",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                        string.Format("Geometry element Id={0} không tìm thấy.", request.GeometryElementId))
                }
            };
        }

        // Find view for alignment
        var candidateViews = GetDimensionViewCandidates(doc, rp, rp);
        View? view = candidateViews.Count > 0 ? candidateViews[0] : null;
        if (view == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.add_alignment",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                        "Không tìm thấy view phù hợp để tạo alignment.")
                }
            };
        }

        // Get references
        var planeRef = rp.GetReference();
        Reference? geomRef = null;

        // Try to get geometry face reference by index
        var geomObj = element.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = true });
        if (geomObj != null)
        {
            var faces = new List<Face>();
            foreach (var go in geomObj)
            {
                if (go is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    foreach (var innerGo in gi.GetInstanceGeometry())
                    {
                        if (innerGo is Solid innerSolid)
                        {
                            foreach (Face face in innerSolid.Faces)
                            {
                                faces.Add(face);
                            }
                        }
                    }
                }
            }

            if (request.GeometryFaceIndex >= 0 && request.GeometryFaceIndex < faces.Count)
            {
                geomRef = faces[request.GeometryFaceIndex].Reference;
            }
        }

        if (geomRef == null)
        {
            // Fallback: use element reference directly
            geomRef = new Reference(element);
        }

        try
        {
            doc.FamilyCreate.NewAlignment(view, planeRef, geomRef);

            diagnostics.Add(DiagnosticRecord.Create("FAMILY_ALIGNMENT_CREATED", DiagnosticSeverity.Info,
                string.Format("Đã tạo alignment giữa ref plane '{0}' và geometry Id={1}.",
                    request.ReferencePlaneName, request.GeometryElementId)));
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyDimensionInvalid, DiagnosticSeverity.Error,
                string.Format("Lỗi tạo alignment: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_alignment",
            Diagnostics = diagnostics,
            ChangedIds = new List<int> { request.GeometryElementId }
        };
    }

    // ── CONNECTOR ──

    internal ExecutionResult PreviewAddConnector(PlatformServices services, Document doc, FamilyAddConnectorRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var host = doc.GetElement(new ElementId((long)request.HostGeometryElementId));
        if (host == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                string.Format("Host geometry Id={0} không tìm thấy.", request.HostGeometryElementId)));
        }

        var validDomains = new[] { "duct", "pipe", "electrical", "conduit", "cable_tray" };
        if (!validDomains.Contains(request.ConnectorDomain?.ToLowerInvariant()))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                string.Format("ConnectorDomain '{0}' không hợp lệ. Phải là: duct, pipe, electrical, conduit, cable_tray.", request.ConnectorDomain)));
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_CONNECTOR_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ tạo {0} connector (type={1}, profile={2}) trên geometry Id={3}, direction={4}.",
                    request.ConnectorDomain, request.ConnectorType, request.ProfileOption,
                    request.HostGeometryElementId, request.Direction)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_connector",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteAddConnector(PlatformServices services, Document doc, FamilyAddConnectorRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        var changedIds = new List<int>();

        var hostElement = doc.GetElement(new ElementId((long)request.HostGeometryElementId));
        if (hostElement == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.add_connector",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                        string.Format("Host geometry Id={0} không tìm thấy.", request.HostGeometryElementId))
                }
            };
        }

        try
        {
            ConnectorElement? connector = null;
            var domain = (request.ConnectorDomain ?? "").ToLowerInvariant();
            var profileOpt = ResolveDuctConnectorProfileType(request.ProfileOption);

            // Get a face reference from the host geometry for connector placement
            var hostRef = GetGeometryFaceReference(doc, hostElement, 0);
            if (hostRef == null)
            {
                return new ExecutionResult
                {
                    OperationName = "family.add_connector",
                    Diagnostics = new List<DiagnosticRecord>
                    {
                        DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                            string.Format("Không thể lấy face reference từ geometry Id={0}.", request.HostGeometryElementId))
                    }
                };
            }

            switch (domain)
            {
                case "duct":
                    var ductSysType = ResolveDuctSystemType(request.SystemClassification);
                    connector = ConnectorElement.CreateDuctConnector(doc, ductSysType, profileOpt, hostRef);
                    break;
                case "pipe":
                    var pipeSysType = ResolvePipeSystemType(request.SystemClassification);
                    connector = ConnectorElement.CreatePipeConnector(doc, pipeSysType, hostRef);
                    break;
                case "conduit":
                    connector = ConnectorElement.CreateConduitConnector(doc, hostRef);
                    break;
                case "cable_tray":
                    connector = ConnectorElement.CreateCableTrayConnector(doc, hostRef);
                    break;
                case "electrical":
                    var elecSysType = ResolveElectricalSystemType(request.SystemClassification);
                    connector = ConnectorElement.CreateElectricalConnector(doc, elecSysType, hostRef);
                    break;
                default:
                    return new ExecutionResult
                    {
                        OperationName = "family.add_connector",
                        Diagnostics = new List<DiagnosticRecord>
                        {
                            DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                                string.Format("ConnectorDomain '{0}' không hợp lệ.", request.ConnectorDomain))
                        }
                    };
            }

            if (connector != null)
            {
                changedIds.Add(checked((int)connector.Id.Value));

                // Set direction
                try
                {
                    var direction = ResolveDirection(request.Direction);
                    connector.CoordinateSystem.GetType(); // Force evaluation
                    // Note: Connector direction is controlled by placement on geometry face,
                    // not directly settable via API in all cases.
                }
                catch (Exception) { /* Direction may not be settable programmatically */ }

                diagnostics.Add(DiagnosticRecord.Create("FAMILY_CONNECTOR_CREATED", DiagnosticSeverity.Info,
                    string.Format("Đã tạo {0} connector (Id={1}) trên geometry Id={2}.",
                        domain, connector.Id.Value, request.HostGeometryElementId)));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyConnectorInvalid, DiagnosticSeverity.Error,
                string.Format("Lỗi tạo connector: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_connector",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ══════════════════════════════════════════════════════════════
    // TIER 2: Visibility, Subcategory Creation, Material Binding, Parameter Visibility
    // ══════════════════════════════════════════════════════════════

    // ── VISIBILITY ──

    internal ExecutionResult PreviewSetVisibility(PlatformServices services, Document doc, FamilySetVisibilityRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var element = doc.GetElement(new ElementId((long)request.FormElementId));
        if (element == null || !(element is GenericForm))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                string.Format("Form element Id={0} không tìm thấy hoặc không phải GenericForm.", request.FormElementId)));
        }
        else
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_VISIBILITY_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ set visibility cho form Id={0}: Fine={1}, Medium={2}, Coarse={3}, Plan={4}, Front/Back={5}, Left/Right={6}.",
                    request.FormElementId, request.IsShownInFine, request.IsShownInMedium, request.IsShownInCoarse,
                    request.IsShownInPlanRCP, request.IsShownInFrontBack, request.IsShownInLeftRight)));
        }

        return new ExecutionResult
        {
            OperationName = "family.set_visibility",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteSetVisibility(PlatformServices services, Document doc, FamilySetVisibilityRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var element = doc.GetElement(new ElementId((long)request.FormElementId));
        if (!(element is GenericForm form))
        {
            return new ExecutionResult
            {
                OperationName = "family.set_visibility",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                        string.Format("Form element Id={0} không phải GenericForm.", request.FormElementId))
                }
            };
        }

        try
        {
            var vis = new FamilyElementVisibility(FamilyElementVisibilityType.Model);
            vis.IsShownInFine = request.IsShownInFine;
            vis.IsShownInMedium = request.IsShownInMedium;
            vis.IsShownInCoarse = request.IsShownInCoarse;
            vis.IsShownInPlanRCPCut = request.IsShownInPlanRCP;
            vis.IsShownInFrontBack = request.IsShownInFrontBack;
            vis.IsShownInLeftRight = request.IsShownInLeftRight;

            form.SetVisibility(vis);

            diagnostics.Add(DiagnosticRecord.Create("FAMILY_VISIBILITY_SET", DiagnosticSeverity.Info,
                string.Format("Đã set visibility cho form Id={0}.", request.FormElementId)));
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                string.Format("Lỗi set visibility: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.set_visibility",
            Diagnostics = diagnostics,
            ChangedIds = new List<int> { request.FormElementId }
        };
    }

    // ── SUBCATEGORY CREATION ──

    internal ExecutionResult PreviewCreateSubcategory(PlatformServices services, Document doc, FamilyCreateSubcategoryRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        if (string.IsNullOrWhiteSpace(request.SubcategoryName))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                "SubcategoryName không được rỗng."));
        }
        else
        {
            // Check if subcategory already exists
            var familyCat = doc.OwnerFamily.FamilyCategory;
            var existing = FindSubcategory(familyCat, request.SubcategoryName);
            if (existing != null)
            {
                diagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCAT_EXISTS", DiagnosticSeverity.Warning,
                    string.Format("Subcategory '{0}' đã tồn tại.", request.SubcategoryName)));
            }
            else
            {
                diagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCAT_PREVIEW", DiagnosticSeverity.Info,
                    string.Format("Sẽ tạo subcategory '{0}' trong category '{1}'.",
                        request.SubcategoryName, familyCat.Name)));
            }
        }

        return new ExecutionResult
        {
            OperationName = "family.create_subcategory",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteCreateSubcategory(PlatformServices services, Document doc, FamilyCreateSubcategoryRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.create_subcategory", (_, txDiagnostics) =>
        {
            var familyCat = doc.OwnerFamily.FamilyCategory;
            var existing = FindSubcategory(familyCat, request.SubcategoryName);

            Category subcat;
            if (existing != null)
            {
                subcat = existing;
                txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCAT_EXISTS", DiagnosticSeverity.Info,
                    string.Format("Subcategory '{0}' ?? t?n t?i, s? c?p nh?t properties.", request.SubcategoryName)));
            }
            else
            {
                subcat = doc.Settings.Categories.NewSubcategory(familyCat, request.SubcategoryName);
                txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCAT_CREATED", DiagnosticSeverity.Info,
                    string.Format("?? t?o subcategory '{0}'.", request.SubcategoryName)));
            }

            changedIds.Add(checked((int)subcat.Id.Value));

            if (request.LineWeight > 0 && request.LineWeight <= 16)
            {
                try { subcat.SetLineWeight(request.LineWeight, GraphicsStyleType.Projection); }
                catch (Exception ex) { Trace.TraceWarning($"FamilyAuthoring: Failed to set line weight on subcategory: {ex.Message}"); }
            }

            if (request.LineColorR >= 0 && request.LineColorG >= 0 && request.LineColorB >= 0)
            {
                try
                {
                    subcat.LineColor = new Color(
                        (byte)Math.Min(255, request.LineColorR),
                        (byte)Math.Min(255, request.LineColorG),
                        (byte)Math.Min(255, request.LineColorB));
                }
                catch (Exception ex) { Trace.TraceWarning($"FamilyAuthoring: Failed to set line color on subcategory: {ex.Message}"); }
            }

            if (!string.IsNullOrWhiteSpace(request.MaterialName))
            {
                var material = FindMaterialByName(doc, request.MaterialName);
                if (material != null)
                {
                    try { subcat.Material = material; }
                    catch (Exception ex) { Trace.TraceWarning($"FamilyAuthoring: Failed to set material '{request.MaterialName}' on subcategory: {ex.Message}"); }
                }
                else
                {
                    txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_MATERIAL_NOT_FOUND", DiagnosticSeverity.Warning,
                        string.Format("Material '{0}' kh?ng t?m th?y trong document.", request.MaterialName)));
                }
            }
        });

        return new ExecutionResult
        {
            OperationName = "family.create_subcategory",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── MATERIAL BINDING ──

    internal ExecutionResult PreviewBindMaterial(PlatformServices services, Document doc, FamilyBindMaterialRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var element = doc.GetElement(new ElementId((long)request.FormElementId));
        if (element == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                string.Format("Form element Id={0} không tìm thấy.", request.FormElementId)));
        }

        if (!string.IsNullOrEmpty(request.MaterialParameterName))
        {
            var fm = doc.FamilyManager;
            var param = FindParameter(fm, request.MaterialParameterName);
            if (param == null)
            {
                diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Warning,
                    string.Format("Material parameter '{0}' chưa tồn tại.", request.MaterialParameterName)));
            }
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_MATERIAL_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ bind material parameter '{0}' vào form Id={1}{2}.",
                    request.MaterialParameterName, request.FormElementId,
                    string.IsNullOrEmpty(request.DefaultMaterialName) ? "" : ", default=" + request.DefaultMaterialName)));
        }

        return new ExecutionResult
        {
            OperationName = "family.bind_material",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteBindMaterial(PlatformServices services, Document doc, FamilyBindMaterialRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.bind_material", (_, txDiagnostics) =>
        {
            var element = doc.GetElement(new ElementId((long)request.FormElementId));
            if (element == null)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                    string.Format("Form element Id={0} kh?ng t?m th?y.", request.FormElementId)));
                return;
            }

            changedIds.Add(request.FormElementId);
            try
            {
                var materialParam = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM) ?? element.LookupParameter("Material");
                if (!string.IsNullOrEmpty(request.MaterialParameterName))
                {
                    var fm = doc.FamilyManager;
                    var familyParam = FindParameter(fm, request.MaterialParameterName);

                    if (familyParam != null && materialParam != null)
                    {
                        fm.AssociateElementParameterToFamilyParameter(materialParam, familyParam);
                        txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_MATERIAL_BOUND", DiagnosticSeverity.Info,
                            string.Format("?? bind material parameter '{0}' v?o form Id={1}.", request.MaterialParameterName, request.FormElementId)));
                    }
                    else if (familyParam == null)
                    {
                        txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Warning,
                            string.Format("Family parameter '{0}' kh?ng t?m th?y.", request.MaterialParameterName)));
                    }
                    else
                    {
                        txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Warning,
                            "Element kh?ng c? material parameter ?? bind."));
                    }
                }

                if (!string.IsNullOrEmpty(request.DefaultMaterialName))
                {
                    var material = FindMaterialByName(doc, request.DefaultMaterialName);
                    if (material != null && materialParam != null && !materialParam.IsReadOnly)
                    {
                        materialParam.Set(material.Id);
                        txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_MATERIAL_DEFAULT_SET", DiagnosticSeverity.Info,
                            string.Format("?? set default material '{0}'.", request.DefaultMaterialName)));
                    }
                }
            }
            catch (Exception ex)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                    string.Format("L?i bind material: {0}", ex.Message)));
            }
        });

        return new ExecutionResult
        {
            OperationName = "family.bind_material",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── PARAMETER-DRIVEN VISIBILITY ──

    internal ExecutionResult PreviewSetParameterVisibility(PlatformServices services, Document doc, FamilySetParameterVisibilityRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var element = doc.GetElement(new ElementId((long)request.FormElementId));
        if (element == null || !(element is GenericForm))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                string.Format("Form element Id={0} không tìm thấy hoặc không phải GenericForm.", request.FormElementId)));
        }

        if (string.IsNullOrWhiteSpace(request.VisibilityParameterName))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                "VisibilityParameterName không được rỗng."));
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_VIS_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ gắn Yes/No parameter '{0}' điều khiển visibility cho form Id={1} (default={2}).",
                    request.VisibilityParameterName, request.FormElementId, request.DefaultVisible)));
        }

        return new ExecutionResult
        {
            OperationName = "family.set_parameter_visibility",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteSetParameterVisibility(PlatformServices services, Document doc, FamilySetParameterVisibilityRequest request)
    {
        GuardFamilyDocument(doc);
        var changedIds = new List<int>();
        var diagnostics = TransactionHelper.RunSafe(doc, "family.set_parameter_visibility", (_, txDiagnostics) =>
        {
            var element = doc.GetElement(new ElementId((long)request.FormElementId));
            if (!(element is GenericForm form))
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                    string.Format("Form element Id={0} kh?ng ph?i GenericForm.", request.FormElementId)));
                return;
            }

            changedIds.Add(request.FormElementId);
            try
            {
                var fm = doc.FamilyManager;
                var param = FindParameter(fm, request.VisibilityParameterName);
                if (param == null)
                {
                    var group = ResolveParameterGroup("geometry");
                    param = fm.AddParameter(request.VisibilityParameterName, group, SpecTypeId.Boolean.YesNo, true);
                    try { fm.Set(param, request.DefaultVisible ? 1 : 0); }
                    catch (Exception ex) { Trace.TraceWarning($"FamilyAuthoring: Failed to set visibility parameter default: {ex.Message}"); }
                    txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_VIS_PARAM_CREATED", DiagnosticSeverity.Info,
                        string.Format("?? t?o Yes/No parameter '{0}'.", request.VisibilityParameterName)));
                }

                form.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM)?.Set(request.DefaultVisible ? 1 : 0);
                var elementVisParam = form.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
                if (elementVisParam != null)
                {
                    try
                    {
                        fm.AssociateElementParameterToFamilyParameter(elementVisParam, param);
                        txDiagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_VIS_SET", DiagnosticSeverity.Info,
                            string.Format("?? g?n parameter '{0}' ?i?u khi?n visibility cho form Id={1}.",
                                request.VisibilityParameterName, request.FormElementId)));
                    }
                    catch (Exception ex)
                    {
                        txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Warning,
                            string.Format("Kh?ng th? associate visibility parameter: {0}", ex.Message)));
                    }
                }
                else
                {
                    txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Warning,
                        "Form kh?ng c? Visible parameter ?? associate."));
                }
            }
            catch (Exception ex)
            {
                txDiagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyVisibilityInvalid, DiagnosticSeverity.Error,
                    string.Format("L?i set parameter visibility: {0}", ex.Message)));
            }
        });

        return new ExecutionResult
        {
            OperationName = "family.set_parameter_visibility",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ══════════════════════════════════════════════════════════════
    // TIER 3: Spline Extrusion, Shared Parameter, Category
    // ══════════════════════════════════════════════════════════════

    // ── SPLINE EXTRUSION ──

    internal ExecutionResult PreviewCreateSplineExtrusion(PlatformServices services, Document doc, FamilyCreateSplineExtrusionRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        if (request.ControlPoints == null || request.ControlPoints.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyProfileInvalid, DiagnosticSeverity.Error,
                "Spline cần ít nhất 3 control points."));
        }

        var validTypes = new[] { "hermite", "nurbs" };
        if (!validTypes.Contains(request.SplineType?.ToLowerInvariant()))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyProfileInvalid, DiagnosticSeverity.Error,
                string.Format("SplineType '{0}' không hợp lệ. Phải là: hermite, nurbs.", request.SplineType)));
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SPLINE_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ tạo {0} spline extrusion ({1} points, start={2:F2}, end={3:F2}, void={4}).",
                    request.SplineType, request.ControlPoints?.Count ?? 0, request.StartOffset, request.EndOffset, request.IsVoid)));
        }

        return new ExecutionResult
        {
            OperationName = "family.create_spline_extrusion",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteCreateSplineExtrusion(PlatformServices services, Document doc, FamilyCreateSplineExtrusionRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();
        var changedIds = new List<int>();

        if (request.ControlPoints == null || request.ControlPoints.Count < 3)
        {
            return new ExecutionResult
            {
                OperationName = "family.create_spline_extrusion",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyProfileInvalid, DiagnosticSeverity.Error,
                        "Spline cần ít nhất 3 control points.")
                }
            };
        }

        try
        {
            var sketchPlane = ResolveOrCreateSketchPlane(doc, request.SketchPlaneName);
            var isSolid = !request.IsVoid;

            // Build spline curve
            var xyzPoints = request.ControlPoints
                .Select(p => new XYZ(p.X, p.Y, 0))
                .ToList();

            Curve splineCurve;
            var splineType = (request.SplineType ?? "hermite").ToLowerInvariant();

            if (splineType == "nurbs" && request.Weights?.Count == xyzPoints.Count)
            {
                // NURBS spline with weights
                var controlPts = xyzPoints;
                var weights = request.Weights;
                var degree = Math.Max(1, Math.Min(request.Degree, xyzPoints.Count - 1));

                // Build uniform knot vector
                var knotCount = controlPts.Count + degree + 1;
                var knots = new List<double>();
                for (var k = 0; k < knotCount; k++)
                {
                    knots.Add((double)k / (knotCount - 1));
                }

                splineCurve = NurbSpline.CreateCurve(degree, knots, controlPts, weights);
            }
            else
            {
                // Hermite spline (default)
                // Hermite spline
                var tangents = new HermiteSplineTangents
                {
                    StartTangent = new XYZ(1, 0, 0),
                    EndTangent = new XYZ(1, 0, 0)
                };
                splineCurve = HermiteSpline.Create(xyzPoints, request.IsClosed, tangents);
            }

            // Build profile from spline
            var curveArr = new CurveArray();
            curveArr.Append(splineCurve);

            // If not closed, close with a line segment
            if (!request.IsClosed)
            {
                var closeLine = Line.CreateBound(xyzPoints.Last(), xyzPoints.First());
                if (closeLine.Length > 1e-9)
                {
                    curveArr.Append(closeLine);
                }
            }

            var profileLoops = new CurveArrArray();
            profileLoops.Append(curveArr);

            var extrusion = doc.FamilyCreate.NewExtrusion(isSolid, profileLoops, sketchPlane, request.EndOffset);

            if (Math.Abs(request.StartOffset) > 1e-9)
            {
                var offsetParam = extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM);
                if (offsetParam != null) offsetParam.Set(request.StartOffset);
            }

            changedIds.Add(checked((int)extrusion.Id.Value));

            // Set subcategory if provided
            if (!string.IsNullOrWhiteSpace(request.SubcategoryName))
            {
                TrySetSubcategory(doc, extrusion, request.SubcategoryName, diagnostics);
            }

            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SPLINE_CREATED", DiagnosticSeverity.Info,
                string.Format("Đã tạo spline extrusion (Id={0}, {1} points).",
                    extrusion.Id.Value, request.ControlPoints.Count)));
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyGeometryInvalid, DiagnosticSeverity.Error,
                string.Format("Lỗi tạo spline extrusion: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.create_spline_extrusion",
            Diagnostics = diagnostics,
            ChangedIds = changedIds
        };
    }

    // ── SHARED PARAMETER ──

    internal ExecutionResult PreviewAddSharedParameter(PlatformServices services, Document doc, FamilyAddSharedParameterRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        if (string.IsNullOrWhiteSpace(request.SharedParameterFilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilySharedParamFileNotFound, DiagnosticSeverity.Error,
                "SharedParameterFilePath không được rỗng."));
        }
        else if (!File.Exists(request.SharedParameterFilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilySharedParamFileNotFound, DiagnosticSeverity.Error,
                string.Format("File shared parameter '{0}' không tồn tại.", request.SharedParameterFilePath)));
        }

        if (string.IsNullOrWhiteSpace(request.ParameterName))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Error,
                "ParameterName không được rỗng."));
        }

        if (request.ReplaceExisting)
        {
            var fm = doc.FamilyManager;
            var existing = FindParameter(fm, request.ParameterName);
            if (existing != null)
            {
                diagnostics.Add(DiagnosticRecord.Create("FAMILY_SHARED_PARAM_REPLACE", DiagnosticSeverity.Warning,
                    string.Format("Family parameter '{0}' sẽ bị thay thế bằng shared parameter.", request.ParameterName)));
            }
        }

        if (diagnostics.All(d => d.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SHARED_PARAM_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ add shared parameter '{0}' từ group '{1}' (file: {2}), isInstance={3}.",
                    request.ParameterName, request.GroupName, Path.GetFileName(request.SharedParameterFilePath), request.IsInstance)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_shared_parameter",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteAddSharedParameter(PlatformServices services, Document doc, FamilyAddSharedParameterRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        if (!File.Exists(request.SharedParameterFilePath))
        {
            return new ExecutionResult
            {
                OperationName = "family.add_shared_parameter",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilySharedParamFileNotFound, DiagnosticSeverity.Error,
                        string.Format("File shared parameter '{0}' không tồn tại.", request.SharedParameterFilePath))
                }
            };
        }

        try
        {
            var app = doc.Application;

            // Save current shared parameter file path (to restore later)
            var previousSharedParamFile = app.SharedParametersFilename;

            // Set the shared parameter file
            app.SharedParametersFilename = request.SharedParameterFilePath;
            var defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                return new ExecutionResult
                {
                    OperationName = "family.add_shared_parameter",
                    Diagnostics = new List<DiagnosticRecord>
                    {
                        DiagnosticRecord.Create(StatusCodes.FamilySharedParamFileNotFound, DiagnosticSeverity.Error,
                            "Không thể mở shared parameter file.")
                    }
                };
            }

            // Find the group and definition
            ExternalDefinition? extDef = null;
            foreach (var group in defFile.Groups)
            {
                if (string.Equals(group.Name, request.GroupName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (ExternalDefinition def in group.Definitions)
                    {
                        if (string.Equals(def.Name, request.ParameterName, StringComparison.OrdinalIgnoreCase))
                        {
                            extDef = def;
                            break;
                        }
                    }
                    break;
                }
            }

            // Restore previous shared parameter file
            if (!string.IsNullOrEmpty(previousSharedParamFile) && File.Exists(previousSharedParamFile))
            {
                app.SharedParametersFilename = previousSharedParamFile;
            }

            if (extDef == null)
            {
                return new ExecutionResult
                {
                    OperationName = "family.add_shared_parameter",
                    Diagnostics = new List<DiagnosticRecord>
                    {
                        DiagnosticRecord.Create(StatusCodes.FamilyParameterNotFound, DiagnosticSeverity.Error,
                            string.Format("Shared parameter '{0}' không tìm thấy trong group '{1}'.",
                                request.ParameterName, request.GroupName))
                    }
                };
            }

            var fm = doc.FamilyManager;
            var paramGroup = ResolveParameterGroup(request.ParameterGroup);

            if (request.ReplaceExisting)
            {
                var existingParam = FindParameter(fm, request.ParameterName);
                if (existingParam != null)
                {
                    fm.ReplaceParameter(existingParam, extDef, paramGroup, request.IsInstance);
                    diagnostics.Add(DiagnosticRecord.Create("FAMILY_SHARED_PARAM_REPLACED", DiagnosticSeverity.Info,
                        string.Format("Đã replace family parameter '{0}' bằng shared parameter.", request.ParameterName)));
                }
                else
                {
                    fm.AddParameter(extDef, paramGroup, request.IsInstance);
                    diagnostics.Add(DiagnosticRecord.Create("FAMILY_SHARED_PARAM_ADDED", DiagnosticSeverity.Info,
                        string.Format("Đã add shared parameter '{0}'.", request.ParameterName)));
                }
            }
            else
            {
                fm.AddParameter(extDef, paramGroup, request.IsInstance);
                diagnostics.Add(DiagnosticRecord.Create("FAMILY_SHARED_PARAM_ADDED", DiagnosticSeverity.Info,
                    string.Format("Đã add shared parameter '{0}'.", request.ParameterName)));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilySharedParamFileNotFound, DiagnosticSeverity.Error,
                string.Format("Lỗi add shared parameter: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.add_shared_parameter",
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    // ── CATEGORY ──

    internal ExecutionResult PreviewSetCategory(PlatformServices services, Document doc, FamilySetCategoryRequest request, ToolRequestEnvelope envelope)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var currentCat = doc.OwnerFamily.FamilyCategory;
        var targetCat = ResolveCategory(doc, request);

        if (targetCat == null)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyCategoryInvalid, DiagnosticSeverity.Error,
                string.Format("Category '{0}' không tìm thấy hoặc không hợp lệ.",
                    string.IsNullOrEmpty(request.CategoryName) ? "id=" + request.BuiltInCategoryId : request.CategoryName)));
        }
        else
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_CATEGORY_PREVIEW", DiagnosticSeverity.Info,
                string.Format("Sẽ đổi category từ '{0}' sang '{1}'.",
                    currentCat?.Name ?? "(none)", targetCat.Name)));
        }

        return new ExecutionResult
        {
            OperationName = "family.set_category",
            DryRun = true,
            ConfirmationRequired = true,
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    internal ExecutionResult ExecuteSetCategory(PlatformServices services, Document doc, FamilySetCategoryRequest request)
    {
        GuardFamilyDocument(doc);
        var diagnostics = new List<DiagnosticRecord>();

        var targetCat = ResolveCategory(doc, request);
        if (targetCat == null)
        {
            return new ExecutionResult
            {
                OperationName = "family.set_category",
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create(StatusCodes.FamilyCategoryInvalid, DiagnosticSeverity.Error,
                        string.Format("Category '{0}' không tìm thấy.", request.CategoryName))
                }
            };
        }

        try
        {
            var previousCat = doc.OwnerFamily.FamilyCategory?.Name ?? "(none)";
            doc.OwnerFamily.FamilyCategory = targetCat;

            diagnostics.Add(DiagnosticRecord.Create("FAMILY_CATEGORY_SET", DiagnosticSeverity.Info,
                string.Format("Đã đổi category từ '{0}' sang '{1}'.", previousCat, targetCat.Name)));
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyCategoryInvalid, DiagnosticSeverity.Error,
                string.Format("Lỗi set category: {0}", ex.Message)));
        }

        return new ExecutionResult
        {
            OperationName = "family.set_category",
            Diagnostics = diagnostics,
            ChangedIds = new List<int>()
        };
    }

    // ══════════════════════════════════════════════════════════════
    // NEW HELPERS (Tier 1-3 support)
    // ══════════════════════════════════════════════════════════════

    private static ReferencePlane? FindReferencePlane(Document doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(ReferencePlane))
            .Cast<ReferencePlane>()
            .FirstOrDefault(rp => string.Equals(rp.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<View> GetDimensionViewCandidates(Document doc, ReferencePlane rp1, ReferencePlane rp2)
    {
        var prefersElevation = Math.Abs(rp1.GetPlane().Normal.Z) > 0.9 && Math.Abs(rp2.GetPlane().Normal.Z) > 0.9;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate
                && v.ViewType != ViewType.Schedule
                && v.ViewType != ViewType.DrawingSheet
                && v.ViewType != ViewType.ThreeD)
            .OrderByDescending(v => ScoreDimensionView(v, prefersElevation))
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreDimensionView(View view, bool prefersElevation)
    {
        var score = 0;
        if (prefersElevation)
        {
            if (view.ViewType == ViewType.Elevation || view.ViewType == ViewType.Section)
            {
                score += 100;
            }
            else if (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.CeilingPlan)
            {
                score += 10;
            }
        }
        else
        {
            if (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.CeilingPlan)
            {
                score += 100;
            }
            else if (view.ViewType == ViewType.Elevation || view.ViewType == ViewType.Section)
            {
                score += 10;
            }
        }

        var name = view.Name ?? string.Empty;
        if (name.IndexOf("Ref", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 20;
        }

        if (name.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 15;
        }

        return score;
    }

    private static IEnumerable<Line> BuildDimensionLineCandidates(View view, XYZ start, XYZ end)
    {
        var measurement = end - start;
        if (measurement.GetLength() < 1e-9)
        {
            yield return Line.CreateBound(start, end);
            yield break;
        }

        var measurementDir = measurement.Normalize();
        var viewDir = view.ViewDirection;
        var candidateOffsets = new List<XYZ>
        {
            viewDir.CrossProduct(measurementDir),
            measurementDir.CrossProduct(viewDir),
            XYZ.BasisZ.CrossProduct(measurementDir),
            measurementDir.CrossProduct(XYZ.BasisZ),
            XYZ.BasisX.CrossProduct(measurementDir),
            measurementDir.CrossProduct(XYZ.BasisX),
            XYZ.BasisY.CrossProduct(measurementDir),
            measurementDir.CrossProduct(XYZ.BasisY)
        };

        var offsetDistance = 1.0
            + Math.Max(
                Math.Max(Math.Max(Math.Abs(start.X), Math.Abs(start.Y)), Math.Abs(start.Z)),
                Math.Max(Math.Max(Math.Abs(end.X), Math.Abs(end.Y)), Math.Abs(end.Z)));

        foreach (var offsetDir in candidateOffsets)
        {
            if (offsetDir == null || offsetDir.GetLength() < 1e-9)
            {
                continue;
            }

            var normalized = offsetDir.Normalize().Multiply(offsetDistance);
            yield return Line.CreateBound(start + normalized, end + normalized);
            yield return Line.CreateBound(start - normalized, end - normalized);
        }

        yield return Line.CreateBound(start, end);
    }

    private static Reference? GetGeometryFaceReference(Document doc, Element element, int faceIndex)
    {
        try
        {
            var geomObj = element.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = true });
            if (geomObj == null) return null;

            var faces = new List<Face>();
            foreach (var go in geomObj)
            {
                if (go is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        faces.Add(face);
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    foreach (var innerGo in gi.GetInstanceGeometry())
                    {
                        if (innerGo is Solid innerSolid && innerSolid.Faces.Size > 0)
                        {
                            foreach (Face face in innerSolid.Faces)
                            {
                                faces.Add(face);
                            }
                        }
                    }
                }
            }

            if (faceIndex >= 0 && faceIndex < faces.Count)
            {
                return faces[faceIndex].Reference;
            }

            // Return first face reference if index out of range
            return faces.Count > 0 ? faces[0].Reference : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Category? FindSubcategory(Category parentCategory, string name)
    {
        if (parentCategory?.SubCategories == null || string.IsNullOrWhiteSpace(name)) return null;

        foreach (Category subCat in parentCategory.SubCategories)
        {
            if (string.Equals(subCat.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return subCat;
            }
        }

        return null;
    }

    private static Material? FindMaterialByName(Document doc, string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return null;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(m => string.Equals(m.Name, materialName, StringComparison.OrdinalIgnoreCase));
    }

    private static void TrySetSubcategory(Document doc, GenericForm form, string subcategoryName, List<DiagnosticRecord> diagnostics)
    {
        try
        {
            var familyCat = doc.OwnerFamily.FamilyCategory;
            var subcat = FindSubcategory(familyCat, subcategoryName);
            if (subcat != null)
            {
                var subcatParam = form.get_Parameter(BuiltInParameter.FAMILY_ELEM_SUBCATEGORY);
                if (subcatParam != null && !subcatParam.IsReadOnly)
                {
                    subcatParam.Set(subcat.Id);
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCAT_ASSIGN_FAILED", DiagnosticSeverity.Warning,
                string.Format("Không thể gán subcategory '{0}': {1}", subcategoryName, ex.Message)));
        }
    }

    private static Category? ResolveCategory(Document doc, FamilySetCategoryRequest request)
    {
        // Try by BuiltInCategory ID first
        if (request.BuiltInCategoryId > 0)
        {
            try
            {
                var bic = (BuiltInCategory)request.BuiltInCategoryId;
                return doc.Settings.Categories.get_Item(bic);
            }
            catch (Exception) { /* Invalid BuiltInCategory */ }
        }

        // Then try by name
        if (!string.IsNullOrWhiteSpace(request.CategoryName))
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (string.Equals(cat.Name, request.CategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return cat;
                }
            }
        }

        return null;
    }

    private static DuctSystemType ResolveDuctSystemType(string classification)
    {
        switch ((classification ?? "").ToLowerInvariant())
        {
            case "supply": return DuctSystemType.SupplyAir;
            case "return": return DuctSystemType.ReturnAir;
            case "exhaust": return DuctSystemType.ExhaustAir;
            default: return DuctSystemType.OtherAir;
        }
    }

    private static PipeSystemType ResolvePipeSystemType(string classification)
    {
        switch ((classification ?? "").ToLowerInvariant())
        {
            case "supply": return PipeSystemType.SupplyHydronic;
            case "return": return PipeSystemType.ReturnHydronic;
            case "domestic_hot": return PipeSystemType.DomesticHotWater;
            case "domestic_cold": return PipeSystemType.DomesticColdWater;
            case "sanitary": return PipeSystemType.Sanitary;
            default: return PipeSystemType.OtherPipe;
        }
    }

    private static ConnectorProfileType ResolveDuctConnectorProfileType(string profile)
    {
        switch ((profile ?? "").ToLowerInvariant())
        {
            case "round": return ConnectorProfileType.Round;
            case "rectangular": return ConnectorProfileType.Rectangular;
            case "oval": return ConnectorProfileType.Oval;
            default: return ConnectorProfileType.Round;
        }
    }

    private static ElectricalSystemType ResolveElectricalSystemType(string classification)
    {
        switch ((classification ?? "").ToLowerInvariant())
        {
            case "power": return ElectricalSystemType.PowerCircuit;
            case "data": return ElectricalSystemType.Data;
            case "telephone": return ElectricalSystemType.Telephone;
            case "fire_alarm": return ElectricalSystemType.FireAlarm;
            case "security": return ElectricalSystemType.Security;
            default: return ElectricalSystemType.PowerCircuit;
        }
    }

    private static XYZ ResolveDirection(string direction)
    {
        switch ((direction ?? "").ToLowerInvariant())
        {
            case "x": return XYZ.BasisX;
            case "y": return XYZ.BasisY;
            case "z": return XYZ.BasisZ;
            case "negx": return -XYZ.BasisX;
            case "negy": return -XYZ.BasisY;
            case "negz": return -XYZ.BasisZ;
            default: return XYZ.BasisZ;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // CREATE FAMILY DOCUMENT FROM TEMPLATE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Preview: validate template exists, save path writable.
    /// Chạy trên UI thread nhưng không tạo file thật.
    /// </summary>
    internal ExecutionResult PreviewCreateDocument(
        Autodesk.Revit.UI.UIApplication uiapp,
        FamilyCreateDocumentRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var templatePath = ResolveTemplatePath(uiapp.Application, request, diagnostics);

        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return new ExecutionResult
            {
                OperationName = "family.create_document",
                Diagnostics = diagnostics
            };
        }

        // Validate save path
        if (string.IsNullOrWhiteSpace(request.SaveAsPath))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilySavePathInvalid, DiagnosticSeverity.Error,
                "SaveAsPath là bắt buộc. Ví dụ: \"C:\\Families\\My_Transition.rfa\""));
            return new ExecutionResult { OperationName = "family.create_document", Diagnostics = diagnostics };
        }

        var saveDir = Path.GetDirectoryName(request.SaveAsPath);
        if (!string.IsNullOrWhiteSpace(saveDir) && !Directory.Exists(saveDir))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_DIR_WILL_CREATE", DiagnosticSeverity.Info,
                string.Format("Thư mục '{0}' chưa tồn tại — sẽ tạo tự động.", saveDir)));
        }

        if (!request.SaveAsPath.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_PATH_HINT", DiagnosticSeverity.Warning,
                "SaveAsPath nên có đuôi .rfa."));
        }

        diagnostics.Add(DiagnosticRecord.Create("FAMILY_CREATE_DOC_PREVIEW", DiagnosticSeverity.Info,
            string.Format("Sẽ tạo family mới từ template '{0}', lưu tại '{1}', ActivateInUI={2}.",
                Path.GetFileName(templatePath), request.SaveAsPath, request.ActivateInUI)));

        return new ExecutionResult
        {
            OperationName = "family.create_document",
            Diagnostics = diagnostics
        };
    }

    /// <summary>
    /// Execute: tạo family document mới từ template, save, activate trong Revit UI.
    /// </summary>
    internal ExecutionResult ExecuteCreateDocument(
        Autodesk.Revit.UI.UIApplication uiapp,
        FamilyCreateDocumentRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var templatePath = ResolveTemplatePath(uiapp.Application, request, diagnostics);

        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return new ExecutionResult
            {
                OperationName = "family.create_document",
                Diagnostics = diagnostics
            };
        }

        // Validate save path
        if (string.IsNullOrWhiteSpace(request.SaveAsPath))
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilySavePathInvalid, DiagnosticSeverity.Error,
                "SaveAsPath là bắt buộc."));
            return new ExecutionResult { OperationName = "family.create_document", Diagnostics = diagnostics };
        }

        // Ensure directory exists
        var saveDir = Path.GetDirectoryName(request.SaveAsPath);
        if (!string.IsNullOrWhiteSpace(saveDir) && !Directory.Exists(saveDir))
        {
            Directory.CreateDirectory(saveDir);
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_DIR_CREATED", DiagnosticSeverity.Info,
                string.Format("Đã tạo thư mục '{0}'.", saveDir)));
        }

        Document? familyDoc = null;
        try
        {
            // Step 1: Create new family document from template
            familyDoc = uiapp.Application.NewFamilyDocument(templatePath);
            if (familyDoc == null || !familyDoc.IsFamilyDocument)
            {
                diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyCreateDocumentFailed, DiagnosticSeverity.Error,
                    string.Format("NewFamilyDocument trả về null hoặc không phải family document. Template: {0}", templatePath)));
                return new ExecutionResult { OperationName = "family.create_document", Diagnostics = diagnostics };
            }

            diagnostics.Add(DiagnosticRecord.Create("FAMILY_DOC_CREATED", DiagnosticSeverity.Info,
                string.Format("Đã tạo family document từ '{0}'.", Path.GetFileName(templatePath))));

            // Step 2: Save to disk
            var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
            familyDoc.SaveAs(request.SaveAsPath, saveOpts);
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_DOC_SAVED", DiagnosticSeverity.Info,
                string.Format("Đã lưu tại '{0}'.", request.SaveAsPath)));

            // Step 3: Close the in-memory document
            familyDoc.Close(false);
            familyDoc = null;

            // Step 4: Open and activate in UI (if requested)
            if (request.ActivateInUI)
            {
                uiapp.OpenAndActivateDocument(request.SaveAsPath);
                diagnostics.Add(DiagnosticRecord.Create("FAMILY_DOC_ACTIVATED", DiagnosticSeverity.Info,
                    "Family document đã được mở và active trong Revit UI. Các tool family.* giờ sẽ thao tác trên document này."));
            }

            return new ExecutionResult
            {
                OperationName = "family.create_document",
                Diagnostics = diagnostics,
                ChangedIds = new List<int>()
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyCreateDocumentFailed, DiagnosticSeverity.Error,
                string.Format("Lỗi tạo family document: {0}", ex.Message)));

            // Cleanup: close doc nếu còn mở
            try { familyDoc?.Close(false); } catch { /* best effort */ }

            return new ExecutionResult { OperationName = "family.create_document", Diagnostics = diagnostics };
        }
    }

    // ── Template Resolution ──

    /// <summary>
    /// Resolve family template (.rft) path based on category hint and Revit version.
    /// Search order: ProgramData/Autodesk/RVT {version}/Family Templates/
    /// Priority: Metric → English → loose search.
    /// </summary>
    private static string? ResolveTemplatePath(
        Autodesk.Revit.ApplicationServices.Application application,
        FamilyCreateDocumentRequest request,
        ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.Equals(request.TemplateCategory, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(request.CustomTemplatePath))
            {
                return request.CustomTemplatePath;
            }

            diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyTemplateNotFound, DiagnosticSeverity.Error,
                string.Format("Custom template kh?ng t?m th?y: '{0}'.", request.CustomTemplatePath)));
            return null;
        }

        var isMetric = string.Equals(request.UnitSystem, "metric", StringComparison.OrdinalIgnoreCase);
        var unitPrefix = isMetric ? "Metric" : string.Empty;
        var categoryKeywords = ResolveCategoryKeywords(request.TemplateCategory);

        var version = application.VersionNumber ?? "2024";
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "RVT " + version, "Family Templates"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "RVT " + version, "Family Templates", "English"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "RVT " + version, "Family Templates", "English_I")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var keyword in categoryKeywords)
            {
                var exactName = string.IsNullOrWhiteSpace(unitPrefix)
                    ? keyword + ".rft"
                    : unitPrefix + " " + keyword + ".rft";

                var exact = Path.Combine(root, exactName);
                if (File.Exists(exact)) return exact;

                try
                {
                    var recursiveExact = Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)
                        .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), exactName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(recursiveExact)) return recursiveExact;
                }
                catch (Exception)
                {
                    // best effort
                }
            }

            try
            {
                foreach (var keyword in categoryKeywords)
                {
                    var match = Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)
                        .Where(candidate =>
                        {
                            var name = Path.GetFileName(candidate);
                            var hasKeyword = name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                            var hasUnit = string.IsNullOrWhiteSpace(unitPrefix)
                                || name.IndexOf(unitPrefix, StringComparison.OrdinalIgnoreCase) >= 0;
                            return hasKeyword && hasUnit && !IsDisallowedTemplateVariant(name, request.TemplateCategory);
                        })
                        .OrderByDescending(candidate => ScoreTemplateCandidate(candidate, keyword, request.TemplateCategory, unitPrefix))
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(match)) return match;
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var fallback = Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)
                    .Where(candidate => Path.GetFileName(candidate).IndexOf("Generic Model", StringComparison.OrdinalIgnoreCase) >= 0
                                        && !IsDisallowedTemplateVariant(Path.GetFileName(candidate), "generic_model"))
                    .OrderByDescending(candidate => ScoreTemplateCandidate(candidate, "Generic Model", "generic_model", unitPrefix))
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    diagnostics.Add(DiagnosticRecord.Create("FAMILY_TEMPLATE_FALLBACK", DiagnosticSeverity.Warning,
                        string.Format("Kh?ng t?m th?y template cho '{0}', fallback sang '{1}'.",
                            request.TemplateCategory, Path.GetFileName(fallback))));
                    return fallback;
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        diagnostics.Add(DiagnosticRecord.Create(StatusCodes.FamilyTemplateNotFound, DiagnosticSeverity.Error,
            string.Format("Kh?ng t?m th?y family template n?o cho category '{0}' (unit={1}). Ki?m tra th? m?c Family Templates trong ProgramData/Autodesk/RVT {2}/.",
                request.TemplateCategory, request.UnitSystem, version)));
        return null;
    }

    private static bool IsDisallowedTemplateVariant(string fileName, string templateCategory)
    {
        var name = fileName ?? string.Empty;
        if (name.IndexOf("face based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (name.IndexOf("adaptive", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (string.Equals(templateCategory, "generic_model", StringComparison.OrdinalIgnoreCase))
        {
            if (name.IndexOf("line based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("pattern based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("ceiling based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("floor based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("roof based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("wall based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("two level based", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    private static int ScoreTemplateCandidate(string path, string keyword, string templateCategory, string unitPrefix)
    {
        var name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        var score = 0;

        if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
        if (!string.IsNullOrWhiteSpace(unitPrefix) && name.IndexOf(unitPrefix, StringComparison.OrdinalIgnoreCase) >= 0) score += 20;

        if (string.Equals(templateCategory, "generic_model", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(name, "Metric Generic Model", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Generic Model", StringComparison.OrdinalIgnoreCase)))
        {
            score += 500;
        }

        if (name.IndexOf("face based", StringComparison.OrdinalIgnoreCase) >= 0) score -= 200;
        if (name.IndexOf("adaptive", StringComparison.OrdinalIgnoreCase) >= 0) score -= 200;
        if (name.IndexOf("pattern based", StringComparison.OrdinalIgnoreCase) >= 0) score -= 100;
        if (name.IndexOf("line based", StringComparison.OrdinalIgnoreCase) >= 0) score -= 100;

        return score;
    }

    private static string[] ResolveCategoryKeywords(string templateCategory)
    {
        switch ((templateCategory ?? "generic_model").ToLowerInvariant())
        {
            case "generic_model":           return new[] { "Generic Model" };
            case "duct_fitting":            return new[] { "Duct Fitting", "Duct Fittings" };
            case "pipe_fitting":            return new[] { "Pipe Fitting", "Pipe Fittings" };
            case "mechanical_equipment":    return new[] { "Mechanical Equipment" };
            case "electrical_fixture":      return new[] { "Electrical Fixture", "Electrical Equipment" };
            case "structural_framing":      return new[] { "Structural Framing", "Structural Column" };
            case "conduit_fitting":         return new[] { "Conduit Fitting", "Conduit Fittings" };
            case "cable_tray_fitting":      return new[] { "Cable Tray Fitting" };
            case "plumbing_fixture":        return new[] { "Plumbing Fixture" };
            case "sprinkler":               return new[] { "Sprinkler" };
            default:                        return new[] { "Generic Model" };
        }
    }

    /// <summary>IFamilyLoadOptions implementation that allows overwriting existing families.</summary>
    private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
