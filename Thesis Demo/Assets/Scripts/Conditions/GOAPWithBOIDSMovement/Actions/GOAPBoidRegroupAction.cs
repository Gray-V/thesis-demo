using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Regroup action for GOAPWithBOIDSMovement: steer toward the swarm centroid.
/// BOIDS cohesion reinforces this naturally.
/// </summary>
public class GOAPBoidRegroupAction : GoapActionBase<GOAPBoidRegroupAction.Data>
{
    private const float RegroupRadius = 8f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public float RegroupTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.RegroupTimer = 8f;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        // Compute flock centroid (same flockId only)
        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(data.Agent.flockId);

        data.Agent.SteerToward(centroid);

        float dist = Vector3.Distance(data.Agent.Position, centroid);
        if (dist <= RegroupRadius)
            return ActionRunState.Completed;

        data.RegroupTimer -= context.DeltaTime;
        if (data.RegroupTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }
    public override void End(IMonoAgent agent, Data data) { }
}
