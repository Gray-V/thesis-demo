using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Returns the flock manager's position as the guard point.
/// The leader holds this position while followers form around via cohesion.
/// </summary>
public class LeaderGuardPointSensor : LocalTargetSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null)
            return existingTarget;

        return new PositionTarget(boid.manager.transform.position);
    }
}
