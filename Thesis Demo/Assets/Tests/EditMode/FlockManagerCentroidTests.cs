using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="FlockManager.ComputeFollowerCentroid"/> — the pure helper
/// powering the leader leash's distance measurement. Production callsite:
/// <c>LeaderGoapBrain.Update</c>'s leash block, via <c>FlockManager.GetFollowerCentroid()</c>.
///
/// The instance method <c>GetFollowerCentroid()</c> is a thin wrapper that delegates
/// here; the helper is what we test because it can be driven without scene setup.
/// Anti-drift pattern matches <c>LeaderGoapBrain.ResolveGoalRequestType</c> and
/// <c>BehavioralMetricsCollector.ShannonEntropy</c>.
/// </summary>
[TestFixture]
public class FlockManagerCentroidTests
{
    private static System.Func<int, Vector3> Positions(params Vector3[] points)
    {
        return i => points[i];
    }

    [Test]
    public void NoLeader_ReturnsFallback()
    {
        Vector3 fallback = new Vector3(99f, 0f, 99f);
        var positions = Positions(new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f));
        Vector3 result = FlockManager.ComputeFollowerCentroid(2, leaderIndex: -1, positions, fallback);
        Assert.AreEqual(fallback, result, "leaderIndex < 0 should return fallback");
    }

    [Test]
    public void OnlyLeader_ReturnsFallback()
    {
        Vector3 fallback = new Vector3(7f, 0f, 7f);
        var positions = Positions(new Vector3(10f, 0f, 0f));
        Vector3 result = FlockManager.ComputeFollowerCentroid(1, leaderIndex: 0, positions, fallback);
        Assert.AreEqual(fallback, result, "Single-boid flock (leader only) should return fallback");
    }

    [Test]
    public void EmptyFlock_ReturnsFallback()
    {
        Vector3 fallback = Vector3.zero;
        Vector3 result = FlockManager.ComputeFollowerCentroid(0, leaderIndex: 0, i => Vector3.zero, fallback);
        Assert.AreEqual(fallback, result, "Empty flock should return fallback");
    }

    [Test]
    public void LeaderAndOneFollower_ReturnsFollowerPosition()
    {
        Vector3 fallback = Vector3.zero;
        var positions = Positions(
            new Vector3(10f, 0f, 0f),  // leader at index 0
            new Vector3(2f, 0f, 6f));  // follower at index 1
        Vector3 result = FlockManager.ComputeFollowerCentroid(2, leaderIndex: 0, positions, fallback);
        Assert.AreEqual(new Vector3(2f, 0f, 6f), result,
            "With one follower the centroid should equal that follower's position");
    }

    [Test]
    public void LeaderAndManyFollowers_ExcludesLeader()
    {
        // Leader at (100,0,0); three followers form a small cluster near the origin.
        // Expected centroid (followers only): ((0+0+4)/3, 0, (0+4+0)/3) = (1.333, 0, 1.333).
        Vector3 fallback = Vector3.zero;
        var positions = Positions(
            new Vector3(100f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 4f),
            new Vector3(4f, 0f, 0f));
        Vector3 result = FlockManager.ComputeFollowerCentroid(4, leaderIndex: 0, positions, fallback);
        Assert.AreEqual(1.333f, result.x, 0.01f, "Follower-only x-centroid");
        Assert.AreEqual(0f, result.y, 0.01f, "Follower-only y-centroid");
        Assert.AreEqual(1.333f, result.z, 0.01f, "Follower-only z-centroid");
    }

    [Test]
    public void DilutionMagnitude_AtN17_LeaderFar_DemonstratesBugTheFixAddresses()
    {
        // Mirrors the integration-test concern: ranged flock at N≈17 (leader + 16 followers),
        // leader 30 m past the follower bulk. GetFlockCenter() (full-flock average) sits ~1/17
        // closer to the leader than the follower-only centroid.
        Vector3[] points = new Vector3[17];
        points[0] = new Vector3(30f, 0f, 0f); // leader
        for (int i = 1; i < 17; i++)
            points[i] = Vector3.zero;          // followers stacked at origin (worst-case bulk)

        // Full-flock centroid (what the buggy GetFlockCenter call would have produced)
        Vector3 fullSum = Vector3.zero;
        for (int i = 0; i < 17; i++) fullSum += points[i];
        Vector3 fullCenter = fullSum / 17f;

        // Follower-only centroid via the helper
        Vector3 followerOnly = FlockManager.ComputeFollowerCentroid(
            17, leaderIndex: 0, i => points[i], fallback: Vector3.zero);

        float fullDist     = Vector3.Distance(points[0], fullCenter);
        float followerDist = Vector3.Distance(points[0], followerOnly);

        // Sanity: follower-only distance equals the leader's actual offset (30 m).
        Assert.AreEqual(30f, followerDist, 0.01f, "Follower-only distance is the leader's true offset from bulk");
        // Dilution: full-flock distance is (N-1)/N of the true distance.
        Assert.AreEqual(30f * 16f / 17f, fullDist, 0.01f, "Full-flock distance is diluted by 1/N");
        // The fix recovers ~1.76 m of "honest" distance at N=17 — enough that the leash threshold (12 m) reads correctly.
        Assert.Greater(followerDist - fullDist, 1.5f,
            "GetFollowerCentroid should report the leader meaningfully farther than GetFlockCenter at small N");
    }
}
