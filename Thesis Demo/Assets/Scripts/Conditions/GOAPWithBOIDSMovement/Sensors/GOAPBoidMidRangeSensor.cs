using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Returns 1 when the player is in mid-range (between guard inner and outer range).
/// </summary>
public class GOAPBoidMidRangeSensor : LocalWorldSensorBase
{
    private const float GuardInnerRange = 15f;
    private const float GuardOuterRange = 30f;

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null || boidAgent.targetPlayer == null) return 0;

        float dist = Vector3.Distance(boidAgent.Position, boidAgent.targetPlayer.position);
        return (dist >= GuardInnerRange && dist <= GuardOuterRange) ? 1 : 0;
    }
}
