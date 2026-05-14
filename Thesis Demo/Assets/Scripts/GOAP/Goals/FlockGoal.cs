using CrashKonijn.Goap.Runtime;

/// <summary>
/// Fallback goal — satisfied by FlockAction.
/// Configured in FlockCapabilityFactory with BaseCost=10 so attack goals are always preferred.
/// </summary>
public class FlockGoal : GoalBase { }
