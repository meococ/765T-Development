using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Agent.Addin.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdSettings : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDirectory = Path.Combine(appData, BridgeConstants.AppDataFolderName);
            Directory.CreateDirectory(settingsDirectory);
            var settingsPath = Path.Combine(settingsDirectory, "settings.json");

            if (!File.Exists(settingsPath))
            {
                File.WriteAllText(settingsPath, "{\r\n}\r\n");
            }

            Process.Start("explorer.exe", "/select,\"" + settingsPath + "\"");
            TaskDialog.Show("765T AI Settings", "Đã mở vị trí settings.json trong Explorer.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
