using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Leader flee: steer away from player.
/// Followers trail the leader away from danger via cohesion redirect.
/// </summary>
public class LeaderFleeAction : GoapActionBase<LeaderFleeAction.Data>
{
    private const float SafeDistance = 40f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
        public float FleeTimer { get; set; }
        public Vector3 FleeDirection { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        LeaderActionDiagnostic.LogStart(nameof(LeaderFleeAction), data.Boid);
        if (data.Target != null)
            data.FleeDirection = (data.Boid.Position - data.Target.Position).normalized;
        else
            data.FleeDirection = data.Boid.cachedTransform.forward;

        // Reproducibility contract: relies on ConditionManager.SetSeed → Random.InitState
        // being called once per trial, and on the leader's goal-selection tick order being
        // deterministic. Covered by ReproducibilityTests.LeaderActionTimers_AreDeterministic
        // and the broader SameSeed_ProducesIdentical* tests.
        data.FleeTimer = Random.Range(3f, 5f);

        if (data.Brain != null)
        {
            data.Brain.isMovementOverridden = true;
            data.Brain.isGoapAttacking = false;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Boid.velocity = data.FleeDirection * data.Boid.settings.maxSpeed;

        data.FleeTimer -= context.DeltaTime;
        if (data.FleeTimer <= 0f)
            return ActionRunState.Completed;

        if (data.Target != null)
        {
            float distance = Vector3.Distance(data.Boid.Position, data.Target.Position);
            if (distance >= SafeDistance)
                return ActionRunState.Completed;
        }

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
