using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader regroup: steer toward the flock centroid when isolated.
/// Followers converge naturally via cohesion redirect toward the leader.
/// </summary>
public class LeaderRegroupAction : GoapActionBase<LeaderRegroupAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public float RegroupTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderRegroupAction), data.Boid);
        data.RegroupTimer = 8f;
        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Boid.manager == null)
            return ActionRunState.Completed;

        Vector3 centroid = data.Boid.manager.GetFlockCenter();
        Vector3 dir = (centroid - data.Boid.Position).normalized;
        data.Boid.velocity = dir * data.Boid.settings.maxSpeed;

        float regroupRadius = data.Boid.settings != null ? data.Boid.settings.regroupRadius : 8f;
        float dist = Vector3.Distance(data.Boid.Position, centroid);
        if (dist <= regroupRadius)
            return ActionRunState.Completed;

        data.RegroupTimer -= context.DeltaTime;
        if (data.RegroupTimer <= 0f)
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
