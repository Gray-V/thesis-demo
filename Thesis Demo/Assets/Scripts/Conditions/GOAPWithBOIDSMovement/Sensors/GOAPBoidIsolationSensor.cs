using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Senses if this GOAPBoid agent is isolated from the swarm centroid.
/// </summary>
public class GOAPBoidIsolationSensor : LocalWorldSensorBase
{
    private const float IsolationThreshold = 20f;

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null || GOAPBoidAgent.GetFlockCount(boidAgent.flockId) <= 1) return 0;

        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(boidAgent.flockId);
        float dist = Vector3.Distance(boidAgent.Position, centroid);
        return dist > IsolationThreshold ? 1 : 0;
    }
}
