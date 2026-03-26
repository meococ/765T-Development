using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateCadGenericModelOverlap(CadGenericModelOverlapRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (double.IsNaN(request.ToleranceFeet) || double.IsInfinity(request.ToleranceFeet) || request.ToleranceFeet <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CAD_GENERIC_TOLERANCE_INVALID", DiagnosticSeverity.Error, "ToleranceFeet phải > 0."));
        }

        if (double.IsNaN(request.SamplingStepFeet) || double.IsInfinity(request.SamplingStepFeet) || request.SamplingStepFeet <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CAD_GENERIC_SAMPLING_STEP_INVALID", DiagnosticSeverity.Error, "SamplingStepFeet phải > 0."));
        }

        if (request.MaxElementsPerSide <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CAD_GENERIC_MAX_ELEMENTS_INVALID", DiagnosticSeverity.Error, "MaxElementsPerSide phải > 0."));
        }

        if (request.MaxPreviewPoints < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CAD_GENERIC_MAX_PREVIEW_INVALID", DiagnosticSeverity.Error, "MaxPreviewPoints không được âm."));
        }

        if (request.MaxSamplePointsPerSide <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CAD_GENERIC_MAX_SAMPLE_INVALID", DiagnosticSeverity.Error, "MaxSamplePointsPerSide phải > 0."));
        }
    }
}
