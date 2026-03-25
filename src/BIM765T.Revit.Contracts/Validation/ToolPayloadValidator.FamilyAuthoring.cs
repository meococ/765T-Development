using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateFamilyLoad(FamilyLoadRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.LibraryRootName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_LIBRARY_ROOT_REQUIRED", DiagnosticSeverity.Error, "LibraryRootName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.RelativeFamilyPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_RELATIVE_PATH_REQUIRED", DiagnosticSeverity.Error, "RelativeFamilyPath không được rỗng."));
        }

        if (Path.IsPathRooted(request.RelativeFamilyPath ?? string.Empty))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_RELATIVE_PATH_NOT_RELATIVE", DiagnosticSeverity.Error, "RelativeFamilyPath phải là relative path dưới library root."));
        }
    }

    private static void ValidateScheduleCreate(ScheduleCreateRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_NAME_REQUIRED", DiagnosticSeverity.Error, "ScheduleName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.CategoryName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_CATEGORY_REQUIRED", DiagnosticSeverity.Error, "CategoryName không được rỗng."));
        }

        RequireNonEmpty(request.Fields, "SCHEDULE_FIELDS_EMPTY", "Fields phải có ít nhất 1 item.", diagnostics);

        if (request.MaxFieldCount <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_MAX_FIELD_COUNT_INVALID", DiagnosticSeverity.Error, "MaxFieldCount phải > 0."));
        }
    }

    private static void ValidateOutputTargetValidation(OutputTargetValidationRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.OperationKind))
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_OPERATION_KIND_REQUIRED", DiagnosticSeverity.Error, "OperationKind không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.OutputRootName))
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_ROOT_REQUIRED", DiagnosticSeverity.Error, "OutputRootName không được rỗng."));
        }

        if (Path.IsPathRooted(request.RelativePath ?? string.Empty))
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_RELATIVE_PATH_NOT_RELATIVE", DiagnosticSeverity.Error, "RelativePath phải là relative path."));
        }
    }

    private static void ValidateIfcExport(IfcExportRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateExportBase(request.PresetName, request.OutputRootName, request.RelativeOutputPath, request.FileName, diagnostics);
    }

    private static void ValidateDwgExport(DwgExportRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateExportBase(request.PresetName, request.OutputRootName, request.RelativeOutputPath, request.FileName, diagnostics);
        if ((request.ViewIds == null || request.ViewIds.Count == 0) &&
            (request.SheetIds == null || request.SheetIds.Count == 0) &&
            !request.UseActiveViewWhenEmpty)
        {
            diagnostics.Add(DiagnosticRecord.Create("DWG_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Cần ViewIds/SheetIds hoặc UseActiveViewWhenEmpty=true."));
        }
    }

    private static void ValidatePdfPrint(PdfPrintRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateExportBase(request.PresetName, request.OutputRootName, request.RelativeOutputPath, request.FileName, diagnostics);
        if ((request.SheetIds == null || request.SheetIds.Count == 0) &&
            (request.SheetNumbers == null || request.SheetNumbers.Count == 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("PDF_SHEET_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Cần SheetIds hoặc SheetNumbers."));
        }
    }

    private static void ValidateExportBase(string presetName, string outputRootName, string relativeOutputPath, string fileName, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("EXPORT_PRESET_REQUIRED", DiagnosticSeverity.Error, "PresetName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(outputRootName))
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_ROOT_REQUIRED", DiagnosticSeverity.Error, "OutputRootName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            diagnostics.Add(DiagnosticRecord.Create("EXPORT_FILE_NAME_REQUIRED", DiagnosticSeverity.Error, "FileName không được rỗng."));
        }

        if (Path.IsPathRooted(relativeOutputPath ?? string.Empty))
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_RELATIVE_PATH_NOT_RELATIVE", DiagnosticSeverity.Error, "RelativeOutputPath phải là relative path."));
        }
    }

    private static void ValidateHullDryRun(HullDryRunRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (double.IsNaN(request.InOffsetInch) || double.IsInfinity(request.InOffsetInch))
        {
            diagnostics.Add(DiagnosticRecord.Create("HULL_OFFSET_INVALID", DiagnosticSeverity.Error, "InOffsetInch không hợp lệ."));
        }
    }

    // ── Family Authoring validators ──

    private static void ValidateFamilyAddParameter(FamilyAddParameterRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParameterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_NAME_REQUIRED", DiagnosticSeverity.Error, "ParameterName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.ParameterType))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_TYPE_REQUIRED", DiagnosticSeverity.Error, "ParameterType không được rỗng."));
        }
    }

    private static void ValidateFamilySetFormula(FamilySetFormulaRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParameterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PARAM_NAME_REQUIRED", DiagnosticSeverity.Error, "ParameterName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.Formula))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_FORMULA_REQUIRED", DiagnosticSeverity.Error, "Formula không được rỗng."));
        }
    }

    private static void ValidateFamilySetTypeCatalog(FamilySetTypeCatalogRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.Types, "FAMILY_TYPES_EMPTY", "Types phải có ít nhất 1 entry.", diagnostics);
        if (request.Types != null)
        {
            foreach (var entry in request.Types)
            {
                if (entry != null && string.IsNullOrWhiteSpace(entry.TypeName))
                {
                    diagnostics.Add(DiagnosticRecord.Create("FAMILY_TYPE_NAME_REQUIRED", DiagnosticSeverity.Error, "TypeName không được rỗng."));
                }
            }
        }
    }

    private static void ValidateFamilyAddReferencePlane(FamilyAddReferencePlaneRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_REFPLANE_NAME_REQUIRED", DiagnosticSeverity.Error, "Name không được rỗng."));
        }

        if (request.ExtentFeet <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_REFPLANE_EXTENT_INVALID", DiagnosticSeverity.Error, "ExtentFeet phải > 0."));
        }

        if (Math.Abs(request.NormalX) < 1e-9 && Math.Abs(request.NormalY) < 1e-9 && Math.Abs(request.NormalZ) < 1e-9)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_REFPLANE_NORMAL_ZERO", DiagnosticSeverity.Error, "Normal vector không được là (0,0,0)."));
        }
    }

    private static void ValidateFamilyCreateExtrusion(FamilyCreateExtrusionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.Profile == null || request.Profile.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PROFILE_MIN_POINTS", DiagnosticSeverity.Error, "Profile phải có ít nhất 3 điểm."));
        }

        if (Math.Abs(request.EndOffset - request.StartOffset) < 1e-9)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_EXTRUSION_HEIGHT_ZERO", DiagnosticSeverity.Error, "EndOffset - StartOffset không được bằng 0."));
        }
    }

    private static void ValidateFamilyCreateSweep(FamilyCreateSweepRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.Profile == null || request.Profile.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PROFILE_MIN_POINTS", DiagnosticSeverity.Error, "Profile phải có ít nhất 3 điểm."));
        }

        if (request.PathPoints == null || request.PathPoints.Count < 2)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SWEEP_PATH_MIN_POINTS", DiagnosticSeverity.Error, "PathPoints phải có ít nhất 2 điểm."));
        }
    }

    private static void ValidateFamilyCreateBlend(FamilyCreateBlendRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.BottomProfile == null || request.BottomProfile.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_BOTTOM_PROFILE_MIN_POINTS", DiagnosticSeverity.Error, "BottomProfile phải có ít nhất 3 điểm."));
        }

        if (request.TopProfile == null || request.TopProfile.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_TOP_PROFILE_MIN_POINTS", DiagnosticSeverity.Error, "TopProfile phải có ít nhất 3 điểm."));
        }

        if (request.TopOffset <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_BLEND_OFFSET_INVALID", DiagnosticSeverity.Error, "TopOffset phải > 0."));
        }
    }

    private static void ValidateFamilyCreateRevolution(FamilyCreateRevolutionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.Profile == null || request.Profile.Count < 3)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_PROFILE_MIN_POINTS", DiagnosticSeverity.Error, "Profile phải có ít nhất 3 điểm."));
        }

        if (Math.Abs(request.EndAngle - request.StartAngle) < 1e-9)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_REVOLUTION_ANGLE_ZERO", DiagnosticSeverity.Error, "EndAngle - StartAngle không được bằng 0."));
        }

        if (Math.Abs(request.AxisDirectionX) < 1e-9 && Math.Abs(request.AxisDirectionY) < 1e-9)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_REVOLUTION_AXIS_ZERO", DiagnosticSeverity.Error, "Axis direction không được là (0,0)."));
        }
    }

    private static void ValidateFamilySetSubcategory(FamilySetSubcategoryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.FormElementId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_FORM_ELEMENT_ID_REQUIRED", DiagnosticSeverity.Error, "FormElementId phải > 0."));
        }

        if (string.IsNullOrWhiteSpace(request.SubcategoryName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SUBCATEGORY_NAME_REQUIRED", DiagnosticSeverity.Error, "SubcategoryName không được rỗng."));
        }
    }

    private static void ValidateFamilyLoadNested(FamilyLoadNestedRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyFilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_FILE_PATH_REQUIRED", DiagnosticSeverity.Error, "FamilyFilePath không được rỗng."));
        }
        else if (!Path.IsPathRooted(request.FamilyFilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_FILE_PATH_NOT_ROOTED", DiagnosticSeverity.Error, "FamilyFilePath phải là absolute path."));
        }
    }

    private static void ValidateFamilySave(FamilySaveRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(request.SaveAsPath) && !Path.IsPathRooted(request.SaveAsPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SAVE_PATH_NOT_ROOTED", DiagnosticSeverity.Error, "SaveAsPath phải là absolute path."));
        }
    }

    // ── Script Orchestration validators ──

    private static void ValidateScriptValidate(ScriptValidateRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptId) && string.IsNullOrWhiteSpace(request.InlineCode))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_SOURCE_REQUIRED", DiagnosticSeverity.Error, "Cần ScriptId hoặc InlineCode."));
        }
    }

    private static void ValidateScriptRun(ScriptRunRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptId) && string.IsNullOrWhiteSpace(request.InlineCode))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_SOURCE_REQUIRED", DiagnosticSeverity.Error, "Cần ScriptId hoặc InlineCode."));
        }

        if (request.TimeoutMs <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_TIMEOUT_INVALID", DiagnosticSeverity.Error, "TimeoutMs phải > 0."));
        }

        if (request.TimeoutMs > 300000)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_TIMEOUT_TOO_LARGE", DiagnosticSeverity.Error, "TimeoutMs không được > 300000 (5 phút)."));
        }
    }

    private static void ValidateScriptCompose(ScriptComposeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.Steps, "SCRIPT_COMPOSE_STEPS_EMPTY", "Steps phải có ít nhất 1 step.", diagnostics);

        if (request.Steps != null)
        {
            for (var i = 0; i < request.Steps.Count; i++)
            {
                var step = request.Steps[i];
                if (step == null)
                {
                    diagnostics.Add(DiagnosticRecord.Create("SCRIPT_COMPOSE_STEP_NULL", DiagnosticSeverity.Error, $"Step [{i}] không được null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(step.ScriptId) && string.IsNullOrWhiteSpace(step.InlineCode))
                {
                    diagnostics.Add(DiagnosticRecord.Create("SCRIPT_SOURCE_REQUIRED", DiagnosticSeverity.Error, $"Step [{i}]: Cần ScriptId hoặc InlineCode."));
                }
            }
        }

        if (request.TimeoutMs <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCRIPT_TIMEOUT_INVALID", DiagnosticSeverity.Error, "TimeoutMs phải > 0."));
        }
    }
}
