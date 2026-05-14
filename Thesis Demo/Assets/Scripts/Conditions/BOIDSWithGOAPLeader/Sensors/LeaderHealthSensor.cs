using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses flock health for the leader (uses FlockManager's shared health pool).
/// </summary>
public class LeaderHealthSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null) return 0;
        return boid.manager.HealthPercent < 0.3f ? 1 : 0;
    }
}
