using UnityEngine;

/// <summary>
/// What an enemy knows about the player (GDD §21: detect by range and zone). Senses five times a second
/// (staggered per enemy), never every frame:
///  - sight: within sightRange, inside the field of view (sightAngle to each side of the head's facing) and
///    with nothing solid (Ground, Obstacle) between its eyes and the target's chest or head;
///  - hearing: within hearingRange, a target that runs, sprints or fights is heard even out of sight;
///  - being hit: the attacker is known at once.
/// Keeps where the target was last seen (or heard) and when. No allocations.
/// </summary>
public class EnemyPerception : MonoBehaviour
{
    [Tooltip("Height (m above the feet) of the eyes.")]
    [SerializeField] private float eyeHeight = 1.6f;
    [Tooltip("Surfaces that block the sight.")]
    [SerializeField] private LayerMask blockingLayers;

    private const float SenseInterval = 0.2f;

    private Enemy _enemy;
    private float _nextSense;

    /// <summary>The target (the player), or null. Its transform is the body's centre (P30), not its feet: see <see cref="TargetFeet"/>.</summary>
    public Transform Target { get; private set; }

    /// <summary>The target's feet on the floor (the player's root is the centre of its body, ~1 m higher).</summary>
    public Vector3 TargetFeet
    {
        get
        {
            if (Target == null) return transform.position;
            Vector3 p = Target.position;
            if (_targetMovement != null) p.y = _targetMovement.FeetY;
            return p;
        }
    }

    /// <summary>The target's chest (where blows and arrows are aimed).</summary>
    public Vector3 TargetChest => TargetFeet + Vector3.up * 1.25f;
    public HealthSystem TargetHealth { get; private set; }
    private PlayerMovement _targetMovement;

    /// <summary>The target is in sight now (last sense).</summary>
    public bool CanSee { get; private set; }

    /// <summary>The enemy knows where the target is (seen, heard or hit by it) since <see cref="LastKnownTime"/>.</summary>
    public bool Aware => LastKnownTime > -100f;

    /// <summary>Where the target was last seen or heard, and when.</summary>
    public Vector3 LastKnownPosition { get; private set; }
    public float LastKnownTime { get; private set; } = -999f;

    /// <summary>A sound to investigate (heard, not seen), consumed by the AI.</summary>
    public bool HasNoise { get; private set; }
    public Vector3 NoisePosition { get; private set; }

    /// <summary>Horizontal distance to the target (m), or infinity.</summary>
    public float Distance { get; private set; } = float.PositiveInfinity;

    /// <summary>Seconds since the target was last seen.</summary>
    public float TimeSinceSeen => Time.time - _lastSeenTime;
    private float _lastSeenTime = -999f;

    /// <summary>The target's movement (its state: attacking, recovering, dodging), or null.</summary>
    public PlayerMovement TargetMovement => _targetMovement;

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
        if (blockingLayers.value == 0) blockingLayers = LayerMask.GetMask("Ground", "Obstacle");
        _nextSense = Time.time + Random.Range(0f, SenseInterval); // staggered between enemies
    }

    /// <summary>Called by Enemy.Update.</summary>
    public void Tick()
    {
        if (Target == null || !Target.gameObject.activeInHierarchy) AcquireTarget();
        if (Target == null) { CanSee = false; Distance = float.PositiveInfinity; return; }

        Vector3 to = Target.position - transform.position;
        to.y = 0f;
        Distance = to.magnitude;
        Vector3 feet = TargetFeet;
        if (Time.time < _nextSense) return;
        _nextSense = Time.time + SenseInterval;

        CanSee = Sees();
        if (CanSee)
        {
            _lastSeenTime = Time.time;
            Remember(feet);
            HasNoise = false;
        }
        else if (Distance <= _enemy.Data.hearingRange && MakesNoise())
        {
            NoisePosition = feet;
            HasNoise = true;
            Remember(feet);
        }
    }

    /// <summary>The target hit this enemy: it knows where the target is.</summary>
    public void OnHitBy(Vector3 source)
    {
        _lastSeenTime = Time.time;
        Remember(Target != null ? TargetFeet : source);
    }

    /// <summary>The noise was dealt with (investigated or the target was found).</summary>
    public void ClearNoise() => HasNoise = false;

    /// <summary>Forgets the target (it died, or the enemy went back to its post).</summary>
    public void Forget()
    {
        LastKnownTime = -999f;
        _lastSeenTime = -999f;
        HasNoise = false;
    }

    private void Remember(Vector3 position)
    {
        LastKnownPosition = position;
        LastKnownTime = Time.time;
    }

    private bool Sees()
    {
        if (TargetHealth != null && TargetHealth.IsDead) return false;
        EnemyData d = _enemy.Data;
        if (Distance > d.sightRange) return false;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 to = Target.position - transform.position;
        to.y = 0f;
        // Very close it is felt even from the side (no blind spot at arm's length)
        if (Distance > 1.5f && Vector3.Angle(transform.forward, to) > d.sightAngle) return false;
        Vector3 feet = TargetFeet;
        return Visible(eye, feet + Vector3.up * 1.25f) || Visible(eye, feet + Vector3.up * 1.7f);
    }

    /// <summary>Nothing solid between two points.</summary>
    public bool Visible(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.magnitude;
        return length < 0.01f || !Physics.Raycast(from, d / length, length, blockingLayers, QueryTriggerInteraction.Ignore);
    }

    /// <summary>The target runs, sprints or fights.</summary>
    private bool MakesNoise()
    {
        if (_targetMovement == null) return false;
        PlayerState s = _targetMovement.StateMachine?.CurrentState;
        return _targetMovement.HorizontalSpeed > 2.5f || s is PlayerAttackState || s == _targetMovement.DodgeState;
    }

    private void AcquireTarget()
    {
        Player player = Player.Instance;
        if (player == null || !player.gameObject.activeInHierarchy) { Target = null; return; }
        Target = player.transform;
        TargetHealth = player.GetComponent<HealthSystem>();
        _targetMovement = player.GetComponent<PlayerMovement>();
    }

    /// <summary>Height of the eyes (m above the feet).</summary>
    public float EyeHeight => eyeHeight;

    /// <summary>The surfaces that block the sight and the attacks.</summary>
    public LayerMask BlockingLayers => blockingLayers;
}
