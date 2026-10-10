using UnityEngine;

/// <summary>
/// The area an enemy guards (GDD §5.14: an enemy never leaves its zone). A box (its BoxCollider, a trigger
/// that blocks nothing): the enemy chases the player only inside it and goes back to its post when the
/// player leaves it. Optional patrol points (child transforms named "Patrulla*") make the guards walk a
/// route; without them they stand at their post and look around.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class EnemyZone : MonoBehaviour
{
    private BoxCollider _box;
    private Transform[] _patrol = new Transform[0];

    /// <summary>The patrol route (may be empty).</summary>
    public Transform[] Patrol => _patrol;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        _box.isTrigger = true;
        int n = 0;
        foreach (Transform child in transform) if (child.name.StartsWith("Patrulla")) n++;
        _patrol = new Transform[n];
        n = 0;
        foreach (Transform child in transform) if (child.name.StartsWith("Patrulla")) _patrol[n++] = child;
    }

    /// <summary>Whether a point (on the floor) is inside the zone, horizontally.</summary>
    public bool Contains(Vector3 point)
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(point) - _box.center;
        Vector3 half = _box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>The closest point of the zone to <paramref name="point"/> (on its floor plane).</summary>
    public Vector3 Clamp(Vector3 point)
    {
        if (_box == null) _box = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(point) - _box.center;
        Vector3 half = _box.size * 0.5f;
        local.x = Mathf.Clamp(local.x, -half.x + 0.5f, half.x - 0.5f);
        local.z = Mathf.Clamp(local.z, -half.z + 0.5f, half.z - 0.5f);
        Vector3 world = transform.TransformPoint(local + _box.center);
        world.y = point.y;
        return world;
    }
}
