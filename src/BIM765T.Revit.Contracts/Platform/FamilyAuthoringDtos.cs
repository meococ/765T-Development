using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ═══════════════════════════════════════════
// Family Authoring — Request / Response DTOs
// ═══════════════════════════════════════════

[DataContract]
public sealed class FamilyAddParameterRequest
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterType { get; set; } = "Length";

    [DataMember(Order = 3)]
    public string ParameterGroup { get; set; } = "PG_GEOMETRY";

    [DataMember(Order = 4)]
    public bool IsInstance { get; set; }

    [DataMember(Order = 5)]
    public string DefaultValue { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Description { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilySetFormulaRequest
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Formula { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyTypeCatalogEntry
{
    [DataMember(Order = 1)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public Dictionary<string, string> ParameterValues { get; set; } = new Dictionary<string, string>();
}

[DataContract]
public sealed class FamilySetTypeCatalogRequest
{
    [DataMember(Order = 1)]
    public List<FamilyTypeCatalogEntry> Types { get; set; } = new List<FamilyTypeCatalogEntry>();

    [DataMember(Order = 2)]
    public bool DeleteExistingTypes { get; set; }
}

[DataContract]
public sealed class FamilyAddReferencePlaneRequest
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public double OriginX { get; set; }

    [DataMember(Order = 3)]
    public double OriginY { get; set; }

    [DataMember(Order = 4)]
    public double OriginZ { get; set; }

    [DataMember(Order = 5)]
    public double NormalX { get; set; }

    [DataMember(Order = 6)]
    public double NormalY { get; set; } = 1.0;

    [DataMember(Order = 7)]
    public double NormalZ { get; set; }

    [DataMember(Order = 8)]
    public double ExtentFeet { get; set; } = 5.0;
}

[DataContract]
public sealed class FamilyProfilePoint
{
    [DataMember(Order = 1)]
    public double X { get; set; }

    [DataMember(Order = 2)]
    public double Y { get; set; }
}

[DataContract]
public sealed class FamilyCreateExtrusionRequest
{
    [DataMember(Order = 1)]
    public List<FamilyProfilePoint> Profile { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 2)]
    public double StartOffset { get; set; }

    [DataMember(Order = 3)]
    public double EndOffset { get; set; } = 1.0;

    [DataMember(Order = 4)]
    public bool IsVoid { get; set; }

    [DataMember(Order = 5)]
    public string SketchPlaneName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SubcategoryName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string MaterialParameterName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyCreateSweepRequest
{
    [DataMember(Order = 1)]
    public List<FamilyProfilePoint> Profile { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 2)]
    public List<FamilyPathPoint> PathPoints { get; set; } = new List<FamilyPathPoint>();

    [DataMember(Order = 3)]
    public bool IsVoid { get; set; }

    [DataMember(Order = 4)]
    public string SketchPlaneName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SubcategoryName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyPathPoint
{
    [DataMember(Order = 1)]
    public double X { get; set; }

    [DataMember(Order = 2)]
    public double Y { get; set; }

    [DataMember(Order = 3)]
    public double Z { get; set; }
}

[DataContract]
public sealed class FamilyCreateBlendRequest
{
    [DataMember(Order = 1)]
    public List<FamilyProfilePoint> BottomProfile { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 2)]
    public List<FamilyProfilePoint> TopProfile { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 3)]
    public double TopOffset { get; set; } = 1.0;

    [DataMember(Order = 4)]
    public bool IsVoid { get; set; }

    [DataMember(Order = 5)]
    public string SketchPlaneName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string SubcategoryName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyCreateRevolutionRequest
{
    [DataMember(Order = 1)]
    public List<FamilyProfilePoint> Profile { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 2)]
    public double StartAngle { get; set; }

    [DataMember(Order = 3)]
    public double EndAngle { get; set; } = 360.0;

    [DataMember(Order = 4)]
    public double AxisOriginX { get; set; }

    [DataMember(Order = 5)]
    public double AxisOriginY { get; set; }

    [DataMember(Order = 6)]
    public double AxisDirectionX { get; set; }

    [DataMember(Order = 7)]
    public double AxisDirectionY { get; set; } = 1.0;

    [DataMember(Order = 8)]
    public bool IsVoid { get; set; }

    [DataMember(Order = 9)]
    public string SketchPlaneName { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string SubcategoryName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilySetSubcategoryRequest
{
    [DataMember(Order = 1)]
    public int FormElementId { get; set; }

    [DataMember(Order = 2)]
    public string SubcategoryName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyLoadNestedRequest
{
    [DataMember(Order = 1)]
    public string FamilyFilePath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class FamilySaveRequest
{
    [DataMember(Order = 1)]
    public string SaveAsPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool OverwriteExisting { get; set; } = true;

    [DataMember(Order = 3)]
    public bool CompactFile { get; set; }
}

// ═══════════════════════════════════════════
// Family Authoring — Tier 1 (Dimension, Alignment, Connector)
// ═══════════════════════════════════════════

[DataContract]
public sealed class FamilyAddDimensionRequest
{
    [DataMember(Order = 1)]
    public string ReferencePlane1Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ReferencePlane2Name { get; set; } = string.Empty;

    /// <summary>Family parameter name to bind as dimension label. Empty = no label.</summary>
    [DataMember(Order = 3)]
    public string LabelParameterName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsLocked { get; set; }

    /// <summary>"linear" (default) | "angular" | "radial"</summary>
    [DataMember(Order = 5)]
    public string DimensionType { get; set; } = "linear";
}

[DataContract]
public sealed class FamilyAddAlignmentRequest
{
    [DataMember(Order = 1)]
    public string ReferencePlaneName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int GeometryElementId { get; set; }

    /// <summary>Face index on the geometry form. 0=start face, 1=end face for extrusion.</summary>
    [DataMember(Order = 3)]
    public int GeometryFaceIndex { get; set; }

    [DataMember(Order = 4)]
    public bool IsLocked { get; set; } = true;
}

[DataContract]
public sealed class FamilyAddConnectorRequest
{
    /// <summary>"duct" | "pipe" | "electrical" | "conduit" | "cable_tray"</summary>
    [DataMember(Order = 1)]
    public string ConnectorDomain { get; set; } = "duct";

    [DataMember(Order = 2)]
    public int HostGeometryElementId { get; set; }

    /// <summary>"End" | "Curve" | "Physical"</summary>
    [DataMember(Order = 3)]
    public string ConnectorType { get; set; } = "End";

    /// <summary>"Round" | "Rectangular" | "Oval" — for duct/pipe only.</summary>
    [DataMember(Order = 4)]
    public string ProfileOption { get; set; } = "Round";

    /// <summary>"X" | "Y" | "Z" | "NegX" | "NegY" | "NegZ"</summary>
    [DataMember(Order = 5)]
    public string Direction { get; set; } = "Z";

    /// <summary>Family parameter to bind connector diameter (Round profile).</summary>
    [DataMember(Order = 6)]
    public string DiameterParameterName { get; set; } = string.Empty;

    /// <summary>Family parameter to bind connector width (Rectangular profile).</summary>
    [DataMember(Order = 7)]
    public string WidthParameterName { get; set; } = string.Empty;

    /// <summary>Family parameter to bind connector height (Rectangular profile).</summary>
    [DataMember(Order = 8)]
    public string HeightParameterName { get; set; } = string.Empty;

    /// <summary>"Supply" | "Return" | "Exhaust" | "Other" — for duct system classification.</summary>
    [DataMember(Order = 9)]
    public string SystemClassification { get; set; } = "Other";
}

// ═══════════════════════════════════════════
// Family Authoring — Tier 2 (Visibility, Subcategory, Material, ParamVisibility)
// ═══════════════════════════════════════════

[DataContract]
public sealed class FamilySetVisibilityRequest
{
    [DataMember(Order = 1)]
    public int FormElementId { get; set; }

    [DataMember(Order = 2)]
    public bool IsShownInFine { get; set; } = true;

    [DataMember(Order = 3)]
    public bool IsShownInMedium { get; set; } = true;

    [DataMember(Order = 4)]
    public bool IsShownInCoarse { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IsShownInPlanRCP { get; set; } = true;

    [DataMember(Order = 6)]
    public bool IsShownInFrontBack { get; set; } = true;

    [DataMember(Order = 7)]
    public bool IsShownInLeftRight { get; set; } = true;
}

[DataContract]
public sealed class FamilyCreateSubcategoryRequest
{
    [DataMember(Order = 1)]
    public string SubcategoryName { get; set; } = string.Empty;

    /// <summary>Line weight 1-16. 0 = use default.</summary>
    [DataMember(Order = 2)]
    public int LineWeight { get; set; }

    [DataMember(Order = 3)]
    public int LineColorR { get; set; } = -1;

    [DataMember(Order = 4)]
    public int LineColorG { get; set; } = -1;

    [DataMember(Order = 5)]
    public int LineColorB { get; set; } = -1;

    /// <summary>Material name to assign to subcategory. Empty = none.</summary>
    [DataMember(Order = 6)]
    public string MaterialName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyBindMaterialRequest
{
    [DataMember(Order = 1)]
    public int FormElementId { get; set; }

    /// <summary>Family parameter of type Material to associate with geometry.</summary>
    [DataMember(Order = 2)]
    public string MaterialParameterName { get; set; } = string.Empty;

    /// <summary>Default material name from document material library. Empty = none.</summary>
    [DataMember(Order = 3)]
    public string DefaultMaterialName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilySetParameterVisibilityRequest
{
    [DataMember(Order = 1)]
    public int FormElementId { get; set; }

    /// <summary>Yes/No parameter that controls geometry visibility. Created if not exists.</summary>
    [DataMember(Order = 2)]
    public string VisibilityParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool DefaultVisible { get; set; } = true;
}

// ═══════════════════════════════════════════
// Family Authoring — Tier 3 (Spline, SharedParam, Category)
// ═══════════════════════════════════════════

[DataContract]
public sealed class FamilyCreateSplineExtrusionRequest
{
    /// <summary>"hermite" | "nurbs"</summary>
    [DataMember(Order = 1)]
    public string SplineType { get; set; } = "hermite";

    [DataMember(Order = 2)]
    public List<FamilyProfilePoint> ControlPoints { get; set; } = new List<FamilyProfilePoint>();

    [DataMember(Order = 3)]
    public bool IsClosed { get; set; } = true;

    [DataMember(Order = 4)]
    public double StartOffset { get; set; }

    [DataMember(Order = 5)]
    public double EndOffset { get; set; } = 1.0;

    [DataMember(Order = 6)]
    public bool IsVoid { get; set; }

    [DataMember(Order = 7)]
    public string SketchPlaneName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string SubcategoryName { get; set; } = string.Empty;

    /// <summary>NURBS degree (default 3). Only used when SplineType="nurbs".</summary>
    [DataMember(Order = 9)]
    public int Degree { get; set; } = 3;

    /// <summary>NURBS weights per control point. Only used when SplineType="nurbs".</summary>
    [DataMember(Order = 10)]
    public List<double> Weights { get; set; } = new List<double>();
}

[DataContract]
public sealed class FamilyAddSharedParameterRequest
{
    [DataMember(Order = 1)]
    public string SharedParameterFilePath { get; set; } = string.Empty;

    /// <summary>Group name within the shared parameter file.</summary>
    [DataMember(Order = 2)]
    public string GroupName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ParameterGroup { get; set; } = "PG_DATA";

    [DataMember(Order = 5)]
    public bool IsInstance { get; set; }

    /// <summary>If true and a family parameter with same name exists, replace it with shared version.</summary>
    [DataMember(Order = 6)]
    public bool ReplaceExisting { get; set; }
}

[DataContract]
public sealed class FamilySetCategoryRequest
{
    /// <summary>Category display name, e.g. "Generic Models", "Mechanical Equipment", "Pipe Fittings".</summary>
    [DataMember(Order = 1)]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>BuiltInCategory integer value for precision. -1 = use CategoryName lookup.</summary>
    [DataMember(Order = 2)]
    public int BuiltInCategoryId { get; set; } = -1;
}

/// <summary>
/// Create a new family document from a Revit template (.rft), optionally save and activate in UI.
/// Supported TemplateCategory values: generic_model, duct_fitting, pipe_fitting,
/// mechanical_equipment, electrical_fixture, structural_framing, custom.
/// </summary>
[DataContract]
public sealed class FamilyCreateDocumentRequest
{
    /// <summary>
    /// Template category hint: "generic_model" | "duct_fitting" | "pipe_fitting" |
    /// "mechanical_equipment" | "electrical_fixture" | "structural_framing" | "custom".
    /// Default: "generic_model".
    /// </summary>
    [DataMember(Order = 1)]
    public string TemplateCategory { get; set; } = "generic_model";

    /// <summary>Full path to a custom .rft file. Only used when TemplateCategory = "custom".</summary>
    [DataMember(Order = 2)]
    public string CustomTemplatePath { get; set; } = string.Empty;

    /// <summary>
    /// Full path where the new .rfa will be saved. Required.
    /// Example: "C:\\Families\\My_Duct_Transition.rfa"
    /// </summary>
    [DataMember(Order = 3)]
    public string SaveAsPath { get; set; } = string.Empty;

    /// <summary>Whether to open and make the new family the active document in Revit. Default: true.</summary>
    [DataMember(Order = 4)]
    public bool ActivateInUI { get; set; } = true;

    /// <summary>Unit system preference: "metric" (default) or "imperial".</summary>
    [DataMember(Order = 5)]
    public string UnitSystem { get; set; } = "metric";
}

/// <summary>Response after creating a new family document.</summary>
[DataContract]
public sealed class FamilyCreateDocumentResponse
{
    [DataMember(Order = 1)]
    public string SavedPath { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TemplatePath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IsActivated { get; set; }
}

// ── Response DTOs ──

[DataContract]
public sealed class FamilyGeometryItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string FormType { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Mode { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SubcategoryName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IsVisible { get; set; } = true;
}

[DataContract]
public sealed class FamilyListGeometryResponse
{
    [DataMember(Order = 1)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<FamilyGeometryItem> Forms { get; set; } = new List<FamilyGeometryItem>();

    [DataMember(Order = 4)]
    public List<string> ReferencePlaneNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public int NestedInstancesCount { get; set; }

    [DataMember(Order = 6)]
    public int ParameterCount { get; set; }

    [DataMember(Order = 7)]
    public int TypeCount { get; set; }
}

// ═══════════════════════════════════════════
// Script Orchestration — Request / Response DTOs
// ═══════════════════════════════════════════

[DataContract]
public sealed class ScriptCatalogEntry
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Language { get; set; } = "csharp";

    [DataMember(Order = 5)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public string ContentHash { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScriptListResponse
{
    [DataMember(Order = 1)]
    public List<ScriptCatalogEntry> Scripts { get; set; } = new List<ScriptCatalogEntry>();

    [DataMember(Order = 2)]
    public string CatalogPath { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScriptValidateRequest
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string InlineCode { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScriptValidationResult
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IsValid { get; set; }

    [DataMember(Order = 3)]
    public List<string> DangerousApis { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> Warnings { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string ContentHash { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScriptRunRequest
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string InlineCode { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ContentHash { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

    [DataMember(Order = 5)]
    public int TimeoutMs { get; set; } = 30000;
}

[DataContract]
public sealed class ScriptRunResult
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Success { get; set; }

    [DataMember(Order = 4)]
    public string Output { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ErrorMessage { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public long DurationMs { get; set; }

    [DataMember(Order = 7)]
    public int ElementsCreated { get; set; }

    [DataMember(Order = 8)]
    public int ElementsModified { get; set; }

    [DataMember(Order = 9)]
    public int ElementsDeleted { get; set; }
}

[DataContract]
public sealed class ScriptComposeStep
{
    [DataMember(Order = 1)]
    public string ScriptId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string InlineCode { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ContentHash { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

    [DataMember(Order = 5)]
    public bool ContinueOnError { get; set; }
}

[DataContract]
public sealed class ScriptComposeRequest
{
    [DataMember(Order = 1)]
    public List<ScriptComposeStep> Steps { get; set; } = new List<ScriptComposeStep>();

    [DataMember(Order = 2)]
    public bool AtomicTransaction { get; set; }

    [DataMember(Order = 3)]
    public int TimeoutMs { get; set; } = 60000;
}

[DataContract]
public sealed class ScriptComposeResult
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool AllSucceeded { get; set; }

    [DataMember(Order = 3)]
    public List<ScriptRunResult> StepResults { get; set; } = new List<ScriptRunResult>();

    [DataMember(Order = 4)]
    public int TotalElementsCreated { get; set; }

    [DataMember(Order = 5)]
    public int TotalElementsModified { get; set; }
}

[DataContract]
public sealed class ScriptGetRunRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;
}
