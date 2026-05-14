using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the fixed-seed reproducibility feature added to ConditionManager.
/// Verifies that Random.InitState produces identical sequences so test runs
/// across conditions can be compared fairly.
/// </summary>
[TestFixture]
public class ReproducibilityTests
{
    // ── Same seed → identical sequences ──

    [Test]
    public void SameSeed_ProducesIdenticalFloatSequence()
    {
        Random.InitState(42);
        float[] run1 = new float[10];
        for (int i = 0; i < run1.Length; i++)
            run1[i] = Random.Range(0f, 1f);

        Random.InitState(42);
        float[] run2 = new float[10];
        for (int i = 0; i < run2.Length; i++)
            run2[i] = Random.Range(0f, 1f);

        for (int i = 0; i < run1.Length; i++)
            Assert.AreEqual(run1[i], run2[i], $"Value at index {i} should be identical with the same seed");
    }

    [Test]
    public void SameSeed_ProducesIdenticalSpawnOffsets()
    {
        // Simulates the GetSpawnOffset random radius used in ConditionManager
        int agentCount = 10;
        float spawnRadius = 15f;

        Random.InitState(42);
        var offsets1 = new Vector3[agentCount];
        for (int i = 0; i < agentCount; i++)
        {
            float angle = (360f / agentCount) * i * Mathf.Deg2Rad;
            float radius = Random.Range(spawnRadius * 0.5f, spawnRadius);
            offsets1[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        Random.InitState(42);
        var offsets2 = new Vector3[agentCount];
        for (int i = 0; i < agentCount; i++)
        {
            float angle = (360f / agentCount) * i * Mathf.Deg2Rad;
            float radius = Random.Range(spawnRadius * 0.5f, spawnRadius);
            offsets2[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < agentCount; i++)
        {
            Assert.AreEqual(offsets1[i].x, offsets2[i].x, 0.001f, $"Offset {i} X");
            Assert.AreEqual(offsets1[i].z, offsets2[i].z, 0.001f, $"Offset {i} Z");
        }
    }

    [Test]
    public void SameSeed_ProducesIdenticalBobPhaseOffsets()
    {
        // Simulates the bob/weave phase offsets from GOAPBoidAgent.Awake()
        int agentCount = 10;

        Random.InitState(42);
        float[] bobs1 = new float[agentCount];
        float[] weaves1 = new float[agentCount];
        for (int i = 0; i < agentCount; i++)
        {
            bobs1[i]   = Random.Range(0f, Mathf.PI * 2f);
            weaves1[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        Random.InitState(42);
        float[] bobs2 = new float[agentCount];
        float[] weaves2 = new float[agentCount];
        for (int i = 0; i < agentCount; i++)
        {
            bobs2[i]   = Random.Range(0f, Mathf.PI * 2f);
            weaves2[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        for (int i = 0; i < agentCount; i++)
        {
            Assert.AreEqual(bobs1[i], bobs2[i], 0.001f, $"Bob phase {i}");
            Assert.AreEqual(weaves1[i], weaves2[i], 0.001f, $"Weave phase {i}");
        }
    }

    // ── Different seeds → different sequences ──

    [Test]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        Random.InitState(42);
        float[] run1 = new float[20];
        for (int i = 0; i < run1.Length; i++)
            run1[i] = Random.Range(0f, 1f);

        Random.InitState(99);
        float[] run2 = new float[20];
        for (int i = 0; i < run2.Length; i++)
            run2[i] = Random.Range(0f, 1f);

        int differences = 0;
        for (int i = 0; i < run1.Length; i++)
            if (Mathf.Abs(run1[i] - run2[i]) > 0.001f)
                differences++;

        Assert.Greater(differences, 0,
            "Different seeds should produce different random sequences");
    }

    // ── Seed covers flee timer range ──

    [Test]
    public void SameSeed_ProducesIdenticalFleeTimers()
    {
        // Simulates LeaderFleeAction.Start() Random.Range(3f, 5f)
        Random.InitState(42);
        float timer1 = Random.Range(3f, 5f);

        Random.InitState(42);
        float timer2 = Random.Range(3f, 5f);

        Assert.AreEqual(timer1, timer2,
            "Flee timer should be identical across runs with the same seed");
        Assert.GreaterOrEqual(timer1, 3f, "Flee timer minimum is 3 seconds");
        Assert.LessOrEqual(timer1, 5f, "Flee timer maximum is 5 seconds");
    }
}
