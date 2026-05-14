using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

[TestFixture]
public class GOAPBoidAgentTests
{
    private GameObject CreateAgent(int flockId, Vector3 position)
    {
        var go = new GameObject($"TestAgent_{flockId}_{position}");
        go.transform.position = position;

        // GOAPBoidAgent requires a Rigidbody (added in Awake via GetComponent)
        go.AddComponent<Rigidbody>();
        var agent = go.AddComponent<GOAPBoidAgent>();
        agent.flockId = flockId;
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        // Clear the static agent list between tests
        GOAPBoidAgent.AllAgents.Clear();

        foreach (var go in GameObject.FindObjectsByType<GOAPBoidAgent>(FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(go.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator GetFlockmates_ReturnsOnlySameFlockId()
    {
        // Spawn 3 in flock 0, 3 in flock 1
        for (int i = 0; i < 3; i++)
            CreateAgent(0, new Vector3(i, 0, 0));
        for (int i = 0; i < 3; i++)
            CreateAgent(1, new Vector3(i + 10, 0, 0));

        yield return null; // Let OnEnable fire

        var flock0 = GOAPBoidAgent.GetFlockmates(0);
        var flock1 = GOAPBoidAgent.GetFlockmates(1);

        Assert.AreEqual(3, flock0.Count, "Flock 0 should have 3 agents");
        Assert.AreEqual(3, flock1.Count, "Flock 1 should have 3 agents");

        foreach (var a in flock0)
            Assert.AreEqual(0, a.flockId, "Flock 0 agent should have flockId 0");
        foreach (var a in flock1)
            Assert.AreEqual(1, a.flockId, "Flock 1 agent should have flockId 1");
    }

    [UnityTest]
    public IEnumerator GetFlockCentroid_CorrectForFlock()
    {
        CreateAgent(0, new Vector3(0, 0, 0));
        CreateAgent(0, new Vector3(6, 0, 0));
        CreateAgent(0, new Vector3(0, 0, 6));

        yield return null;

        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(0);
        Vector3 expected = new Vector3(2f, 0f, 2f);

        Assert.AreEqual(expected.x, centroid.x, 0.1f, "Centroid X");
        Assert.AreEqual(expected.z, centroid.z, 0.1f, "Centroid Z");
    }

    [UnityTest]
    public IEnumerator GetFlockCount_ReturnsCorrectCount()
    {
        CreateAgent(0, Vector3.zero);
        CreateAgent(0, Vector3.right);
        CreateAgent(1, Vector3.forward * 10f);

        yield return null;

        Assert.AreEqual(2, GOAPBoidAgent.GetFlockCount(0), "Flock 0 count");
        Assert.AreEqual(1, GOAPBoidAgent.GetFlockCount(1), "Flock 1 count");
        Assert.AreEqual(0, GOAPBoidAgent.GetFlockCount(99), "Non-existent flock count");
    }

    [UnityTest]
    public IEnumerator TakeDamage_ReducesHealth()
    {
        var go = CreateAgent(0, Vector3.zero);
        yield return null;

        var agent = go.GetComponent<GOAPBoidAgent>();
        float before = agent.HealthPercent;
        agent.TakeDamage(10f);
        float after = agent.HealthPercent;

        Assert.Less(after, before, "Health should decrease after damage");
    }

    [UnityTest]
    public IEnumerator TakeDamage_KillsAtZero()
    {
        var go = CreateAgent(0, Vector3.zero);
        yield return null;

        var agent = go.GetComponent<GOAPBoidAgent>();
        agent.TakeDamage(9999f);

        Assert.IsTrue(agent.IsDead, "Agent should be dead after lethal damage");
    }

    [UnityTest]
    public IEnumerator GetFlockCentroid_EmptyFlock_ReturnsZero()
    {
        yield return null;

        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(99);
        Assert.AreEqual(Vector3.zero, centroid, "Empty flock centroid should be zero");
    }
}
