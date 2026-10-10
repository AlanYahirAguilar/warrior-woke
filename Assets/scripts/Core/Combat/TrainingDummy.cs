using UnityEngine;

/// <summary>
/// Training dummy of the Parkour Test Area (decision P37): a test fixture to validate the combat's
/// impact → reaction → recovery while the GDD's enemies are pending (P4). It is not an enemy: it never
/// moves on its own or attacks. A post on a base, on layer Enemy, with a HealthSystem that never lets
/// it die (it heals itself back to full).
/// Reaction: every hit pushes its body away from the attacker, a damped spring (it sways and settles)
/// whose push grows with the damage — a few degrees for a punch, so the next blow of the chain still
/// lands; a heavy hit (KnockbackDamage or more) also slides it back at once, which it recovers slowly
/// (GDD §5.7: the heavy attack knocks the enemy back). The slide starts fast (it gives way under the
/// kick, which keeps extending through the space it leaves) and stops within knockbackDistance.
/// Exposes what the tests measure: hits taken, the last damage and the current sway and offset.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
public class TrainingDummy : MonoBehaviour
{
    [Tooltip("The part that sways (pivots at this object's origin, on the floor).")]
    [SerializeField] private Transform body;

    [Tooltip("Degrees of sway per point of damage.")]
    [SerializeField] private float swayPerDamage = 0.5f;
    [Tooltip("Largest sway (°).")]
    [SerializeField] private float maxSway = 35f;
    [Tooltip("Stiffness (1/s²) and damping (1/s) of the sway spring.")]
    [SerializeField] private float stiffness = 120f, damping = 10f;

    [Tooltip("Damage from which a hit knocks the dummy back (the unarmed heavy attack does 20).")]
    [SerializeField] private float knockbackDamage = 20f;
    [Tooltip("How far (m) a knockback slides the dummy, and how fast (1/s) it returns to its place.")]
    [SerializeField] private float knockbackDistance = 0.35f, returnRate = 1.5f;
    [Tooltip("Speed (m/s) at which a knockback starts: it gives way under the kick at once.")]
    [SerializeField] private float knockbackSpeed = 3.5f;

    private HealthSystem _health;
    private Vector3 _anchor;
    private Vector3 _swayAxis;          // horizontal axis the body tilts around
    private float _sway, _swayVelocity; // degrees and degrees per second
    private Vector3 _slide, _slideVelocity;

    /// <summary>Hits taken since the scene started.</summary>
    public int Hits { get; private set; }

    /// <summary>Damage of the last hit.</summary>
    public int LastDamage { get; private set; }

    /// <summary>Current tilt of the body (°).</summary>
    public float Sway => Mathf.Abs(_sway);

    /// <summary>Largest tilt since the last hit (°).</summary>
    public float PeakSway { get; private set; }

    /// <summary>Horizontal distance (m) the dummy is from its place (knocked back).</summary>
    public float Displacement => new Vector3(_slide.x, 0f, _slide.z).magnitude;

    /// <summary>Largest knockback since the last hit (m).</summary>
    public float PeakDisplacement { get; private set; }

    private void Awake()
    {
        _health = GetComponent<HealthSystem>();
        _anchor = transform.position;
        if (body == null && transform.childCount > 0) body = transform.GetChild(0);
    }

    private void OnEnable()  => _health.OnDamageReceived += HandleHit;
    private void OnDisable() => _health.OnDamageReceived -= HandleHit;

    private void HandleHit(int damage, Vector3 source)
    {
        Hits++;
        LastDamage = damage;
        PeakSway = 0f;
        PeakDisplacement = 0f;
        Vector3 away = transform.position - source;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();

        // The body swings away from the attacker: a kick on the spring's velocity
        _swayAxis = Vector3.Cross(Vector3.up, away);
        float push = Mathf.Min(maxSway, damage * swayPerDamage);
        _sway = 0f;
        _swayVelocity = push * Mathf.Sqrt(stiffness);
        if (damage >= knockbackDamage)
            _slideVelocity = away * knockbackSpeed;

        // Never dies: it is a fixture for testing
        if (_health.CurrentHealth < 50) _health.InitializeHealth(1000);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Damped spring back to upright
        _swayVelocity += (-stiffness * _sway - damping * _swayVelocity) * dt;
        _sway = Mathf.Clamp(_sway + _swayVelocity * dt, -maxSway, maxSway);
        PeakSway = Mathf.Max(PeakSway, Mathf.Abs(_sway));
        if (body != null)
            body.localRotation = _swayAxis.sqrMagnitude > 0f
                ? Quaternion.AngleAxis(_sway, transform.InverseTransformDirection(_swayAxis))
                : Quaternion.identity;

        // Knockback: the slide's speed fades so it stops within knockbackDistance (v² / 2d), then the
        // dummy creeps back to its place
        _slide += _slideVelocity * dt;
        float braking = knockbackSpeed * knockbackSpeed / (2f * Mathf.Max(0.05f, knockbackDistance));
        _slideVelocity = Vector3.MoveTowards(_slideVelocity, Vector3.zero, braking * dt);
        if (_slideVelocity.sqrMagnitude < 0.0001f)
            _slide = Vector3.MoveTowards(_slide, Vector3.zero, returnRate * _slide.magnitude * dt + 0.02f * dt);
        PeakDisplacement = Mathf.Max(PeakDisplacement, Displacement);
        transform.position = _anchor + _slide;
    }
}
