using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Provides the flock centroid as a target position for the leader's regroup action.
/// </summary>
public class LeaderCentroidTargetSensor : LocalTargetSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null)
            return existingTarget;

        Vector3 centroid = boid.manager.GetFlockCenter();
        return new PositionTarget(centroid);
    }
}
