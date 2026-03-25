using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class AnnotationMutationService
{
    internal ExecutionResult PreviewAddTextNote(PlatformServices services, Document doc, AddTextNoteRequest request, ToolRequestEnvelope envelope)
    {
        var view = ResolvePlacementView(doc, request.ViewId);
        var textTypeId = ResolveTextNoteTypeId(doc, request.TextNoteTypeId);
        var point = ResolveTextNotePoint(view, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("TEXTNOTE_VIEW_RESOLVED", DiagnosticSeverity.Info, "Resolved view: " + view.Name, checked((int)view.Id.Value)),
                DiagnosticRecord.Create("TEXTNOTE_TYPE_RESOLVED", DiagnosticSeverity.Info, "Resolved TextNoteTypeId: " + textTypeId.Value.ToString(CultureInfo.InvariantCulture), checked((int)textTypeId.Value)),
                DiagnosticRecord.Create("TEXTNOTE_POINT_RESOLVED", DiagnosticSeverity.Info, $"Resolved point: ({point.X.ToString(CultureInfo.InvariantCulture)}, {point.Y.ToString(CultureInfo.InvariantCulture)}, {point.Z.ToString(CultureInfo.InvariantCulture)})")
            },
            Artifacts = new List<string>
            {
                "viewId=" + view.Id.Value.ToString(CultureInfo.InvariantCulture),
                "textNoteTypeId=" + textTypeId.Value.ToString(CultureInfo.InvariantCulture),
                "point=" + point
            }
        };
    }

    internal ExecutionResult ExecuteAddTextNote(PlatformServices services, Document doc, AddTextNoteRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var view = ResolvePlacementView(doc, request.ViewId);
        var textTypeId = ResolveTextNoteTypeId(doc, request.TextNoteTypeId);
        var point = ResolveTextNotePoint(view, request);
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::annotation.add_text_note_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Add text note safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        var note = TextNote.Create(doc, view.Id, point, request.Text ?? string.Empty, textTypeId);

        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            CreatedIds = new List<int> { checked((int)note.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("text_note_review", diff);
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "annotation.add_text_note_safe",
            DryRun = false,
            ChangedIds = diff.CreatedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "viewId=" + view.Id.Value.ToString(CultureInfo.InvariantCulture),
                "point=" + point,
                diff.CreatedIds.Count > 0 ? "createdId=" + diff.CreatedIds[0].ToString(CultureInfo.InvariantCulture) : "createdId=<none>"
            },
            ReviewSummary = review
        };
    }

    internal ExecutionResult PreviewUpdateTextNoteStyle(PlatformServices services, Document doc, UpdateTextNoteStyleRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildTextNoteStylePlan(doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "textNoteId=" + plan.TextNote.Id.Value.ToString(CultureInfo.InvariantCulture),
                "currentTypeId=" + plan.CurrentType.Id.Value.ToString(CultureInfo.InvariantCulture),
                "targetTypeName=" + plan.TargetTypeName,
                "reuseExisting=" + plan.ReuseExistingType
            },
            ReviewSummary = new ReviewReport
            {
                Name = "text_note_style_preview",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = plan.Diagnostics.Count,
                Issues = plan.Diagnostics.Select(x => new ReviewIssue
                {
                    Code = x.Code,
                    Severity = x.Severity,
                    Message = x.Message,
                    ElementId = x.SourceId
                }).ToList()
            }
        };
    }

    internal ExecutionResult ExecuteUpdateTextNoteStyle(PlatformServices services, Document doc, UpdateTextNoteStyleRequest request)
    {
        var plan = BuildTextNoteStylePlan(doc, request);
        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::annotation.update_text_note_style_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Update text note style safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        var targetType = ResolveOrCreateTextNoteTargetType(doc, plan, diagnostics);
        if (plan.TextNote.GetTypeId() != targetType.Id)
        {
            plan.TextNote.ChangeTypeId(targetType.Id);
        }

        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            ModifiedIds = new List<int> { checked((int)plan.TextNote.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };
        if (targetType.Id != plan.CurrentType.Id)
        {
            diff.ModifiedIds.Add(checked((int)targetType.Id.Value));
        }
        if (plan.CreatedNewType)
        {
            diff.CreatedIds.Add(checked((int)targetType.Id.Value));
        }

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("text_note_style_review", diff);
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "annotation.update_text_note_style_safe",
            DryRun = false,
            ChangedIds = diff.CreatedIds.Concat(diff.ModifiedIds).Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "textNoteId=" + plan.TextNote.Id.Value.ToString(CultureInfo.InvariantCulture),
                "currentTypeId=" + plan.CurrentType.Id.Value.ToString(CultureInfo.InvariantCulture),
                "targetTypeName=" + plan.TargetTypeName,
                diff.CreatedIds.Count > 0 ? "createdTypeId=" + diff.CreatedIds[0].ToString(CultureInfo.InvariantCulture) : "createdTypeId=<none>"
            },
            ReviewSummary = review
        };
    }

    internal ExecutionResult PreviewUpdateTextNoteContent(PlatformServices services, Document doc, UpdateTextNoteContentRequest request, ToolRequestEnvelope envelope)
    {
        var textNote = ResolveTextNote(doc, request.TextNoteId);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("TEXTNOTE_CONTENT_NOTE_RESOLVED", DiagnosticSeverity.Info, "Resolved TextNote.", checked((int)textNote.Id.Value)),
                DiagnosticRecord.Create("TEXTNOTE_CONTENT_OLD", DiagnosticSeverity.Info, "Old text: " + textNote.Text, checked((int)textNote.Id.Value)),
                DiagnosticRecord.Create("TEXTNOTE_CONTENT_NEW", DiagnosticSeverity.Info, "New text: " + (request.NewText ?? string.Empty), checked((int)textNote.Id.Value))
            },
            Artifacts = new List<string>
            {
                "textNoteId=" + textNote.Id.Value.ToString(CultureInfo.InvariantCulture)
            },
            ReviewSummary = new ReviewReport
            {
                Name = "text_note_content_preview",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = 0
            }
        };
    }

    internal ExecutionResult ExecuteUpdateTextNoteContent(PlatformServices services, Document doc, UpdateTextNoteContentRequest request)
    {
        var textNote = ResolveTextNote(doc, request.TextNoteId);
        var diagnostics = new List<DiagnosticRecord>();
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::annotation.update_text_note_content_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Update text note content safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        textNote.Text = request.NewText ?? string.Empty;

        doc.Regenerate();
        transaction.Commit();

        var diff = new DiffSummary
        {
            ModifiedIds = new List<int> { checked((int)textNote.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("text_note_content_review", diff);
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "annotation.update_text_note_content_safe",
            DryRun = false,
            ChangedIds = diff.ModifiedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "textNoteId=" + textNote.Id.Value.ToString(CultureInfo.InvariantCulture)
            },
            ReviewSummary = review
        };
    }

    private static View ResolvePlacementView(Document doc, int? viewId)
    {
        if (viewId.HasValue)
        {
            var explicitView = doc.GetElement(new ElementId((long)viewId.Value)) as View;
            if (explicitView != null)
            {
                return explicitView;
            }
        }

        if (doc.ActiveView == null)
        {
            throw new RevitContextException("Không có active view để dùng cho annotation placement.");
        }

        return doc.ActiveView;
    }

    private static TextNoteStylePlan BuildTextNoteStylePlan(Document doc, UpdateTextNoteStyleRequest request)
    {
        var textNote = ResolveTextNote(doc, request.TextNoteId);
        var currentType = doc.GetElement(textNote.GetTypeId()) as TextNoteType
            ?? throw new InvalidOperationException("Không resolve được TextNoteType hiện tại.");

        var color = ResolveRequestedColor(request, currentType);
        var textSizeValue = string.IsNullOrWhiteSpace(request.TextSizeValue)
            ? currentType.LookupParameter("Text Size")?.AsValueString() ?? "1/8\""
            : request.TextSizeValue.Trim();

        var targetTypeName = string.IsNullOrWhiteSpace(request.TargetTypeName)
            ? BuildTextNoteTypeName(currentType.Name, textSizeValue, color)
            : request.TargetTypeName.Trim();

        var usageCount = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNote))
            .Cast<TextNote>()
            .Count(x => x.GetTypeId() == currentType.Id);

        var plan = new TextNoteStylePlan
        {
            TextNote = textNote,
            CurrentType = currentType,
            TargetTypeName = targetTypeName,
            TextSizeValue = textSizeValue,
            ColorValue = color,
            ReuseExistingType = request.ReuseMatchingExistingType
        };

        plan.Diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_NOTE_RESOLVED", DiagnosticSeverity.Info, "Resolved TextNote.", checked((int)textNote.Id.Value)));
        plan.Diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_CURRENT_TYPE", DiagnosticSeverity.Info, "Current type: " + currentType.Name, checked((int)currentType.Id.Value)));
        plan.Diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_USAGE_COUNT", usageCount > 1 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info, $"Current type usage count: {usageCount}", checked((int)currentType.Id.Value)));
        plan.Diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_TARGET", DiagnosticSeverity.Info, $"Target style => Name: {targetTypeName}, Size: {textSizeValue}, Color: {color}"));

        if (usageCount > 1 && !request.DuplicateCurrentTypeIfNeeded)
        {
            throw new InvalidOperationException("Current TextNoteType đang được dùng bởi nhiều note; DuplicateCurrentTypeIfNeeded=false nên không thể sửa an toàn.");
        }

        return plan;
    }

    private static ElementId ResolveTextNoteTypeId(Document doc, int? textNoteTypeId)
    {
        if (textNoteTypeId.HasValue)
        {
            var requested = new ElementId((long)textNoteTypeId.Value);
            if (doc.GetElement(requested) is TextNoteType)
            {
                return requested;
            }
        }

        var fallback = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .WhereElementIsElementType()
            .FirstElementId();
        if (fallback == ElementId.InvalidElementId)
        {
            throw new InvalidOperationException("Không tìm thấy TextNoteType nào trong document.");
        }

        return fallback;
    }

    private static TextNoteType ResolveOrCreateTextNoteTargetType(Document doc, TextNoteStylePlan plan, List<DiagnosticRecord> diagnostics)
    {
        var matching = FindMatchingTextNoteType(doc, plan.TargetTypeName, plan.TextSizeValue, plan.ColorValue);
        if (matching != null && plan.ReuseExistingType)
        {
            diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_REUSE_MATCH", DiagnosticSeverity.Info, "Reuse existing matching type.", checked((int)matching.Id.Value)));
            return matching;
        }

        if (IsCurrentTypeMatching(plan.CurrentType, plan.TextSizeValue, plan.ColorValue))
        {
            diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_CURRENT_MATCH", DiagnosticSeverity.Info, "Current type already matches requested style.", checked((int)plan.CurrentType.Id.Value)));
            return plan.CurrentType;
        }

        var typeName = EnsureUniqueTextNoteTypeName(doc, plan.TargetTypeName);
        var duplicated = plan.CurrentType.Duplicate(typeName) as TextNoteType
            ?? throw new InvalidOperationException("Duplicate TextNoteType thất bại.");

        var textSize = duplicated.LookupParameter("Text Size");
        if (textSize == null || textSize.IsReadOnly)
        {
            throw new InvalidOperationException("Không set được parameter `Text Size` trên TextNoteType.");
        }
        ParameterMutationHelper.SetParameterValue(textSize, plan.TextSizeValue);

        var color = duplicated.LookupParameter("Color");
        if (color == null || color.IsReadOnly)
        {
            throw new InvalidOperationException("Không set được parameter `Color` trên TextNoteType.");
        }
        color.Set(plan.ColorValue);

        plan.CreatedNewType = true;
        diagnostics.Add(DiagnosticRecord.Create("TEXTNOTE_STYLE_TYPE_CREATED", DiagnosticSeverity.Info, "Created new TextNoteType: " + duplicated.Name, checked((int)duplicated.Id.Value)));
        return duplicated;
    }

    private static TextNoteType? FindMatchingTextNoteType(Document doc, string targetTypeName, string textSizeValue, int colorValue)
    {
        foreach (var type in new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>())
        {
            if (!string.Equals(type.Name, targetTypeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsCurrentTypeMatching(type, textSizeValue, colorValue))
            {
                return type;
            }
        }

        return null;
    }

    private static bool IsCurrentTypeMatching(TextNoteType type, string textSizeValue, int colorValue)
    {
        var size = type.LookupParameter("Text Size")?.AsValueString() ?? string.Empty;
        var color = type.LookupParameter("Color")?.AsInteger() ?? 0;
        return string.Equals(size.Trim(), (textSizeValue ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            && color == colorValue;
    }

    private static string EnsureUniqueTextNoteTypeName(Document doc, string baseName)
    {
        var names = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().Select(x => x.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; i < 500; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không tạo được unique TextNoteType name.");
    }

    private static int ResolveRequestedColor(UpdateTextNoteStyleRequest request, TextNoteType currentType)
    {
        if (request.Red.HasValue || request.Green.HasValue || request.Blue.HasValue)
        {
            return ToRevitColorInt(request.Red ?? 0, request.Green ?? 0, request.Blue ?? 0);
        }

        return currentType.LookupParameter("Color")?.AsInteger() ?? 0;
    }

    private static int ToRevitColorInt(int red, int green, int blue)
    {
        red = ClampColor(red);
        green = ClampColor(green);
        blue = ClampColor(blue);
        return red + (green << 8) + (blue << 16);
    }

    private static int ClampColor(int value)
    {
        return Math.Max(0, Math.Min(255, value));
    }

    private static string BuildTextNoteTypeName(string currentName, string textSizeValue, int colorValue)
    {
        return $"{SanitizeTypeNameSegment(currentName)}_BIM765T_{SanitizeTypeNameSegment(textSizeValue)}_C{colorValue}";
    }

    private static string SanitizeTypeNameSegment(string value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "Custom" : value.Trim();
        var invalid = new[] { "\\", "/", ":", ";", ",", "\"", "'", "|", "[", "]", "{", "}", "(", ")", "<", ">", "?", "`", "~" };
        foreach (var item in invalid)
        {
            raw = raw.Replace(item, "_");
        }
        return raw.Replace(" ", "_");
    }

    private static TextNote ResolveTextNote(Document doc, int textNoteId)
    {
        var textNote = doc.GetElement(new ElementId((long)textNoteId)) as TextNote;
        if (textNote == null)
        {
            throw new InvalidOperationException("TextNoteId không tồn tại hoặc không phải TextNote.");
        }

        return textNote;
    }

    private static XYZ ResolveTextNotePoint(View view, AddTextNoteRequest request)
    {
        if (request.UseViewCenterWhenPossible && view.CropBox != null)
        {
            var crop = view.CropBox;
            return (crop.Min + crop.Max) * 0.5;
        }

        return new XYZ(request.X, request.Y, request.Z);
    }

    private sealed class TextNoteStylePlan
    {
        internal TextNote TextNote { get; set; } = null!;
        internal TextNoteType CurrentType { get; set; } = null!;
        internal string TargetTypeName { get; set; } = string.Empty;
        internal string TextSizeValue { get; set; } = string.Empty;
        internal int ColorValue { get; set; }
        internal bool ReuseExistingType { get; set; }
        internal bool CreatedNewType { get; set; }
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }
}
