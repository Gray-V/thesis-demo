using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// AgentType factory for GOAPWithBOIDSMovement condition.
/// Registers "GOAPBoidAgent" with flee, combat, and wander capabilities.
/// </summary>
public class GOAPBoidAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = this.CreateBuilder("GOAPBoidAgent");

        builder.AddCapability<GOAPBoidFleeCapabilityFactory>();
        builder.AddCapability<GOAPBoidCombatCapabilityFactory>();
        builder.AddCapability<GOAPBoidRangedCombatCapabilityFactory>();
        builder.AddCapability<GOAPBoidWanderCapabilityFactory>();
        builder.AddCapability<GOAPBoidScatterCapabilityFactory>();
        builder.AddCapability<GOAPBoidRegroupCapabilityFactory>();
        builder.AddCapability<GOAPBoidFlankCapabilityFactory>();
        builder.AddCapability<GOAPBoidGuardCapabilityFactory>();
        builder.AddCapability<GOAPBoidKiteCapabilityFactory>();

        return builder.Build();
    }
}
