using UnityEngine;

/// <summary>
/// Defines a spherical boundary for PureGOAP agents.
/// Place in scene to constrain wander targets and agent movement.
/// Singleton — agents access via RoomBounds.Instance.
/// </summary>
public class RoomBounds : MonoBehaviour
{
    [Tooltip("Radius of the room boundary sphere.")]
    [SerializeField] private float radius = 50f;

    public static RoomBounds Instance { get; private set; }

    public Vector3 Center => transform.position;
    public float Radius => radius;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Returns true if the position is inside the boundary sphere.
    /// </summary>
    public bool IsInsideBounds(Vector3 pos)
    {
        return Vector3.Distance(pos, Center) <= radius;
    }

    /// <summary>
    /// Clamps a position to be within the boundary sphere.
    /// </summary>
    public Vector3 ClampToRoom(Vector3 pos)
    {
        Vector3 offset = pos - Center;
        if (offset.magnitude > radius)
            return Center + offset.normalized * radius;
        return pos;
    }

    /// <summary>
    /// Returns a random point inside the boundary sphere.
    /// </summary>
    public Vector3 GetRandomPointInside()
    {
        return Center + Random.insideUnitSphere * radius;
    }

    /// <summary>
    /// Returns a steering force pushing inward when the agent is near the boundary edge.
    /// Force increases as the agent gets closer to the boundary.
    /// </summary>
    public Vector3 GetBoundarySteeringForce(Vector3 pos, float margin)
    {
        Vector3 offset = pos - Center;
        float dist = offset.magnitude;
        float edgeDist = radius - dist;

        if (edgeDist >= margin || dist < 0.001f)
            return Vector3.zero;

        // Force increases as agent approaches boundary (0 at margin, max at edge)
        float strength = 1f - (edgeDist / margin);
        return -offset.normalized * strength * 15f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
