using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Agent.Config;

[DataContract]
internal sealed class BridgePolicy
{
    [DataMember(Order = 1)]
    public List<string> DisabledTools { get; set; } = new List<string>();

    [DataMember(Order = 2)]
    public List<string> HighRiskTools { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public bool DenyBackgroundOpenRead { get; set; }

    [DataMember(Order = 4)]
    public List<string> DisabledCapabilityPacks { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> DisabledSkillGroups { get; set; } = new List<string>();
}
