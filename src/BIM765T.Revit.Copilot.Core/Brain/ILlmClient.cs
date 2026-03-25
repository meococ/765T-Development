using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.Copilot.Core.Brain;

public interface ILlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken);
}
