/// <summary>
/// Defines the four experimental conditions for the multi-agent AI thesis.
/// Each condition represents a distinct AI architecture for comparative analysis.
/// </summary>
public enum AgentCondition
{
    /// <summary>
    /// Pure Goal-Oriented Action Planning.
    /// Individual agents use GOAP for decision-making, NavMesh for pathfinding.
    /// No flocking behaviors. Target: player.
    /// </summary>
    PureGOAP,

    /// <summary>
    /// Pure BOIDS flocking system.
    /// No individual planning. Three steering forces (separation, alignment, cohesion)
    /// applied each frame within perception radius. Flee force when player enters radius.
    /// NavMesh used only for obstacle avoidance.
    /// </summary>
    PureBOIDS,

    /// <summary>
    /// BOIDS with GOAP Leader.
    /// One agent per flock uses GOAP for high-level decisions.
    /// Remaining agents are BOIDS that use leader position as cohesion target,
    /// while applying separation and alignment among themselves.
    /// </summary>
    BOIDSWithGOAPLeader,

    /// <summary>
    /// GOAP with BOIDS Movement.
    /// Individual GOAP planning per agent. NavMesh pathfinding as base movement layer.
    /// BOIDS steering forces (separation, alignment, cohesion) applied on top of
    /// NavMesh velocity for group awareness.
    /// </summary>
    GOAPWithBOIDSMovement
}
