using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for the boid floor / inside-collider containment fix
/// (methodology-revisions-2026-04 item 9). Covers the three pure / near-pure
/// surfaces extracted out of BoidAgent so the regression net does not require
/// a full scene + manager + settings scaffold:
///
///   - <see cref="BoidAgent.ApplyFloorClamp"/>      — the Y-floor hard clamp
///   - <see cref="BoidAgent.ApplyKnockback"/>       — Y-impulse clamp
///   - <see cref="BoidAgent.ComputeOverlapEscape"/> — math half of the
///     OverlapSphere inside-collider recovery probe
///   - <see cref="BoidAgent.GetEscapeReference"/>   — collider-type-safe
///     reference point for the recovery probe (must not throw on non-convex
///     MeshColliders / TerrainColliders)
///
/// The Physics.OverlapSphereNonAlloc call itself is verified end-to-end by the
/// PlayMode containment smoke (no boid drops below y=0.4 in a 60s N=200 trial),
/// not here — EditMode cannot exercise PhysX overlap queries against real
/// colliders.
/// </summary>
[TestFixture]
public class BoidContainmentTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // ApplyFloorClamp — pure ref-param helper.
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void FloorClamp_AboveFloor_NoOp()
    {
        Vector3 pos = new Vector3(1f, 5f, 2f);
        Vector3 vel = new Vector3(0f, -3f, 0f);
        bool fired = BoidAgent.ApplyFloorClamp(ref pos, ref vel, 0.5f);

        Assert.IsFalse(fired, "Boid above the floor must not trigger the clamp.");
        Assert.AreEqual(5f, pos.y, 1e-5f, "Position Y unchanged when above floor.");
        Assert.AreEqual(-3f, vel.y, 1e-5f, "Velocity Y unchanged when above floor.");
    }

    [Test]
    public void FloorClamp_BelowFloor_RaisesPositionAndZeroesDownwardVelocity()
    {
        Vector3 pos = new Vector3(1f, -2f, 2f);
        Vector3 vel = new Vector3(4f, -10f, 1f);
        bool fired = BoidAgent.ApplyFloorClamp(ref pos, ref vel, 0.5f);

        Assert.IsTrue(fired, "Below-floor must trigger the clamp.");
        Assert.AreEqual(0.5f, pos.y, 1e-5f, "Position Y must be raised to minY.");
        Assert.AreEqual(4f,  vel.x, 1e-5f, "Lateral velocity X unchanged.");
        Assert.AreEqual(0f,  vel.y, 1e-5f, "Downward velocity Y must be zeroed.");
        Assert.AreEqual(1f,  vel.z, 1e-5f, "Lateral velocity Z unchanged.");
    }

    [Test]
    public void FloorClamp_BelowFloorWithUpwardVelocity_PreservesUpwardVelocity()
    {
        // Edge case: a boid clipping out of the floor. We raise the position
        // but must NOT clamp the upward velocity to zero — that would freeze
        // the recovery motion.
        Vector3 pos = new Vector3(0f, -0.1f, 0f);
        Vector3 vel = new Vector3(0f, 5f, 0f);
        bool fired = BoidAgent.ApplyFloorClamp(ref pos, ref vel, 0.5f);

        Assert.IsTrue(fired);
        Assert.AreEqual(0.5f, pos.y, 1e-5f);
        Assert.AreEqual(5f, vel.y, 1e-5f, "Upward velocity must survive the clamp.");
    }

    [Test]
    public void FloorClamp_ExactlyAtMinY_NoOp()
    {
        // Boundary condition: exactly at minY is "above floor" per the >=
        // comparison — no clamp, no velocity edit.
        Vector3 pos = new Vector3(0f, 0.5f, 0f);
        Vector3 vel = new Vector3(0f, -1f, 0f);
        bool fired = BoidAgent.ApplyFloorClamp(ref pos, ref vel, 0.5f);

        Assert.IsFalse(fired);
        Assert.AreEqual(0.5f, pos.y, 1e-5f);
        Assert.AreEqual(-1f, vel.y, 1e-5f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ApplyKnockback — Y-impulse clamp on a real BoidAgent component.
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Knockback_DownwardImpulse_ClampedAtZero()
    {
        var go = new GameObject("KnockbackTestBoid");
        var agent = go.AddComponent<BoidAgent>();
        // Awake() does fire on AddComponent in EditMode for a freshly added
        // component; cachedTransform is set, velocity defaults to zero.

        agent.ApplyKnockback(new Vector3(5f, -10f, 0f));

        Assert.AreEqual(5f, agent.velocity.x, 1e-5f, "Lateral X knockback preserved.");
        Assert.AreEqual(0f, agent.velocity.y, 1e-5f, "Downward Y knockback must be clamped to 0.");
        Assert.AreEqual(0f, agent.velocity.z, 1e-5f);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Knockback_UpwardImpulse_Preserved()
    {
        var go = new GameObject("KnockbackTestBoid");
        var agent = go.AddComponent<BoidAgent>();

        agent.ApplyKnockback(new Vector3(0f, 7f, 0f));

        Assert.AreEqual(7f, agent.velocity.y, 1e-5f, "Upward knockback must NOT be clamped.");

        Object.DestroyImmediate(go);
    }

    [Test]
    public void Knockback_AccumulatesAcrossCalls()
    {
        // Sanity: two lateral knockbacks should sum, just like the original
        // behaviour. The Y-clamp must not break additive impulses on other axes.
        var go = new GameObject("KnockbackTestBoid");
        var agent = go.AddComponent<BoidAgent>();

        agent.ApplyKnockback(new Vector3(3f, 0f, 0f));
        agent.ApplyKnockback(new Vector3(2f, 0f, 1f));

        Assert.AreEqual(5f, agent.velocity.x, 1e-5f);
        Assert.AreEqual(0f, agent.velocity.y, 1e-5f);
        Assert.AreEqual(1f, agent.velocity.z, 1e-5f);

        Object.DestroyImmediate(go);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ComputeOverlapEscape — math half of the inside-collider recovery probe.
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void OverlapEscape_NonZeroOutward_PointsOutwardScaledByWeightAndMaxSteer()
    {
        Vector3 outward = new Vector3(2f, 0f, 0f); // length 2, +X
        Vector3 escape = BoidAgent.ComputeOverlapEscape(outward, maxSteerForce: 3f, weight: 10f);

        // Direction: +X (outward.normalized).
        Assert.Greater(Vector3.Dot(escape, outward.normalized), 0.99f,
            "Escape vector must align with the outward direction.");
        // Magnitude: maxSteerForce * weight = 30.
        Assert.AreEqual(30f, escape.magnitude, 1e-4f,
            "Magnitude must be maxSteerForce * weight.");
    }

    [Test]
    public void OverlapEscape_DegenerateOutward_ReturnsZero()
    {
        // Degenerate input — caller is expected to substitute Vector3.up before
        // calling, but the helper is also defensive: returns zero rather than
        // NaN-propagating through normalized.
        Vector3 escape = BoidAgent.ComputeOverlapEscape(Vector3.zero, 3f, 10f);
        Assert.AreEqual(Vector3.zero, escape);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetEscapeReference — collider-type-safe reference point for the recovery
    // probe. Regression net for the showcase-breaking bug where ClosestPoint
    // threw every frame against the scene's non-convex Environment MeshColliders.
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void EscapeReference_ConvexCollider_UsesClosestPoint()
    {
        // BoxCollider supports ClosestPoint — a point outside the box must
        // resolve to a point ON the box surface, not the AABB center.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // 1x1x1 BoxCollider at origin
        var box = go.GetComponent<BoxCollider>();

        Vector3 reference = BoidAgent.GetEscapeReference(box, new Vector3(5f, 0f, 0f));

        Assert.AreEqual(0.5f, reference.x, 1e-4f, "ClosestPoint must land on the +X face of the unit cube.");
        Assert.AreEqual(0f, reference.y, 1e-4f);
        Assert.AreEqual(0f, reference.z, 1e-4f);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void EscapeReference_NonConvexMeshCollider_DoesNotThrowAndUsesBoundsCenter()
    {
        // The exact crash scenario: a non-convex MeshCollider. Collider.ClosestPoint
        // throws for these; GetEscapeReference must instead fall back to the AABB
        // center without throwing.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        var mesh = go.AddComponent<MeshCollider>();
        mesh.convex = false;

        Vector3 reference = Vector3.zero;
        Assert.DoesNotThrow(
            () => reference = BoidAgent.GetEscapeReference(mesh, new Vector3(5f, 0f, 0f)),
            "Non-convex MeshCollider must not reach Collider.ClosestPoint.");
        Assert.AreEqual(mesh.bounds.center, reference,
            "Non-convex MeshCollider must fall back to the AABB center.");

        Object.DestroyImmediate(go);
    }
}
