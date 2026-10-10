using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// An enemy of the GDD (§12, §13, §21; decisions P4, P40): the context of its state machine, like
/// PlayerMovement for the player (D2, D3). It owns the components the states use and the decisions that
/// are the archetype's (EnemyData, D5):
///  - movement: a NavMeshAgent moves the body on the baked NavMesh (with a kinematic Rigidbody, as AI
///    Navigation recommends); the body turns toward where it goes or toward the target at its own rate;
///  - perception (EnemyPerception), the zone it never leaves (EnemyZone), the attack tokens and the slots
///    around the player (EnemyCoordinator);
///  - its guard (IDamageModifier: a blocked frontal hit keeps only guardDamageKept of the damage; a heavy
///    hit breaks the guard) and its poise (damage taken within PoiseWindow that interrupts it: light
///    enemies flinch at every blow, the heavy one and the bosses only at heavy blows or a series);
///  - the choice of attack by kind: the light warrior chains fast cuts and lunges, the heavy one swings
///    hard and charges, the archer shoots, the clan leader presses with combos and charges, the Commander
///    adapts to how the player fights (blocks a lot → heavy blows that break the guard; dodges a lot →
///    held wind-ups and follow-up lunges; attacks a lot → guards and punishes).
/// The states (EnemyStates.cs) do the rest. No allocations in the loop.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(EnemyPerception))]
[RequireComponent(typeof(EnemyAnimator))]
public class Enemy : MonoBehaviour, IDamageModifier
{
    [SerializeField] private EnemyData data;
    [SerializeField] private EnemyZone zone;
    [Tooltip("Where an archer shoots from (an elevated post); empty for the others.")]
    [SerializeField] private Transform[] archerPosts = new Transform[0];
    [Tooltip("Arrows of an archer's quiver (pooled, D6).")]
    [SerializeField] private Arrow[] quiver = new Arrow[0];
    [Tooltip("Where an arrow leaves the bow.")]
    [SerializeField] private Transform bowMuzzle;

    /// <summary>Seconds over which the damage taken adds up against the poise.</summary>
    public const float PoiseWindow = 1.5f;

    public EnemyData Data => data;
    public NavMeshAgent Agent { get; private set; }
    public HealthSystem Health { get; private set; }
    public EnemyPerception Perception { get; private set; }
    public EnemyAnimator Anim { get; private set; }
    public EnemyWeapon Weapon { get; private set; }
    public EnemyZone Zone => zone;
    public Transform[] ArcherPosts => archerPosts;
    public Transform BowMuzzle => bowMuzzle;

    public EnemyStateMachine StateMachine { get; private set; }
    public EnemyIdleState IdleState { get; private set; }
    public EnemyInvestigateState InvestigateState { get; private set; }
    public EnemyChaseState ChaseState { get; private set; }
    public EnemyCombatState CombatState { get; private set; }
    public EnemyAttackState AttackState { get; private set; }
    public EnemyShootState ShootState { get; private set; }
    public EnemyBlockState BlockState { get; private set; }
    public EnemyDodgeState DodgeState { get; private set; }
    public EnemyHitState HitState { get; private set; }
    public EnemyRepositionState RepositionState { get; private set; }
    public EnemyReturnState ReturnState { get; private set; }
    public EnemyDeadState DeadState { get; private set; }

    /// <summary>Where the enemy stands guard (where it was placed).</summary>
    public Vector3 Post { get; private set; }
    public Quaternion PostRotation { get; private set; }

    public bool IsDead => Health != null && Health.IsDead;
    public bool IsBoss => data != null && data.kind == EnemyKind.Boss;
    public bool IsRanged => data != null && data.kind == EnemyKind.Archer;

    /// <summary>Fighting the player (chasing, positioning, attacking, reacting).</summary>
    public bool IsEngaged
    {
        get
        {
            EnemyState s = StateMachine?.CurrentState;
            return s == ChaseState || s == CombatState || s == AttackState || s == ShootState || s == BlockState ||
                   s == DodgeState || s == HitState || s == RepositionState;
        }
    }

    /// <summary>Its slot around the player (world yaw, °), dealt by EnemyCoordinator.</summary>
    public float SlotYaw { get; set; }

    /// <summary>Earliest time of its next attack (GDD §5.14: one every ~0.5–1.5 s).</summary>
    public float NextAttackTime { get; set; }

    /// <summary>The guard is up (EnemyBlockState).</summary>
    public bool Guarding { get; set; }

    /// <summary>Damage taken since the last time the poise was spent.</summary>
    public int PoiseDamage { get; private set; }
    private float _poiseSince;

    /// <summary>The body's measured velocity this frame (m/s, horizontal).</summary>
    public Vector3 Velocity { get; private set; }
    private Vector3 _lastPosition;

    // What the Commander learned of the player (GDD §13: it adapts to the player's style)
    public int SeenBlocks { get; private set; }
    public int SeenDodges { get; private set; }
    public int SeenAttacks { get; private set; }
    private PlayerMovement _watched;

    /// <summary>Times each state was entered (tests).</summary>
    public int Attacks { get; private set; }
    public int Blocks { get; private set; }
    public int Dodges { get; private set; }
    public int Staggers { get; private set; }
    public int Shots { get; private set; }

    /// <summary>The last attack chosen (tests).</summary>
    public EnemyAttack LastAttack { get; private set; }

    /// <summary>Fires for every attack started (tests, the camera).</summary>
    public event System.Action<Enemy, EnemyAttack> AttackStarted;

    private void Awake()
    {
        Agent = GetComponent<NavMeshAgent>();
        Health = GetComponent<HealthSystem>();
        Perception = GetComponent<EnemyPerception>();
        Anim = GetComponent<EnemyAnimator>();
        Weapon = GetComponent<EnemyWeapon>();
        if (data != null) Health.InitializeHealth(data.maxHealth);
        Agent.updateRotation = false;
        Post = transform.position;
        PostRotation = transform.rotation;
        _lastPosition = transform.position;

        StateMachine = new EnemyStateMachine();
        IdleState = new EnemyIdleState(this, StateMachine);
        InvestigateState = new EnemyInvestigateState(this, StateMachine);
        ChaseState = new EnemyChaseState(this, StateMachine);
        CombatState = new EnemyCombatState(this, StateMachine);
        AttackState = new EnemyAttackState(this, StateMachine);
        ShootState = new EnemyShootState(this, StateMachine);
        BlockState = new EnemyBlockState(this, StateMachine);
        DodgeState = new EnemyDodgeState(this, StateMachine);
        HitState = new EnemyHitState(this, StateMachine);
        RepositionState = new EnemyRepositionState(this, StateMachine);
        ReturnState = new EnemyReturnState(this, StateMachine);
        DeadState = new EnemyDeadState(this, StateMachine);
        StateMachine.OnStateChanged += CountState;

        // The quiver's arrows fly in the world: they leave the archer's hierarchy (pooled, D6)
        foreach (Arrow a in quiver)
        {
            if (a == null) continue;
            a.transform.SetParent(null, true);
            a.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        foreach (Arrow a in quiver) if (a != null) Destroy(a.gameObject);
    }

    private void OnEnable()
    {
        Health.OnDamageReceived += OnDamaged;
        Health.OnDeath += OnDeath;
        EnemyCoordinator.Register(this);
    }

    private void OnDisable()
    {
        Health.OnDamageReceived -= OnDamaged;
        Health.OnDeath -= OnDeath;
        EnemyCoordinator.Unregister(this);
        if (_watched != null) _watched.StateChanged -= WatchPlayer;
        _watched = null;
    }

    private void Start()
    {
        if (data != null) Health.InitializeHealth(data.maxHealth);
        // Placed a little off the NavMesh (by hand, by a spawner): onto the closest point of it
        if (Agent.isActiveAndEnabled && !Agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            Agent.Warp(hit.position);
            Post = hit.position;
        }
        StateMachine.Initialize(IdleState);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || data == null) return;
        Perception.Tick();
        WatchTarget();
        if (Time.time - _poiseSince > PoiseWindow) PoiseDamage = 0;
        StateMachine.CurrentState?.Tick(dt);

        Vector3 moved = transform.position - _lastPosition;
        moved.y = 0f;
        Velocity = moved / dt;
        _lastPosition = transform.position;
        if (!IsDead) Anim.Move(Velocity);
    }

    // ─── Movement ────────────────────────────────────────────────────────────────

    /// <summary>Walks or runs toward <paramref name="point"/> on the NavMesh (kept inside its zone).</summary>
    public void MoveTo(Vector3 point, float speed)
    {
        if (!Agent.isActiveAndEnabled || !Agent.isOnNavMesh) return;
        if (zone != null) point = zone.Clamp(point);
        Agent.speed = speed;
        Agent.isStopped = false;
        if ((Agent.destination - point).sqrMagnitude > 0.04f || !Agent.hasPath) Agent.SetDestination(point);
    }

    /// <summary>Stands still.</summary>
    public void Stop()
    {
        if (!Agent.isActiveAndEnabled || !Agent.isOnNavMesh) return;
        Agent.isStopped = true;
        Agent.ResetPath();
        Agent.velocity = Vector3.zero;
    }

    /// <summary>Moves the body by <paramref name="delta"/> along the NavMesh (a lunge, a dodge, a knockback).</summary>
    public void Shift(Vector3 delta)
    {
        if (!Agent.isActiveAndEnabled || !Agent.isOnNavMesh) return;
        delta.y = 0f;
        Agent.Move(delta);
    }

    /// <summary>Closest the body steps toward the target's centre (m): arm's length, never into it.</summary>
    public const float MinGap = 0.85f;

    /// <summary>
    /// The clip's own travel (an attack's step or charge, EnemyAnimator.RootMotion): moves the body along
    /// the NavMesh, but the part that would bring it closer than <see cref="MinGap"/> to the target is
    /// dropped (the charge stops at the player instead of passing through).
    /// </summary>
    public void StepToward(Vector3 delta)
    {
        delta.y = 0f;
        if (Perception.Target != null)
        {
            Vector3 to = Perception.TargetFeet - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > 0.001f)
            {
                Vector3 dir = to / distance;
                float closer = Vector3.Dot(delta, dir);
                float room = Mathf.Max(0f, distance - MinGap);
                if (closer > room) delta -= dir * (closer - room);
            }
        }
        Shift(delta);
    }

    /// <summary>Turns toward <paramref name="direction"/> at the archetype's rate (×<paramref name="rateScale"/>).</summary>
    public void Face(Vector3 direction, float dt, float rateScale = 1f)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, data.turnRate * rateScale * dt);
    }

    /// <summary>Turns toward where the path goes (moving) or keeps its facing.</summary>
    public void FaceMovement(float dt)
    {
        if (Agent.isActiveAndEnabled && Agent.velocity.sqrMagnitude > 0.05f) Face(Agent.velocity, dt);
    }

    /// <summary>Turns toward the target.</summary>
    public void FaceTarget(float dt, float rateScale = 1f)
    {
        if (Perception.Target != null) Face(Perception.Target.position - transform.position, dt, rateScale);
    }

    /// <summary>Horizontal direction to the target (unit), or the facing.</summary>
    public Vector3 ToTarget
    {
        get
        {
            if (Perception.Target == null) return transform.forward;
            Vector3 to = Perception.Target.position - transform.position;
            to.y = 0f;
            return to.sqrMagnitude > 0.0001f ? to.normalized : transform.forward;
        }
    }

    /// <summary>The target is inside its zone (or it has none).</summary>
    public bool TargetInZone => Perception.Target != null && (zone == null || zone.Contains(Perception.TargetFeet));

    /// <summary>The target is alive and known.</summary>
    public bool TargetAlive => Perception.Target != null && Perception.TargetHealth != null && !Perception.TargetHealth.IsDead;

    // ─── Decisions ───────────────────────────────────────────────────────────────

    /// <summary>Schedules the next attack (GDD: every ~0.5–1.5 s; the archetype's interval).</summary>
    public void ScheduleNextAttack(float extra = 0f) =>
        NextAttackTime = Time.time + Random.Range(data.attackIntervalMin, data.attackIntervalMax) + extra;

    /// <summary>
    /// The attack to start now at <paramref name="distance"/> (m) from the target, or −1. The opening attack
    /// of each archetype: the closest-reaching ones when near, the lunges and charges from farther.
    /// </summary>
    public int ChooseAttack(float distance)
    {
        EnemyAttack[] attacks = data.attacks;
        if (attacks == null || attacks.Length == 0) return -1;
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < attacks.Length; i++)
        {
            EnemyAttack a = attacks[i];
            if (IsFollowUp(i)) continue;  // only reached from its combo
            if (distance > a.reach) continue; // out of its measured reach (it closes in first)
            float score = Random.value;
            bool lunge = a.lunge > 0.5f || (a.rootMotion && a.reach > 3.5f);
            if (lunge) score += distance > 2.2f ? 1.2f : -0.6f; // lunges close distance
            if (IsBoss && i == _lastOpener) score -= 0.5f;     // a boss varies its openings
            if (a.heavy) score += HeavyBias();
            if (a.next >= 0) score += ComboBias();
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private int _lastOpener = -1;

    /// <summary>Called by EnemyAttackState when an opening attack (not a follow-up) starts.</summary>
    public void NotifyOpener(int index) => _lastOpener = index;

    /// <summary>Whether attack <paramref name="index"/> follows another one in a combo.</summary>
    public bool IsFollowUp(int index)
    {
        foreach (EnemyAttack a in data.attacks) if (a.next == index) return true;
        return false;
    }

    private float HeavyBias()
    {
        if (data.bossStyle == BossStyle.Commander && SeenBlocks > SeenDodges && SeenBlocks >= 2) return 0.8f; // breaks a turtle's guard
        return data.kind == EnemyKind.Heavy ? 0.4f : 0f;
    }

    private float ComboBias() => data.kind == EnemyKind.Light || data.bossStyle == BossStyle.ClanLeader ? 0.4f : 0f;

    /// <summary>
    /// Seconds an attack's wind-up is held: a heavy blow's telegraph, and the Commander's delay against a
    /// player who dodges early.
    /// </summary>
    public float WindupHold(EnemyAttack attack)
    {
        float hold = attack.windupHold;
        if (data.bossStyle == BossStyle.Commander && SeenDodges > SeenBlocks && SeenDodges >= 2) hold += Random.Range(0.15f, 0.35f);
        return hold;
    }

    /// <summary>Chance to guard against the target's attack (the Commander guards more against a player who attacks a lot).</summary>
    public float BlockChance => data.bossStyle == BossStyle.Commander && SeenAttacks >= 6 ? Mathf.Max(data.blockChance, 0.55f) : data.blockChance;

    /// <summary>Called by EnemyAttackState: an attack started.</summary>
    public void NotifyAttack(EnemyAttack attack)
    {
        LastAttack = attack;
        Attacks++;
        AttackStarted?.Invoke(this, attack);
    }

    /// <summary>Called by EnemyShootState: an arrow left.</summary>
    public void NotifyShot() => Shots++;

    /// <summary>A free arrow of the quiver, or null.</summary>
    public Arrow TakeArrow()
    {
        foreach (Arrow a in quiver) if (a != null && !a.gameObject.activeSelf) return a;
        return null;
    }

    // ─── Damage, guard, poise ────────────────────────────────────────────────────

    /// <summary>A blocked frontal hit keeps a share of its damage; a heavy hit (20 or more) breaks the guard.</summary>
    public int ModifyIncomingDamage(int amount, Vector3 source)
    {
        if (!Guarding) return amount;
        Vector3 from = source - transform.position;
        from.y = 0f;
        bool frontal = from.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, from) < 70f;
        if (!frontal || amount >= 20) return amount;
        return Mathf.Max(1, Mathf.RoundToInt(amount * data.guardDamageKept));
    }

    private void OnDamaged(int amount, Vector3 source)
    {
        if (IsDead) return;
        Perception.OnHitBy(source);
        if (Time.time - _poiseSince > PoiseWindow) { PoiseDamage = 0; _poiseSince = Time.time; }
        PoiseDamage += amount;
        Anim.HitStop(0.06f);

        bool heavy = amount >= 20;
        bool guarded = Guarding && !heavy;
        if (guarded) return; // the guard holds: no reaction
        // Hyper armor of a committed heavy swing: only a heavy blow interrupts it
        if (StateMachine.CurrentState == AttackState && AttackState.Committed && !heavy) return;
        if (PoiseDamage >= data.poise || heavy)
        {
            PoiseDamage = 0;
            _poiseSince = Time.time;
            HitState.Prepare(source, heavy);
            StateMachine.ChangeState(HitState);
        }
        else if (!IsEngaged)
            StateMachine.ChangeState(ChaseState); // struck from its blind side: it turns to fight
    }

    private void OnDeath()
    {
        StateMachine.ChangeState(DeadState);
    }

    // ─── Arena and learning ──────────────────────────────────────────────────────

    /// <summary>Starts fighting at once (a boss whose arena closed).</summary>
    public void Engage()
    {
        if (IsDead) return;
        Perception.OnHitBy(transform.position);
        if (!IsEngaged) StateMachine.ChangeState(ChaseState);
    }

    /// <summary>Back to its post with full health (the player died during its fight).</summary>
    public void ResetToPost()
    {
        if (IsDead) return;
        EnemyCoordinator.Release(this);
        Perception.Forget();
        Health.Revive();
        if (Agent.isActiveAndEnabled && Agent.isOnNavMesh) Agent.Warp(Post);
        transform.rotation = PostRotation;
        SeenBlocks = SeenDodges = SeenAttacks = 0;
        StateMachine.ChangeState(IdleState);
    }

    private void WatchTarget()
    {
        PlayerMovement target = Perception.TargetMovement;
        if (target == _watched || data.bossStyle != BossStyle.Commander) return;
        if (_watched != null) _watched.StateChanged -= WatchPlayer;
        _watched = target;
        if (_watched != null) _watched.StateChanged += WatchPlayer;
    }

    /// <summary>The Commander keeps count of what the player does while fighting it.</summary>
    private void WatchPlayer(PlayerState state)
    {
        if (!IsEngaged || _watched == null || Perception.Distance > 6f) return;
        if (state == _watched.BlockState) SeenBlocks++;
        else if (state == _watched.DodgeState) SeenDodges++;
        else if (state is PlayerAttackState) SeenAttacks++;
    }

    private void CountState(EnemyState state)
    {
        if (state == BlockState) Blocks++;
        else if (state == DodgeState) Dodges++;
        else if (state == HitState) Staggers++;
    }

    /// <summary>Its zone and its posts (an enemy placed at runtime: the tests, a spawner).</summary>
    public void SetZone(EnemyZone enemyZone, Transform[] posts = null)
    {
        zone = enemyZone;
        if (posts != null) archerPosts = posts;
    }

    /// <summary>Wires the enemy (EnemySetup and the area builder).</summary>
    public void Configure(EnemyData enemyData, EnemyZone enemyZone, Transform[] posts, Arrow[] arrows, Transform muzzle)
    {
        data = enemyData;
        zone = enemyZone;
        archerPosts = posts ?? new Transform[0];
        quiver = arrows ?? new Arrow[0];
        bowMuzzle = muzzle;
    }
}
