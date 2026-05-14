using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Shared wander target sensor for GOAPWithBOIDSMovement agents.
/// Same logic as WanderTargetSensor but uses GOAPBoidAgent.AllAgents for centroid.
/// </summary>
public class GOAPBoidWanderTargetSensor : LocalTargetSensorBase
{
    private static readonly float WanderDistance = 15f;
    private static readonly float RefreshInterval = 10f;

    private static Vector3 sharedTarget;
    public static Vector3 SharedTargetDebug => sharedTarget;
    private static float nextRefreshTime;
    private static bool initialized;

    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        if (!initialized || Time.time >= nextRefreshTime)
            PickNewSharedTarget(agent.Transform.position);

        if (existingTarget is PositionTarget posTarget)
            return posTarget.SetPosition(sharedTarget);

        return new PositionTarget(sharedTarget);
    }

    private static void PickNewSharedTarget(Vector3 fallbackOrigin)
    {
        Vector3 centroid = Vector3.zero;
        int count = GOAPBoidAgent.AllAgents.Count;

        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                centroid += GOAPBoidAgent.AllAgents[i].Position;
            centroid /= count;
        }
        else
        {
            centroid = fallbackOrigin;
        }

        Vector3 randomDir = Random.onUnitSphere;
        randomDir.y *= 0.3f;
        sharedTarget = centroid + randomDir.normalized * Random.Range(10f, WanderDistance);

        if (RoomBounds.Instance != null)
            sharedTarget = RoomBounds.Instance.ClampToRoom(sharedTarget);

        nextRefreshTime = Time.time + RefreshInterval;
        initialized = true;
    }
}
