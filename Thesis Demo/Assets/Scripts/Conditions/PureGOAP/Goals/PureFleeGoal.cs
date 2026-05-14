using CrashKonijn.Goap.Runtime;

/// <summary>
/// Goal: Flee from player when health is low.
/// Highest priority (cost 0) — overrides attack and wander.
/// </summary>
public class PureFleeGoal : GoalBase { }
