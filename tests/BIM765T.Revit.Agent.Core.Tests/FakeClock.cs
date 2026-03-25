using System;
using BIM765T.Revit.Agent.Infrastructure.Time;

namespace BIM765T.Revit.Agent.Core.Tests;

internal sealed class FakeClock : ISystemClock
{
    internal FakeClock(DateTime utcNow)
    {
        UtcNow = utcNow;
        Now = utcNow.ToLocalTime();
    }

    public DateTime UtcNow { get; private set; }

    public DateTime Now { get; private set; }

    internal void Advance(TimeSpan delta)
    {
        UtcNow = UtcNow.Add(delta);
        Now = Now.Add(delta);
    }
}
