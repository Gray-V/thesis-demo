using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP swarm movement: agents move as a school toward a shared wander target.
/// Blends cohesion (steer toward group centroid) with target-seeking each frame.
/// When scattered, cohesion dominates and agents converge.
/// When tight, target-seeking dominates and the school moves together.
///
/// Replaces the old GroupUpAction + GroupWanderAction chain with a single action.
/// No static shared state — each agent computes its own centroid independently.
///
/// Effect: IsWandering (satisfies PureWanderGoal)
/// Target: WanderTarget (shared position from WanderTargetSensor)
/// </summary>
public class PureSwarmMoveAction : GoapActionBase<PureSwarmMoveAction.Data>
{
    private const float CohesionWeight = 3.0f;
    private const float TargetWeight = 2.0f;
    private const float CohesionRadius = 25f;
    private const float ArrivalDistance = 5f;
    private const float MaxTime = 12f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }

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

        Vector3 myPos = data.Agent.Position;
        Vector3 wanderTarget = data.Target.Position;

        // --- Cohesion: steer toward centroid of nearby agents ---
        Vector3 centroid = Vector3.zero;
        int neighborCount = 0;
        var allAgents = PureGOAPAgent.AllAgents;
        float cohesionRadiusSq = CohesionRadius * CohesionRadius;

        for (int i = 0; i < allAgents.Count; i++)
        {
            if (allAgents[i] == data.Agent) continue;
            float distSq = (allAgents[i].Position - myPos).sqrMagnitude;
            if (distSq <= cohesionRadiusSq)
            {
                centroid += allAgents[i].Position;
                neighborCount++;
            }
        }

        Vector3 cohesionSteer = Vector3.zero;
        if (neighborCount > 0)
        {
            centroid /= neighborCount;
            cohesionSteer = (centroid - myPos).normalized * CohesionWeight;
        }

        // --- Target-seeking: steer toward shared wander target ---
        Vector3 targetSteer = (wanderTarget - myPos).normalized * TargetWeight;

        // --- Blend and apply ---
        Vector3 desired = cohesionSteer + targetSteer;
        if (desired.sqrMagnitude > 0.001f)
        {
            data.Agent.SteerToward(myPos + desired.normalized * 10f);
        }

        // --- Completion: group centroid near target, or timeout ---
        Vector3 groupCenter = myPos;
        if (neighborCount > 0)
        {
            groupCenter = (centroid * neighborCount + myPos) / (neighborCount + 1);
        }
        float distToTarget = Vector3.Distance(groupCenter, wanderTarget);

        data.Timer -= context.DeltaTime;
        if (distToTarget <= ArrivalDistance || data.Timer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data) { }
}
