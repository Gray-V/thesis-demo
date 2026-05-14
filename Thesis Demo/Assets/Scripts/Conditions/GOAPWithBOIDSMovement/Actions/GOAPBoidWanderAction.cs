using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Simple wander for GOAPWithBOIDSMovement: just steer toward shared target.
/// BOIDS forces (always-on in GOAPBoidAgent) handle group cohesion automatically.
/// No need for cohesion blending like PureSwarmMoveAction — that's the thesis distinction.
/// </summary>
public class GOAPBoidWanderAction : GoapActionBase<GOAPBoidWanderAction.Data>
{
    private const float ArrivalDistance = 5f;
    private const float MaxTime = 12f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public float Timer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.Timer = MaxTime;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null)
            return ActionRunState.Continue;

        // Just steer toward the shared wander target — BOIDS forces keep the group together
        data.Agent.SteerToward(data.Target.Position);

        float distance = Vector3.Distance(data.Agent.Position, data.Target.Position);
        data.Timer -= context.DeltaTime;

        if (distance <= ArrivalDistance || data.Timer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }
    public override void End(IMonoAgent agent, Data data) { }
}
