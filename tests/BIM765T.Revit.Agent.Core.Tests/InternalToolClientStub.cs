using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Agent.UI;

internal sealed class InternalToolClient
{
    internal static InternalToolClient Instance { get; } = new InternalToolClient();

    internal Task<ToolResponseEnvelope> CallAsync(string toolName, string? payloadJson = null, bool dryRun = true, string? approvalToken = null, string? previewRunId = null)
    {
        return Task.FromResult(new ToolResponseEnvelope
        {
            ToolName = toolName,
            StatusCode = StatusCodes.ReadSucceeded,
            Succeeded = true
        });
    }
}
