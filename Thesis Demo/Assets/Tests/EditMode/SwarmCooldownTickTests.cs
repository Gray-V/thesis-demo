using NUnit.Framework;

/// <summary>
/// Validates the cooldown-tick invariant used in GOAPBoidAgent.Update — regardless
/// of how many agents in a flock execute Update in a given frame, the flock's
/// swarm cooldown must be decremented exactly once per frame. Previously enforced
/// by an O(n) "am I first in AllAgents?" scan; now enforced by a per-flock
/// last-ticked-frame dictionary (O(1) per agent).
///
/// This test exercises the invariant at the logic level without a scene. It
/// simulates many agents in one flock calling the tick logic on the same frame
/// and asserts only one decrement happened.
/// </summary>
[TestFixture]
public class SwarmCooldownTickTests
{
    // Local mirror of the tick logic in GOAPBoidAgent.Update so EditMode tests can
    // exercise it without spawning GameObjects. Any change in the real code path
    // should be mirrored here — keep both side by side when refactoring.
    private static void TickOnce(
        System.Collections.Generic.Dictionary<int, float> cooldown,
        System.Collections.Generic.Dictionary<int, int> lastTick,
        int flockId,
        int currentFrame,
        float deltaTime)
    {
        if (cooldown.TryGetValue(flockId, out float cd) && cd > 0f)
        {
            if (!lastTick.TryGetValue(flockId, out int tickedAt) || tickedAt != currentFrame)
            {
                cooldown[flockId] = cd - deltaTime;
                lastTick[flockId] = currentFrame;
            }
        }
    }

    [Test]
    public void SingleFrame_ManyAgentsSameFlock_DecrementsOnce()
    {
        var cd = new System.Collections.Generic.Dictionary<int, float> { { 0, 3f } };
        var lastTick = new System.Collections.Generic.Dictionary<int, int>();

        // Simulate 400 agents in flock 0 all calling the tick on frame 42.
        for (int i = 0; i < 400; i++)
            TickOnce(cd, lastTick, flockId: 0, currentFrame: 42, deltaTime: 0.016f);

        Assert.AreEqual(3f - 0.016f, cd[0], 1e-5f,
            "Cooldown should decrement exactly once regardless of agent count per frame.");
    }

    [Test]
    public void SeparateFrames_DecrementEachFrame()
    {
        var cd = new System.Collections.Generic.Dictionary<int, float> { { 0, 3f } };
        var lastTick = new System.Collections.Generic.Dictionary<int, int>();

        for (int frame = 0; frame < 10; frame++)
        {
            // 100 agents per frame all tick; only frame-unique decrement applies.
            for (int i = 0; i < 100; i++)
                TickOnce(cd, lastTick, flockId: 0, currentFrame: frame, deltaTime: 0.016f);
        }

        Assert.AreEqual(3f - 10 * 0.016f, cd[0], 1e-5f,
            "Cooldown should decrement once per frame across 10 frames.");
    }

    [Test]
    public void TwoFlocks_TickIndependently()
    {
        var cd = new System.Collections.Generic.Dictionary<int, float> { { 0, 3f }, { 1, 5f } };
        var lastTick = new System.Collections.Generic.Dictionary<int, int>();

        // Frame 5: 50 agents in flock 0 tick, 50 agents in flock 1 tick.
        for (int i = 0; i < 50; i++)
            TickOnce(cd, lastTick, flockId: 0, currentFrame: 5, deltaTime: 0.02f);
        for (int i = 0; i < 50; i++)
            TickOnce(cd, lastTick, flockId: 1, currentFrame: 5, deltaTime: 0.02f);

        Assert.AreEqual(3f - 0.02f, cd[0], 1e-5f, "Flock 0 should have decremented once.");
        Assert.AreEqual(5f - 0.02f, cd[1], 1e-5f, "Flock 1 should have decremented once.");
    }

    [Test]
    public void CooldownAtZero_DoesNotDecrementBelow()
    {
        var cd = new System.Collections.Generic.Dictionary<int, float> { { 0, 0f } };
        var lastTick = new System.Collections.Generic.Dictionary<int, int>();

        for (int frame = 0; frame < 5; frame++)
            TickOnce(cd, lastTick, flockId: 0, currentFrame: frame, deltaTime: 0.016f);

        Assert.AreEqual(0f, cd[0], 1e-5f, "Zero cooldown should stay at zero.");
    }

    [Test]
    public void MissingFlockEntry_NoError()
    {
        var cd = new System.Collections.Generic.Dictionary<int, float>();
        var lastTick = new System.Collections.Generic.Dictionary<int, int>();

        Assert.DoesNotThrow(() => TickOnce(cd, lastTick, flockId: 99, currentFrame: 0, deltaTime: 0.016f),
            "Missing flockId should be a no-op, not throw.");
    }
}
