using UnityEngine;

// The states of an enemy's AI (GDD §21: basic enemies Idle → Detect → Approach → Attack → Defend → Search →
// Return; bosses add analysis of the distance, repositioning, blocks and dodges; decisions P4, P40). One
// class per state, its transitions inside it (D2). Shared rules:
//  - an enemy never leaves its zone (EnemyZone): a target outside it ends the chase;
//  - it attacks only with a token (EnemyCoordinator) and gives it back when the attack ends;
//  - every attack has a wind-up (a heavy one holds it: the telegraph), a strike window that matches its
//    clip's measured active part (EnemyWeapon), and a recovery the player can punish.

// ────────────────────────────────────────────────────────────────────────────────
// Idle: guarding the post, or walking the zone's patrol route
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyIdleState : EnemyState
{
    private int _waypoint;
    private float _waitUntil, _lookYaw, _nextLook;

    public EnemyIdleState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        enemy.Stop();
        enemy.Anim.PlayLocomotion();
        enemy.Guarding = false;
        EnemyCoordinator.Release(enemy);
        EnemyCoordinator.StopWaiting(enemy);
        _lookYaw = enemy.PostRotation.eulerAngles.y;
    }

    public override void Tick(float dt)
    {
        EnemyPerception p = enemy.Perception;
        if (p.CanSee && enemy.TargetInZone && enemy.TargetAlive) { stateMachine.ChangeState(enemy.ChaseState); return; }
        if (p.HasNoise && (enemy.Zone == null || enemy.Zone.Contains(p.NoisePosition))) { stateMachine.ChangeState(enemy.InvestigateState); return; }

        Transform[] route = enemy.Zone != null ? enemy.Zone.Patrol : null;
        if (route != null && route.Length > 0)
        {
            Vector3 point = route[_waypoint % route.Length].position;
            if (Time.time < _waitUntil) { enemy.Stop(); return; }
            enemy.MoveTo(point, enemy.Data.walkSpeed);
            enemy.FaceMovement(dt);
            Vector3 to = point - enemy.transform.position;
            to.y = 0f;
            if (to.magnitude < 0.6f) { _waypoint++; _waitUntil = Time.time + 1.5f; }
            return;
        }

        // Standing guard: now and then it looks a little to one side and the other
        if (Time.time >= _nextLook)
        {
            _nextLook = Time.time + Random.Range(2.5f, 4f);
            _lookYaw = enemy.PostRotation.eulerAngles.y + Random.Range(-45f, 45f);
        }
        enemy.Face(Quaternion.Euler(0f, _lookYaw, 0f) * Vector3.forward, dt, 0.15f);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Investigate: goes to look where something was heard or the target was last seen
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyInvestigateState : EnemyState
{
    private Vector3 _point;
    private float _arrivedAt;
    private bool _arrived;

    public EnemyInvestigateState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    /// <summary>Where it went to look (tests).</summary>
    public Vector3 Point => _point;

    public override void Enter()
    {
        base.Enter();
        EnemyPerception p = enemy.Perception;
        _point = p.HasNoise ? p.NoisePosition : p.LastKnownPosition;
        p.ClearNoise();
        _arrived = false;
        enemy.Anim.PlayLocomotion();
        EnemyCoordinator.Release(enemy);
    }

    public override void Tick(float dt)
    {
        EnemyPerception p = enemy.Perception;
        if (p.CanSee && enemy.TargetInZone && enemy.TargetAlive) { stateMachine.ChangeState(enemy.ChaseState); return; }
        if (p.HasNoise) { _point = p.NoisePosition; p.ClearNoise(); _arrived = false; }

        if (!_arrived)
        {
            enemy.MoveTo(_point, enemy.Data.walkSpeed * 1.3f);
            enemy.FaceMovement(dt);
            Vector3 to = _point - enemy.transform.position;
            to.y = 0f;
            if (to.magnitude < 1f || Elapsed > 12f) { _arrived = true; _arrivedAt = Time.time; enemy.Stop(); }
            return;
        }
        // Looks around, then goes back to its post
        enemy.Face(Quaternion.Euler(0f, Mathf.Sin((Time.time - _arrivedAt) * 1.7f) * 80f, 0f) * enemy.transform.forward, dt, 0.25f);
        if (Time.time - _arrivedAt > enemy.Data.investigateTime) stateMachine.ChangeState(enemy.ReturnState);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Chase: runs to the target inside its zone
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyChaseState : EnemyState
{
    public EnemyChaseState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        enemy.Anim.PlayLocomotion();
        if (enemy.NextAttackTime < Time.time) enemy.ScheduleNextAttack(-0.3f);
    }

    public override void Tick(float dt)
    {
        EnemyPerception p = enemy.Perception;
        if (!enemy.TargetAlive || !enemy.TargetInZone) { stateMachine.ChangeState(enemy.ReturnState); return; }
        if (!p.CanSee && p.TimeSinceSeen > enemy.Data.loseTime) { stateMachine.ChangeState(enemy.InvestigateState); return; }

        Vector3 goal = p.CanSee ? p.TargetFeet : p.LastKnownPosition;
        // An archer fights from as far as it sees (it shoots, and walks closer only when out of its band)
        float engage = enemy.IsRanged ? enemy.Data.sightRange : enemy.Data.preferredMax + 0.6f;
        if (p.CanSee && p.Distance <= engage) { stateMachine.ChangeState(enemy.CombatState); return; }

        enemy.MoveTo(goal, enemy.Data.runSpeed);
        enemy.FaceMovement(dt);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Combat: keeps its distance and slot, defends, and attacks when it holds a token
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyCombatState : EnemyState
{
    private PlayerState _lastTargetState;
    private float _nextMove;
    private bool _punishRolled;

    public EnemyCombatState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        enemy.Anim.PlayLocomotion(0.15f);
        _nextMove = 0f;
    }

    public override void Tick(float dt)
    {
        EnemyPerception p = enemy.Perception;
        EnemyData d = enemy.Data;
        if (!enemy.TargetAlive || !enemy.TargetInZone) { stateMachine.ChangeState(enemy.ReturnState); return; }
        if (!p.CanSee && p.TimeSinceSeen > d.loseTime) { stateMachine.ChangeState(enemy.InvestigateState); return; }

        float distance = p.Distance;
        Vector3 target = p.TargetFeet;
        enemy.FaceTarget(dt);

        if (React(distance)) return;

        if (enemy.IsRanged) { Ranged(distance, target); return; }

        // The opening the player leaves (a whiffed blow's recovery, a hit reaction): punish it
        bool open = TargetOpen();
        if (!open) _punishRolled = false;
        bool punish = open && !_punishRolled && distance <= MaxReach() + 0.3f;
        if (punish) { _punishRolled = true; punish = Random.value < d.punishChance; }

        if ((Time.time >= enemy.NextAttackTime || punish) && EnemyCoordinator.Request(enemy))
        {
            int attack = enemy.ChooseAttack(distance);
            if (attack >= 0)
            {
                enemy.AttackState.Prepare(attack);
                stateMachine.ChangeState(enemy.AttackState);
                return;
            }
            // Holding a token out of reach: close in
            enemy.MoveTo(target, d.runSpeed);
            return;
        }

        // Waiting: its slot around the target, in its distance band
        if (Time.time < _nextMove) return;
        _nextMove = Time.time + 0.25f;
        float yaw = EnemyCoordinator.SlotAngle(enemy, target) * Mathf.Deg2Rad;
        float band = Mathf.Lerp(d.preferredMin, d.preferredMax, 0.6f);
        Vector3 slot = target + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * band;
        if (distance < d.preferredMin) slot = enemy.transform.position - enemy.ToTarget * 1.5f; // too close: step back
        enemy.MoveTo(slot, d.strafeSpeed);
    }

    /// <summary>Guards or dodges once, when the target starts an attack in front of it.</summary>
    private bool React(float distance)
    {
        PlayerMovement target = enemy.Perception.TargetMovement;
        PlayerState s = target != null ? target.StateMachine?.CurrentState : null;
        bool started = s is PlayerAttackState && s != _lastTargetState;
        _lastTargetState = s;
        if (!started || distance > 3.2f) return false;
        Vector3 toMe = enemy.transform.position - target.transform.position;
        toMe.y = 0f;
        if (Vector3.Angle(target.transform.forward, toMe) > 60f) return false; // not aimed at it

        float roll = Random.value;
        if (roll < enemy.Data.dodgeChance) { stateMachine.ChangeState(enemy.DodgeState); return true; }
        if (roll < enemy.Data.dodgeChance + enemy.BlockChance) { stateMachine.ChangeState(enemy.BlockState); return true; }
        return false;
    }

    /// <summary>The target cannot defend now: recovering from its own blow, hurt, or landing hard.</summary>
    private bool TargetOpen()
    {
        PlayerMovement target = enemy.Perception.TargetMovement;
        if (target == null) return false;
        PlayerState s = target.StateMachine?.CurrentState;
        if (s == target.HurtState) return true;
        return s is PlayerAttackState attack && attack.Progress >= 0.7f;
    }

    private float MaxReach()
    {
        float r = 0f;
        foreach (EnemyAttack a in enemy.Data.attacks) r = Mathf.Max(r, a.reach);
        return r;
    }

    private void Ranged(float distance, Vector3 target)
    {
        EnemyData d = enemy.Data;
        if (distance < d.retreatDistance) { stateMachine.ChangeState(enemy.RepositionState); return; }
        if (Time.time >= enemy.NextAttackTime && enemy.Perception.CanSee && EnemyShootState.ClearShot(enemy) && EnemyCoordinator.Request(enemy))
        {
            stateMachine.ChangeState(enemy.ShootState);
            return;
        }
        // Keep the band: a little closer if too far, otherwise hold the post; with the line blocked, walk to
        // the nearest point beside it with a clear line to the target (behind cover it looks for an angle)
        if (Time.time < _nextMove) return;
        _nextMove = Time.time + 0.4f;
        bool clear = EnemyShootState.ClearShot(enemy);
        if (!clear && (_hasFiringPoint || FindFiringPoint(target)))
        {
            enemy.MoveTo(_firingPoint, d.runSpeed);
            Vector3 left = _firingPoint - enemy.transform.position;
            left.y = 0f;
            if (left.magnitude < 0.6f || Time.time > _firingPointUntil) _hasFiringPoint = false;
            return;
        }
        _hasFiringPoint = false;
        if (distance > d.preferredMax) enemy.MoveTo(target, d.walkSpeed);
        else enemy.Stop();
    }

    private Vector3 _firingPoint;
    private bool _hasFiringPoint;
    private float _firingPointUntil;
    private static readonly float[] Offsets = { 3f, -3f, 5f, -5f, 7f, -7f, 9f, -9f };

    /// <summary>The closest point beside the archer (on the NavMesh, in its zone) with a clear line to the target.</summary>
    private bool FindFiringPoint(Vector3 target)
    {
        EnemyPerception p = enemy.Perception;
        Vector3 side = Vector3.Cross(Vector3.up, enemy.ToTarget);
        Vector3 chest = p.TargetChest;
        foreach (float offset in Offsets)
        {
            Vector3 candidate = enemy.transform.position + side * offset;
            if (enemy.Zone != null && !enemy.Zone.Contains(candidate)) continue;
            if (!UnityEngine.AI.NavMesh.SamplePosition(candidate, out UnityEngine.AI.NavMeshHit hit, 1f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            Vector3 eye = hit.position + Vector3.up * p.EyeHeight;
            if (!p.Visible(eye, chest)) continue;
            _firingPoint = hit.position;
            _hasFiringPoint = true;
            _firingPointUntil = Time.time + 4f;
            return true;
        }
        return false;
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Attack: wind-up → strike (the measured window) → recovery, chaining its combo
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyAttackState : EnemyState
{
    public enum Phase { Windup, Strike, Recovery }

    private int _index;
    private EnemyAttack _attack;
    private int _hash;
    private float _hold, _recoverUntil, _waitClip;
    private bool _struck, _closed, _clipDone;

    public EnemyAttackState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    /// <summary>The attack being played.</summary>
    public EnemyAttack Attack => _attack;
    public Phase Current { get; private set; }

    /// <summary>A heavy blow between its wind-up and its strike: only a heavy hit interrupts it (hyper armor).</summary>
    public bool Committed => _attack != null && _attack.heavy && Current != Phase.Recovery;

    /// <summary>Normalized clip time of the attack (−1 before it starts).</summary>
    public float Progress => _attack != null ? enemy.Anim.Progress(_hash) : -1f;

    /// <summary>Chooses the attack for the next Enter.</summary>
    public void Prepare(int index) => _index = index;

    public override void Enter()
    {
        base.Enter();
        enemy.NotifyOpener(_index);
        Begin(_index);
    }

    private void Begin(int index)
    {
        _index = index;
        _attack = enemy.Data.attacks[index];
        _hash = Animator.StringToHash(_attack.state);
        _hold = enemy.WindupHold(_attack);
        _struck = _closed = _clipDone = false;
        _waitClip = Time.time + 1f;
        Current = Phase.Windup;
        startTime = Time.time;
        enemy.Stop();
        enemy.Anim.RootMotion = _attack.rootMotion; // a dash or a charge moves with its clip
        enemy.Anim.Play(_hash, 0.08f, _attack.rate);
        enemy.NotifyAttack(_attack);
    }

    public override void Tick(float dt)
    {
        float p = Progress;
        if (p < 0f)
        {
            if (Time.time > _waitClip) End(); // the clip never started: give up
            return;
        }

        switch (Current)
        {
            case Phase.Windup:
                enemy.FaceTarget(dt, 1.2f);
                // A heavy blow holds its wind-up just before the strike: the telegraph
                if (_hold > 0f && p >= _attack.activeStart * 0.85f)
                {
                    enemy.Anim.SetSpeed(_attack.rate * 0.12f);
                    _hold -= dt;
                    if (_hold <= 0f) enemy.Anim.SetSpeed(_attack.rate);
                }
                if (p >= _attack.activeStart)
                {
                    Current = Phase.Strike;
                    if (enemy.Weapon != null) enemy.Weapon.BeginStrike(_attack.damage, _attack.bodyHit);
                    _struck = true;
                }
                break;

            case Phase.Strike:
                enemy.FaceTarget(dt, 0.25f); // committed: barely tracks
                float window = Mathf.Max(0.05f, (_attack.activeEnd - _attack.activeStart) * _attack.length / Mathf.Max(0.1f, _attack.rate));
                if (_attack.lunge > 0f) enemy.Shift(enemy.transform.forward * (_attack.lunge * dt / window));
                if (p >= _attack.activeEnd)
                {
                    Current = Phase.Recovery;
                    if (enemy.Weapon != null) enemy.Weapon.EndStrike();
                    _closed = true;
                }
                break;

            case Phase.Recovery:
                if (!_clipDone && p >= 0.97f)
                {
                    _clipDone = true;
                    _recoverUntil = Time.time + _attack.vulnerableAfter;
                    enemy.Anim.PlayLocomotion(0.15f);
                }
                if (!_clipDone) break;
                // Its combo: the next blow if the target is still in its reach
                if (_attack.next >= 0 && Time.time >= _recoverUntil - _attack.vulnerableAfter)
                {
                    EnemyAttack next = enemy.Data.attacks[_attack.next];
                    if (enemy.TargetAlive && enemy.Perception.Distance <= next.reach + 0.1f && Random.value < 0.8f)
                    {
                        Begin(_attack.next);
                        return;
                    }
                }
                if (Time.time >= _recoverUntil) End();
                break;
        }
    }

    private void End()
    {
        EnemyCoordinator.Release(enemy);
        enemy.ScheduleNextAttack();
        stateMachine.ChangeState(enemy.TargetAlive ? (EnemyState)enemy.CombatState : enemy.ReturnState);
    }

    public override void Exit()
    {
        if (enemy.Weapon != null) enemy.Weapon.EndStrike();
        enemy.Anim.RootMotion = false;
        enemy.Anim.SetSpeed(1f);
        if (stateMachine.CurrentState != this) EnemyCoordinator.Release(enemy);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Shoot (archer): draws, aims and releases an arrow at the clip's release
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyShootState : EnemyState
{
    private EnemyAttack _shot;
    private int _hash;
    private bool _released;

    public EnemyShootState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    private static int _enemyMask = -1, _arrowMask = -1;
    private static int EnemyMask => _enemyMask >= 0 ? _enemyMask : (_enemyMask = LayerMask.GetMask("Enemy"));
    private static int ArrowMask => _arrowMask >= 0 ? _arrowMask : (_arrowMask = LayerMask.GetMask("Player", "Ground", "Obstacle"));

    /// <summary>
    /// A clear line from the bow to the target's chest: nothing solid and no other enemy in the way (an
    /// archer does not shoot into a wall or an ally).
    /// </summary>
    public static bool ClearShot(Enemy enemy)
    {
        EnemyPerception p = enemy.Perception;
        if (p.Target == null) return false;
        Vector3 from = enemy.BowMuzzle != null ? enemy.BowMuzzle.position : enemy.transform.position + Vector3.up * 1.5f;
        Vector3 to = p.TargetChest;
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.1f) return true;
        int mask = p.BlockingLayers.value | EnemyMask;
        if (!Physics.Raycast(from, d / length, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform.IsChildOf(enemy.transform) &&
               !Physics.Raycast(hit.point + d / length * 0.05f, d / length, length - hit.distance - 0.05f, mask, QueryTriggerInteraction.Ignore);
    }

    public override void Enter()
    {
        base.Enter();
        _shot = enemy.Data.attacks.Length > 0 ? enemy.Data.attacks[0] : null;
        _hash = Animator.StringToHash(_shot != null ? _shot.state : EnemyAnimator.ShootName);
        _released = false;
        enemy.Stop();
        enemy.Anim.Play(_hash, 0.1f, _shot != null ? _shot.rate : 1f);
    }

    public override void Tick(float dt)
    {
        enemy.FaceTarget(dt, 1.5f);
        float p = enemy.Anim.Progress(_hash);
        float release = _shot != null ? _shot.activeStart : 0.6f;
        if (!_released && p >= release)
        {
            _released = true;
            Release();
        }
        if (p >= 0.97f || Elapsed > 3f)
        {
            EnemyCoordinator.Release(enemy);
            enemy.ScheduleNextAttack();
            stateMachine.ChangeState(enemy.CombatState);
        }
    }

    private void Release()
    {
        if (!ClearShot(enemy)) return; // the target moved behind cover: no wasted shot
        Arrow arrow = enemy.TakeArrow();
        if (arrow == null) return;
        EnemyData d = enemy.Data;
        Vector3 from = enemy.BowMuzzle != null ? enemy.BowMuzzle.position : enemy.transform.position + Vector3.up * 1.5f;
        Vector3 aim = enemy.Perception.TargetChest;
        // Lead the target a little by its velocity over the flight
        PlayerMovement m = enemy.Perception.TargetMovement;
        if (m != null) aim += new Vector3(m.Velocity.x, 0f, m.Velocity.z) * ((aim - from).magnitude / d.arrowSpeed) * 0.6f;
        arrow.transform.position = from;
        arrow.Launch(aim - from, d.arrowSpeed, d.arrowDamage, ArrowMask, enemy.transform);
        enemy.NotifyShot();
    }

    public override void Exit()
    {
        if (stateMachine.CurrentState != this) EnemyCoordinator.Release(enemy);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Block: the guard up for a moment (heavy warrior, bosses)
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyBlockState : EnemyState
{
    private float _duration;
    private int _healthAtStart;

    public EnemyBlockState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        enemy.Stop();
        enemy.Guarding = true;
        _duration = Random.Range(0.6f, 1.1f);
        _healthAtStart = enemy.Health.CurrentHealth;
        enemy.Anim.Play(EnemyAnimator.Block, 0.08f);
    }

    public override void Tick(float dt)
    {
        enemy.FaceTarget(dt, 1.5f);
        if (Elapsed < _duration) return;
        // A blocked blow is answered at once by a boss (riposte)
        bool blockedSomething = enemy.Health.CurrentHealth < _healthAtStart;
        if (blockedSomething && enemy.IsBoss) enemy.NextAttackTime = Time.time;
        stateMachine.ChangeState(enemy.CombatState);
    }

    public override void Exit() => enemy.Guarding = false;
}

// ────────────────────────────────────────────────────────────────────────────────
// Dodge: a roll away from the target's blow (light warrior, archer, bosses)
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyDodgeState : EnemyState
{
    private const float Duration = 0.5f, Distance = 2.2f, IFrames = 0.2f;
    private Vector3 _direction;

    public EnemyDodgeState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        enemy.Stop();
        Vector3 away = -enemy.ToTarget;
        Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? -1f : 1f);
        _direction = (away * 0.4f + side).normalized;
        Vector3 local = enemy.transform.InverseTransformDirection(_direction);
        enemy.Anim.SetDodge(new Vector2(local.x, local.z));
        enemy.Anim.Play(EnemyAnimator.Dodge, 0.05f);
        enemy.Health.ActivateIFrames(IFrames);
    }

    public override void Tick(float dt)
    {
        // Fast at first, slowing to a stop (like the player's dodge)
        float t = Mathf.Clamp01(Elapsed / Duration);
        float v0 = 1.5f * Distance / Duration;
        enemy.Shift(_direction * (v0 * (1f - t * t) * dt));
        if (Elapsed >= Duration)
        {
            enemy.NextAttackTime = Mathf.Min(enemy.NextAttackTime, Time.time + 0.2f); // a counter right after
            stateMachine.ChangeState(enemy.CombatState);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Hit: flinch, heavy hit or knockback; the attack (and its token) is lost
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyHitState : EnemyState
{
    private Vector3 _source;
    private bool _heavy;
    private int _hash;
    private Vector3 _push;
    private float _pushTime;

    public EnemyHitState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    /// <summary>The blow that caused it (where from, heavy or not).</summary>
    public void Prepare(Vector3 source, bool heavy)
    {
        _source = source;
        _heavy = heavy;
    }

    /// <summary>The reaction played (tests): Hit, HitHeavy or Knockback.</summary>
    public int Reaction => _hash;

    public override void Enter()
    {
        base.Enter();
        EnemyCoordinator.Release(enemy);
        if (enemy.Weapon != null) enemy.Weapon.EndStrike();
        enemy.Guarding = false;
        enemy.Stop();
        Vector3 away = enemy.transform.position - _source;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -enemy.transform.forward;
        bool big = enemy.Data.kind == EnemyKind.Heavy || enemy.IsBoss;
        // GDD §5.7: the heavy attack knocks the enemy back (less the big ones)
        _hash = !_heavy ? EnemyAnimator.Hit : big ? EnemyAnimator.HitHeavy : EnemyAnimator.Knockback;
        _push = away * (!_heavy ? 0.25f : big ? 0.5f : 1.2f);
        _pushTime = !_heavy ? 0.15f : 0.35f;
        enemy.Face(-away, 1f, 1000f); // facing the blow
        enemy.Anim.Play(_hash, 0.05f);
    }

    public override void Tick(float dt)
    {
        if (Elapsed < _pushTime) enemy.Shift(_push * (dt / _pushTime));
        float p = enemy.Anim.Progress(_hash);
        if ((p >= 0.8f && Elapsed > 0.25f) || Elapsed > 1.6f)
        {
            enemy.ScheduleNextAttack(0.2f);
            stateMachine.ChangeState(enemy.CombatState);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Reposition: the archer backs away to its range (or to a post above); a crowded melee enemy moves aside
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyRepositionState : EnemyState
{
    private Vector3 _goal;

    public EnemyRepositionState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    /// <summary>Where it goes (tests).</summary>
    public Vector3 Goal => _goal;

    public override void Enter()
    {
        base.Enter();
        enemy.Anim.PlayLocomotion(0.15f);
        EnemyCoordinator.Release(enemy);
        Vector3 target = enemy.Perception.Target != null ? enemy.Perception.TargetFeet : enemy.transform.position;
        _goal = enemy.transform.position - enemy.ToTarget * enemy.Data.preferredMin;
        // A post (high ground) far from the target, if it has some
        float best = float.MinValue;
        foreach (Transform post in enemy.ArcherPosts)
        {
            if (post == null) continue;
            float score = Vector3.Distance(post.position, target) - Vector3.Distance(post.position, enemy.transform.position) * 0.5f;
            if (Vector3.Distance(post.position, target) < enemy.Data.retreatDistance + 1f) continue;
            if (score > best) { best = score; _goal = post.position; }
        }
    }

    public override void Tick(float dt)
    {
        enemy.MoveTo(_goal, enemy.Data.runSpeed);
        enemy.FaceMovement(dt);
        Vector3 to = _goal - enemy.transform.position;
        to.y = 0f;
        if (to.magnitude < 0.7f || Elapsed > 3.5f) stateMachine.ChangeState(enemy.CombatState);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Return: back to its post (the target left the zone or was lost)
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyReturnState : EnemyState
{
    public EnemyReturnState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        EnemyCoordinator.Release(enemy);
        EnemyCoordinator.StopWaiting(enemy);
        enemy.Anim.PlayLocomotion();
        enemy.Guarding = false;
    }

    public override void Tick(float dt)
    {
        EnemyPerception p = enemy.Perception;
        if (p.CanSee && enemy.TargetInZone && enemy.TargetAlive && Elapsed > 0.5f) { stateMachine.ChangeState(enemy.ChaseState); return; }
        enemy.MoveTo(enemy.Post, enemy.Data.walkSpeed);
        enemy.FaceMovement(dt);
        Vector3 to = enemy.Post - enemy.transform.position;
        to.y = 0f;
        if (to.magnitude < 0.5f)
        {
            p.Forget();
            stateMachine.ChangeState(enemy.IdleState);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Dead: falls, stops fighting, lets the player through, and is removed later
// ────────────────────────────────────────────────────────────────────────────────

public class EnemyDeadState : EnemyState
{
    /// <summary>Seconds the body stays before it is removed.</summary>
    public const float BodyTime = 8f;

    public EnemyDeadState(Enemy enemy, EnemyStateMachine sm) : base(enemy, sm) { }

    public override void Enter()
    {
        base.Enter();
        EnemyCoordinator.Unregister(enemy);
        if (enemy.Weapon != null) enemy.Weapon.EndStrike();
        enemy.Guarding = false;
        enemy.Stop();
        enemy.Agent.enabled = false;
        foreach (Collider c in enemy.GetComponentsInChildren<Collider>()) c.enabled = false;
        enemy.Anim.Play(EnemyAnimator.Death, 0.1f);
    }

    public override void Tick(float dt)
    {
        if (Elapsed > BodyTime) enemy.gameObject.SetActive(false);
    }
}
