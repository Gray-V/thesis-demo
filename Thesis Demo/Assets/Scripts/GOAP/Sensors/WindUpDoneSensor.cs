using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses WindUpDone: 1 once WindUpAction.Complete has locked a charge direction.
/// Reset to false by ChargeAction.End so each attack cycle re-plans the wind-up.
/// </summary>
public class WindUpDoneSensor : LocalWorldSensorBase
{
    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var brain = references.GetCachedComponent<BoidGoapBrain>();
        return brain != null && brain.windUpComplete ? 1 : 0;
    }
}
