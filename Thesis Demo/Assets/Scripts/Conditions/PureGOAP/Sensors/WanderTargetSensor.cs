using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Target sensor for PureGOAP wander behavior.
/// All agents share the same wander target so groups move together.
/// A new shared target is picked when the previous one is reached or expires.
/// </summary>
public class WanderTargetSensor : LocalTargetSensorBase
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

        // Reuse existing target to reduce GC
        if (existingTarget is PositionTarget posTarget)
            return posTarget.SetPosition(sharedTarget);

        return new PositionTarget(sharedTarget);
    }

    private static void PickNewSharedTarget(Vector3 fallbackOrigin)
    {
        // Compute group centroid so the next target is relative to where the school is
        Vector3 centroid = Vector3.zero;
        int count = PureGOAPAgent.AllAgents.Count;

        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                centroid += PureGOAPAgent.AllAgents[i].Position;
            centroid /= count;
        }
        else
        {
            centroid = fallbackOrigin;
        }

        // Pick a point offset from the group centroid in a random direction
        Vector3 randomDir = Random.onUnitSphere;
        randomDir.y *= 0.3f; // flatten vertical bias
        sharedTarget = centroid + randomDir.normalized * Random.Range(10f, WanderDistance);

        // Clamp to room bounds if available
        if (RoomBounds.Instance != null)
        {
            sharedTarget = RoomBounds.Instance.ClampToRoom(sharedTarget);
        }

        nextRefreshTime = Time.time + RefreshInterval;
        initialized = true;
    }
}
