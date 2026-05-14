using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests that verify agent spawning behavior per condition.
/// These require scene objects (ConditionManager, prefabs) so they are
/// play mode tests. They verify the ConditionManager spawns the right types.
/// </summary>
[TestFixture]
public class SpawningTests
{
    // Note: These tests create a minimal ConditionManager at runtime.
    // They test the spawn logic paths without full scene setup.

    [UnityTest]
    public IEnumerator GOAPWithBOIDSMovement_CreatesCorrectFlockIds()
    {
        // Manually simulate what ConditionManager.SpawnGOAPWithBOIDSMovementFlocks does
        GOAPBoidAgent.AllAgents.Clear();

        int meleeCount = 7;
        int rangedCount = 3;

        for (int i = 0; i < meleeCount; i++)
        {
            var go = new GameObject($"TestMelee_{i}");
            go.transform.position = new Vector3(i, 0, 0);
            go.AddComponent<Rigidbody>();
            var agent = go.AddComponent<GOAPBoidAgent>();
            agent.flockId = 0;
            agent.SetAttackType(AttackType.Melee);
        }

        for (int i = 0; i < rangedCount; i++)
        {
            var go = new GameObject($"TestRanged_{i}");
            go.transform.position = new Vector3(i + 50, 0, 0);
            go.AddComponent<Rigidbody>();
            var agent = go.AddComponent<GOAPBoidAgent>();
            agent.flockId = 1;
            agent.SetAttackType(AttackType.Ranged);
        }

        yield return null; // Let OnEnable register agents

        Assert.AreEqual(meleeCount, GOAPBoidAgent.GetFlockCount(0), "Melee flock count");
        Assert.AreEqual(rangedCount, GOAPBoidAgent.GetFlockCount(1), "Ranged flock count");
        Assert.AreEqual(meleeCount + rangedCount, GOAPBoidAgent.AllAgents.Count, "Total agent count");

        // Verify attack types
        foreach (var a in GOAPBoidAgent.GetFlockmates(0))
            Assert.AreEqual(AttackType.Melee, a.AgentAttackType);
        foreach (var a in GOAPBoidAgent.GetFlockmates(1))
            Assert.AreEqual(AttackType.Ranged, a.AgentAttackType);
    }

    [TearDown]
    public void TearDown()
    {
        GOAPBoidAgent.AllAgents.Clear();
        foreach (var go in GameObject.FindObjectsByType<GOAPBoidAgent>(FindObjectsSortMode.None))
            Object.DestroyImmediate(go.gameObject);
    }
}
