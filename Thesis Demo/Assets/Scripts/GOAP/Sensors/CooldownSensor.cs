using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses IsOnCooldown: 1 while BoidGoapBrain.cooldownTimer > 0.
/// </summary>
public class CooldownSensor : LocalWorldSensorBase
{
    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var brain = references.GetCachedComponent<BoidGoapBrain>();
        return brain != null && brain.cooldownTimer > 0f ? 1 : 0;
    }
}
