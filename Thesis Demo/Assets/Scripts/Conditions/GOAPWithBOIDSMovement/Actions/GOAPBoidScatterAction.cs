using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Scatter action for GOAPWithBOIDSMovement: each agent picks a random direction
/// biased away from the swarm centroid and sprints. Cohesion/alignment are suppressed
/// via the isScattering flag for true chaotic dispersal.
/// </summary>
public class GOAPBoidScatterAction : GoapActionBase<GOAPBoidScatterAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Vector3 ScatterDirection { get; set; }
        public float ScatterTimer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        // Compute flock centroid (same flockId only)
        Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(data.Agent.flockId);
        if (centroid == Vector3.zero)
            centroid = data.Agent.Position;

        data.ScatterDirection = ScatterDirectionPicker.PickScatterDirection(
            data.Agent.Position, centroid, data.Agent.subgroupId, FlockManager.ScatterSubgroupCount);
        data.ScatterTimer = Random.Range(4f, 6f);
        data.Agent.isScattering = true;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        float speed = data.Agent.MaxSpeed * 1.5f;
        data.Agent.SetVelocity(data.ScatterDirection * speed);

        data.ScatterTimer -= context.DeltaTime;
        if (data.ScatterTimer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        data.Agent.isScattering = false;
    }
}
