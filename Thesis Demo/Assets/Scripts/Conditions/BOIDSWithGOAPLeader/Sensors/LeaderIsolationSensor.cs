using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Senses if the leader is isolated (too far from the flock centroid).
/// </summary>
public class LeaderIsolationSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null) return 0;

        float threshold = boid.settings != null ? boid.settings.isolationThreshold : 20f;
        Vector3 centroid = boid.manager.GetFlockCenter();
        float dist = Vector3.Distance(boid.Position, centroid);
        return dist > threshold ? 1 : 0;
    }
}
