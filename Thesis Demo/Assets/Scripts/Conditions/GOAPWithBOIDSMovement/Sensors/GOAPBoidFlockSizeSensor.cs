using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Returns 1 when 3 or more GOAPBoid agents are alive (enough for flanking).
/// </summary>
public class GOAPBoidFlockSizeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null) return 0;
        return GOAPBoidAgent.GetFlockCount(boidAgent.flockId) >= 3 ? 1 : 0;
    }
}
