using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// MultiSensor for PureGOAP agents: detects player visibility and provides player target.
/// Combines world sensing (is player visible?) and target sensing (where is player?).
/// </summary>
public class PlayerVisionSensor : MultiSensorBase
{
    [SerializeField] private float visionRange = 30f;
    [SerializeField] private string playerTag = "Player";

    private Transform playerTransform;

    public PlayerVisionSensor()
    {
        // Register sensors in constructor (NOT in Created())
        AddLocalWorldSensor<PlayerVisible>((agent, references) =>
        {
            if (playerTransform == null) return false;

            float distance = Vector3.Distance(agent.Transform.position, playerTransform.position);
            return distance <= visionRange;
        });

        AddLocalTargetSensor<PlayerTarget>((agent, references, existingTarget) =>
        {
            if (playerTransform == null) return null;

            // Reuse existing target to reduce GC pressure
            if (existingTarget is TransformTarget transformTarget)
                return transformTarget.SetTransform(playerTransform);

            return new TransformTarget(playerTransform);
        });
    }

    public override void Created() { }

    public override void Update()
    {
        // Cache player transform
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTransform = player.transform;
        }
    }
}
