using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Context;

namespace BIM765T.Revit.Agent.Addin.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdCurrentContext : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var ctx = new Services.Context.CurrentContextService().Read(commandData.Application);
        Show(ctx);
        return Result.Succeeded;
    }

    private static void Show(CurrentContextDto ctx)
    {
        var text =
            $"Document: {ctx.DocumentName}\n" +
            $"View: {ctx.ViewName} ({ctx.ViewType})\n" +
            $"Level: {ctx.LevelName ?? "<unknown>"}\n" +
            $"Mode: {ctx.LevelMode}\n" +
            $"Confidence: {ctx.Confidence}";
        TaskDialog.Show("765T Revit Bridge Context", text);
    }
}
