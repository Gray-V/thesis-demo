using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the arc-approach direction formula used in GOAPBoidAttackAction and
/// GOAPBoidRangedAttackAction. Each attacker stores a preferred approach direction
/// (player → agent, XZ-plane) at action start so simultaneous attackers spread in
/// an arc rather than converging on the same point.
/// </summary>
[TestFixture]
public class ArcApproachDirectionTests
{
    // Mirrors the formula in GOAPBoidAttackAction.Start() and GOAPBoidRangedAttackAction.Start()
    private static Vector3 ComputePreferredApproachDir(Vector3 agentPos, Vector3 playerPos)
    {
        Vector3 toAgent = agentPos - playerPos;
        toAgent.y = 0f;
        return toAgent.sqrMagnitude > 0.001f ? toAgent.normalized : Vector3.forward;
    }

    // ── Direction is unique per approach angle ──

    [Test]
    public void AgentsAtOppositeAngles_HaveOppositeApproachDirs()
    {
        Vector3 player = Vector3.zero;
        Vector3 agent0 = new Vector3(10f, 0f, 0f);   // east
        Vector3 agent1 = new Vector3(-10f, 0f, 0f);  // west

        Vector3 dir0 = ComputePreferredApproachDir(agent0, player);
        Vector3 dir1 = ComputePreferredApproachDir(agent1, player);

        // Dot product of opposite directions is -1
        Assert.AreEqual(-1f, Vector3.Dot(dir0, dir1), 0.001f,
            "Agents on opposite sides should have opposite approach directions");
    }

    [Test]
    public void AgentsAt90Degrees_HaveOrthogonalApproachDirs()
    {
        Vector3 player = Vector3.zero;
        Vector3 agentNorth = new Vector3(0f, 0f, 10f);
        Vector3 agentEast  = new Vector3(10f, 0f, 0f);

        Vector3 dirN = ComputePreferredApproachDir(agentNorth, player);
        Vector3 dirE = ComputePreferredApproachDir(agentEast, player);

        Assert.AreEqual(0f, Vector3.Dot(dirN, dirE), 0.001f,
            "Agents at 90° from player should have orthogonal approach directions");
    }

    [Test]
    public void TwoAgentsAtDifferentAngles_HaveDifferentDirs()
    {
        Vector3 player = new Vector3(5f, 0f, 5f);
        Vector3 agentA = new Vector3(0f, 0f, 0f);
        Vector3 agentB = new Vector3(10f, 2f, 0f);

        Vector3 dirA = ComputePreferredApproachDir(agentA, player);
        Vector3 dirB = ComputePreferredApproachDir(agentB, player);

        // Directions should not be identical
        Assert.AreNotEqual(dirA, dirB,
            "Agents at different positions relative to player must have different approach directions");
    }

    // ── Y-component is always zeroed (XZ-plane only) ──

    [Test]
    public void ApproachDir_YComponentIsAlwaysZero()
    {
        Vector3 player = Vector3.zero;

        // Agent above the player
        Vector3 dir1 = ComputePreferredApproachDir(new Vector3(5f, 20f, 0f), player);
        Assert.AreEqual(0f, dir1.y, 0.001f, "Y component must be zero even if agent is above player");

        // Agent below the player
        Vector3 dir2 = ComputePreferredApproachDir(new Vector3(5f, -20f, 0f), player);
        Assert.AreEqual(0f, dir2.y, 0.001f, "Y component must be zero even if agent is below player");
    }

    // ── Result is always unit length ──

    [Test]
    public void ApproachDir_IsNormalized()
    {
        Vector3 dir = ComputePreferredApproachDir(new Vector3(3f, 5f, 7f), Vector3.zero);
        Assert.AreEqual(1f, dir.magnitude, 0.001f, "Approach direction should be a unit vector");
    }

    // ── Fallback when agent is at same XZ position as player ──

    [Test]
    public void AgentAtSameXZ_UsesFallbackForward()
    {
        Vector3 player = new Vector3(5f, 0f, 5f);
        Vector3 agent  = new Vector3(5f, 99f, 5f); // same XZ, different Y

        Vector3 dir = ComputePreferredApproachDir(agent, player);
        Assert.AreEqual(Vector3.forward, dir,
            "Agent directly above/below player (same XZ) should fall back to Vector3.forward");
    }

    // ── Spread: 3 simultaneous attackers have meaningfully different directions ──

    [Test]
    public void ThreeAttackersFromDifferentAngles_AllHaveUniqueDirections()
    {
        Vector3 player = Vector3.zero;
        Vector3[] agentPositions = {
            new Vector3(10f, 0f, 0f),
            new Vector3(-5f, 0f, 8.66f),
            new Vector3(-5f, 0f, -8.66f),
        };

        var dirs = new Vector3[3];
        for (int i = 0; i < 3; i++)
            dirs[i] = ComputePreferredApproachDir(agentPositions[i], player);

        // Each pair must be distinct
        Assert.AreNotEqual(dirs[0], dirs[1], "Attacker 0 and 1 must have different dirs");
        Assert.AreNotEqual(dirs[1], dirs[2], "Attacker 1 and 2 must have different dirs");
        Assert.AreNotEqual(dirs[0], dirs[2], "Attacker 0 and 2 must have different dirs");
    }
}
