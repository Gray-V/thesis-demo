using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader wander: steer toward a shared target.
/// Followers auto-follow via FlockManager's cohesion redirect — no swarm logic needed here.
/// </summary>
public class LeaderWanderAction : GoapActionBase<LeaderWanderAction.Data>
{
    private const float ArrivalDistance = 5f;
    private const float MaxTime = 12f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public float Timer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderWanderAction), data.Boid);
        data.Timer = MaxTime;
        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null) return ActionRunState.Continue;

        Vector3 targetPos = data.Target.Position;
        Vector3 myPos = data.Boid.Position;

        // Steer toward target — same steering as BoidAgent.SteerTowards pattern
        Vector3 desired = (targetPos - myPos).normalized * data.Boid.settings.maxSpeed;
        Vector3 steer = Vector3.ClampMagnitude(desired - data.Boid.velocity, data.Boid.settings.maxSteerForce);
        data.Boid.velocity += steer * context.DeltaTime;

        float distance = Vector3.Distance(myPos, targetPos);
        data.Timer -= context.DeltaTime;

        if (distance <= ArrivalDistance || data.Timer <= 0f)
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
