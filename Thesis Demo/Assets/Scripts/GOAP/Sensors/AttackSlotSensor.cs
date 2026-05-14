using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses AttackSlotFree: 1 when the flock still has an available simultaneous-attacker slot.
/// Read-only check — does NOT consume the slot (that happens in WindUpAction/CircleAction.Start).
/// </summary>
public class AttackSlotSensor : LocalWorldSensorBase
{
    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boid = references.GetCachedComponent<BoidAgent>();
        if (boid == null || boid.manager == null)
            return 0;

        return boid.manager.CanAttack ? 1 : 0;
    }
}
