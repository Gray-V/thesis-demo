using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP flee action: steers away from player when health is low.
/// Caches flee direction on Start so the agent keeps fleeing even if
/// the player leaves vision range mid-action.
///
/// Condition: IsHealthLow >= 1
/// Effect: IsHealthLow Decrease
/// Target: PlayerTarget (flee FROM this position)
/// </summary>
public class PureFleeAction : GoapActionBase<PureFleeAction.Data>
{
    private static readonly float SafeDistance = 40f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }

        public float FleeTimer { get; set; }
        public Vector3 FleeDirection { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        // Cache flee direction away from player
        if (data.Target != null)
        {
            data.FleeDirection = (data.Agent.Position - data.Target.Position).normalized;
        }
        else
        {
            // No target — flee in current forward direction
            data.FleeDirection = data.Agent.cachedTransform.forward;
        }

        data.FleeTimer = Random.Range(3f, 5f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        // Steer away from player using cached direction
        Vector3 fleeTarget = data.Agent.Position + data.FleeDirection * 20f;
        data.Agent.SteerToward(fleeTarget);

        data.FleeTimer -= context.DeltaTime;

        // Complete if timer expired
        if (data.FleeTimer <= 0f)
            return ActionRunState.Completed;

        // Complete if we've reached safe distance from player
        if (data.Target != null)
        {
            float distance = Vector3.Distance(data.Agent.Position, data.Target.Position);
            if (distance >= SafeDistance)
                return ActionRunState.Completed;
        }

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data) { }
}
