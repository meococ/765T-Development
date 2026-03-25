namespace BIM765T.Revit.Copilot.Core.Brain;

public interface IPromptContextBuilder
{
    string BuildPromptContext(WorkerConversationSessionState session);
}
