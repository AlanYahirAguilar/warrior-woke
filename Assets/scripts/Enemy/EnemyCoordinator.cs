using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordination of the enemies around the player (P40), after the attack-token budgets of DOOM (2016) and
/// The Last of Us Part II: a player follows about one melee blow and a couple of shots at a time, so an
/// enemy must hold a token to attack and gives it back when its attack ends (or it is hit, or dies):
///  - melee: <see cref="MeleeTokens"/> at once; ranged: <see cref="RangedTokens"/> at once;
///  - a boss fights alone in its arena and always has its token;
///  - the melee enemies around the player get spread slots (angles around the player, kept by each one
///    while it waits), so they surround instead of piling up; the slots are re-dealt by angle, so nobody
///    has to cross the circle;
///  - an enemy that waited longest gets the next token (no one is left out), and a token is never held
///    longer than <see cref="TokenTimeout"/> (a stuck attacker gives it back).
/// One static registry; no allocations after warm-up.
/// </summary>
public static class EnemyCoordinator
{
    /// <summary>Melee attackers at once (one light blow and one more, at most).</summary>
    public const int MeleeTokens = 2;
    /// <summary>Archers shooting at once.</summary>
    public const int RangedTokens = 2;
    /// <summary>Longest a token is held (s).</summary>
    public const float TokenTimeout = 4f;

    private static readonly List<Enemy> Enemies = new List<Enemy>(16);
    private static readonly List<Enemy> Holders = new List<Enemy>(8);
    private static readonly Dictionary<Enemy, float> TokenTime = new Dictionary<Enemy, float>(16);
    private static readonly Dictionary<Enemy, float> WaitingSince = new Dictionary<Enemy, float>(16);
    private static readonly List<Enemy> Melee = new List<Enemy>(16);
    private static float _nextSlots;

    /// <summary>Enemies alive and registered (tests).</summary>
    public static IReadOnlyList<Enemy> All => Enemies;

    /// <summary>How many hold a token now, by kind (tests).</summary>
    public static int Holding(bool ranged)
    {
        int n = 0;
        foreach (Enemy e in Holders) if (e != null && e.IsRanged == ranged && !e.IsBoss) n++;
        return n;
    }

    public static void Register(Enemy enemy)
    {
        if (!Enemies.Contains(enemy)) Enemies.Add(enemy);
    }

    public static void Unregister(Enemy enemy)
    {
        Enemies.Remove(enemy);
        Release(enemy);
        WaitingSince.Remove(enemy);
    }

    /// <summary>
    /// Asks for an attack token. Granted if a token of its kind is free and no other enemy of its kind has
    /// been waiting clearly longer (fairness). Holding one already is a grant.
    /// </summary>
    public static bool Request(Enemy enemy)
    {
        ExpireTokens();
        if (Holders.Contains(enemy)) return true;
        if (enemy.IsBoss) { Grant(enemy); return true; }
        if (!WaitingSince.ContainsKey(enemy)) WaitingSince[enemy] = Time.time;
        int held = Holding(enemy.IsRanged), budget = enemy.IsRanged ? RangedTokens : MeleeTokens;
        if (held >= budget) return false;
        // Fairness: another of its kind, engaged, waiting 1 s longer goes first
        float mine = WaitingSince[enemy];
        foreach (var pair in WaitingSince)
        {
            Enemy other = pair.Key;
            if (other == enemy || other == null || other.IsRanged != enemy.IsRanged || !other.IsEngaged || Holders.Contains(other)) continue;
            if (pair.Value < mine - 1f) return false;
        }
        Grant(enemy);
        return true;
    }

    /// <summary>Gives the token back (the attack ended, the enemy was hit or died).</summary>
    public static void Release(Enemy enemy)
    {
        Holders.Remove(enemy);
        TokenTime.Remove(enemy);
    }

    /// <summary>The enemy no longer waits for a token (it left the fight).</summary>
    public static void StopWaiting(Enemy enemy) => WaitingSince.Remove(enemy);

    public static bool HasToken(Enemy enemy) => Holders.Contains(enemy);

    private static void Grant(Enemy enemy)
    {
        Holders.Add(enemy);
        TokenTime[enemy] = Time.time;
        WaitingSince.Remove(enemy);
    }

    private static void ExpireTokens()
    {
        for (int i = Holders.Count - 1; i >= 0; i--)
        {
            Enemy e = Holders[i];
            if (e == null || !e.isActiveAndEnabled || e.IsDead || Time.time - TokenTime[e] > TokenTimeout)
            {
                TokenTime.Remove(e);
                Holders.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The angle (°, around the target, world yaw) this melee enemy should wait at: the engaged melee enemies
    /// sorted by their current angle get evenly spread slots (at least 60° apart), re-dealt twice a second.
    /// </summary>
    public static float SlotAngle(Enemy enemy, Vector3 target)
    {
        if (Time.time >= _nextSlots) DealSlots(target);
        return enemy.SlotYaw;
    }

    private static void DealSlots(Vector3 target)
    {
        _nextSlots = Time.time + 0.5f;
        Melee.Clear();
        foreach (Enemy e in Enemies)
            if (e != null && !e.IsDead && e.IsEngaged && !e.IsRanged && !e.IsBoss) Melee.Add(e);
        if (Melee.Count == 0) return;
        _sortTarget = target;
        Melee.Sort(ByAngle);
        float step = Mathf.Max(60f, 360f / Melee.Count);
        // Keep the arc centred where they already are: the first one stays, the others spread from it
        float start = Yaw(Melee[0].transform.position - target);
        for (int i = 0; i < Melee.Count; i++) Melee[i].SlotYaw = start + step * i;
    }

    private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    private static Vector3 _sortTarget;
    private static readonly System.Comparison<Enemy> ByAngle = (a, b) =>
        Yaw(a.transform.position - _sortTarget).CompareTo(Yaw(b.transform.position - _sortTarget));

    /// <summary>Starts empty in every Play session (also with Enter Play Mode Options skipping the domain reload).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => Clear();

    /// <summary>Forgets everything (a scene load, the tests).</summary>
    public static void Clear()
    {
        Enemies.Clear();
        Holders.Clear();
        TokenTime.Clear();
        WaitingSince.Clear();
        _nextSlots = 0f;
    }
}
