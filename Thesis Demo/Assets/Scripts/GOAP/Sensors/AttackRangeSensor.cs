using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Senses IsInAttackRange: 1 when this boid is within attackTriggerDistance of the player.
/// Key and timer are wired in MeleeCombatCapabilityFactory / RangedCombatCapabilityFactory.
/// </summary>
public class AttackRangeSensor : LocalWorldSensorBase
{
    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null || boid.manager.Target == null)
            return 0;

        float dist = Vector3.Distance(boid.Position, boid.manager.Target.position);
        return dist <= boid.settings.attackTriggerDistance ? 1 : 0;
    }
}
