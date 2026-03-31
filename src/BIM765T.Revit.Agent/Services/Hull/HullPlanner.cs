using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;

namespace BIM765T.Revit.Agent.Services.Hull;

internal sealed class HullPlanner
{
    internal HullDryRunResponse Plan(Document doc, HullCollectionResponse collected, HullDryRunRequest request)
    {
        var response = new HullDryRunResponse
        {
            DocumentName = collected.DocumentName,
            ViewName = collected.ViewName,
            EligibleCount = collected.EligibleCount
        };

        var classificationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in collected.Sources)
        {
            if (!source.Eligible)
            {
                response.SkipCount += 1;
                response.Diagnostics.AddRange(source.Diagnostics);
                continue;
            }

            var element = doc.GetElement(new ElementId((long)source.SourceId));
            if (element == null)
            {
                response.SkipCount += 1;
                response.Diagnostics.Add(DiagnosticRecord.Create("SOURCE_MISSING", DiagnosticSeverity.Warning, "Cannot reacquire source element.", source.SourceId));
                continue;
            }

            var action = BuildAction(doc, element, source, request);
            response.Actions.Add(action);
            if (string.Equals(action.Action, "UPSERT", StringComparison.OrdinalIgnoreCase))
            {
                response.PlannedUpsertCount += 1;
            }
            else
            {
                response.SkipCount += 1;
            }

            if (!classificationCounts.ContainsKey(action.Classification))
            {
                classificationCounts[action.Classification] = 0;
            }

            classificationCounts[action.Classification] += 1;
        }

        response.ClassificationCounts = classificationCounts
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new CounterValue { Name = kvp.Key, Count = kvp.Value })
            .ToList();

        return response;
    }

    private static HullPlannedAction BuildAction(Document doc, Element element, HullSourceInfo source, HullDryRunRequest request)
    {
        var classification = Classify(source);
        var action = new HullPlannedAction
        {
            SourceId = source.SourceId,
            SourceKind = source.SourceKind,
            Classification = classification,
            FamilyKey = string.Equals(source.SourceKind, "Wall", StringComparison.OrdinalIgnoreCase)
                ? "SR_Mii_Wall Hull"
                : "SR_Mii_Floor-Ceiling Hull",
            TraceKey = !string.IsNullOrWhiteSpace(source.Comments) ? source.Comments! : "HULL::" + source.SourceId,
            Action = string.Equals(classification, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ? "SKIP_LOW_CONF" : "UPSERT",
            Confidence = string.Equals(classification, "UNKNOWN", StringComparison.OrdinalIgnoreCase) ? 0.35 : 0.95,
            DimDepthInch = source.StructureThicknessInch,
            SplitTwoPanels = ContainsToken(source.CassetteId, "2 PANELS") || ContainsToken(source.TypeName, "2 PANELS") || ContainsToken(source.Comments, "2 PANELS")
        };

        ResolveDimensions(doc, element, action);
        ApplyRules(action, request);

        if (action.DimDepthInch.GetValueOrDefault() <= 0.0 || action.DimLengthInch.GetValueOrDefault() <= 0.0)
        {
            action.Action = "SKIP_LOW_CONF";
            action.Confidence = Math.Min(action.Confidence, 0.45);
            action.Diagnostics.Add(DiagnosticRecord.Create("DIMENSION_LOW_CONF", DiagnosticSeverity.Warning, "Cannot determine stable dimension for dry-run.", source.SourceId));
        }

        return action;
    }

    private static void ResolveDimensions(Document doc, Element element, HullPlannedAction action)
    {
        if (element is Wall wall)
        {
            var curve = (wall.Location as LocationCurve)?.Curve;
            if (curve != null)
            {
                action.DimLengthInch = UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Inches);
            }

            var height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();
            if (height.HasValue && height.Value > 0.0)
            {
                action.DimHeightOrWidthInch = UnitUtils.ConvertFromInternalUnits(height.Value, UnitTypeId.Inches);
                return;
            }
        }

        var bbox = element.get_BoundingBox(doc.ActiveView) ?? element.get_BoundingBox(null);
        if (bbox == null)
        {
            return;
        }

        var dx = Math.Abs(bbox.Max.X - bbox.Min.X);
        var dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
        var dz = Math.Abs(bbox.Max.Z - bbox.Min.Z);
        if (element is Wall)
        {
            if (!action.DimLengthInch.HasValue || action.DimLengthInch <= 0.0)
            {
                action.DimLengthInch = UnitUtils.ConvertFromInternalUnits(Math.Max(dx, dy), UnitTypeId.Inches);
            }

            action.DimHeightOrWidthInch = UnitUtils.ConvertFromInternalUnits(dz, UnitTypeId.Inches);
            return;
        }

        var major = Math.Max(dx, dy);
        var minor = Math.Min(dx, dy);
        action.DimLengthInch = UnitUtils.ConvertFromInternalUnits(major, UnitTypeId.Inches);
        action.DimHeightOrWidthInch = UnitUtils.ConvertFromInternalUnits(minor, UnitTypeId.Inches);
        if (Math.Abs(dx - dy) < UnitUtils.ConvertToInternalUnits(0.5, UnitTypeId.Inches))
        {
            action.Diagnostics.Add(DiagnosticRecord.Create("NEAR_SQUARE_AXIS_LOCK", DiagnosticSeverity.Info, "Near-square detected; planner will lock major/minor axis deterministically.", action.SourceId));
        }
    }

    private static void ApplyRules(HullPlannedAction action, HullDryRunRequest request)
    {
        if (string.Equals(action.SourceKind, "Wall", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(action.Classification, "IN", StringComparison.OrdinalIgnoreCase) && request.UseInOffset)
            {
                action.Diagnostics.Add(DiagnosticRecord.Create("RULE_IN_OFFSET", DiagnosticSeverity.Info, $"Apply IN offset = {request.InOffsetInch} in.", action.SourceId));
            }

            if ((string.Equals(action.Classification, "EX", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(action.Classification, "CR", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(action.Classification, "ML", StringComparison.OrdinalIgnoreCase)) && request.UseExCut)
            {
                action.Diagnostics.Add(DiagnosticRecord.Create("RULE_EX_CUT", DiagnosticSeverity.Info, $"Apply EX cut = {request.ExCutInch} in.", action.SourceId));
            }

            if ((string.Equals(action.Classification, "EX", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(action.Classification, "CR", StringComparison.OrdinalIgnoreCase)) && request.UseWallHoldback)
            {
                action.Diagnostics.Add(DiagnosticRecord.Create("RULE_WALL_HOLDBACK", DiagnosticSeverity.Info, $"Apply Wall Holdback = {request.WallHoldbackInch} in.", action.SourceId));
            }

            return;
        }

        if (request.UseFCHoldback)
        {
            action.Diagnostics.Add(DiagnosticRecord.Create("RULE_FC_HOLDBACK", DiagnosticSeverity.Info, $"Apply FC Holdback = {request.FCHoldbackInch} in.", action.SourceId));
        }
    }

    private static string Classify(HullSourceInfo source)
    {
        var ordered = new[] { source.CassetteId, source.TypeName, source.Comments };
        foreach (var value in ordered)
        {
            var cls = ClassifyFromText(value);
            if (!string.Equals(cls, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return cls;
            }
        }

        return "UNKNOWN";
    }

    private static string ClassifyFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "UNKNOWN";
        var t = text?.Trim().ToUpperInvariant() ?? string.Empty;

        if (t.StartsWith("IN", StringComparison.Ordinal) || t.Contains("_IN") || t.Contains("-IN") || t.Contains(" IN ")) return "IN";
        if (t.StartsWith("EX", StringComparison.Ordinal) || t.Contains("_EX") || t.Contains("-EX") || t.Contains(" EX ")) return "EX";
        if (t.StartsWith("CR", StringComparison.Ordinal) || t.Contains(" CORRIDOR") || t.Contains("-CR") || t.Contains("_CR")) return "CR";
        if (t.StartsWith("ML", StringComparison.Ordinal) || t.Contains("-ML") || t.Contains("_ML")) return "ML";
        return "UNKNOWN";
    }

    private static bool ContainsToken(string? text, string token)
    {
        return text?.ToUpperInvariant().Contains(token.ToUpperInvariant()) == true;
    }
}
