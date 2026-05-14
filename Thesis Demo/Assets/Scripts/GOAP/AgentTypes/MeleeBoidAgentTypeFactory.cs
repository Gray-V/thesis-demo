using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// AgentType factory for melee boids. Add as a component on the GOAP Manager GameObject
/// and drag it into GoapBehaviour.agentTypeConfigFactories.
/// Registers under the key "MeleeBoid" — used by FlockManager.SpawnFlock() to look it up.
/// </summary>
public class MeleeBoidAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = this.CreateBuilder("MeleeBoid");

        builder.AddCapability<FlockCapabilityFactory>();
        builder.AddCapability<MeleeCombatCapabilityFactory>();

        return builder.Build();
    }
}
