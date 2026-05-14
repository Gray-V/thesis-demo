using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Returns 1 when the flock has 3 or more boids (enough for flanking).
/// </summary>
public class LeaderFlockSizeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null) return 0;
        return boid.manager.BoidCount >= 3 ? 1 : 0;
    }
}
