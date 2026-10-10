using System.Collections.Generic;
using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerAttackState (light and heavy attacks)
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An unarmed attack (decision P37): a clip played through its measured phases (AttackData) — the
/// anticipation, the strike and the recovery — instead of a fixed time. The state follows the clip's
/// progress (ICombatAnimation), so a hit stop, which freezes the animation, also holds the state.
///
/// On Enter it picks the direction of the strike: toward the nearest target in front of the input (or of
/// the facing), within reach of a short step. Until the end of the strike the body turns toward it and
/// closes the distance so the limb arrives there at full extension (the clip's own step, if it has
/// one, is root motion the motor applies, scaled so the body does not walk into the target). During the strike
/// the striking limb's bone sweeps for targets every frame (Hitbox.Sweep): a hit is real contact, once
/// per target and attack, and freezes the attacker for a moment (hit stop). Attack presses are buffered
/// from the start and fire when the chain window opens; moving, dodging or blocking only interrupt the
/// recovery, once the limb is back.
/// </summary>
public abstract class PlayerAttackState : PlayerState
{
    /// <summary>Largest angle (°) between the aim and a target the attack turns toward.</summary>
    private const float AssistAngle = 60f;
    /// <summary>How far (m) the swept fist or foot goes into the target's surface at full extension: the spacing aimed for.</summary>
    private const float Overlap = 0.12f;
    /// <summary>Shortest the clip's own step gets for a target already close (fraction): shorter, the planted foot would skate.</summary>
    private const float MinStepScale = 0.5f;
    /// <summary>Turn rate (°/s) toward the target during the anticipation.</summary>
    private const float TurnRate = 900f;
    /// <summary>Fastest the body closes the distance to the target (m/s): a lunge into the blow.</summary>
    private const float MaxApproachSpeed = 5f;
    /// <summary>Seconds without the clip playing before the attack gives up (a missing animation).</summary>
    private const float AnimationTimeout = 2f;

    protected readonly Hitbox       hitbox;
    protected readonly WeaponHolder weapons;
    private readonly List<IDamageable> _struck = new List<IDamageable>(4);

    private Vector3   _aim;
    private float     _spacing;        // distance (m) from the target's axis the body should be at the impact
    private Vector3   _lastLimb;
    private bool      _hasLimb;
    private float     _rootMotionScale = 1f;

    /// <summary>The attack playing (clip, phases, reach).</summary>
    public AttackData Data { get; private set; }

    /// <summary>Normalized time of the attack's clip, or −1 if it is not playing yet.</summary>
    public float Progress => player.CombatAnimation != null ? player.CombatAnimation.ActionProgress : -1f;

    /// <summary>The target the attack turned toward (null: struck at the air in front).</summary>
    public Transform Target { get; private set; }

    /// <summary>Targets this attack has struck.</summary>
    public int Hits { get; private set; }

    /// <summary>Damage of this attack.</summary>
    protected abstract int Damage { get; }

    /// <summary>The recovery can be interrupted from here (normalized).</summary>
    protected virtual float MoveCancel => Data.MoveCancel;

    /// <summary>Most the body steps toward a target to reach it (m), on top of the clip's own step.</summary>
    protected abstract float MaxAssist { get; }

    protected PlayerAttackState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine)
    {
        // Cache components once — never inside the loop
        hitbox  = player.GetComponent<Hitbox>();
        weapons = player.GetComponent<WeaponHolder>();
    }

    /// <summary>Starts <paramref name="data"/>: picks the target and the direction, the step to take.</summary>
    protected void Begin(AttackData data)
    {
        Data = data;
        Hits = 0;
        _struck.Clear();
        _hasLimb = false;
        player.CancelSprint(); // GDD §5.2: attacking cancels the sprint

        Vector3 aim = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        aim.y = 0f;
        aim.Normalize();
        Target = null;
        _rootMotionScale = 1f;
        Vector3 center = player.transform.position;
        float range = data.Reach + data.StepToImpact + MaxAssist + 0.6f;
        if (hitbox != null && hitbox.FindTarget(center, aim, range, AssistAngle, out Transform target, out float targetRadius))
        {
            Target = target;
            Vector3 to = target.position - center;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > 0.01f) aim = to / distance;
            // Where the limb arrives at full extension, a little into the target's surface
            _spacing = data.Reach + data.LimbRadius + targetRadius - Overlap;
            float gap = distance - _spacing;
            // A step of at most MaxAssist (on top of the clip's own): a farther target is struck short
            _spacing = Mathf.Max(_spacing, distance - data.StepToImpact - MaxAssist);
            // A target closer than the clip's own step: the step shrinks so the body stops in front of it
            if (data.StepToImpact > 0.1f)
                _rootMotionScale = Mathf.Clamp(gap / data.StepToImpact, MinStepScale, 1f);
        }
        _aim = aim;
        player.ActionRootMotionScale = _rootMotionScale;
        player.TurnToward(_aim, TurnRate);
        player.StopHorizontal(player.Velocity.y);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float n = Progress;
        bool frozen = player.CombatAnimation != null && player.CombatAnimation.IsHitStopped;
        if (Target != null && !frozen && n >= 0f && n < Data.HitEnd)
        {
            // Close the distance, stepping into the blow, measured every tick on the real distance (the
            // locomotion fades out under the first frames): what is left once the clip's own remaining
            // step is counted, over the time left until the limb's full extension (and while it holds
            // there, what a long lunge still lacks)
            Vector3 to = Target.position - player.transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            float stepLeft = Data.StepToImpact * _rootMotionScale * (1f - Mathf.InverseLerp(Data.StartAt, Data.Impact, n));
            float remaining = distance - _spacing - stepLeft;
            if (remaining > 0.01f && distance > 0.01f)
            {
                float timeLeft = Mathf.Max(0.05f, Data.SecondsTo(Data.Impact) - Data.SecondsTo(n));
                float speed = Mathf.Min(MaxApproachSpeed, remaining / timeLeft);
                _aim = to / distance;
                player.TurnToward(_aim, TurnRate);
                player.SetVelocity(_aim * speed, player.Velocity.y);
                return;
            }
        }
        player.StopHorizontal(player.Velocity.y);
    }

    /// <summary>
    /// Every frame, on the final pose (PlayerAnimator.LateUpdate): during the strike the limb's bone
    /// sweeps from where it was last frame to where it is now, and every target it touches is struck once.
    /// </summary>
    public void Sweep(Vector3 limb)
    {
        float n = Progress;
        if (hitbox != null && n >= Data.HitStart && n <= Data.HitEnd)
        {
            int hits = hitbox.Sweep(_hasLimb ? _lastLimb : limb, limb, Data.LimbRadius, Damage, _struck);
            if (hits > 0)
            {
                Hits += hits;
                player.CombatAnimation?.HitStop(Data.HitStop);
            }
        }
        _lastLimb = limb;
        _hasLimb = true;
    }

    /// <summary>
    /// The end of the attack, shared by both: off the ground it falls; past the recovery point moving,
    /// dodging or blocking take over; when the clip ends it returns to the locomotion. Returns true if the
    /// state changed.
    /// </summary>
    protected bool TryEnd(float n)
    {
        if (!player.IsGrounded && player.AirTime > player.FallGraceTime)
        {
            stateMachine.ChangeState(player.FallState);
            return true;
        }
        if (n < 0f)
        {
            if (Time.time - startTime < AnimationTimeout) return false;
            stateMachine.ChangeState(player.IdleState);
            return true;
        }
        if (n >= MoveCancel)
        {
            if (player.DodgeTriggered && player.DodgeState.CanDodge) { stateMachine.ChangeState(player.DodgeState); return true; }
            if (player.IsBlockHeld) { stateMachine.ChangeState(player.BlockState); return true; }
            if (player.HasMoveInput) { stateMachine.ChangeState(player.RunState); return true; }
        }
        if (n >= EndOfClip)
        {
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
            return true;
        }
        return false;
    }

    /// <summary>The clip is over (normalized): its last frames are the guard, the locomotion blends in from it.</summary>
    protected const float EndOfClip = 0.97f;

    public override void Exit()
    {
        base.Exit();
        player.ActionRootMotionScale = 1f;
        player.StopTurning();
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLightAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Light attack (J, GDD §5.6): the chain jab → cross → hook, up to 3 blows (CombatTimings). A press
/// during a blow is buffered and the next blow starts when the chain window opens (~0.25 s apart); the
/// chain restarts at the jab if more than 0.5 s pass between two presses (GDD §5.9). K in the chain
/// window ends the combo with the heavy attack (J → J → K). The hook ends the chain: there is no fourth
/// light blow; J during it starts a new chain once its recovery can be interrupted.
/// </summary>
public class PlayerLightAttackState : PlayerAttackState
{
    private int   _chainCount;
    private bool  _chaining;
    private float _lastPressTime = -10f;
    private float _chainPress;                                   // the press that starts the next blow
    private float _pendingLight = -10f, _nextLight = -10f;       // queued J presses, oldest first (−10: none)
    private float _bufferedHeavy = -10f;                         // K press time (−10: none)

    /// <summary>Position of the current blow in the chain (1..3).</summary>
    public int ChainIndex => _chainCount;

    protected override int Damage => weapons != null ? weapons.GetLightDamage() : 10;
    protected override float MaxAssist => 0.7f;

    public PlayerLightAttackState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        // The press that started this blow: a queued one when chaining (the presses still queued after it
        // stay for the next blows), otherwise now
        float press = _chaining ? _chainPress : Time.time;
        bool continues = _chaining && press - _lastPressTime <= CombatTimings.ComboResetTime && _chainCount < CombatTimings.MaxLightChain;
        _chainCount = continues ? _chainCount + 1 : 1;
        _lastPressTime = press;
        if (!_chaining) _pendingLight = _nextLight = -10f;
        _chaining = false;
        _bufferedHeavy = -10f;
        Begin(_chainCount == 1 ? CombatTimings.Jab : _chainCount == 2 ? CombatTimings.Cross : CombatTimings.Hook);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Buffer presses from the start of the blow; they fire when the chain window opens
        if (player.LightAttackTriggered)
        {
            if (_pendingLight < 0f) _pendingLight = Time.time;
            else _nextLight = Time.time;
        }
        if (player.HeavyAttackTriggered) _bufferedHeavy = Time.time;

        float n = Progress;
        if (n >= Data.ChainOpen)
        {
            // J → J → K: the kick ends the combo from any blow of the chain (a combo if it came in time)
            if (_bufferedHeavy > 0f)
            {
                player.HeavyAttackState.FromCombo = _bufferedHeavy - _lastPressTime <= CombatTimings.ComboResetTime;
                stateMachine.ChangeState(player.HeavyAttackState);
                return;
            }
            // The next blow (re-entering the state); after the hook, a new chain once it can be interrupted
            bool chainFull = _chainCount >= CombatTimings.MaxLightChain;
            if (_pendingLight > 0f && (!chainFull || n >= MoveCancel))
            {
                _chainPress = _pendingLight;
                _pendingLight = _nextLight;
                _nextLight = -10f;
                _chaining = !chainFull;
                stateMachine.ChangeState(this);
                return;
            }
        }
        TryEnd(n);
    }

    public override void Exit()
    {
        base.Exit();
        if (!_chaining) _pendingLight = _nextLight = _bufferedHeavy = -10f;
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerHeavyAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Heavy attack (K, GDD §5.7): the front kick (CMU mocap). Slower than the punches (its anticipation
/// shifts the weight and chambers the leg), its step is root motion, it knocks the target back (the
/// target reacts to 20 damage or more) and it leaves the player open if it misses: a kick that struck
/// recovers at its measured MoveCancel, one that missed only at the end of the clip.
/// </summary>
public class PlayerHeavyAttackState : PlayerAttackState
{
    /// <summary>The kick that missed can only be left here (normalized): the player stays open (GDD §5.7).</summary>
    private const float MissedRecovery = 0.93f;

    /// <summary>Entered from the light chain (J → J → K) rather than on its own.</summary>
    public bool FromCombo { get; set; }

    protected override int Damage => weapons != null ? weapons.GetHeavyDamage() : 20;
    protected override float MaxAssist => 0.9f;
    protected override float MoveCancel => Hits > 0 ? Data.MoveCancel : MissedRecovery;

    public PlayerHeavyAttackState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        Begin(CombatTimings.Kick);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        TryEnd(Progress);
    }

    public override void Exit()
    {
        base.Exit();
        FromCombo = false;
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerHurtState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hit reaction (GDD §5.1, §5.5): taking damage on the ground interrupts what the player was doing —
/// moving, attacking or the end of a dodge — with a short reaction (Quaternius "Hit_Chest", or
/// "Hit_Head" for a heavy hit). The body turns toward where the hit came from and is pushed a little
/// away from it; it cannot move, attack or dodge until the reaction ends (GDD §5.5: no dodge during a
/// damage animation). A hit blocked from the front does not interrupt the guard.
/// </summary>
// ────────────────────────────────────────────────────────────────────────────────
// PlayerDeadState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Dead (GDD §5.11: health 0 or a fatal fall): the body falls with the death clip and takes no input
/// (GDD §5.1: it does not move while dead); after RespawnDelay the player reappears at the respawn point
/// with full health (GDD §17).
/// </summary>
public class PlayerDeadState : PlayerState
{
    /// <summary>Seconds from the death to the respawn.</summary>
    public const float RespawnDelay = 3f;

    public PlayerDeadState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>Seconds since the death.</summary>
    public float Elapsed => Time.time - startTime;

    public override void Enter()
    {
        base.Enter();
        player.CancelSprint();
        player.StopTurning();
        player.StopHorizontal(player.Velocity.y);
    }

    public override void PhysicsUpdate()
    {
        // The body only falls: no steering, no momentum (gravity keeps acting)
        player.StopHorizontal(player.Velocity.y);
    }

    public override void LogicUpdate()
    {
        if (Elapsed >= RespawnDelay) player.Respawn();
    }
}

public class PlayerHurtState : PlayerState
{
    /// <summary>Damage from which the reaction is the heavy one (a hit to the head).</summary>
    public const int HeavyHitDamage = 20;
    /// <summary>How far (m) a hit pushes the body, and in how long (s).</summary>
    private const float PushDistance = 0.25f, PushTime = 0.2f;
    /// <summary>The reaction ends here (normalized) or after MaxTime seconds.</summary>
    private const float EndAt = 0.9f, MaxTime = 1f;

    private Vector3 _push;

    /// <summary>The reaction to a heavy hit (to the head) instead of the body one.</summary>
    public bool Heavy { get; private set; }

    public PlayerHurtState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>Sets up the reaction to a hit of <paramref name="damage"/> from <paramref name="source"/>, before entering.</summary>
    public void Prepare(int damage, Vector3 source)
    {
        Heavy = damage >= HeavyHitDamage;
        Vector3 away = player.transform.position - source;
        away.y = 0f;
        _push = away.sqrMagnitude > 0.0001f ? away.normalized : -player.transform.forward;
    }

    public override void Enter()
    {
        base.Enter();
        player.CancelSprint();
        player.TurnToward(-_push, 720f); // the reactions are hits from the front: face the hit
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float n = player.CombatAnimation != null ? player.CombatAnimation.ActionProgress : -1f;
        if (n >= EndAt || Time.time - startTime > MaxTime)
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float t = Time.time - startTime;
        // The push fades out over PushTime: a velocity that decays linearly covers PushDistance
        float speed = t < PushTime ? 2f * PushDistance / PushTime * (1f - t / PushTime) : 0f;
        player.SetVelocity(_push * speed, player.Velocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.StopTurning();
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerBlockState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defensive state (L held). Restricts mobility — player cannot run or jump while blocking.
/// Light attack while blocking counterattacks.
/// Reduces damage by 70 % only for hits coming from the front (GDD §5.8). HealthSystem asks
/// through IDamageModifier (implemented by PlayerMovement), which forwards here while blocking.
/// </summary>
public class PlayerBlockState : PlayerState
{
    private const float DamageReductionFactor = 0.3f; // absorbs 70%, player takes 30% (GDD §5.8)

    // cos(60°): hits within ±60° of the facing direction count as frontal.
    private const float FrontalDotThreshold = 0.5f;

    public float DamageReductionMultiplier => DamageReductionFactor;

    /// <summary>The last hit taken while blocking came from the front (it was blocked).</summary>
    public bool LastHitBlocked { get; private set; }

    /// <summary>Applies the block reduction if the source is in front of the player.</summary>
    public int ModifyIncomingDamage(int amount, Vector3 source)
    {
        Vector3 toSource = source - player.transform.position;
        toSource.y = 0f;

        // A source at the player's own position has no direction: treat it as unblocked.
        LastHitBlocked = toSource.sqrMagnitude >= 0.0001f &&
                         Vector3.Dot(player.transform.forward, toSource.normalized) >= FrontalDotThreshold;
        return LastHitBlocked ? Mathf.RoundToInt(amount * DamageReductionFactor) : amount;
    }

    public PlayerBlockState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        LastHitBlocked = false;
        player.StopHorizontal(player.Velocity.y);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Exit when L is released
        if (!player.IsBlockHeld)
        {
            stateMachine.ChangeState(player.IdleState);
            return;
        }

        // Allow counterattack: light attack while blocking exits to LightAttack
        if (player.LightAttackTriggered)
        {
            stateMachine.ChangeState(player.LightAttackState);
            return;
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Block anchors the player in place
        player.StopHorizontal(player.Velocity.y);
    }

}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerDodgeState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Quick evasion state (Q + direction, GDD §5.5). Duration 0.5s with 0.2s of iframes.
/// Direction: the camera-relative movement input at the moment of the dodge; without input,
/// the current facing direction. The body does not turn during the dodge, so the animation
/// picks a forward/back/left/right roll from LocalDirection.
/// The roll covers ~2.6 m: it starts fast and slows down to a stop (v = v0 · (1 − (t/T)²)), instead
/// of the 6 m dash at a constant 12 m/s it was. Walls stop it (the motor's CharacterController).
/// Once the invulnerability is over, J or K answer with an attack right away (GDD §5.5: avoid an
/// attack and respond immediately).
/// Cannot be used while airborne (only grounded).
/// Cooldown (1 s, GDD §5.5) enforced via Time.time (zero GC — no Coroutine); the transitions
/// into this state check CanDodge.
/// </summary>
public class PlayerDodgeState : PlayerState
{
    private const float DodgeDuration   = 0.5f;
    private const float IFramesDuration = 0.2f;
    private const float DodgeDistance   = 2.6f;
    private const float DodgeCooldown   = 1f;
    /// <summary>From here (s) an attack press ends the dodge with the attack.</summary>
    private const float AttackFrom      = 0.3f;

    private float _lastDodgeTime = -999f;

    /// <summary>Returns true if the dodge cooldown has elapsed.</summary>
    public bool CanDodge => Time.time - _lastDodgeTime >= DodgeCooldown;

    private HealthSystem _healthSystem;
    private Vector3      _dodgeDirection;
    private bool         _bufferedLight, _bufferedHeavy;

    /// <summary>Dodge direction relative to the character (x = right, y = forward), normalized.</summary>
    public Vector2 LocalDirection { get; private set; }

    public PlayerDodgeState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine)
    {
        _healthSystem = player.GetComponent<HealthSystem>();
    }

    public override void Enter()
    {
        base.Enter();
        _lastDodgeTime    = Time.time;
        _bufferedLight = _bufferedHeavy = false;

        _dodgeDirection = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        Vector3 local   = player.transform.InverseTransformDirection(_dodgeDirection);
        LocalDirection  = new Vector2(local.x, local.z).normalized;

        // Activate invincibility frames on the HealthSystem for IFramesDuration seconds
        _healthSystem?.ActivateIFrames(IFramesDuration);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        float t = Time.time - startTime;
        if (player.LightAttackTriggered) _bufferedLight = true;
        if (player.HeavyAttackTriggered) _bufferedHeavy = true;
        if (t >= AttackFrom && (_bufferedLight || _bufferedHeavy))
        {
            stateMachine.ChangeState(_bufferedHeavy ? (PlayerState)player.HeavyAttackState : player.LightAttackState);
            return;
        }

        if (t >= DodgeDuration)
        {
            // Return to run if still holding a direction, otherwise idle
            stateMachine.ChangeState(
                player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Roll in the direction chosen on Enter, even if the input changes mid-dodge: fast at first,
        // slowing to a stop (the integral of v0 · (1 − (t/T)²) over T is DodgeDistance)
        float t = Mathf.Clamp01((Time.time - startTime) / DodgeDuration);
        float v0 = 1.5f * DodgeDistance / DodgeDuration;
        player.SetVelocity(_dodgeDirection * (v0 * (1f - t * t)), player.Velocity.y);
    }
}
