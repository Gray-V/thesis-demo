using CrashKonijn.Goap.Runtime;

/// <summary>
/// World key: a full attack sequence (or flock action) has been completed this planning cycle.
/// Used as the shared goal condition for FlockGoal, MeleeAttackGoal, and RangedAttackGoal.
/// </summary>
public class AttackComplete : WorldKeyBase { }
