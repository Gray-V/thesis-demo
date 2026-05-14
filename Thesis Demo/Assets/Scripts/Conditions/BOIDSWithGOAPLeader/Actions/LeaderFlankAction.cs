using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader flank: move to an angular offset position around the player.
/// The leader picks a side; followers trail via cohesion redirect, creating a surround.
/// </summary>
public class LeaderFlankAction : GoapActionBase<LeaderFlankAction.Data>
{
    private const float SlotTolerance = 3f;
    private const float FlankRadius = 10f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Vector3 FlankPosition { get; set; }
        public float FlankTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderFlankAction), data.Boid);
        data.FlankTimer = 6f;

        if (data.Target != null && data.Boid.manager != null)
        {
            Vector3 playerPos = data.Target.Position;
            Vector3 centroid = data.Boid.manager.GetFlockCenter();
            Vector3 toPlayer = (playerPos - centroid);
            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude < 0.01f)
                toPlayer = Vector3.forward;
            else
                toPlayer.Normalize();

            // Leader flanks at +90 degrees from the approach vector
            Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
            Vector3 flankDir = rot * toPlayer;
            data.FlankPosition = playerPos + flankDir * FlankRadius;
        }

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        Vector3 toSlot = data.FlankPosition - data.Boid.Position;
        float dist = toSlot.magnitude;

        if (dist <= SlotTolerance)
        {
            // At the flank slot — hold position. Returning Completed here (the
            // pre-2026-05-05 behaviour) caused GOAP to immediately replan Flank
            // and Start() the action again every ~50 ms while the leader's
            // resolver still wanted Flank, producing the restart-spam pattern
            // visible in EventLog_batch_2026-05-05_16-10-45.csv (~25 LeaderFlank
            // ActionStart events in 2 s). Holding with Continue lets the timer
            // run out naturally; the action terminates once when FlankTimer
            // expires, matching what a single Flank should look like in logs.
            data.Boid.velocity = Vector3.zero;
        }
        else
        {
            Vector3 dir = toSlot / Mathf.Max(dist, 0.001f);
            data.Boid.velocity = dir * data.Boid.settings.maxSpeed;
        }

        data.FlankTimer -= context.DeltaTime;
        if (data.FlankTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        if (data.Brain != null)
            data.Brain.isMovementOverridden = false;
    }
}
