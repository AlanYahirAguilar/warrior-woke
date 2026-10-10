using UnityEngine;

/// <summary>
/// An archer's arrow (GDD §12: 10 damage per arrow). Flies straight at its speed with a little gravity,
/// checked by a ray each frame along its path (no tunneling at speed): the first collider it meets on the
/// target or blocking layers ends it, and a living IDamageable there takes its damage. It stays a moment
/// where it hit and is deactivated: its archer's quiver reuses it (pooled, D6). No allocations.
/// </summary>
public class Arrow : MonoBehaviour
{
    [Tooltip("Downward acceleration (m/s²): a flat, fast shot.")]
    [SerializeField] private float gravity = 3f;
    [Tooltip("Seconds it stays stuck where it hit before returning to the quiver.")]
    [SerializeField] private float stuckTime = 1.5f;
    [Tooltip("Longest flight (s).")]
    [SerializeField] private float maxFlightTime = 3f;

    private Vector3 _velocity;
    private int _damage;
    private LayerMask _mask;
    private Transform _owner;
    private float _launched, _stuckAt;
    private bool _flying;

    /// <summary>Fires when the arrow strikes something living (tests).</summary>
    public event System.Action<IDamageable> OnHit;

    /// <summary>In the air.</summary>
    public bool Flying => _flying;

    /// <summary>Shoots from where it is toward <paramref name="direction"/>.</summary>
    public void Launch(Vector3 direction, float speed, int damage, LayerMask mask, Transform owner)
    {
        _velocity = direction.normalized * speed;
        _damage = damage;
        _mask = mask;
        _owner = owner;
        _launched = Time.time;
        _flying = true;
        transform.rotation = Quaternion.LookRotation(_velocity);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (!_flying)
        {
            if (Time.time - _stuckAt > stuckTime) gameObject.SetActive(false);
            return;
        }
        if (Time.time - _launched > maxFlightTime) { gameObject.SetActive(false); _flying = false; return; }

        _velocity += Vector3.down * (gravity * dt);
        Vector3 step = _velocity * dt;
        float length = step.magnitude;
        if (length > 0f && Physics.Raycast(transform.position, step / length, out RaycastHit hit, length, _mask, QueryTriggerInteraction.Ignore) &&
            (_owner == null || !hit.collider.transform.IsChildOf(_owner)))
        {
            transform.position = hit.point;
            _flying = false;
            _stuckAt = Time.time;
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && !target.IsDead)
            {
                target.TakeDamage(_damage, _owner != null ? _owner.position : hit.point - step);
                OnHit?.Invoke(target);
            }
            return;
        }
        transform.position += step;
        transform.rotation = Quaternion.LookRotation(_velocity);
    }

    private void OnDisable() => _flying = false;
}
