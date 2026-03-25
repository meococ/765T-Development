using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.McpHost;

internal static class ToolCatalog
{
    internal static object[] Build(IEnumerable<ToolManifest> manifests)
    {
        return manifests
            .OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(Tool)
            .ToArray();
    }

    private static object Tool(ToolManifest manifest)
    {
        var readOnly = manifest.PermissionLevel == PermissionLevel.Read || manifest.PermissionLevel == PermissionLevel.Review;
        return new
        {
            name = manifest.ToolName,
            description = manifest.Description,
            inputSchema = BuildInputSchema(manifest, readOnly),
            annotations = new
            {
                title = manifest.ToolName,
                readOnlyHint = readOnly,
                destructiveHint = manifest.PermissionLevel == PermissionLevel.FileLifecycle
                    || manifest.RiskTags.Any(x => string.Equals(x, "delete", StringComparison.OrdinalIgnoreCase)),
                idempotentHint = !string.Equals(manifest.Idempotency, "non_idempotent", StringComparison.OrdinalIgnoreCase),
                openWorldHint = true,
                approvalRequirement = manifest.ApprovalRequirement.ToString(),
                permissionLevel = manifest.PermissionLevel.ToString(),
                enabled = manifest.Enabled,
                supportsDryRun = manifest.SupportsDryRun,
                inputSchemaHint = string.IsNullOrWhiteSpace(manifest.InputSchemaHint) ? null : manifest.InputSchemaHint,
                requiredContext = manifest.RequiredContext,
                mutatesModel = manifest.MutatesModel,
                touchesActiveView = manifest.TouchesActiveView,
                requiresExpectedContext = manifest.RequiresExpectedContext,
                batchMode = manifest.BatchMode,
                riskTags = manifest.RiskTags,
                rulePackTags = manifest.RulePackTags,
                previewArtifacts = manifest.PreviewArtifacts,
                riskTier = manifest.RiskTier,
                canAutoExecute = manifest.CanAutoExecute,
                latencyClass = manifest.LatencyClass,
                uiSurface = manifest.UiSurface,
                progressMode = manifest.ProgressMode,
                recommendedNextTools = manifest.RecommendedNextTools,
                domainGroup = manifest.DomainGroup,
                taskFamily = manifest.TaskFamily,
                packId = manifest.PackId,
                recommendedPlaybooks = manifest.RecommendedPlaybooks,
                capabilityDomain = manifest.CapabilityDomain,
                determinismLevel = manifest.DeterminismLevel,
                requiresPolicyPack = manifest.RequiresPolicyPack,
                verificationMode = manifest.VerificationMode,
                supportedDisciplines = manifest.SupportedDisciplines,
                issueKinds = manifest.IssueKinds,
                commandFamily = manifest.CommandFamily,
                executionMode = manifest.ExecutionMode,
                nativeCommandId = manifest.NativeCommandId,
                sourceKind = manifest.SourceKind,
                sourceRef = manifest.SourceRef,
                safetyClass = manifest.SafetyClass,
                canPreview = manifest.CanPreview,
                coverageTier = manifest.CoverageTier,
                fallbackEntryIds = manifest.FallbackEntryIds
            }
        };
    }

    private static object BuildInputSchema(ToolManifest manifest, bool readOnly = true)
    {
        if (!string.IsNullOrWhiteSpace(manifest.InputSchemaJson))
        {
            try
            {
                return JsonNode.Parse(manifest.InputSchemaJson!) ?? BuildGenericInputSchema(readOnly);
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.InputSchemaHint))
        {
            try
            {
                return BuildSchemaFromHint(manifest.InputSchemaHint!, readOnly);
            }
            catch
            {
            }
        }

        return BuildGenericInputSchema(readOnly);
    }

    private static object BuildGenericInputSchema(bool readOnly = true)
    {
        var properties = new JsonObject
        {
            ["target_document"] = new JsonObject { ["type"] = "string" },
            ["target_view"] = new JsonObject { ["type"] = "string" },
            ["payload"] = new JsonObject { ["type"] = "object" }
        };

        if (!readOnly)
        {
            properties["dry_run"] = new JsonObject { ["type"] = "boolean" };
            properties["approval_token"] = new JsonObject { ["type"] = "string" };
            properties["preview_run_id"] = new JsonObject { ["type"] = "string" };
            properties["expected_context"] = new JsonObject { ["type"] = "object" };
            properties["scope_descriptor"] = new JsonObject { ["type"] = "object" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };
    }

    private static JsonObject BuildSchemaFromHint(string hintJson, bool readOnly)
    {
        var payloadNode = JsonNode.Parse(hintJson);
        var properties = new JsonObject
        {
            ["target_document"] = new JsonObject { ["type"] = "string" },
            ["target_view"] = new JsonObject { ["type"] = "string" },
            ["payload"] = InferSchemaNode(payloadNode)
        };

        if (!readOnly)
        {
            properties["dry_run"] = new JsonObject { ["type"] = "boolean" };
            properties["approval_token"] = new JsonObject { ["type"] = "string" };
            properties["preview_run_id"] = new JsonObject { ["type"] = "string" };
            properties["expected_context"] = new JsonObject { ["type"] = "object" };
            properties["scope_descriptor"] = new JsonObject { ["type"] = "object" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };
    }

    private static JsonNode InferSchemaNode(JsonNode? sample)
    {
        if (sample is JsonObject obj)
        {
            var properties = new JsonObject();
            foreach (var kvp in obj)
            {
                properties[kvp.Key] = InferSchemaNode(kvp.Value);
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties
            };
        }

        if (sample is JsonArray array)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = array.Count > 0 ? InferSchemaNode(array[0]) : new JsonObject { ["type"] = "string" }
            };
        }

        if (sample == null)
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (sample is JsonValue value)
        {
            if (value.TryGetValue<bool>(out _))
            {
                return new JsonObject { ["type"] = "boolean" };
            }
            if (value.TryGetValue<int>(out _))
            {
                return new JsonObject { ["type"] = "integer" };
            }
            if (value.TryGetValue<long>(out _))
            {
                return new JsonObject { ["type"] = "integer" };
            }
            if (value.TryGetValue<double>(out _))
            {
                return new JsonObject { ["type"] = "number" };
            }
        }

        return new JsonObject { ["type"] = "string" };
    }
}
