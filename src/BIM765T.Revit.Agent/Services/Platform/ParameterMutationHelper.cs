using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace BIM765T.Revit.Agent.Services.Platform;

internal static class ParameterMutationHelper
{
    internal static void SetParameterValue(Parameter parameter, string value)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                parameter.Set(value);
                return;
            case StorageType.Integer:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    parameter.Set(i);
                    return;
                }
                break;
            case StorageType.Double:
                if (parameter.SetValueString(value))
                {
                    return;
                }
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    parameter.Set(d);
                    return;
                }
                break;
            case StorageType.ElementId:
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                {
                    parameter.Set(new ElementId(eid));
                    return;
                }
                break;
        }

        throw new InvalidOperationException($"Không convert được value `{value}` cho parameter `{parameter.Definition?.Name}`.");
    }
}
