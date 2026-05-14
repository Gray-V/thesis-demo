using UnityEngine;

/// <summary>
/// Static utility for picking scatter directions biased away from the group centroid.
/// Used by Scatter actions in both Leader and GOAPBoids conditions.
/// </summary>
public static class ScatterDirectionPicker
{
    /// <summary>
    /// Returns a random horizontal direction biased away from the centroid.
    /// Single-subgroup form — preserved for callers that don't partition the flock.
    /// </summary>
    public static Vector3 PickScatterDirection(Vector3 agentPos, Vector3 centroid)
    {
        return PickScatterDirection(agentPos, centroid, subgroupId: 0, subgroupCount: 1);
    }

    /// <summary>
    /// Returns a horizontal direction biased away from the centroid, partitioned into
    /// <paramref name="subgroupCount"/> shared-angle buckets. Agents passing the same
    /// <paramref name="subgroupId"/> get directions within ±15° of one another, so the
    /// flock visibly splits into sub-flocks rather than dispersing chaotically.
    /// </summary>
    public static Vector3 PickScatterDirection(Vector3 agentPos, Vector3 centroid, int subgroupId, int subgroupCount)
    {
        if (subgroupCount < 1) subgroupCount = 1;
        subgroupId = Mathf.Clamp(subgroupId, 0, subgroupCount - 1);

        Vector3 awayFromCenter = agentPos - centroid;
        awayFromCenter.y = 0f;

        if (awayFromCenter.sqrMagnitude < 0.01f)
            awayFromCenter = Random.insideUnitSphere;

        awayFromCenter.Normalize();

        // Each subgroup gets a fixed bucket angle in [-90, 90); intra-subgroup jitter
        // keeps members from stacking exactly on top of one another.
        float bucketCenter = -90f + (180f / subgroupCount) * (subgroupId + 0.5f);
        float jitter = Random.Range(-15f, 15f);
        float angle = bucketCenter + jitter;

        Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
        Vector3 scatterDir = rotation * awayFromCenter;
        scatterDir.y = 0f;

        return scatterDir.normalized;
    }
}
