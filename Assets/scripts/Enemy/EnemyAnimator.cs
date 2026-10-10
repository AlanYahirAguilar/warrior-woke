using UnityEngine;

/// <summary>
/// The enemy's animation (P40), driven by its AI like PlayerAnimator by the player's FSM (D10): the
/// locomotion blend follows the real velocity under the body (the "animation follows the agent" coupling of
/// AI Navigation: the agent moves the body, the animator shows it; beyond its fastest clip the blend plays
/// faster instead of sliding), and every action (attack, guard, dodge, hit reaction, death) is a
/// cross-fade to its Animator state, whose progress the states read. While <see cref="RootMotion"/> is on
/// (an attack that steps or charges), the clip's own travel moves the body along the NavMesh
/// (EnemyRootMotion forwards OnAnimatorMove); otherwise the root motion is dropped. A hit pauses the
/// Animator for an instant (hit stop), like the player's. No allocations.
/// </summary>
public class EnemyAnimator : MonoBehaviour
{
    public const string LocomotionName = "Locomotion", BlockName = "Block", DodgeName = "Dodge",
                        HitName = "Hit", HitHeavyName = "HitHeavy", KnockbackName = "Knockback", DeathName = "Death",
                        DrawName = "Draw", ShootName = "Shoot";
    public static readonly int MoveX = Animator.StringToHash("MoveX"), MoveZ = Animator.StringToHash("MoveZ"),
                               DodgeX = Animator.StringToHash("DodgeX"), DodgeY = Animator.StringToHash("DodgeY"),
                               LocomotionRate = Animator.StringToHash("LocomotionRate");
    public static readonly int Locomotion = Animator.StringToHash(LocomotionName), Block = Animator.StringToHash(BlockName),
                               Dodge = Animator.StringToHash(DodgeName), Hit = Animator.StringToHash(HitName),
                               HitHeavy = Animator.StringToHash(HitHeavyName), Knockback = Animator.StringToHash(KnockbackName),
                               Death = Animator.StringToHash(DeathName), Draw = Animator.StringToHash(DrawName),
                               Shoot = Animator.StringToHash(ShootName);

    [SerializeField] private Animator animator;
    [Tooltip("Seconds the locomotion parameters take to follow the velocity.")]
    [SerializeField] private float damping = 0.08f;
    [Tooltip("Ground speed (m/s) of the fastest locomotion clip (EnemySetup measures it): faster, the blend plays faster.")]
    [SerializeField] private float fastestClipSpeed = 4f;

    private int _current;
    private float _hitStopUntil;

    public Animator Animator => animator;

    /// <summary>The clip's root motion moves the body (an attack that steps or charges).</summary>
    public bool RootMotion { get; set; }

    private Enemy _enemy;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        _enemy = GetComponent<Enemy>();
        // The root motion is handled by script (EnemyRootMotion on the model): the agent moves the body
        if (animator != null) animator.applyRootMotion = true;
    }

    /// <summary>Called by EnemyRootMotion with the frame's root motion.</summary>
    public void OnRootMotion(Vector3 delta)
    {
        if (RootMotion && _enemy != null) _enemy.StepToward(delta);
    }

    /// <summary>Locomotion from the body's velocity (world), in the character's frame.</summary>
    public void Move(Vector3 velocity)
    {
        if (animator == null) return;
        // A bigger body takes longer strides: the blend reads the speed at the clips' scale
        float scale = Mathf.Max(0.01f, transform.lossyScale.y * (animator.transform.localScale.y));
        Vector3 local = transform.InverseTransformDirection(velocity) / scale;
        animator.SetFloat(MoveX, local.x, damping, Time.deltaTime);
        animator.SetFloat(MoveZ, local.z, damping, Time.deltaTime);
        animator.SetFloat(LocomotionRate, Mathf.Max(1f, local.magnitude / fastestClipSpeed));
    }

    /// <summary>Back to the locomotion.</summary>
    public void PlayLocomotion(float fade = 0.2f) => Play(Locomotion, fade);

    /// <summary>Cross-fades to a state (from its start), at <paramref name="speed"/>.</summary>
    public void Play(int state, float fade, float speed = 1f)
    {
        if (animator == null) return;
        _current = state;
        animator.speed = speed;
        animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    public void Play(string state, float fade, float speed = 1f) => Play(Animator.StringToHash(state), fade, speed);

    /// <summary>Playback speed of the current action (1 = the clip's).</summary>
    public void SetSpeed(float speed)
    {
        if (animator != null && Time.time >= _hitStopUntil) animator.speed = speed;
    }

    /// <summary>Normalized time of <paramref name="state"/> if it is playing (or the target of the cross-fade), else −1.</summary>
    public float Progress(int state)
    {
        if (animator == null) return -1f;
        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
        if (animator.IsInTransition(0) && next.shortNameHash == state) return next.normalizedTime;
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == state ? info.normalizedTime : -1f;
    }

    /// <summary>Progress of the state last played.</summary>
    public float CurrentProgress => Progress(_current);

    /// <summary>The dodge's direction in the character's frame (x right, y forward).</summary>
    public void SetDodge(Vector2 local)
    {
        if (animator == null) return;
        animator.SetFloat(DodgeX, local.x);
        animator.SetFloat(DodgeY, local.y);
    }

    /// <summary>Freezes the animation for <paramref name="seconds"/> (the impact of a blow).</summary>
    public void HitStop(float seconds)
    {
        if (animator == null) return;
        _hitStopUntil = Time.time + seconds;
        _speedBeforeStop = animator.speed > 0f ? animator.speed : _speedBeforeStop;
        animator.speed = 0f;
    }

    private float _speedBeforeStop = 1f;

    private void Update()
    {
        if (animator != null && animator.speed == 0f && _hitStopUntil > 0f && Time.time >= _hitStopUntil)
        {
            animator.speed = _speedBeforeStop;
            _hitStopUntil = 0f;
        }
    }
}
