using System;

namespace BIM765T.Revit.Agent.Infrastructure.Time;

internal sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Now => DateTime.Now;
}
