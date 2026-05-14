using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses IsHealthLow: 1 when agent health is below 30%.
/// </summary>
public class HealthLowSensor : LocalWorldSensorBase
{
    private static readonly float FleeThreshold = 0.3f;

    public override ISensorTimer Timer => SensorTimer.Interval(0.1f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var pureAgent = references.GetCachedComponent<PureGOAPAgent>();
        if (pureAgent == null)
            return 0;

        return pureAgent.HealthPercent < FleeThreshold ? 1 : 0;
    }
}
