using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses if flock health is critically low (below criticalHealthThreshold).
/// Used to trigger Scatter, which has a lower threshold than Flee.
/// </summary>
public class LeaderCriticalHealthSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null) return 0;
        float threshold = boid.settings != null ? boid.settings.criticalHealthThreshold : 0.15f;
        return boid.manager.HealthPercent < threshold ? 1 : 0;
    }
}
