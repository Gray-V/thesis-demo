using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class FlankingSlotCalculatorTests
{
    // ── ComputeFlankSlots ──

    [Test]
    public void FlankSlots_ZeroAgents_ReturnsEmpty()
    {
        var slots = FlankingSlotCalculator.ComputeFlankSlots(Vector3.zero, Vector3.forward * 5f, 0, 10f);
        Assert.AreEqual(0, slots.Length);
    }

    [Test]
    public void FlankSlots_SingleAgent_ReturnsOneSlot()
    {
        var slots = FlankingSlotCalculator.ComputeFlankSlots(Vector3.zero, Vector3.forward * 5f, 1, 10f);
        Assert.AreEqual(1, slots.Length);
    }

    [Test]
    public void FlankSlots_SingleAgent_AtCorrectRadius()
    {
        Vector3 playerPos = new Vector3(3f, 0f, 4f);
        float radius = 7f;
        var slots = FlankingSlotCalculator.ComputeFlankSlots(playerPos, Vector3.forward * 10f, 1, radius);
        float dist = Vector3.Distance(playerPos, slots[0]);
        Assert.AreEqual(radius, dist, 0.01f, "Slot should be exactly 'radius' away from player");
    }

    [Test]
    public void FlankSlots_MultipleAgents_EvenlySpaced()
    {
        int count = 4;
        var slots = FlankingSlotCalculator.ComputeFlankSlots(Vector3.zero, Vector3.forward * 5f, count, 10f);
        Assert.AreEqual(count, slots.Length);

        float expectedAngle = 360f / count; // 90 degrees

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            float angle = Vector3.Angle(slots[i].normalized, slots[next].normalized);
            Assert.AreEqual(expectedAngle, angle, 1f, $"Angle between slot {i} and {next} should be {expectedAngle}");
        }
    }

    [Test]
    public void FlankSlots_AllSlotsAtCorrectRadius()
    {
        Vector3 playerPos = new Vector3(2f, 0f, -3f);
        float radius = 12f;
        int count = 6;
        var slots = FlankingSlotCalculator.ComputeFlankSlots(playerPos, Vector3.forward * 5f, count, radius);

        for (int i = 0; i < count; i++)
        {
            float dist = Vector3.Distance(playerPos, slots[i]);
            Assert.AreEqual(radius, dist, 0.01f, $"Slot {i} should be at radius distance from player");
        }
    }

    [Test]
    public void FlankSlots_SlotsOnHorizontalPlane()
    {
        Vector3 playerPos = new Vector3(0f, 5f, 0f);
        var slots = FlankingSlotCalculator.ComputeFlankSlots(playerPos, new Vector3(0f, 5f, 10f), 4, 10f);

        for (int i = 0; i < slots.Length; i++)
        {
            Assert.AreEqual(playerPos.y, slots[i].y, 0.01f, $"Slot {i} Y should match player Y");
        }
    }

    [Test]
    public void FlankSlots_NegativeCount_ReturnsEmpty()
    {
        var slots = FlankingSlotCalculator.ComputeFlankSlots(Vector3.zero, Vector3.forward, -1, 10f);
        Assert.AreEqual(0, slots.Length);
    }

    [Test]
    public void FlankSlots_CentroidEqualsPlayer_UsesForwardFallback()
    {
        // When centroid == playerPos, direction is degenerate; should use Vector3.forward fallback
        var slots = FlankingSlotCalculator.ComputeFlankSlots(Vector3.zero, Vector3.zero, 3, 10f);
        Assert.AreEqual(3, slots.Length);
        foreach (var s in slots)
        {
            Assert.IsFalse(float.IsNaN(s.x) || float.IsNaN(s.y) || float.IsNaN(s.z), "No NaN values");
        }
    }

    // ── ComputeGuardSlots ──

    [Test]
    public void GuardSlots_ZeroAgents_ReturnsEmpty()
    {
        var slots = FlankingSlotCalculator.ComputeGuardSlots(Vector3.zero, 0, 10f);
        Assert.AreEqual(0, slots.Length);
    }

    [Test]
    public void GuardSlots_MultipleAgents_FormRing()
    {
        int count = 6;
        Vector3 center = new Vector3(5f, 0f, 5f);
        float ringRadius = 8f;
        var slots = FlankingSlotCalculator.ComputeGuardSlots(center, count, ringRadius);

        Assert.AreEqual(count, slots.Length);
        for (int i = 0; i < count; i++)
        {
            float dist = Vector3.Distance(center, slots[i]);
            Assert.AreEqual(ringRadius, dist, 0.01f, $"Guard slot {i} should be at ringRadius from center");
        }
    }

    [Test]
    public void GuardSlots_CorrectRadius()
    {
        float ringRadius = 15f;
        var slots = FlankingSlotCalculator.ComputeGuardSlots(Vector3.zero, 8, ringRadius);

        foreach (var slot in slots)
        {
            float dist = slot.magnitude;
            Assert.AreEqual(ringRadius, dist, 0.01f);
        }
    }

    [Test]
    public void GuardSlots_AllOnHorizontalPlane()
    {
        Vector3 center = new Vector3(0f, 3f, 0f);
        var slots = FlankingSlotCalculator.ComputeGuardSlots(center, 5, 10f);

        foreach (var slot in slots)
        {
            Assert.AreEqual(center.y, slot.y, 0.01f, "Guard slots should be on same Y as center");
        }
    }
}
