using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// The enemy and boss sections of the Play Mode test (P40). The enemies are spawned on the empty test pad
    /// of S13 (its own zone and a 3 m wall) and removed after each case; the bosses are the ones of S14.
    /// Each case measures what the GDD and the brief ask: perception (sight, hearing, walls), the differences
    /// between archetypes (speed, telegraph, damage, poise), the strike windows (only while the measured
    /// window is open, once per target, never through a wall), the reactions and the death, the archer
    /// (range, distance kept, never through cover), the attack tokens and the arenas and patterns of the
    /// bosses. The randomness of the AI is handled by measuring over time or over many choices, never by one
    /// roll.
    /// </summary>
    internal static partial class ParkourPlayModeTest
    {
        private static EnemyZone _padZone;
        private static HealthSystem _playerHealth;
        private static readonly List<Enemy> Spawned = new List<Enemy>();

        /// <summary>A point of the test pad (feet), relative to its centre (+Z toward the lanes).</summary>
        private static Vector3 Pad(float x, float z) =>
            new Vector3(ParkourTestCircuitBuilder.PadX + x, Floor, ParkourTestCircuitBuilder.PadZ + z);

        /// <summary>The player's root standing with its feet at <paramref name="feet"/>.</summary>
        private static Vector3 Body(Vector3 feet) => new Vector3(feet.x, Floor + OriginAboveFeet, feet.z);

        private static Enemy SpawnEnemy(string prefabPath, Vector3 feet, float yaw, EnemyZone zone, bool noEvasion = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var go = Object.Instantiate(prefab, feet, Quaternion.Euler(0f, yaw, 0f));
            var enemy = go.GetComponent<Enemy>();
            enemy.SetZone(zone);
            if (noEvasion)
            {
                // A copy of its data (never the asset: a change in Play Mode would stay in it) without dodges or guards
                EnemyData copy = Object.Instantiate(enemy.Data);
                copy.dodgeChance = 0f;
                copy.blockChance = 0f;
                enemy.Configure(copy, zone, null, null, null);
            }
            Spawned.Add(enemy);
            return enemy;
        }

        private static IEnumerator ClearEnemies()
        {
            foreach (Enemy e in Spawned) if (e != null) Object.Destroy(e.gameObject);
            Spawned.Clear();
            Keys();
            yield return 0.3f;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>Back to full health, no invulnerability left over, standing.</summary>
        private static IEnumerator FreshPlayer()
        {
            Keys();
            for (float t = 0f; t < 8f && (_playerHealth.IsDead || Current == _movement.DeadState); t += 0.1f) yield return 0.1f;
            _playerHealth.Revive();
            yield return 0.1f;
        }

        // ─── What one enemy did, sampled every frame ─────────────────────────────────

        private sealed class EnemyLog : System.IDisposable
        {
            private readonly Enemy _enemy;
            private readonly HealthSystem _target;
            public readonly List<string> AttackNames = new List<string>();
            public readonly List<float> AttackTimes = new List<float>();
            public readonly List<int> Damage = new List<int>();
            public readonly List<string> HitPhases = new List<string>();
            public int HitsThisAttack, MaxHitsPerAttack, HitsOutsideWindow;
            public float MinTelegraph = float.MaxValue, MaxTelegraph, MinRecovery = float.MaxValue;
            public readonly List<string> Strikes = new List<string>();
            public float MinGap = float.MaxValue, TipForward, HipsHeight;
            public string GapAt = "";
            private float _attackStart = -1f, _strikeEnd = -1f;
            private bool _wasActive, _wasAttacking;

            public EnemyLog(Enemy enemy, HealthSystem target)
            {
                _enemy = enemy;
                _target = target;
                enemy.AttackStarted += OnAttack;
                target.OnDamageReceived += OnDamage;
            }

            public int Attacks => AttackNames.Count;

            private void OnAttack(Enemy e, EnemyAttack attack)
            {
                AttackNames.Add(attack.name);
                AttackTimes.Add(Time.time);
                _attackStart = Time.time;
                HitsThisAttack = 0;
            }

            private void OnDamage(int amount, Vector3 source)
            {
                if ((source - _enemy.transform.position).sqrMagnitude > 0.25f) return; // another source
                Damage.Add(amount);
                HitsThisAttack++;
                MaxHitsPerAttack = Mathf.Max(MaxHitsPerAttack, HitsThisAttack);
                EnemyAttackState s = _enemy.AttackState;
                float p = s.Progress;
                EnemyAttack a = s.Attack;
                bool inside = a != null && _enemy.StateMachine.CurrentState == s && p >= a.activeStart - 0.02f && p <= a.activeEnd + 0.06f;
                if (!inside) HitsOutsideWindow++;
                HitPhases.Add(a != null ? $"{a.name} n={p:F2} ({a.activeStart:F2}–{a.activeEnd:F2})" : "sin ataque");
            }

            /// <summary>Every frame: when the strike window opens and closes, and when the attack ends.</summary>
            public void Sample()
            {
                bool attacking = _enemy.StateMachine.CurrentState == _enemy.AttackState;
                bool active = _enemy.Weapon != null && _enemy.Weapon.IsActive;
                if (active && !_wasActive && _attackStart >= 0f)
                {
                    Vector3 to = _target.transform.position - _enemy.transform.position;
                    to.y = 0f;
                    Strikes.Add($"{_enemy.AttackState.Attack?.name} a {to.magnitude:F2} m ({Vector3.Angle(_enemy.transform.forward, to):F0}°, alcance {_enemy.AttackState.Attack?.reach:F2})");
                    float telegraph = Time.time - _attackStart;
                    MinTelegraph = Mathf.Min(MinTelegraph, telegraph);
                    MaxTelegraph = Mathf.Max(MaxTelegraph, telegraph);
                }
                if (active && _enemy.Weapon.Grip != null)
                {
                    for (int k = 0; k <= 10; k++)
                    {
                        Vector3 q = Vector3.Lerp(_enemy.Weapon.Grip.position, _enemy.Weapon.Tip.position, k / 10f);
                        float y = q.y - _enemy.transform.position.y;
                        if (y < 0.4f || y > 1.8f) continue;
                        TipForward = Mathf.Max(TipForward, Vector3.Dot(q - _enemy.transform.position, _enemy.transform.forward));
                    }
                    Transform hips = _enemy.Anim.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    HipsHeight = hips.position.y - _enemy.transform.position.y;
                    // How close the blade came to the target's body (its capsule: axis from feet + r to head − r)
                    var cc = _target.GetComponent<CharacterController>();
                    Vector3 c = _target.transform.position + (cc != null ? cc.center : Vector3.zero);
                    float half = cc != null ? cc.height * 0.5f - cc.radius : 0.6f, r = cc != null ? cc.radius : 0.35f;
                    Vector3 a0 = c + Vector3.down * half, a1 = c + Vector3.up * half;
                    for (int k = 0; k <= 10; k++)
                    {
                        Vector3 q = Vector3.Lerp(_enemy.Weapon.Grip.position, _enemy.Weapon.Tip.position, k / 10f);
                        Vector3 onAxis = a0 + Vector3.Project(q - a0, a1 - a0);
                        if (Vector3.Dot(onAxis - a0, a1 - a0) < 0f) onAxis = a0;
                        if ((onAxis - a0).sqrMagnitude > (a1 - a0).sqrMagnitude) onAxis = a1;
                        float gap = Vector3.Distance(q, onAxis) - r;
                        if (gap < MinGap) { MinGap = gap; GapAt = $"{gap * 100f:F0} cm en k={k}, altura {q.y:F2}, punta a {Flat(_enemy.Weapon.Tip.position, _target.transform.position):F2} m del eje"; }
                    }
                }
                if (!active && _wasActive) _strikeEnd = Time.time;
                if (!attacking && _wasAttacking && _strikeEnd >= 0f) MinRecovery = Mathf.Min(MinRecovery, Time.time - _strikeEnd);
                _wasActive = active;
                _wasAttacking = attacking;
            }

            public void Dispose()
            {
                if (_enemy != null) _enemy.AttackStarted -= OnAttack;
                if (_target != null) _target.OnDamageReceived -= OnDamage;
            }
        }

        private static IEnumerator Observe(EnemyLog log, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.01f)
            {
                log.Sample();
                yield return 0.01f;
            }
        }

        // ─── Enemies (S13) ───────────────────────────────────────────────────────────

        private static IEnumerator Enemies()
        {
            _playerHealth = _movement.GetComponent<HealthSystem>();
            GameObject pad = GameObject.Find("Zona_Pruebas");
            _padZone = pad != null ? pad.GetComponent<EnemyZone>() : null;
            if (!Check(_padZone != null, "S13: la pista de pruebas de enemigos está en el área")) yield break;
            Check(NavMesh.SamplePosition(Pad(0f, 0f), out NavMeshHit _, 0.3f, NavMesh.AllAreas), "S13: la pista tiene NavMesh horneado");
            int placed = Object.FindObjectsByType<Enemy>().Length;
            Check(placed >= 7, $"S13 y S14: el encuentro mixto (2 ligeros, 1 pesado, 2 arqueros) y los 2 jefes están colocados ({placed} enemigos)");

            yield return EnemySight();
            yield return LightWarrior();
            yield return HeavyWarrior(); // compares its telegraph with the light one's
            yield return EnemyReactions();
            yield return EnemyThroughWalls();
            yield return Archer();
            yield return Coordination();
            yield return PlayerStrikesEnemy();
            yield return FreshPlayer();
        }

        // Sight in front, blind behind, hearing a run, and walls
        private static IEnumerator EnemySight()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 12f)), 180f);
            Enemy e = SpawnEnemy(EnemySetup.LightPrefab, Pad(0f, 2f), 0f, _padZone);
            float seen = -1f;
            for (float t = 0f; t < 2f; t += 0.05f)
            {
                if (e.IsEngaged) { seen = t; break; }
                yield return 0.05f;
            }
            Check(seen >= 0f && seen < 1.0f, $"Enemigo: ve al jugador de frente a 10 m y lo persigue (a los {seen:F2} s)");
            float reach = -1f, d = 99f;
            for (float t = 0f; t < 5f; t += 0.05f)
            {
                d = Flat(e.transform.position, _movement.transform.position);
                if (d <= e.Data.preferredMax + 0.8f) { reach = t; break; }
                yield return 0.05f;
            }
            Check(reach >= 0f, $"Enemigo: corre hasta su distancia de combate ({d:F2} m, banda {e.Data.preferredMin:F1}–{e.Data.preferredMax:F1} m) en {reach:F1} s");
            yield return ClearEnemies();

            yield return Teleport(Body(Pad(0f, 6f)), 180f);
            e = SpawnEnemy(EnemySetup.LightPrefab, Pad(0f, 0f), 180f, _padZone); // facing away from the player
            yield return 1.5f;
            Check(e.StateMachine.CurrentState == e.IdleState, $"Enemigo: no ve al jugador quieto 6 m detrás de él ({e.StateMachine.CurrentState?.Name})");
            Keys(Key.D);
            yield return 0.9f;
            Keys();
            EnemyState heard = null;
            for (float t = 0f; t < 1.5f && heard == null; t += 0.05f)
            {
                if (e.StateMachine.CurrentState != e.IdleState) heard = e.StateMachine.CurrentState;
                yield return 0.05f;
            }
            Check(heard != null, $"Enemigo: oye al jugador correr detrás de él a menos de {e.Data.hearingRange:F0} m ({heard?.Name ?? "sigue en guardia"})");
            yield return ClearEnemies();

            float wx = ParkourTestCircuitBuilder.PadWallX, wz = ParkourTestCircuitBuilder.PadWallZ;
            yield return Teleport(Body(new Vector3(wx, Floor, wz + 4f)), 180f);
            e = SpawnEnemy(EnemySetup.LightPrefab, new Vector3(wx, Floor, wz - 3f), 0f, _padZone);
            yield return 1.5f;
            Check(e.StateMachine.CurrentState == e.IdleState && !e.Perception.CanSee,
                  $"Enemigo: con el muro de 3 m en medio no ve al jugador de frente a 7 m ({e.StateMachine.CurrentState?.Name})");
            yield return ClearEnemies();
        }

        /// <summary>Seconds from the start of the light warrior's attack to its strike (measured by LightWarrior).</summary>
        private static float _lightTelegraph;

        // Light warrior: fast chained cuts, 12 per cut, inside the measured window, once per attack
        private static IEnumerator LightWarrior()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 4f)), 180f);
            Enemy e = SpawnEnemy(EnemySetup.LightPrefab, Pad(0f, 1.6f), 0f, _padZone);
            Visited.Clear();
            using (var log = new EnemyLog(e, _playerHealth))
            {
                yield return Observe(log, 6f);
                float interval = log.AttackTimes.Count > 1 ? (log.AttackTimes[log.AttackTimes.Count - 1] - log.AttackTimes[0]) / (log.AttackTimes.Count - 1) : 0f;
                Check(log.Attacks >= 3, $"Ligero: ataca seguido ({log.Attacks} ataques en 6 s, uno cada {interval:F2} s: {string.Join(", ", log.AttackNames)})");
                Check(log.AttackNames.Contains("Corte 2") || log.AttackNames.Contains("Corte 3"), "Ligero: encadena cortes (combo corte 1 → 2 → 3)");
                Check(log.Damage.Count >= 1 && log.Damage.TrueForAll(x => x == 12), $"Ligero: sus cortes hacen 12 de daño (GDD §12): [{string.Join(", ", log.Damage)}]");
                Check(log.MaxHitsPerAttack <= 1, $"Ligero: cada ataque golpea al jugador una sola vez (máximo {log.MaxHitsPerAttack})");
                Check(log.HitsOutsideWindow == 0, $"Ligero: solo golpea dentro de la ventana medida del clip ({string.Join("; ", log.HitPhases)})");
                Check(Visited.Contains(_movement.HurtState), "Ligero: el jugador reacciona a sus golpes");
                _lightTelegraph = log.MinTelegraph;
                Debug.Log($"{Tag} Ligero: del inicio del ataque al golpe {log.MinTelegraph:F2}–{log.MaxTelegraph:F2} s; recuperación mínima {log.MinRecovery:F2} s");
            }
            yield return ClearEnemies();
        }

        // Heavy warrior: slow, telegraphed, 25 per blow, open after it; light blows hurt but do not stop it
        private static IEnumerator HeavyWarrior()
        {
            float lightTelegraph = _lightTelegraph;
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 4f)), 180f);
            Enemy e = SpawnEnemy(EnemySetup.HeavyPrefab, Pad(0f, 1.3f), 0f, _padZone, true);
            Visited.Clear();
            bool armorChecked = false, armorHeld = false;
            int hpBefore = 0, hpAfter = 0;
            using (var log = new EnemyLog(e, _playerHealth))
            {
                for (float t = 0f; t < 9f; t += 0.01f)
                {
                    log.Sample();
                    // A light blow in the middle of its wind-up: it hurts, but the swing goes on (hyper armor)
                    if (!armorChecked && e.StateMachine.CurrentState == e.AttackState && e.AttackState.Committed &&
                        e.AttackState.Current == EnemyAttackState.Phase.Windup && e.AttackState.Progress > 0.05f)
                    {
                        armorChecked = true;
                        hpBefore = e.Health.CurrentHealth;
                        e.Health.TakeDamage(10, _movement.transform.position);
                        hpAfter = e.Health.CurrentHealth;
                        yield return 0.05f;
                        armorHeld = e.StateMachine.CurrentState == e.AttackState;
                        continue;
                    }
                    yield return 0.01f;
                }
                Check(log.Attacks >= 1, $"Pesado: ataca ({log.Attacks} ataques en 9 s: {string.Join(", ", log.AttackNames)})");
                Check(log.MinTelegraph >= 0.55f && log.MinTelegraph > lightTelegraph + 0.2f,
                      $"Pesado: sus golpes se anuncian: {log.MinTelegraph:F2}–{log.MaxTelegraph:F2} s del inicio al golpe (el ligero: {lightTelegraph:F2} s)");
                Check(log.Damage.Count >= 1 && log.Damage.TrueForAll(x => x == 25), $"Pesado: 25 de daño por golpe (GDD §12): [{string.Join(", ", log.Damage)}] (golpes: {string.Join("; ", log.Strikes)}; el arma más cerca del cuerpo: {log.GapAt}; la hoja llega {log.TipForward:F2} m adelante, cadera a {log.HipsHeight:F2} m, escala {e.transform.lossyScale.y:F2}/{e.Anim.Animator.transform.lossyScale.y:F2})");
                Check(log.HitsOutsideWindow == 0 && log.MaxHitsPerAttack <= 1, $"Pesado: golpea una vez y dentro de su ventana ({string.Join("; ", log.HitPhases)})");
                Check(log.MinRecovery < float.MaxValue && log.MinRecovery >= 0.5f, $"Pesado: queda abierto después de golpear ({log.MinRecovery:F2} s de recuperación)");
                Check(armorChecked && hpAfter < hpBefore && armorHeld,
                      $"Pesado: un golpe ligero le hace daño ({hpBefore} → {hpAfter}) pero no interrumpe su golpe pesado");
                Check(Visited.Contains(_movement.HurtState) && _movement.HurtState.Heavy, "Pesado: su golpe le da al jugador la reacción fuerte");
            }
            yield return ClearEnemies();
        }

        // Reactions and death
        private static IEnumerator EnemyReactions()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 21f)), 180f); // outside the pad's zone: they stay on guard
            Enemy light = SpawnEnemy(EnemySetup.LightPrefab, Pad(-3f, 0f), 0f, _padZone);
            Enemy heavy = SpawnEnemy(EnemySetup.HeavyPrefab, Pad(3f, 0f), 0f, _padZone);
            yield return 0.5f;

            Vector3 front = light.transform.position + light.transform.forward * 1.2f;
            light.Health.TakeDamage(10, front);
            yield return 0.05f;
            Check(light.StateMachine.CurrentState == light.HitState && light.HitState.Reaction == EnemyAnimator.Hit,
                  $"Reacción: un golpe ligero hace retroceder al ligero (Hit; estado {light.StateMachine.CurrentState?.Name})");
            yield return 1.6f;
            Vector3 before = light.transform.position;
            light.Health.TakeDamage(25, light.transform.position + light.transform.forward * 1.2f);
            yield return 0.05f;
            bool knock = light.StateMachine.CurrentState == light.HitState && light.HitState.Reaction == EnemyAnimator.Knockback;
            yield return 0.5f;
            float pushed = Flat(before, light.transform.position);
            Check(knock && pushed > 0.8f, $"Reacción: un golpe fuerte derriba al ligero (Knockback, empujado {pushed:F2} m)");

            int hp = heavy.Health.CurrentHealth;
            heavy.Health.TakeDamage(10, heavy.transform.position + heavy.transform.forward);
            yield return 0.05f;
            Check(heavy.Health.CurrentHealth == hp - 10 && heavy.StateMachine.CurrentState != heavy.HitState,
                  $"Reacción: un golpe ligero le hace daño al pesado ({hp} → {heavy.Health.CurrentHealth}) sin hacerlo retroceder (aguante {heavy.Data.poise})");
            yield return 0.2f;
            heavy.Health.TakeDamage(25, heavy.transform.position + heavy.transform.forward);
            yield return 0.05f;
            Check(heavy.StateMachine.CurrentState == heavy.HitState && heavy.HitState.Reaction == EnemyAnimator.HitHeavy,
                  "Reacción: un golpe fuerte sí hace retroceder al pesado (HitHeavy, sin derribarlo)");
            yield return 1.6f;

            // Guard: a blocked frontal blow keeps a share of its damage; from behind it gets through whole
            heavy.StateMachine.ChangeState(heavy.BlockState);
            hp = heavy.Health.CurrentHealth;
            heavy.Health.TakeDamage(10, heavy.transform.position + heavy.transform.forward);
            int blocked = hp - heavy.Health.CurrentHealth;
            yield return 0.15f;
            hp = heavy.Health.CurrentHealth;
            heavy.Health.TakeDamage(10, heavy.transform.position - heavy.transform.forward);
            int behind = hp - heavy.Health.CurrentHealth;
            Check(blocked == Mathf.RoundToInt(10 * heavy.Data.guardDamageKept) && behind == 10,
                  $"Guardia: de frente pasa {blocked} de 10, por la espalda {behind}");

            // Death: the fall, no more collisions or navigation, removed later
            light.Health.InstantKill();
            yield return 0.4f;
            bool collidersOff = true;
            foreach (Collider c in light.GetComponentsInChildren<Collider>()) collidersOff &= !c.enabled;
            Check(light.StateMachine.CurrentState == light.DeadState && light.Anim.Progress(EnemyAnimator.Death) >= 0f && collidersOff && !light.Agent.enabled,
                  "Muerte: cae con su animación, sin colisiones ni navegación");
            Check(!EnemyCoordinator.All.Contains(light), "Muerte: deja de contar para los turnos de ataque");
            yield return ClearEnemies();
        }

        // Nothing hits through a wall
        private static IEnumerator EnemyThroughWalls()
        {
            yield return FreshPlayer();
            float wx = ParkourTestCircuitBuilder.PadWallX, wz = ParkourTestCircuitBuilder.PadWallZ;
            // Control: the same cut at the same distance in the open reaches the player
            yield return Teleport(Body(Pad(-10f, 1.8f)), 180f);
            Enemy open = SpawnEnemy(EnemySetup.LightPrefab, Pad(-10f, 0f), 0f, _padZone);
            yield return 0.1f; // its Start (the AI begins on guard) runs first
            int hp = _playerHealth.CurrentHealth;
            open.AttackState.Prepare(0);
            open.StateMachine.ChangeState(open.AttackState);
            yield return 1.0f;
            bool openHit = _playerHealth.CurrentHealth < hp;
            yield return ClearEnemies();
            yield return FreshPlayer();

            yield return Teleport(Body(new Vector3(wx, Floor, wz + 0.25f + 0.45f)), 180f);
            Enemy walled = SpawnEnemy(EnemySetup.LightPrefab, new Vector3(wx, Floor, wz - 0.25f - 0.65f), 0f, _padZone);
            yield return 0.1f;
            hp = _playerHealth.CurrentHealth;
            walled.AttackState.Prepare(0);
            walled.StateMachine.ChangeState(walled.AttackState);
            bool opened = false;
            for (float t = 0f; t < 1.0f; t += 0.01f)
            {
                opened |= walled.Weapon.IsActive;
                yield return 0.01f;
            }
            Check(openHit, "Muros: en campo abierto, el mismo corte a 1.8 m alcanza al jugador (control)");
            Check(opened && _playerHealth.CurrentHealth == hp, $"Muros: con el muro de 0.5 m en medio el corte no atraviesa (vida {hp} → {_playerHealth.CurrentHealth})");
            Vector3 chest = _movement.transform.position + Vector3.up * 0.25f;
            var hitbox = _movement.GetComponent<Hitbox>();
            Check(hitbox != null && !hitbox.ClearPath(walled.transform.position + Vector3.up * 1.2f) && hitbox.ClearPath(chest + _movement.transform.forward * 0.3f),
                  "Muros: los golpes del jugador tampoco atraviesan un muro (comprobación de línea del Hitbox)");
            yield return ClearEnemies();
        }

        // Archer: range, a hit, keeps its distance, never shoots through cover, looks for a clear line
        private static IEnumerator Archer()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(-8f, 14f)), 180f);
            Enemy a = SpawnEnemy(EnemySetup.ArcherPrefab, Pad(-8f, -9f), 0f, _padZone); // clear of the pad's wall
            int arrowDamage = 0, arrowHits = 0;
            System.Action<int, Vector3> onDamage = (amount, src) => { arrowHits++; arrowDamage = amount; };
            _playerHealth.OnDamageReceived += onDamage;
            float minDistance = 99f;
            for (float t = 0f; t < 7f; t += 0.05f)
            {
                minDistance = Mathf.Min(minDistance, Flat(a.transform.position, _movement.transform.position));
                yield return 0.05f;
            }
            _playerHealth.OnDamageReceived -= onDamage;
            Check(a.Shots >= 2, $"Arquero: dispara desde lejos ({a.Shots} flechas en 7 s a {Flat(a.transform.position, _movement.transform.position):F1} m)");
            Check(arrowHits >= 1 && arrowDamage == 10, $"Arquero: sus flechas alcanzan al jugador quieto (10 de daño, GDD §12): {arrowHits} impactos de {arrowDamage}");
            Check(minDistance >= a.Data.retreatDistance, $"Arquero: no se acerca (mínimo {minDistance:F1} m)");

            // Approached: it backs away to its range
            yield return FreshPlayer();
            Vector3 ap = a.transform.position;
            yield return Teleport(Body(ap + Vector3.forward * 3f), 180f);
            float d0 = Flat(a.transform.position, _movement.transform.position);
            bool repositioned = false;
            for (float t = 0f; t < 3.5f; t += 0.05f)
            {
                repositioned |= a.StateMachine.CurrentState == a.RepositionState;
                yield return 0.05f;
            }
            float d1 = Flat(a.transform.position, _movement.transform.position);
            Check(repositioned && d1 > d0 + 1.5f, $"Arquero: si el jugador se le acerca a {d0:F1} m retrocede (a {d1:F1} m en 3.5 s)");
            yield return ClearEnemies();

            // Behind the wall: the line is blocked, so no arrow leaves (pinned in place)
            yield return FreshPlayer();
            float wx = ParkourTestCircuitBuilder.PadWallX, wz = ParkourTestCircuitBuilder.PadWallZ;
            yield return Teleport(Body(new Vector3(wx, Floor, wz + 1.5f)), 180f);
            a = SpawnEnemy(EnemySetup.ArcherPrefab, new Vector3(wx, Floor, wz - 9f), 0f, _padZone);
            yield return 0.3f;
            a.Engage();
            a.Agent.enabled = false; // pinned: it cannot walk to a clear line
            int hp = _playerHealth.CurrentHealth;
            Check(!EnemyShootState.ClearShot(a), "Arquero: con el muro en medio su línea de tiro está bloqueada");
            yield return 4f;
            Check(a.Shots == 0 && _playerHealth.CurrentHealth == hp, $"Arquero: con el muro en medio no dispara ({a.Shots} flechas en 4 s)");
            // Free to move, it looks for a clear line around the wall
            a.Agent.enabled = true;
            Vector3 start = a.transform.position;
            for (float t = 0f; t < 10f && a.Shots == 0; t += 0.1f) yield return 0.1f;
            Check(a.Shots >= 1 && Flat(start, a.transform.position) > 1.5f,
                  $"Arquero: se mueve hasta tener línea de tiro y dispara (se movió {Flat(start, a.transform.position):F1} m, {a.Shots} flechas)");
            yield return ClearEnemies();
        }

        // Attack tokens: never more than two melee attackers at once, and they spread around the player
        private static IEnumerator Coordination()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 0f)), 180f);
            _playerHealth.ActivateIFrames(30f); // the player stays standing while they take turns
            var group = new List<Enemy>();
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 4.5f;
                group.Add(SpawnEnemy(EnemySetup.LightPrefab, Pad(offset.x, offset.z), yaw + 180f, _padZone, true));
            }
            int maxHolding = 0, maxAttacking = 0;
            var attackers = new HashSet<Enemy>();
            float minSpread = 360f;
            for (float t = 0f; t < 8f; t += 0.05f)
            {
                maxHolding = Mathf.Max(maxHolding, EnemyCoordinator.Holding(false));
                int attacking = 0;
                foreach (Enemy e in group)
                {
                    if (e.StateMachine.CurrentState != e.AttackState) continue;
                    attacking++;
                    attackers.Add(e);
                }
                maxAttacking = Mathf.Max(maxAttacking, attacking);
                if (t > 3f) minSpread = Mathf.Min(minSpread, MinAngleAround(group, _movement.transform.position));
                yield return 0.05f;
            }
            Check(maxHolding <= EnemyCoordinator.MeleeTokens && maxAttacking <= EnemyCoordinator.MeleeTokens,
                  $"Coordinación: nunca atacan más de {EnemyCoordinator.MeleeTokens} a la vez (máximo {maxAttacking} atacando, {maxHolding} turnos)");
            Check(attackers.Count >= 3, $"Coordinación: los turnos se reparten ({attackers.Count} de 4 atacaron en 8 s)");
            Check(minSpread >= 35f, $"Coordinación: rodean al jugador en lugar de amontonarse (separación mínima {minSpread:F0}°)");
            _playerHealth.Revive();
            yield return ClearEnemies();
        }

        private static float MinAngleAround(List<Enemy> group, Vector3 center)
        {
            float min = 360f;
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    Vector3 a = group[i].transform.position - center, b = group[j].transform.position - center;
                    a.y = b.y = 0f;
                    min = Mathf.Min(min, Vector3.Angle(a, b));
                }
            return min;
        }

        // The player's own blows reach an enemy (the integration of the combat)
        private static IEnumerator PlayerStrikesEnemy()
        {
            yield return FreshPlayer();
            yield return Teleport(Body(Pad(0f, 4f)), 180f);
            Enemy e = SpawnEnemy(EnemySetup.HeavyPrefab, Pad(0f, 2.9f), 0f, _padZone, true);
            yield return 0.2f;
            int hp = e.Health.CurrentHealth;
            yield return Tap(Key.J);
            for (float t = 0f; t < 0.8f && e.Health.CurrentHealth == hp; t += 0.02f) yield return 0.02f;
            Check(e.Health.CurrentHealth == hp - 10, $"Integración: el jab del jugador le hace 10 al enemigo ({hp} → {e.Health.CurrentHealth})");
            yield return ClearEnemies();
        }

        // ─── Bosses (S14) ────────────────────────────────────────────────────────────

        private static IEnumerator Bosses()
        {
            _playerHealth = _movement.GetComponent<HealthSystem>();
            BossArena[] arenas = Object.FindObjectsByType<BossArena>();
            BossArena leader = null, commander = null;
            foreach (BossArena arena in arenas)
            {
                if (arena.Boss == null || arena.Boss.Data == null) continue;
                if (arena.Boss.Data.bossStyle == BossStyle.ClanLeader) leader = arena;
                if (arena.Boss.Data.bossStyle == BossStyle.Commander) commander = arena;
            }
            if (!Check(leader != null && commander != null, $"S14: dos arenas, la del líder del clan y la del Comandante ({arenas.Length})")) yield break;
            Check(leader.Boss.Health.MaxHealth == 300 && commander.Boss.Health.MaxHealth == 450,
                  $"Jefes: vida del GDD (líder {leader.Boss.Health.MaxHealth}, Comandante {commander.Boss.Health.MaxHealth})");

            yield return BossFight(leader);
            yield return BossFight(commander);
            yield return BossIdentity(leader.Boss, commander.Boss);
            yield return BossReset(commander);

            // The leader falls: its arena opens for good
            leader.Boss.Health.InstantKill();
            yield return 0.3f;
            Check(leader.Cleared && !leader.Closed && !Gate(leader).activeSelf, "Arena: al morir el jefe la puerta se abre y queda abierta");
            yield return FreshPlayer();
            yield return Teleport(Body(new Vector3(0f, Floor, 8f)), 180f);
        }

        private static GameObject Gate(BossArena arena) => arena.transform.parent.Find("Puerta").gameObject;

        /// <summary>Walks in: the gate closes, the boss comes; then a fight is watched while the player attacks now and then.</summary>
        private static IEnumerator BossFight(BossArena arena)
        {
            Enemy boss = arena.Boss;
            string who = boss.Data.enemyName;
            yield return FreshPlayer();
            Check(!arena.Closed && !Gate(arena).activeSelf, $"{who}: antes de entrar, la puerta de la arena está abierta");
            Vector3 c = arena.transform.position;
            yield return Teleport(Body(new Vector3(c.x, Floor, c.z + 6f)), 180f);
            _playerHealth.ActivateIFrames(30f);
            float closed = -1f, engaged = -1f;
            for (float t = 0f; t < 2f; t += 0.05f)
            {
                if (closed < 0f && arena.Closed) closed = t;
                if (engaged < 0f && boss.IsEngaged) engaged = t;
                yield return 0.05f;
            }
            Check(closed >= 0f && closed < 0.5f && Gate(arena).activeSelf, $"{who}: al entrar, la puerta se cierra detrás del jugador ({closed:F2} s)");
            Check(engaged >= 0f && engaged < 1f, $"{who}: el jefe va por el jugador ({engaged:F2} s)");

            using (var log = new EnemyLog(boss, _playerHealth))
            {
                int blocks = boss.Blocks, dodges = boss.Dodges;
                float pressed = -1f;
                for (float t = 0f; t < 16f; t += 0.01f)
                {
                    log.Sample();
                    // The player attacks now and then (a 60 ms tap of J every 1.5 s)
                    if (pressed < 0f && Mathf.Repeat(t, 1.5f) < 0.011f) { Keys(Key.J); pressed = t; }
                    else if (pressed >= 0f && t - pressed > 0.06f) { Keys(); pressed = -1f; }
                    yield return 0.01f;
                }
                Keys();
                var distinct = new HashSet<string>(log.AttackNames);
                Check(log.Attacks >= 4 && distinct.Count >= 3, $"{who}: ataca con un repertorio variado ({log.Attacks} ataques, {distinct.Count} distintos: {string.Join(", ", log.AttackNames)})");
                Check(boss.Blocks + boss.Dodges > blocks + dodges, $"{who}: se defiende de los ataques del jugador ({boss.Blocks - blocks} bloqueos, {boss.Dodges - dodges} esquivas)");
                Check(log.HitsOutsideWindow == 0, $"{who}: solo golpea dentro de sus ventanas medidas");
            }
            Check(arena.Closed, $"{who}: la puerta sigue cerrada mientras pelean");
            Check(boss.Zone == null || boss.Zone.Contains(boss.transform.position), $"{who}: no sale de su arena");
            _playerHealth.Revive();
        }

        /// <summary>
        /// The two identities, over many choices (the AI rolls): the leader opens with combos and lunges from
        /// afar; the Commander answers a player who blocks with blows that break the guard.
        /// </summary>
        /// <summary>Distances (m) the bosses' choices are sampled at: inside every blow's reach, and beyond the cuts' but inside the lunge's.</summary>
        private const float CloseRange = 1.8f, FarRange = 3.5f;

        private static IEnumerator BossIdentity(Enemy leader, Enemy commander)
        {
            const int n = 600;
            float LeaderCombos()
            {
                int combos = 0;
                for (int i = 0; i < n; i++)
                {
                    int a = leader.ChooseAttack(2.0f);
                    if (a >= 0 && leader.Data.attacks[a].next >= 0) combos++;
                }
                return combos / (float)n;
            }
            float Lunges(Enemy boss, float distance)
            {
                int lunges = 0, any = 0;
                for (int i = 0; i < n; i++)
                {
                    int a = boss.ChooseAttack(distance);
                    if (a < 0) continue;
                    any++;
                    if (boss.Data.attacks[a].rootMotion) lunges++;
                }
                return any > 0 ? lunges / (float)any : 0f;
            }
            float HeavyShare(Enemy boss)
            {
                int heavy = 0, any = 0;
                for (int i = 0; i < n; i++)
                {
                    int a = boss.ChooseAttack(CloseRange);
                    if (a < 0) continue;
                    any++;
                    if (boss.Data.attacks[a].heavy) heavy++;
                }
                return any > 0 ? heavy / (float)any : 0f;
            }
            float combo = LeaderCombos(), far = Lunges(leader, FarRange);
            Check(combo >= 0.3f, $"Líder: abre con combos ({combo * 100f:F0} % de sus aperturas a 2 m)");
            Check(far >= 0.9f, $"Líder: desde lejos ({FarRange:F1} m, fuera del alcance de sus cortes) se lanza con la estocada ({far * 100f:F0} %)");

            // The Commander learns that the player blocks
            float before = HeavyShare(commander);
            yield return FreshPlayer();
            BossArena arena = null;
            foreach (BossArena b in Object.FindObjectsByType<BossArena>()) if (b.Boss == commander) arena = b;
            Vector3 c = arena.transform.position;
            yield return Teleport(Body(new Vector3(c.x, Floor, c.z + 4f)), 180f);
            _playerHealth.ActivateIFrames(30f);
            yield return 1.0f;
            for (int i = 0; i < 4; i++)
            {
                Keys(Key.L);
                yield return 0.6f;
                Keys();
                yield return 0.4f;
            }
            float after = HeavyShare(commander);
            Check(commander.SeenBlocks >= 2 && after > before + 0.15f,
                  $"Comandante: se adapta al jugador que bloquea ({commander.SeenBlocks} bloqueos vistos): golpes que rompen la guardia del {before * 100f:F0} % al {after * 100f:F0} %");
            _playerHealth.Revive();
        }

        /// <summary>The player dies inside an arena: the gate opens, the boss goes back with full health.</summary>
        private static IEnumerator BossReset(BossArena arena)
        {
            Enemy boss = arena.Boss;
            if (!arena.Closed)
            {
                Vector3 c = arena.transform.position;
                yield return Teleport(Body(new Vector3(c.x, Floor, c.z + 6f)), 180f);
                yield return 0.5f;
            }
            boss.Health.TakeDamage(100, _movement.transform.position);
            int hurt = boss.Health.CurrentHealth;
            _playerHealth.Revive();
            _playerHealth.InstantKill();
            yield return 0.3f;
            Check(!arena.Closed && !Gate(arena).activeSelf, "Arena: si el jugador muere dentro, la puerta se abre");
            Check(boss.Health.CurrentHealth == boss.Health.MaxHealth && hurt < boss.Health.MaxHealth,
                  $"Arena: el jefe vuelve con la vida completa ({hurt} → {boss.Health.CurrentHealth})");
            Check(Flat(boss.transform.position, boss.Post) < 0.6f && boss.StateMachine.CurrentState == boss.IdleState,
                  "Arena: el jefe vuelve a su puesto en guardia");
            yield return FreshPlayer();
        }
    }
}
