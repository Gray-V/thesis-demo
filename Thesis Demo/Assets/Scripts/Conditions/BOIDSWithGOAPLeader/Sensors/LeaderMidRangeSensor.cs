using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Returns 1 when the player is in mid-range (between guardInnerRange and guardOuterRange).
/// This is the "aware but not engaging" zone where Guard behavior activates.
/// </summary>
public class LeaderMidRangeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.settings == null) return 0;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return 0;

        float dist = Vector3.Distance(boid.Position, player.transform.position);
        float inner = boid.settings.guardInnerRange;
        float outer = boid.settings.guardOuterRange;
        return (dist >= inner && dist <= outer) ? 1 : 0;
    }
}
