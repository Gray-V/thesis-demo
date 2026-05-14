using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
public class FlockSeparationTests
{
    private GameObject CreateAgent(int flockId, AttackType attackType, Vector3 position)
    {
        var go = new GameObject($"TestBoid_{flockId}_{attackType}");
        go.transform.position = position;
        go.AddComponent<Rigidbody>();
        var agent = go.AddComponent<GOAPBoidAgent>();
        agent.flockId = flockId;
        agent.SetAttackType(attackType);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        GOAPBoidAgent.AllAgents.Clear();
        foreach (var go in GameObject.FindObjectsByType<GOAPBoidAgent>(FindObjectsSortMode.None))
            Object.DestroyImmediate(go.gameObject);
    }

    [UnityTest]
    public IEnumerator SpawnGOAPBoids_AssignsCorrectFlockIds()
    {
        // Simulate what ConditionManager does: melee=flock0, ranged=flock1
        for (int i = 0; i < 5; i++)
            CreateAgent(0, AttackType.Melee, new Vector3(i, 0, 0));
        for (int i = 0; i < 3; i++)
            CreateAgent(1, AttackType.Ranged, new Vector3(i + 20, 0, 0));

        yield return null;

        Assert.AreEqual(5, GOAPBoidAgent.GetFlockCount(0), "Melee flock count");
        Assert.AreEqual(3, GOAPBoidAgent.GetFlockCount(1), "Ranged flock count");
    }

    [UnityTest]
    public IEnumerator SpawnGOAPBoids_AssignsCorrectAttackTypes()
    {
        CreateAgent(0, AttackType.Melee, Vector3.zero);
        CreateAgent(1, AttackType.Ranged, Vector3.right * 20f);

        yield return null;

        var melee = GOAPBoidAgent.GetFlockmates(0);
        var ranged = GOAPBoidAgent.GetFlockmates(1);

        Assert.AreEqual(AttackType.Melee, melee[0].AgentAttackType);
        Assert.AreEqual(AttackType.Ranged, ranged[0].AgentAttackType);
    }

    [UnityTest]
    public IEnumerator FlocksCohereWithinNotAcross()
    {
        // Place two flocks far apart
        for (int i = 0; i < 4; i++)
            CreateAgent(0, AttackType.Melee, new Vector3(i * 2f, 0f, 0f));
        for (int i = 0; i < 4; i++)
            CreateAgent(1, AttackType.Ranged, new Vector3(i * 2f + 100f, 0f, 0f));

        // Run some frames to let BOIDS forces act
        for (int f = 0; f < 30; f++)
            yield return null;

        Vector3 centroid0 = GOAPBoidAgent.GetFlockCentroid(0);
        Vector3 centroid1 = GOAPBoidAgent.GetFlockCentroid(1);

        // Each agent should be closer to its own centroid than the other
        foreach (var agent in GOAPBoidAgent.GetFlockmates(0))
        {
            float distOwn = Vector3.Distance(agent.Position, centroid0);
            float distOther = Vector3.Distance(agent.Position, centroid1);
            Assert.Less(distOwn, distOther,
                $"Flock 0 agent at {agent.Position} should be closer to own centroid ({centroid0}) than other ({centroid1})");
        }

        foreach (var agent in GOAPBoidAgent.GetFlockmates(1))
        {
            float distOwn = Vector3.Distance(agent.Position, centroid1);
            float distOther = Vector3.Distance(agent.Position, centroid0);
            Assert.Less(distOwn, distOther,
                $"Flock 1 agent at {agent.Position} should be closer to own centroid ({centroid1}) than other ({centroid0})");
        }
    }
}
