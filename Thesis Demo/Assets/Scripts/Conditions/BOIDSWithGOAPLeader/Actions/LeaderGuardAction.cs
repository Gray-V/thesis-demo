using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader guard: hold position at the guard point (flock manager position).
/// Followers form a ring around the leader via cohesion redirect.
/// Continuous action until player leaves mid-range or safety timeout.
/// </summary>
public class LeaderGuardAction : GoapActionBase<LeaderGuardAction.Data>
{
    private const float HoldTolerance = 2f;
    private const float SafetyTimeout = 30f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Vector3 GuardPoint { get; set; }
        public float GuardTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderGuardAction), data.Boid);
        data.GuardTimer = SafetyTimeout;
        data.GuardPoint = data.Boid.manager != null
            ? data.Boid.manager.transform.position
            : data.Boid.Position;

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        float distToGuard = Vector3.Distance(data.Boid.Position, data.GuardPoint);
        if (distToGuard > HoldTolerance)
        {
            Vector3 dir = (data.GuardPoint - data.Boid.Position).normalized;
            data.Boid.velocity = dir * data.Boid.settings.maxSpeed * 0.5f;
        }
        else
        {
            // Hold position — slow drift
            data.Boid.velocity *= 0.9f;
        }

        data.GuardTimer -= context.DeltaTime;
        if (data.GuardTimer <= 0f)
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
