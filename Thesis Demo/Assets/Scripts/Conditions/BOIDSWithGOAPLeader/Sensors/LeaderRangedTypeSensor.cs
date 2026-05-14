using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Returns 1 when this leader's flock is ranged type.
/// </summary>
public class LeaderRangedTypeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.settings == null) return 0;
        return boid.settings.flockType == FlockType.Ranged ? 1 : 0;
    }
}
