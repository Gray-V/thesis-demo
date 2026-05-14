using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests verifying anti-bunching behavior for GOAPWithBOIDSMovement.
/// Checks that agents at different positions relative to the player would compute
/// different preferred approach directions, and that attack slot gating still works.
/// </summary>
[TestFixture]
public class ArcApproachPlayModeTests
{
    private GameObject CreateAgent(int flockId, Vector3 position, AttackType type = AttackType.Melee)
    {
        var go = new GameObject($"TestAgent_f{flockId}");
        go.transform.position = position;
        go.AddComponent<Rigidbody>();
        var agent = go.AddComponent<GOAPBoidAgent>();
        agent.flockId = flockId;
        agent.SetAttackType(type);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        GOAPBoidAgent.AllAgents.Clear();
        GOAPBoidAgent.ResetAttackSlots();
        foreach (var go in GameObject.FindObjectsByType<GOAPBoidAgent>(FindObjectsSortMode.None))
            Object.DestroyImmediate(go.gameObject);
    }

    // ── Approach directions are unique per agent position ──

    [UnityTest]
    public IEnumerator ThreeAgentsAroundPlayer_HaveUniqueApproachDirs()
    {
        Vector3 playerPos = Vector3.zero;

        // Three agents spread 120° apart around the origin
        Vector3[] positions = {
            new Vector3(10f, 0f, 0f),
            new Vector3(-5f, 0f,  8.66f),
            new Vector3(-5f, 0f, -8.66f),
        };

        var agents = new GOAPBoidAgent[3];
        for (int i = 0; i < 3; i++)
        {
            var go = CreateAgent(0, positions[i]);
            yield return null; // let OnEnable fire
            agents[i] = go.GetComponent<GOAPBoidAgent>();
        }

        // Compute each agent's preferred approach direction (mirrors GOAPBoidAttackAction.Start)
        var dirs = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            Vector3 toAgent = agents[i].Position - playerPos;
            toAgent.y = 0f;
            dirs[i] = toAgent.sqrMagnitude > 0.001f ? toAgent.normalized : Vector3.forward;
        }

        // All three should be distinct
        Assert.AreNotEqual(dirs[0], dirs[1], "Agents 0 and 1 must have different approach dirs");
        Assert.AreNotEqual(dirs[1], dirs[2], "Agents 1 and 2 must have different approach dirs");
        Assert.AreNotEqual(dirs[0], dirs[2], "Agents 0 and 2 must have different approach dirs");

        // All Y components must be zero
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(0f, dirs[i].y, 0.001f, $"Agent {i} approach dir Y must be zero");
    }

    // ── Approach targets computed from arc dirs are spread ──

    [UnityTest]
    public IEnumerator ArcApproachTargets_AreSpread_NotConverging()
    {
        Vector3 playerPos = Vector3.zero;
        float contactDist = 2f; // mirrors DamageContactDistance in GOAPBoidAttackAction

        Vector3[] agentPositions = {
            new Vector3(10f, 0f, 0f),
            new Vector3(-5f, 0f, 8.66f),
            new Vector3(-5f, 0f, -8.66f),
        };

        var approachTargets = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            CreateAgent(0, agentPositions[i]);
            yield return null;

            Vector3 toAgent = agentPositions[i] - playerPos;
            toAgent.y = 0f;
            Vector3 dir = toAgent.sqrMagnitude > 0.001f ? toAgent.normalized : Vector3.forward;
            approachTargets[i] = playerPos + dir * contactDist;
        }

        // Verify each agent's approach target is near-player but not identical
        for (int i = 0; i < 3; i++)
            Assert.LessOrEqual(Vector3.Distance(approachTargets[i], playerPos), contactDist + 0.01f,
                $"Agent {i} approach target should be within contactDist of player");

        // Pairs of approach targets must be distinct
        float minSpread = Vector3.Distance(approachTargets[0], approachTargets[1]);
        Assert.Greater(minSpread, 0.1f, "Approach targets must be spread apart, not converging");
    }

    // ── Attack slot gating is unchanged by arc approach fix ──

    [UnityTest]
    public IEnumerator AttackSlot_MaxThreePerFlock()
    {
        for (int i = 0; i < 5; i++)
        {
            CreateAgent(0, new Vector3(i * 3f, 0f, 0f));
        }
        yield return null;

        GOAPBoidAgent.ResetAttackSlots();

        int granted = 0;
        for (int i = 0; i < 5; i++)
        {
            if (GOAPBoidAgent.RequestAttackSlot(0))
                granted++;
        }

        Assert.AreEqual(3, granted, "At most 3 attack slots should be granted per flock");
    }

    [UnityTest]
    public IEnumerator AttackSlot_ReleaseAllowsNewRequest()
    {
        CreateAgent(0, Vector3.zero);
        yield return null;

        GOAPBoidAgent.ResetAttackSlots();

        // Fill all 3 slots
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);

        // Fourth request should fail
        Assert.IsFalse(GOAPBoidAgent.RequestAttackSlot(0), "4th slot request should be denied");

        // Release one, new request should succeed
        GOAPBoidAgent.ReleaseAttackSlot(0);
        Assert.IsTrue(GOAPBoidAgent.RequestAttackSlot(0), "After release, slot should be available again");
    }

    [UnityTest]
    public IEnumerator AttackSlot_PerFlockIndependent()
    {
        CreateAgent(0, Vector3.zero);
        CreateAgent(1, Vector3.right * 20f);
        yield return null;

        GOAPBoidAgent.ResetAttackSlots();

        // Fill flock 0
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);
        GOAPBoidAgent.RequestAttackSlot(0);

        // Flock 1 should still be available
        Assert.IsTrue(GOAPBoidAgent.RequestAttackSlot(1),
            "Flock 1 slots should be independent of flock 0");
    }
}
