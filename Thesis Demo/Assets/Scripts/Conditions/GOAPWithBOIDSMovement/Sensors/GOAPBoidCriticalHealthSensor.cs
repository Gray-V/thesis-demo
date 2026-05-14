using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses if this GOAPBoid agent's health is critically low.
/// Uses agent's own health (individual, not flock-based).
/// </summary>
public class GOAPBoidCriticalHealthSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null) return 0;
        return boidAgent.HealthPercent < 0.15f ? 1 : 0;
    }
}
