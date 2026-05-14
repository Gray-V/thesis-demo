using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses CircleDone: 1 once CircleAction.Complete has finished the orbit phase.
/// Reset to false by FireAction.End so each attack cycle re-plans the circle.
/// </summary>
public class CircleDoneSensor : LocalWorldSensorBase
{
    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var brain = references.GetCachedComponent<BoidGoapBrain>();
        return brain != null && brain.circleDone ? 1 : 0;
    }
}
