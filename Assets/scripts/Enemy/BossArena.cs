using UnityEngine;

/// <summary>
/// A boss's arena (GDD §5.15: entering the boss's zone blocks the exit; the bosses have no phases). When
/// the player walks in while the boss lives, the gates close; when the boss dies they open for good. If
/// the player dies inside (they reappear at the checkpoint, outside), the gates open and the boss goes back
/// to its post with full health, so the fight starts over.
/// The trigger is this object's BoxCollider; the gates are children with colliders and renderers
/// (disabled while open).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BossArena : MonoBehaviour
{
    [SerializeField] private Enemy boss;
    [SerializeField] private GameObject[] gates = new GameObject[0];

    private BoxCollider _area;
    private HealthSystem _playerHealth;

    /// <summary>The gates are closed (the fight is on).</summary>
    public bool Closed { get; private set; }

    /// <summary>The boss died: the arena stays open.</summary>
    public bool Cleared { get; private set; }

    public Enemy Boss => boss;

    /// <summary>Wires the arena (the area builder).</summary>
    public void Configure(Enemy bossEnemy, GameObject[] arenaGates)
    {
        boss = bossEnemy;
        gates = arenaGates;
    }

    private void Awake()
    {
        _area = GetComponent<BoxCollider>();
        _area.isTrigger = true;
        SetGates(false);
    }

    private HealthSystem _bossHealth;

    // In Start: the boss's HealthSystem is known after every Awake (OnEnable could run before the boss's)
    private void Start()
    {
        _bossHealth = boss != null ? boss.GetComponent<HealthSystem>() : null;
        if (_bossHealth != null) _bossHealth.OnDeath += OnBossDeath;
    }

    private void OnDestroy()
    {
        if (_bossHealth != null) _bossHealth.OnDeath -= OnBossDeath;
        if (_playerHealth != null) _playerHealth.OnDeath -= OnPlayerDeath;
    }

    private void Update()
    {
        if (Cleared || boss == null) return;
        Player player = Player.Instance;
        if (player == null) return;
        if (_playerHealth == null)
        {
            _playerHealth = player.GetComponent<HealthSystem>();
            if (_playerHealth != null) _playerHealth.OnDeath += OnPlayerDeath;
        }
        // A dead player lying inside does not close it again (it reappears outside, at its checkpoint)
        bool playerAlive = _playerHealth == null || !_playerHealth.IsDead;
        if (!Closed && playerAlive && Inside(player.transform.position) && !boss.IsDead)
        {
            Closed = true;
            SetGates(true);
            boss.Engage(); // the boss turns to the intruder
        }
    }

    /// <summary>Whether a point is inside the arena (horizontally).</summary>
    public bool Inside(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point) - _area.center;
        Vector3 half = _area.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    private void OnBossDeath()
    {
        Cleared = true;
        Closed = false;
        SetGates(false);
    }

    private void OnPlayerDeath()
    {
        if (Cleared || !Closed) return;
        Closed = false;
        SetGates(false);
        boss.ResetToPost();
    }

    private void SetGates(bool closed)
    {
        foreach (GameObject gate in gates)
            if (gate != null) gate.SetActive(closed);
    }
}
