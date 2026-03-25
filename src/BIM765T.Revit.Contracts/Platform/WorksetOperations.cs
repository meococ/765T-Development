using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Workset Create ────────────────────────────────────────────────────────────

/// <summary>Request to create a new workset in the active workshared document.</summary>
[DataContract]
public sealed class WorksetCreateRequest
{
    /// <summary>Document key identifying the target workshared document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Name for the new workset. Must be unique within the document.</summary>
    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the workset is visible by default in new views. Defaults to true.</summary>
    [DataMember(Order = 3)]
    public bool IsVisible { get; set; } = true;
}

/// <summary>Result of a workset creation operation.</summary>
[DataContract]
public sealed class WorksetCreateResponse
{
    /// <summary>The Revit ElementId of the newly created workset.</summary>
    [DataMember(Order = 1)]
    public int WorksetId { get; set; }

    /// <summary>The resolved name of the created workset (may be normalised by Revit).</summary>
    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>True when a new workset was created; false if the operation was a no-op (e.g. dry-run or already exists).</summary>
    [DataMember(Order = 3)]
    public bool Created { get; set; }
}

// ── Workset Bulk Reassign ─────────────────────────────────────────────────────

/// <summary>Request to bulk-reassign elements to a target workset, with optional category and element-id filters.</summary>
[DataContract]
public sealed class WorksetBulkReassignRequest
{
    /// <summary>Document key identifying the target workshared document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Name of the workset to reassign elements to.</summary>
    [DataMember(Order = 2)]
    public string TargetWorksetName { get; set; } = string.Empty;

    /// <summary>
    /// Optional list of Revit category names to restrict reassignment.
    /// Empty or null means all categories.
    /// Example: ["Walls", "Floors"]
    /// </summary>
    [DataMember(Order = 3)]
    public string[] CategoryFilter { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Optional explicit list of element ids to reassign.
    /// Empty or null means all elements matching CategoryFilter.
    /// </summary>
    [DataMember(Order = 4)]
    public int[] ElementIds { get; set; } = System.Array.Empty<int>();

    /// <summary>When true, reports what would change without committing the transaction.</summary>
    [DataMember(Order = 5)]
    public bool DryRun { get; set; }
}

/// <summary>Result of a bulk workset reassignment operation.</summary>
[DataContract]
public sealed class WorksetBulkReassignResponse
{
    /// <summary>Number of elements actually (or would-be in dry-run) reassigned.</summary>
    [DataMember(Order = 1)]
    public int ReassignedCount { get; set; }

    /// <summary>Number of elements skipped (already on target workset, or not eligible).</summary>
    [DataMember(Order = 2)]
    public int SkippedCount { get; set; }

    /// <summary>Per-element detail records. May be empty when the total count is large and DryRun is false.</summary>
    [DataMember(Order = 3)]
    public List<WorksetReassignItem> Items { get; set; } = new List<WorksetReassignItem>();
}

/// <summary>Detail record for a single element in a bulk workset reassignment.</summary>
[DataContract]
public sealed class WorksetReassignItem
{
    /// <summary>Revit ElementId of the element.</summary>
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    /// <summary>Name of the workset the element was on before reassignment.</summary>
    [DataMember(Order = 2)]
    public string OriginalWorkset { get; set; } = string.Empty;

    /// <summary>Name of the workset the element was assigned to.</summary>
    [DataMember(Order = 3)]
    public string TargetWorkset { get; set; } = string.Empty;
}

// ── Workset Open / Close ──────────────────────────────────────────────────────

/// <summary>Request to open or close a workset in the active workshared document.</summary>
[DataContract]
public sealed class WorksetOpenCloseRequest
{
    /// <summary>Document key identifying the target workshared document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Name of the workset to open or close.</summary>
    [DataMember(Order = 2)]
    public string WorksetName { get; set; } = string.Empty;

    /// <summary>True to open the workset; false to close it.</summary>
    [DataMember(Order = 3)]
    public bool IsOpen { get; set; }
}

/// <summary>Result of a workset open/close operation.</summary>
[DataContract]
public sealed class WorksetOpenCloseResponse
{
    /// <summary>Name of the workset that was targeted.</summary>
    [DataMember(Order = 1)]
    public string WorksetName { get; set; } = string.Empty;

    /// <summary>The resulting open/closed state of the workset after the operation.</summary>
    [DataMember(Order = 2)]
    public bool IsOpen { get; set; }

    /// <summary>True when the operation completed successfully.</summary>
    [DataMember(Order = 3)]
    public bool Success { get; set; }
}
