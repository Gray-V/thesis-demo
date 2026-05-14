using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Returns 1 when this GOAPBoid agent is a ranged attack type.
/// </summary>
public class GOAPBoidRangedTypeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null) return 0;
        return boidAgent.AgentAttackType == AttackType.Ranged ? 1 : 0;
    }
}
