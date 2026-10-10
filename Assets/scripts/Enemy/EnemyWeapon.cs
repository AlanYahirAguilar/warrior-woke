using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The blade (or club) of a melee enemy and its strike (P40). Hits are not "two colliders touching": only
/// while the attack's measured active window is open (EnemyAttackState opens and closes it), the blade's
/// path this frame — a capsule from its grip to its tip, also swept from where it was the frame before — is
/// checked against the target layers; every IDamageable in it takes the attack's damage once per attack,
/// and never through a wall: nothing solid (Ground, Obstacle) may stand between the enemy's chest and the
/// point struck. The blade is a pair of points under the hand bone, set by EnemySetup on the prefab.
/// Runs in LateUpdate, on the final pose of the frame. No allocations.
/// </summary>
public class EnemyWeapon : MonoBehaviour
{
    [Tooltip("Where the grip and the tip of the weapon are (children of the hand bone).")]
    [SerializeField] private Transform grip, tip;
    [Tooltip("Radius (m) of the blade's sweep.")]
    [SerializeField] private float radius = 0.08f;
    [SerializeField] private LayerMask targetLayers;
    [SerializeField] private LayerMask blockingLayers;

    private readonly Collider[] _buffer = new Collider[8];
    private readonly List<IDamageable> _struck = new List<IDamageable>(4);
    private Vector3 _lastGrip, _lastTip;
    private bool _active, _hasLast;
    private int _damage;
    private Transform _owner;

    /// <summary>Fires for every hit: what was struck, how much, where.</summary>
    public event System.Action<IDamageable, int, Vector3> OnHit;

    /// <summary>The window is open (tests).</summary>
    public bool IsActive => _active;

    /// <summary>Hits of the current (or last) strike.</summary>
    public int StrikeHits => _struck.Count;

    public Transform Grip => grip;
    public Transform Tip => tip;

    private void Awake()
    {
        _owner = transform;
        if (targetLayers.value == 0) targetLayers = LayerMask.GetMask("Player");
        if (blockingLayers.value == 0) blockingLayers = LayerMask.GetMask("Ground", "Obstacle");
    }

    /// <summary>Wires the blade (EnemySetup).</summary>
    public void Configure(Transform gripPoint, Transform tipPoint, float bladeRadius)
    {
        grip = gripPoint;
        tip = tipPoint;
        radius = bladeRadius;
    }

    /// <summary>
    /// Opens the strike window: the attack's damage, its targets struck at most once. A charge
    /// (<paramref name="body"/>) strikes with the front of the body instead of the weapon.
    /// </summary>
    public void BeginStrike(int damage, bool body = false)
    {
        _damage = damage;
        _struck.Clear();
        _active = true;
        _hasLast = false;
        _body = body;
    }

    private bool _body;

    /// <summary>Height (m, unscaled) of the shoulder line a charge strikes with, and how far ahead of the body's axis.</summary>
    private const float ChargeLow = 0.9f, ChargeHigh = 1.5f, ChargeAhead = 0.35f, ChargeRadius = 0.4f;

    /// <summary>Closes the strike window.</summary>
    public void EndStrike() => _active = false;

    private void LateUpdate()
    {
        if (!_active) return;
        if (_body)
        {
            float s = _owner.lossyScale.y;
            Vector3 front = _owner.position + _owner.forward * (ChargeAhead * s);
            float keep = radius;
            radius = ChargeRadius * s;
            Sweep(front + Vector3.up * (ChargeLow * s), front + Vector3.up * (ChargeHigh * s));
            radius = keep;
            return;
        }
        if (grip == null || tip == null) return;
        Vector3 g = grip.position, t = tip.position;
        Sweep(g, t);
        // The path between frames: a fast swing would pass a target in between
        if (_hasLast)
        {
            Sweep(Vector3.Lerp(_lastGrip, g, 0.5f), Vector3.Lerp(_lastTip, t, 0.5f));
            Sweep(_lastTip, t);
        }
        _lastGrip = g;
        _lastTip = t;
        _hasLast = true;
    }

    private void Sweep(Vector3 a, Vector3 b)
    {
        int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, _buffer, targetLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = _buffer[i];
            if (col.transform.IsChildOf(_owner)) continue;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || _struck.Contains(target)) continue;
            Vector3 point = col.ClosestPoint(b);
            if (!ClearPath(point)) continue; // never through a wall
            _struck.Add(target);
            target.TakeDamage(_damage, _owner.position);
            OnHit?.Invoke(target, _damage, point);
        }
    }

    /// <summary>Nothing solid between the owner's chest and <paramref name="point"/>.</summary>
    public bool ClearPath(Vector3 point)
    {
        Vector3 chest = _owner.position + Vector3.up * 1.3f;
        Vector3 d = point - chest;
        float length = d.magnitude;
        return length < 0.05f || !Physics.Raycast(chest, d / length, length - 0.02f, blockingLayers, QueryTriggerInteraction.Ignore);
    }
}
