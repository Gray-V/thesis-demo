using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for <see cref="AutomatedPlayer.TakeClosest"/>, the per-attack
/// hit-cap helper added 2026-05-04 to bound per-cast damage by physical swing
/// reach. Without this cap, dense-cohesion conditions (notably
/// BOIDSWithGOAPLeader after the 2026-04-25 follower-mimic-leader fix) drain
/// the flock pooled HP super-linearly with N because every boid in the AOE
/// radius takes a hit. Each cap (light=3, heavy=6, AOE=8) is load-bearing
/// methodology — a regression here silently breaks RQ2 trial-duration parity.
///
/// The static signature `(List&lt;IEnemy&gt; enemies, Vector3 origin,
/// Func&lt;IEnemy, Vector3&gt; getPos, int maxTargets)` is chosen so this test
/// fixture can supply a stub IEnemy + a position lookup without instantiating
/// any GameObjects.
/// </summary>
[TestFixture]
public class AutomatedPlayerTakeClosestTests
{
    /// <summary>Minimal IEnemy stub. We don't exercise damage paths — only the
    /// closest-N filter — so all behaviour members are no-ops.</summary>
    private sealed class StubEnemy : IEnemy
    {
        public bool IsDead => false;
        public float HealthPercent => 1f;
        public void TakeDamage(float amount) { /* no-op */ }
    }

    /// <summary>Build N stubs at increasing distance from origin along +X. Returns
    /// the list and a lookup so the test can verify which stubs survived.</summary>
    private static (List<IEnemy> enemies, Dictionary<IEnemy, Vector3> positions)
        MakeRow(int count, float spacing = 1f)
    {
        var enemies = new List<IEnemy>(count);
        var positions = new Dictionary<IEnemy, Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            var e = new StubEnemy();
            enemies.Add(e);
            positions[e] = new Vector3(i * spacing, 0f, 0f);
        }
        return (enemies, positions);
    }

    // ── Cap behaviour ──

    [Test]
    public void TakeClosest_KeepsExactlyMaxTargets_WhenCountExceedsCap()
    {
        var (enemies, positions) = MakeRow(50);
        AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: 8);
        Assert.AreEqual(8, enemies.Count);
    }

    [Test]
    public void TakeClosest_KeepsTheClosestEntries_WhenCountExceedsCap()
    {
        // Closest 8 are at x = 0..7 since stubs are placed at x = 0..49.
        var (enemies, positions) = MakeRow(50);
        AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: 8);
        for (int i = 0; i < 8; i++)
        {
            Assert.AreEqual(i, (int)positions[enemies[i]].x,
                $"Survivor at index {i} should be the {i}th closest stub");
        }
    }

    [Test]
    public void TakeClosest_NoOp_WhenCountAlreadyWithinCap()
    {
        var (enemies, positions) = MakeRow(5);
        AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: 8);
        Assert.AreEqual(5, enemies.Count, "Cap above count should not drop any entries");
    }

    [Test]
    public void TakeClosest_NoOp_WhenMaxTargetsZeroOrNegative()
    {
        var (enemies, positions) = MakeRow(10);
        AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: 0);
        Assert.AreEqual(10, enemies.Count, "maxTargets=0 should disable the cap (keep all)");

        AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: -3);
        Assert.AreEqual(10, enemies.Count, "Negative maxTargets should also disable the cap");
    }

    [Test]
    public void TakeClosest_HandlesEmptyAndNullList()
    {
        Assert.DoesNotThrow(() => AutomatedPlayer.TakeClosest(
            new List<IEnemy>(), Vector3.zero, _ => Vector3.zero, maxTargets: 8));

        Assert.DoesNotThrow(() => AutomatedPlayer.TakeClosest(
            null, Vector3.zero, _ => Vector3.zero, maxTargets: 8));
    }

    // ── Origin sensitivity ──

    [Test]
    public void TakeClosest_UsesOriginForSorting_NotWorldZero()
    {
        // 10 stubs at x = 0..9; cap to 3. With origin at x=9 the closest 3 are
        // the right-most stubs (x=7,8,9), not the left-most.
        var (enemies, positions) = MakeRow(10);
        AutomatedPlayer.TakeClosest(enemies,
            origin: new Vector3(9f, 0f, 0f),
            getPos: e => positions[e],
            maxTargets: 3);
        Assert.AreEqual(3, enemies.Count);
        var survivorXs = new HashSet<int>();
        foreach (var e in enemies) survivorXs.Add((int)positions[e].x);
        Assert.IsTrue(survivorXs.Contains(7) && survivorXs.Contains(8) && survivorXs.Contains(9),
            "With origin at x=9 the 3 closest stubs should be x∈{7,8,9}");
    }

    // ── Cluster-density invariant (the methodology promise) ──

    [Test]
    public void TakeClosest_PerCastImpactBoundedByCap_AcrossClusterDensities()
    {
        // The methodology guarantee: per-cast damage to the flock pool is bounded
        // by maxTargets × dmg, regardless of cluster density. Verify the
        // "bounded survivors" half of that contract for cluster sizes that mirror
        // the 25..800 sweep — the cap must hold at every population.
        foreach (int n in new[] { 25, 50, 100, 200, 400, 800 })
        {
            var (enemies, positions) = MakeRow(n);
            AutomatedPlayer.TakeClosest(enemies, Vector3.zero, e => positions[e], maxTargets: 8);
            Assert.LessOrEqual(enemies.Count, 8,
                $"At cluster size {n} the cap should hold survivors at ≤ 8");
        }
    }
}
