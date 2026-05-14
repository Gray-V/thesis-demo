using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Flee action for GOAPWithBOIDSMovement: steer away from player.
/// Same as PureFleeAction but references GOAPBoidAgent.
/// </summary>
public class GOAPBoidFleeAction : GoapActionBase<GOAPBoidFleeAction.Data>
{
    private static readonly float SafeDistance = 40f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public float FleeTimer { get; set; }
        public Vector3 FleeDirection { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        if (data.Target != null)
            data.FleeDirection = (data.Agent.Position - data.Target.Position).normalized;
        else
            data.FleeDirection = data.Agent.cachedTransform.forward;

        data.FleeTimer = Random.Range(3f, 5f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        Vector3 fleeTarget = data.Agent.Position + data.FleeDirection * 20f;
        data.Agent.SteerToward(fleeTarget);

        data.FleeTimer -= context.DeltaTime;
        if (data.FleeTimer <= 0f)
            return ActionRunState.Completed;

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
