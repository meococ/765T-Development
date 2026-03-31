using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateSetParameters(SetParametersRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.Changes, "PARAMETER_CHANGES_EMPTY", "Changes must have at least 1 item.", diagnostics);
        if (request.Changes == null)
        {
            return;
        }

        foreach (var change in request.Changes)
        {
            if (change == null)
            {
                diagnostics.Add(DiagnosticRecord.Create("PARAMETER_CHANGE_NULL", DiagnosticSeverity.Error, "Change item must not be null."));
                continue;
            }

            if (change.ElementId <= 0)
            {
                diagnostics.Add(DiagnosticRecord.Create("PARAMETER_CHANGE_ELEMENT_INVALID", DiagnosticSeverity.Error, "ElementId in Changes must be greater than 0."));
            }

            if (string.IsNullOrWhiteSpace(change.ParameterName))
            {
                diagnostics.Add(DiagnosticRecord.Create("PARAMETER_NAME_REQUIRED", DiagnosticSeverity.Error, "ParameterName must not be empty."));
            }
        }
    }

    private static void ValidateDeleteElements(DeleteElementsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.ElementIds, "DELETE_ELEMENT_IDS_EMPTY", "ElementIds must have at least 1 value.", diagnostics);
    }

    private static void ValidateMoveElements(MoveElementsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.ElementIds, "MOVE_ELEMENT_IDS_EMPTY", "ElementIds must have at least 1 value.", diagnostics);
        if (request.DeltaX == 0 && request.DeltaY == 0 && request.DeltaZ == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MOVE_DELTA_ZERO", DiagnosticSeverity.Error, "DeltaX/DeltaY/DeltaZ must not all be zero."));
        }
    }

    private static void ValidateRotateElements(RotateElementsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.ElementIds, "ROTATE_ELEMENT_IDS_EMPTY", "ElementIds must have at least 1 value.", diagnostics);
        if (Math.Abs(request.AngleDegrees) <= 1e-9)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROTATE_ANGLE_ZERO", DiagnosticSeverity.Error, "AngleDegrees must not be zero."));
        }

        if (!IsAllowedValue(request.AxisMode, "element_basis_z", "project_z_at_element_origin", "explicit"))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROTATE_AXIS_MODE_INVALID", DiagnosticSeverity.Error, "AxisMode is invalid."));
            return;
        }

        if (string.Equals(request.AxisMode, "explicit", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.AxisOriginX.HasValue || !request.AxisOriginY.HasValue || !request.AxisOriginZ.HasValue)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROTATE_AXIS_ORIGIN_REQUIRED", DiagnosticSeverity.Error, "Explicit axis requires AxisOriginX/Y/Z."));
            }

            if (!request.AxisDirectionX.HasValue || !request.AxisDirectionY.HasValue || !request.AxisDirectionZ.HasValue)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROTATE_AXIS_DIRECTION_REQUIRED", DiagnosticSeverity.Error, "Explicit axis requires AxisDirectionX/Y/Z."));
            }
            else if (Math.Abs(request.AxisDirectionX.Value) <= 1e-9 &&
                     Math.Abs(request.AxisDirectionY.Value) <= 1e-9 &&
                     Math.Abs(request.AxisDirectionZ.Value) <= 1e-9)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROTATE_AXIS_DIRECTION_ZERO", DiagnosticSeverity.Error, "AxisDirection must not be a zero vector."));
            }
        }
    }

    private static void ValidatePlaceFamilyInstance(PlaceFamilyInstanceRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.FamilySymbolId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_SYMBOL_REQUIRED", DiagnosticSeverity.Error, "FamilySymbolId must be greater than 0."));
        }

        if (!IsAllowedValue(request.PlacementMode, "auto", "point", "curve", "face", "view"))
        {
            diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_MODE_INVALID", DiagnosticSeverity.Error, "PlacementMode is invalid."));
        }

        if (string.Equals(request.PlacementMode, "curve", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.StartX.HasValue || !request.StartY.HasValue || !request.StartZ.HasValue ||
                !request.EndX.HasValue || !request.EndY.HasValue || !request.EndZ.HasValue)
            {
                diagnostics.Add(DiagnosticRecord.Create("CURVE_POINTS_REQUIRED", DiagnosticSeverity.Error, "Curve placement requires Start/End coordinates."));
            }
        }
    }

    private static void ValidateSaveAs(SaveAsDocumentRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILE_PATH_REQUIRED", DiagnosticSeverity.Error, "FilePath must not be empty."));
        }
    }

    private static void ValidateOpenBackground(OpenBackgroundDocumentRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILE_PATH_REQUIRED", DiagnosticSeverity.Error, "FilePath must not be empty."));
            return;
        }

        if (!Path.IsPathRooted(request.FilePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILE_PATH_NOT_ROOTED", DiagnosticSeverity.Error, "FilePath must be an absolute path."));
        }
    }

    private static void ValidateCloseDocument(CloseDocumentRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            diagnostics.Add(DiagnosticRecord.Create("DOCUMENT_KEY_REQUIRED", DiagnosticSeverity.Error, "DocumentKey must not be empty."));
        }
    }

    private static void ValidateSynchronize(SynchronizeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(request.Comment) && request.Comment.Length > 1024)
        {
            diagnostics.Add(DiagnosticRecord.Create("SYNC_COMMENT_TOO_LONG", DiagnosticSeverity.Error, "Comment is too long."));
        }
    }

    private static void ValidateCreate3DView(Create3DViewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ViewName))
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_NAME_REQUIRED", DiagnosticSeverity.Error, "ViewName must not be empty."));
        }
    }

    private static void ValidateCreateProjectView(CreateProjectViewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ViewKind))
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_KIND_REQUIRED", DiagnosticSeverity.Error, "ViewKind must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.ViewName))
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_NAME_REQUIRED", DiagnosticSeverity.Error, "ViewName must not be empty."));
        }

        var viewKind = request.ViewKind?.Trim().ToLowerInvariant() ?? string.Empty;
        var requiresLevel = string.Equals(viewKind, "floor_plan", StringComparison.Ordinal)
            || string.Equals(viewKind, "ceiling_plan", StringComparison.Ordinal)
            || string.Equals(viewKind, "engineering_plan", StringComparison.Ordinal);
        if (requiresLevel && !request.LevelId.HasValue && string.IsNullOrWhiteSpace(request.LevelName))
        {
            diagnostics.Add(DiagnosticRecord.Create("LEVEL_REQUIRED", DiagnosticSeverity.Error, "Floor/Ceiling/Engineering plan requires LevelId or LevelName."));
        }

        if (request.ScaleValue.HasValue && request.ScaleValue.Value <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_SCALE_INVALID", DiagnosticSeverity.Error, "ScaleValue must be greater than 0."));
        }
    }

    private static void ValidateCreateOrUpdateViewFilter(CreateOrUpdateViewFilterRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FilterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILTER_NAME_REQUIRED", DiagnosticSeverity.Error, "FilterName must not be empty."));
        }

        RequireNonEmpty(request.Rules, "FILTER_RULES_EMPTY", "Rules must have at least 1 rule.", diagnostics);
        if (request.Rules == null)
        {
            return;
        }

        foreach (var rule in request.Rules)
        {
            if (rule == null)
            {
                diagnostics.Add(DiagnosticRecord.Create("FILTER_RULE_NULL", DiagnosticSeverity.Error, "Rule must not be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ParameterName))
            {
                diagnostics.Add(DiagnosticRecord.Create("FILTER_PARAMETER_REQUIRED", DiagnosticSeverity.Error, "Rule.ParameterName must not be empty."));
            }

            if (!IsAllowedValue(rule.Operator, "equals", "contains", "begins_with", "ends_with", "greater", "greater_or_equal", "less", "less_or_equal", "has_value", "has_no_value"))
            {
                diagnostics.Add(DiagnosticRecord.Create("FILTER_OPERATOR_INVALID", DiagnosticSeverity.Error, "Rule.Operator is invalid."));
            }
        }
    }

    private static void ValidateApplyViewFilter(ApplyViewFilterRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!request.ViewId.HasValue && string.IsNullOrWhiteSpace(request.ViewName))
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "ViewId or ViewName is required."));
        }

        if (!request.FilterId.HasValue && string.IsNullOrWhiteSpace(request.FilterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILTER_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "FilterId or FilterName is required."));
        }

        ValidateRgb(request.ProjectionLineColorRed, request.ProjectionLineColorGreen, request.ProjectionLineColorBlue, diagnostics);
    }

    private static void ValidateRemoveFilterFromView(RemoveFilterFromViewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!request.ViewId.HasValue && string.IsNullOrWhiteSpace(request.ViewName))
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "ViewId or ViewName is required."));
        }

        if (!request.FilterId.HasValue && string.IsNullOrWhiteSpace(request.FilterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILTER_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "FilterId or FilterName is required."));
        }
    }

    private static void ValidateDeleteFilter(DeleteFilterRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!request.FilterId.HasValue && string.IsNullOrWhiteSpace(request.FilterName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FILTER_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "FilterId or FilterName is required."));
        }
    }

    private static void ValidateElementTypeQuery(ElementTypeQueryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }
    }

    private static void ValidateTextTypeUsage(TextNoteTypeUsageRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }

        if (request.MaxSampleTextNotesPerType <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_SAMPLE_TEXT_NOTES_INVALID", DiagnosticSeverity.Error, "MaxSampleTextNotesPerType must be greater than 0."));
        }
    }

    private static void ValidateFamilyAxisAlignment(FamilyAxisAlignmentRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.AngleToleranceDegrees < 0 || request.AngleToleranceDegrees > 180)
        {
            diagnostics.Add(DiagnosticRecord.Create("ANGLE_TOLERANCE_INVALID", DiagnosticSeverity.Error, "AngleToleranceDegrees must be in range 0..180."));
        }

        if (request.MaxElements <= 0 || request.MaxIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_AXIS_LIMIT_INVALID", DiagnosticSeverity.Error, "MaxElements and MaxIssues must be greater than 0."));
        }
    }
}
