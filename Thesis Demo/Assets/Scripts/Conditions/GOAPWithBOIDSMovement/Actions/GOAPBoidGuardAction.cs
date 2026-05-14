using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Guard action for GOAPWithBOIDSMovement: each agent computes a defensive ring slot
/// around the swarm centroid and holds position. BOIDS separation keeps them spaced.
/// Continues until player leaves mid-range or safety timeout.
/// </summary>
public class GOAPBoidGuardAction : GoapActionBase<GOAPBoidGuardAction.Data>
{
    private const float HoldTolerance = 2f;
    private const float GuardRingRadius = 8f;
    private const float SafetyTimeout = 30f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Vector3 GuardSlot { get; set; }
        public float GuardTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.GuardTimer = SafetyTimeout;

        // Compute guard center (flock centroid, same flockId only)
        var flockmates = GOAPBoidAgent.GetFlockmates(data.Agent.flockId);
        int count = flockmates.Count;
        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(data.Agent.flockId);

        int slotIndex = flockmates.IndexOf(data.Agent);
        if (slotIndex < 0) slotIndex = 0;

        Vector3[] slots = FlankingSlotCalculator.ComputeGuardSlots(centroid, count, GuardRingRadius);
        if (slotIndex < slots.Length)
            data.GuardSlot = slots[slotIndex];
        else
            data.GuardSlot = centroid;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        float distToSlot = Vector3.Distance(data.Agent.Position, data.GuardSlot);
        if (distToSlot > HoldTolerance)
        {
            data.Agent.SteerToward(data.GuardSlot);
        }
        else
        {
            // Hold position — reduce velocity
            data.Agent.SetVelocity(data.Agent.Velocity * 0.9f);
        }

        data.GuardTimer -= context.DeltaTime;
        if (data.GuardTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }
    public override void End(IMonoAgent agent, Data data) { }
}
