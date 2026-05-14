using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// AgentType factory for ranged boids. Add as a component on the GOAP Manager GameObject
/// and drag it into GoapBehaviour.agentTypeConfigFactories.
/// Registers under the key "RangedBoid" — used by FlockManager.SpawnFlock() to look it up.
/// </summary>
public class RangedBoidAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = this.CreateBuilder("RangedBoid");

        builder.AddCapability<FlockCapabilityFactory>();
        builder.AddCapability<RangedCombatCapabilityFactory>();

        return builder.Build();
    }
}
