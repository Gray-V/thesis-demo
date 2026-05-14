using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class ScatterDirectionPickerTests
{
    [Test]
    public void ScatterDirection_IsNormalized()
    {
        Vector3 dir = ScatterDirectionPicker.PickScatterDirection(Vector3.zero, Vector3.forward * 5f);
        Assert.AreEqual(1f, dir.magnitude, 0.01f, "Scatter direction should be a unit vector");
    }

    [Test]
    public void ScatterDirection_IsHorizontal()
    {
        // Run multiple times since there is randomness
        for (int i = 0; i < 20; i++)
        {
            Vector3 dir = ScatterDirectionPicker.PickScatterDirection(
                new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f)),
                new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f)));

            Assert.AreEqual(0f, dir.y, 0.001f, $"Iteration {i}: Y component should be 0");
        }
    }

    [Test]
    public void ScatterDirection_BiasedAwayFromCentroid()
    {
        // Statistical test: over many samples, majority should have positive dot product
        // with the away-from-centroid direction
        Vector3 agentPos = new Vector3(10f, 0f, 0f);
        Vector3 centroid = Vector3.zero;
        Vector3 awayDir = (agentPos - centroid).normalized;

        int awayCount = 0;
        int total = 100;

        for (int i = 0; i < total; i++)
        {
            Vector3 dir = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid);
            if (Vector3.Dot(dir, awayDir) > 0f)
                awayCount++;
        }

        // With +-90 degree offset, at least ~50% should face away; expect well over that
        Assert.Greater(awayCount, total * 0.4f,
            $"Expected majority of scatter directions to face away from centroid, got {awayCount}/{total}");
    }

    [Test]
    public void ScatterDirection_HandlesAgentAtCentroid()
    {
        // When agent is exactly at centroid, should not produce NaN or zero vector
        Vector3 dir = ScatterDirectionPicker.PickScatterDirection(Vector3.zero, Vector3.zero);

        Assert.IsFalse(float.IsNaN(dir.x) || float.IsNaN(dir.y) || float.IsNaN(dir.z),
            "Should not produce NaN when agent is at centroid");
        Assert.Greater(dir.magnitude, 0.5f,
            "Should produce a non-zero direction even when positions match");
    }

    [Test]
    public void ScatterDirection_VaryingPositions_NeverReturnsZero()
    {
        for (int i = 0; i < 50; i++)
        {
            Vector3 agentPos = new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-50f, 50f));
            Vector3 centroid = new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-50f, 50f));

            Vector3 dir = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid);
            Assert.Greater(dir.magnitude, 0.5f,
                $"Iteration {i}: Direction should never be near-zero");
        }
    }

    // ── Subgroup bucketing (Bug 5 fix) ──

    [Test]
    public void SubgroupScatter_SameBucket_DirectionsClose()
    {
        // 50 calls with the same subgroupId should all land within the bucket's
        // ±15° jitter envelope of one another. Two members of the same subgroup
        // should never differ by more than 30° of yaw.
        Vector3 agentPos = new Vector3(10f, 0f, 0f);
        Vector3 centroid = Vector3.zero;

        Vector3 first = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid, subgroupId: 1, subgroupCount: 3);
        for (int i = 0; i < 50; i++)
        {
            Vector3 dir = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid, subgroupId: 1, subgroupCount: 3);
            float angle = Vector3.Angle(first, dir);
            Assert.LessOrEqual(angle, 31f,
                $"Iteration {i}: same-subgroup directions should be within 30° of each other (was {angle:F1}°)");
        }
    }

    [Test]
    public void SubgroupScatter_DifferentBuckets_Diverge()
    {
        // Buckets 0 and 2 in a 3-bucket partition are 120° apart in the angular
        // domain. Their directions must therefore have a non-trivial angular gap.
        Vector3 agentPos = new Vector3(10f, 0f, 0f);
        Vector3 centroid = Vector3.zero;

        for (int i = 0; i < 20; i++)
        {
            Vector3 a = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid, subgroupId: 0, subgroupCount: 3);
            Vector3 c = ScatterDirectionPicker.PickScatterDirection(agentPos, centroid, subgroupId: 2, subgroupCount: 3);
            float angle = Vector3.Angle(a, c);
            Assert.GreaterOrEqual(angle, 60f,
                $"Iteration {i}: different-bucket directions should diverge by at least 60° (was {angle:F1}°)");
        }
    }

    [Test]
    public void SubgroupScatter_ClampsInvalidArgs()
    {
        // subgroupCount < 1 should be treated as a single bucket;
        // subgroupId out of range should be clamped.
        Vector3 dir1 = ScatterDirectionPicker.PickScatterDirection(Vector3.right * 10f, Vector3.zero, 0, 0);
        Assert.AreEqual(1f, dir1.magnitude, 0.01f);
        Assert.AreEqual(0f, dir1.y, 0.001f);

        Vector3 dir2 = ScatterDirectionPicker.PickScatterDirection(Vector3.right * 10f, Vector3.zero, 99, 3);
        Assert.AreEqual(1f, dir2.magnitude, 0.01f);
        Assert.AreEqual(0f, dir2.y, 0.001f);
    }
}
