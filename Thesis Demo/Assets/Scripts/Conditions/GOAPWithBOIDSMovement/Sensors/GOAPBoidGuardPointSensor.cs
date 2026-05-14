using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Provides the swarm centroid as the guard point for GOAPBoids agents.
/// </summary>
public class GOAPBoidGuardPointSensor : LocalTargetSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null || GOAPBoidAgent.GetFlockCount(boidAgent.flockId) == 0)
            return existingTarget;

        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(boidAgent.flockId);
        return new PositionTarget(centroid);
    }
}
