using System;

namespace BIM765T.Revit.Agent.Infrastructure.Time;

internal interface ISystemClock
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
}
