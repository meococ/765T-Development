using System.Collections.Generic;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Workflow;

internal static class BuiltInWorkflows
{
    internal static List<WorkflowDefinition> Create()
    {
        return new List<WorkflowDefinition>
        {
            new WorkflowDefinition
            {
                WorkflowName = "workflow.model_health",
                DisplayName = "Model Health",
                Description = "Task context → model health → warnings → snapshot → evidence bundle.",
                Category = "qc",
                SupportsApply = true,
                RequiresApproval = false,
                RulePackTags = new List<string> { "document_health_v1", "model_health" },
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"DocumentKey\":{\"type\":\"string\"},\"SnapshotScope\":{\"type\":\"string\"}},\"required\":[]}",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepName = "task_context", ToolName = ToolNames.SessionGetTaskContext, Mode = "read", Description = "Capture task context bundle." },
                    new WorkflowStep { StepName = "model_health", ToolName = ToolNames.ReviewModelHealth, Mode = "read", Description = "Run model health summary." },
                    new WorkflowStep { StepName = "warnings", ToolName = ToolNames.ReviewModelWarnings, Mode = "read", Description = "Collect Revit warnings." },
                    new WorkflowStep { StepName = "snapshot", ToolName = ToolNames.ReviewCaptureSnapshot, Mode = "read", Description = "Capture structured snapshot.", IsCheckpoint = true }
                }
            },
            new WorkflowDefinition
            {
                WorkflowName = "workflow.sheet_qc",
                DisplayName = "Sheet QC",
                Description = "Sheet summary → viewport layout → rule pack → snapshot/evidence.",
                Category = "qc",
                SupportsApply = true,
                RequiresApproval = false,
                RulePackTags = new List<string> { "sheet_qc", "sheet_summary" },
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":true,\"properties\":{\"SheetId\":{\"type\":\"integer\"},\"SheetNumber\":{\"type\":\"string\"},\"SheetName\":{\"type\":\"string\"}}}",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepName = "sheet_summary", ToolName = ToolNames.ReviewSheetSummary, Mode = "read", Description = "Read sheet metadata and placed views." },
                    new WorkflowStep { StepName = "viewport_layout", ToolName = ToolNames.SheetGetViewportLayout, Mode = "read", Description = "Read viewport layout on sheet." },
                    new WorkflowStep { StepName = "snapshot", ToolName = ToolNames.ReviewCaptureSnapshot, Mode = "read", Description = "Capture sheet evidence snapshot.", IsCheckpoint = true }
                }
            },
            new WorkflowDefinition
            {
                WorkflowName = "workflow.parameter_rollout",
                DisplayName = "Parameter Rollout",
                Description = "Preview import/fill → completeness audit → guarded execute → evidence.",
                Category = "mutation",
                SupportsApply = true,
                RequiresApproval = true,
                RiskTags = new List<string> { "mutation", "parameters", "batch" },
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":true,\"properties\":{\"InputPath\":{\"type\":\"string\"},\"MatchParameterName\":{\"type\":\"string\"},\"ParameterName\":{\"type\":\"string\"},\"FillValue\":{\"type\":\"string\"},\"ElementIds\":{\"type\":\"array\",\"items\":{\"type\":\"integer\"}},\"CategoryNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepName = "preview", ToolName = ToolNames.DataPreviewImport, Mode = "read", Description = "Preview import or target set." },
                    new WorkflowStep { StepName = "completeness", ToolName = ToolNames.ReviewParameterCompleteness, Mode = "read", Description = "Audit parameter completeness." },
                    new WorkflowStep { StepName = "dry_run", ToolName = ToolNames.ParameterBatchFillSafe, Mode = "dry_run", Description = "Generate preview diff and approval.", IsCheckpoint = true },
                    new WorkflowStep { StepName = "apply", ToolName = ToolNames.ParameterBatchFillSafe, Mode = "apply", Description = "Execute approved rollout." }
                }
            },
            new WorkflowDefinition
            {
                WorkflowName = "workflow.family_axis_audit",
                DisplayName = "Family Axis Audit",
                Description = "Family axis audit → issues → evidence bundle.",
                Category = "qc",
                SupportsApply = true,
                RequiresApproval = false,
                RulePackTags = new List<string> { "family_axis_alignment" },
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":true,\"properties\":{\"ViewId\":{\"type\":\"integer\"},\"ViewName\":{\"type\":\"string\"},\"CategoryNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepName = "axis_audit", ToolName = ToolNames.ReviewFamilyAxisAlignment, Mode = "read", Description = "Audit visible family axes." },
                    new WorkflowStep { StepName = "snapshot", ToolName = ToolNames.ReviewCaptureSnapshot, Mode = "read", Description = "Capture evidence snapshot.", IsCheckpoint = true }
                }
            },
            new WorkflowDefinition
            {
                WorkflowName = "workflow.penetration_round_shadow",
                DisplayName = "Penetration Round Shadow",
                Description = "Inventory → round shadow plan → dry-run batch create → approved execute.",
                Category = "mutation",
                SupportsApply = true,
                RequiresApproval = true,
                RiskTags = new List<string> { "mutation", "penetration", "batch" },
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":true,\"properties\":{\"SourceFamilyName\":{\"type\":\"string\"},\"RoundFamilyName\":{\"type\":\"string\"},\"PreferredReferenceMark\":{\"type\":\"string\"},\"MaxResults\":{\"type\":\"integer\"}}}",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepName = "inventory", ToolName = ToolNames.ReportPenetrationAlphaInventory, Mode = "read", Description = "Inventory source penetrations." },
                    new WorkflowStep { StepName = "plan", ToolName = ToolNames.ReportPenetrationRoundShadowPlan, Mode = "read", Description = "Build round shadow plan." },
                    new WorkflowStep { StepName = "dry_run", ToolName = ToolNames.BatchCreateRoundShadowSafe, Mode = "dry_run", Description = "Generate preview diff and approval.", IsCheckpoint = true },
                    new WorkflowStep { StepName = "apply", ToolName = ToolNames.BatchCreateRoundShadowSafe, Mode = "apply", Description = "Execute approved batch create." }
                }
            }
        };
    }
}
