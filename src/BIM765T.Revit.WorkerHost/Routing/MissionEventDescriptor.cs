namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionEventDescriptor
{
    public string EventType { get; set; } = string.Empty;

    public object Payload { get; set; } = new { };

    public bool Terminal { get; set; }
}
