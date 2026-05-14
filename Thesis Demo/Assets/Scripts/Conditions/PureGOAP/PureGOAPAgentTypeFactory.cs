using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// AgentType factory for PureGOAP experimental condition.
/// Combines flee, combat, group-up, and wander capabilities for individual GOAP agents.
///
/// Behavior: Each agent independently plans using GOAP, no flocking.
/// Movement: Direct steering toward targets.
/// Target Detection: VisionSensor for player.
/// </summary>
public class PureGOAPAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = this.CreateBuilder("PureGOAPAgent");

        builder.AddCapability<PureFleeCapabilityFactory>();
        builder.AddCapability<PureCombatCapabilityFactory>();
        builder.AddCapability<PureRangedCombatCapabilityFactory>();
        builder.AddCapability<PureWanderCapabilityFactory>();

        return builder.Build();
    }
}
