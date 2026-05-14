using UnityEngine;

/// <summary>
/// Static utility for computing angular slot positions around a target.
/// Used by Flank and Guard actions in both Leader and GOAPBoids conditions.
/// </summary>
public static class FlankingSlotCalculator
{
    /// <summary>
    /// Computes evenly spaced positions around the player, offset from the player-to-centroid axis.
    /// </summary>
    public static Vector3[] ComputeFlankSlots(Vector3 playerPos, Vector3 centroid, int agentCount, float radius)
    {
        if (agentCount <= 0)
            return System.Array.Empty<Vector3>();

        Vector3[] slots = new Vector3[agentCount];
        Vector3 baseDir = (centroid - playerPos);
        baseDir.y = 0f;

        if (baseDir.sqrMagnitude < 0.01f)
            baseDir = Vector3.forward;
        else
            baseDir.Normalize();

        float angleStep = 360f / agentCount;

        for (int i = 0; i < agentCount; i++)
        {
            float angle = i * angleStep;
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            Vector3 dir = rot * baseDir;
            slots[i] = playerPos + dir * radius;
        }

        return slots;
    }

    /// <summary>
    /// Computes evenly spaced positions in a defensive ring around a guard center.
    /// </summary>
    public static Vector3[] ComputeGuardSlots(Vector3 guardCenter, int agentCount, float ringRadius)
    {
        if (agentCount <= 0)
            return System.Array.Empty<Vector3>();

        Vector3[] slots = new Vector3[agentCount];
        float angleStep = 360f / agentCount;

        for (int i = 0; i < agentCount; i++)
        {
            float angle = i * angleStep;
            float rad = angle * Mathf.Deg2Rad;
            slots[i] = guardCenter + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * ringRadius;
        }

        return slots;
    }
}
