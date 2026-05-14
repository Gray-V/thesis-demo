using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader scatter: sprint in a random direction away from flock centroid.
/// Followers lose cohesion to the leader and drift apart naturally.
/// Triggered when health is critically low (below Flee threshold).
/// </summary>
public class LeaderScatterAction : GoapActionBase<LeaderScatterAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public Vector3 ScatterDirection { get; set; }
        public float ScatterTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderScatterAction), data.Boid);
        Vector3 centroid = data.Boid.manager != null
            ? data.Boid.manager.GetFlockCenter()
            : data.Boid.Position;

        data.ScatterDirection = ScatterDirectionPicker.PickScatterDirection(
            data.Boid.Position, centroid, data.Boid.subgroupId, FlockManager.ScatterSubgroupCount);
        // Reproducibility contract — see LeaderFleeAction.Start for details.
        data.ScatterTimer = Random.Range(4f, 6f);

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        float speedMultiplier = data.Boid.settings != null ? data.Boid.settings.scatterSpeedMultiplier : 1.5f;
        data.Boid.velocity = data.ScatterDirection * data.Boid.settings.maxSpeed * speedMultiplier;

        data.ScatterTimer -= context.DeltaTime;
        if (data.ScatterTimer <= 0f)
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
