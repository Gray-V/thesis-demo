using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Flank action for GOAPWithBOIDSMovement: each agent computes a unique angular slot
/// around the player and steers to it. BOIDS forces layer on top for natural spreading.
/// </summary>
public class GOAPBoidFlankAction : GoapActionBase<GOAPBoidFlankAction.Data>
{
    private const float SlotTolerance = 3f;
    private const float FlankRadius = 10f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Vector3 FlankPosition { get; set; }
        public float FlankTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.FlankTimer = 6f;

        if (data.Target == null) return;

        Vector3 playerPos = data.Target.Position;

        // Compute flock centroid and count (same flockId only)
        var flockmates = GOAPBoidAgent.GetFlockmates(data.Agent.flockId);
        int count = flockmates.Count;
        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(data.Agent.flockId);

        // Get unique slot index within the flock
        int slotIndex = flockmates.IndexOf(data.Agent);
        if (slotIndex < 0) slotIndex = 0;

        Vector3[] slots = FlankingSlotCalculator.ComputeFlankSlots(playerPos, centroid, count, FlankRadius);
        if (slotIndex < slots.Length)
            data.FlankPosition = slots[slotIndex];
        else
            data.FlankPosition = playerPos + (data.Agent.Position - playerPos).normalized * FlankRadius;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Agent.SteerToward(data.FlankPosition);

        float dist = Vector3.Distance(data.Agent.Position, data.FlankPosition);
        if (dist <= SlotTolerance)
            return ActionRunState.Completed;

        data.FlankTimer -= context.DeltaTime;
        if (data.FlankTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }
    public override void End(IMonoAgent agent, Data data) { }
}
