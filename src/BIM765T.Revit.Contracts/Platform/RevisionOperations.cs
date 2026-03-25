using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Revision Create ───────────────────────────────────────────────────────────

/// <summary>Request to create a new project revision in the active document.</summary>
[DataContract]
public sealed class RevisionCreateRequest
{
    /// <summary>Document key identifying the target document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Human-readable description for the revision (e.g. "Issued for Construction").</summary>
    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Name or organisation the revision is issued to.</summary>
    [DataMember(Order = 3)]
    public string IssuedTo { get; set; } = string.Empty;

    /// <summary>Name or organisation the revision is issued by.</summary>
    [DataMember(Order = 4)]
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>
    /// Date string for the revision in a project-localised format (e.g. "2026-03-21").
    /// Pass an empty string to let Revit use today's date.
    /// </summary>
    [DataMember(Order = 5)]
    public string RevisionDate { get; set; } = string.Empty;

    /// <summary>
    /// Numbering scheme to apply.
    /// One of: <c>numeric</c>, <c>alphanumeric</c>.
    /// Defaults to the project's current numbering scheme when not specified.
    /// </summary>
    [DataMember(Order = 6)]
    public string Numbering { get; set; } = "numeric";
}

/// <summary>Result of a revision creation operation.</summary>
[DataContract]
public sealed class RevisionCreateResponse
{
    /// <summary>Revit ElementId of the newly created revision element.</summary>
    [DataMember(Order = 1)]
    public int RevisionId { get; set; }

    /// <summary>Sequence number assigned to the revision by Revit (1-based, project-wide order).</summary>
    [DataMember(Order = 2)]
    public int Sequence { get; set; }

    /// <summary>Description as stored on the created revision element.</summary>
    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;
}

// ── Revision List ─────────────────────────────────────────────────────────────

/// <summary>Request to list all project revisions in a document.</summary>
[DataContract]
public sealed class RevisionListRequest
{
    /// <summary>Document key identifying the target document.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;
}

/// <summary>Response containing all project revisions ordered by sequence number.</summary>
[DataContract]
public sealed class RevisionListResponse
{
    /// <summary>All revisions in the document, ordered by ascending sequence number.</summary>
    [DataMember(Order = 1)]
    public List<RevisionItem> Revisions { get; set; } = new List<RevisionItem>();
}

/// <summary>Summary record for a single project revision.</summary>
[DataContract]
public sealed class RevisionItem
{
    /// <summary>Revit ElementId of the revision element.</summary>
    [DataMember(Order = 1)]
    public int RevisionId { get; set; }

    /// <summary>Sequence number (1-based) indicating the revision's position in the project revision table.</summary>
    [DataMember(Order = 2)]
    public int Sequence { get; set; }

    /// <summary>Human-readable description of the revision.</summary>
    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Name or organisation the revision is issued to.</summary>
    [DataMember(Order = 4)]
    public string IssuedTo { get; set; } = string.Empty;

    /// <summary>Name or organisation the revision is issued by.</summary>
    [DataMember(Order = 5)]
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>Date of the revision as stored in the document.</summary>
    [DataMember(Order = 6)]
    public string Date { get; set; } = string.Empty;

    /// <summary>Numbering type used for this revision: <c>numeric</c> or <c>alphanumeric</c>.</summary>
    [DataMember(Order = 7)]
    public string NumberType { get; set; } = string.Empty;
}
