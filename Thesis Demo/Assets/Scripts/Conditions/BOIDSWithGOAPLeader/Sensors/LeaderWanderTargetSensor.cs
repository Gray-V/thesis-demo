using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Picks random wander targets near the leader's current position.
/// </summary>
public class LeaderWanderTargetSensor : LocalTargetSensorBase
{
    private static readonly float WanderDistance = 15f;
    private static readonly float RefreshInterval = 10f;

    private static Vector3 sharedTarget;
    private static float nextRefreshTime;
    private static bool initialized;

    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        if (!initialized || Time.time >= nextRefreshTime)
            PickNewTarget(agent.Transform.position);

        if (existingTarget is PositionTarget posTarget)
            return posTarget.SetPosition(sharedTarget);

        return new PositionTarget(sharedTarget);
    }

    private static void PickNewTarget(Vector3 origin)
    {
        Vector3 randomDir = Random.onUnitSphere;
        randomDir.y *= 0.3f;
        sharedTarget = origin + randomDir.normalized * Random.Range(10f, WanderDistance);

        if (RoomBounds.Instance != null)
            sharedTarget = RoomBounds.Instance.ClampToRoom(sharedTarget);

        nextRefreshTime = Time.time + RefreshInterval;
        initialized = true;
    }
}
