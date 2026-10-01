using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Automatic Play Mode test of the player on the parkour circuit of Level-1. It enters Play Mode,
    /// adds a virtual keyboard and drives the real input path (Input System → PlayerInputHandler →
    /// FSM), then checks movement, backpedal, sprint, jump/fall/landing, vault, slide, auto step,
    /// ledge, combat, dodge, block and that no errors were logged.
    /// Menu: Tools → Warrior Woke → Probar Personaje en Play Mode.
    /// Batch: -executeMethod WarriorWoke.EditorTools.ParkourPlayModeTest.RunBatch (do not pass -quit).
    /// </summary>
    [InitializeOnLoad]
    internal static class ParkourPlayModeTest
    {
        private const string ScenePath       = "Assets/Scenes/Level-1.unity";
        private const string RunningKey      = "WW_PlayModeTest_Running";
        private const string FailsKey        = "WW_PlayModeTest_Fails";
        private const string Tag             = "[PlayModeTest]";
        private const float  OriginAboveFeet = 0.99f;                            // CapsuleCollider half height of Player.prefab
        private const float  Slab            = ParkourTestCircuitBuilder.SlabTop; // circuit floor height
        private const float  CorridorX       = 17f;                              // free lane for general tests
        private const float  Z               = ParkourTestCircuitBuilder.ZOffset; // circuit shift along Z

        private static readonly Stack<IEnumerator> Routines = new Stack<IEnumerator>();
        private static readonly List<PlayerState>  Visited  = new List<PlayerState>();
        private static float          _waitUntil;
        private static int            _fails;
        private static int            _errors;
        private static Keyboard       _keyboard;
        private static PlayerMovement _movement;
        private static Animator       _animator;

        static ParkourPlayModeTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Warrior Woke/Probar Personaje en Play Mode")]
        private static void RunMenu() => Start();

        public static void RunBatch() => Start();

        private static void Start()
        {
            SessionState.SetBool(RunningKey, true);
            SessionState.SetInt(FailsKey, 0);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                _fails = 0;
                _errors = 0;
                Application.logMessageReceived += OnLog;
                Routines.Clear();
                Routines.Push(Run());
                _waitUntil = 0f;
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseBool(RunningKey);
                int fails = SessionState.GetInt(FailsKey, 1);
                Debug.Log($"{Tag} {(fails == 0 ? "TODAS LAS PRUEBAS OK" : fails + " PRUEBA(S) FALLARON")}");
                if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || Routines.Count == 0) return;
            if (Time.time < _waitUntil) return;

            // Runs nested coroutines: a yielded IEnumerator runs to completion before its parent continues.
            while (Routines.Count > 0)
            {
                IEnumerator top = Routines.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"{Tag} Excepción en la prueba: {e}");
                    _fails++;
                    Routines.Clear();
                    break;
                }

                if (!more) { Routines.Pop(); continue; }
                if (top.Current is IEnumerator nested) { Routines.Push(nested); continue; }
                if (top.Current is float seconds) _waitUntil = Time.time + seconds;
                return;
            }

            Finish();
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Routines.Clear();
            Check(_errors == 0, $"Sin errores ni excepciones en consola ({_errors})");
            SessionState.SetInt(FailsKey, _fails);
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            EditorApplication.ExitPlaymode();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(Tag))
                _errors++;
        }

        // ─── Scenarios ───────────────────────────────────────────────────────────────

        private static IEnumerator Run()
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _keyboard.MakeCurrent();
            Keys();

            yield return 2f; // spawn from the pool and fall to the ground
            _movement = Player.Instance != null ? Player.Instance.GetComponent<PlayerMovement>() : null;
            if (!Check(_movement != null, "El Player aparece en la escena")) yield break;
            _animator = _movement.GetComponentInChildren<Animator>();
            _movement.StateChanged += s => Visited.Add(s);

            // 1. Idle on the ground (the spawn stands on the Ground dome)
            Check(Current == _movement.IdleState && _movement.IsGrounded && Mathf.Abs(Feet - GroundBelow()) < 0.1f,
                  $"Idle apoyado en el suelo (pies a {Feet:F2} m, suelo a {GroundBelow():F2} m)");

            // 1b. From the spawn, walking south reaches the circuit (the player tests it by hand)
            Vector3 spawn = _movement.transform.position;
            yield return Teleport(spawn);
            Check((spawn - ParkourTestCircuitBuilder.SpawnPosition).sqrMagnitude < 4f, $"El jugador aparece en la entrada del circuito ({spawn})");
            Keys(Key.W);
            yield return 0.8f; // stop before the AutoStep stairs, which start at z = -18.6 on this lane
            float reachedZ = _movement.transform.position.z;
            float reachedFeet = Feet;
            Keys();
            Check(reachedZ < -15f && Mathf.Abs(reachedFeet - Slab) < 0.1f,
                  $"Desde el spawn camina y sube a la losa del circuito (z = {reachedZ:F2}, pies a {reachedFeet:F2} m; losa a {Slab})");
            yield return 0.8f;

            // 2. Acceleration Idle → Walk → Run, then braking (straight corridor on the slab)
            yield return Teleport(Corridor);
            Keys(Key.W);
            yield return 0.1f;
            float early = Speed;
            yield return 1.2f;
            float cruise = Speed;
            Check(early > 0.2f && early < 2.6f, $"Arranque gradual: a 0.1 s va a {early:F2} m/s (zona Walk/Jog del blend)");
            Check(Mathf.Abs(cruise - _movement.BaseSpeed) < 0.3f, $"Correr a {cruise:F2} m/s (BaseSpeed {_movement.BaseSpeed})");
            Check(AnimSpeed > 0.8f, $"Parámetro Speed del Animator en carrera: {AnimSpeed:F2}");
            Keys();
            yield return 0.12f;
            float braking = Speed;
            yield return 1f;
            Check(braking > 0.3f && braking < cruise, $"Frenado gradual: a 0.12 s va a {braking:F2} m/s");
            Check(Current == _movement.IdleState && Speed < 0.05f, "Vuelve a Idle y se detiene");

            // 3. Backpedal: walks backward facing forward
            yield return Teleport(new Vector3(CorridorX, Slab + OriginAboveFeet, -20f + Z));
            float yaw0 = _movement.transform.eulerAngles.y;
            Vector3 p0 = _movement.transform.position;
            Keys(Key.S);
            yield return 1.2f;
            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(yaw0, _movement.transform.eulerAngles.y));
            float backDist = Vector3.Dot(_movement.transform.position - p0, -_movement.transform.forward);
            Check(yawDelta < 10f, $"Caminar hacia atrás conserva la orientación (giró {yawDelta:F1}°)");
            Check(backDist > 1.2f, $"Retrocede {backDist:F2} m");
            Check(Mathf.Abs(Speed - _movement.BackpedalSpeed) < 0.2f, $"Velocidad hacia atrás {Speed:F2} m/s");
            Check(AnimSpeed < -0.1f, $"Speed negativo en el Animator (blend WalkBackward): {AnimSpeed:F2}");
            Keys();
            yield return 0.8f;

            // 4. Sprint (+40 %)
            yield return Teleport(Corridor);
            Keys(Key.LeftShift, Key.W);
            yield return 1.2f;
            Check(Mathf.Abs(Speed - _movement.SprintSpeed) < 0.3f, $"Sprint a {Speed:F2} m/s (SprintSpeed {_movement.SprintSpeed})");
            Keys();
            yield return 1f;

            // 5. Jump → Fall → Landing (visual)
            yield return Teleport(Corridor);
            Visited.Clear();
            yield return Tap(Key.Space);
            bool sawLand = false;
            for (float t = 0f; t < 2.2f; t += 0.02f)
            {
                sawLand |= AnimIs(PlayerAnimatorIds.Land);
                yield return 0.02f;
            }
            Check(Visited.Contains(_movement.JumpState) && Visited.Contains(_movement.FallState), "Salto → caída");
            Check(Current == _movement.IdleState && Mathf.Abs(Feet - Slab) < 0.1f, $"Aterriza en Idle (pies a {Feet - Slab:F2} m de la losa)");
            Check(sawLand, "Se reproduce la animación de aterrizaje");

            // 6–9. Vault lane (x = −8): 0.6 m, 1.0 m, 1.2 m wide; 1.6 m is too high
            yield return VaultCase(-11.75f + Z, -13.4f + Z, "0.6 m");
            yield return VaultCase(-17.7f + Z, -19.4f + Z, "1.0 m");
            yield return VaultCase(-23.35f + Z, -25.8f + Z, "1.1 m de 1.2 m de ancho");
            yield return Teleport(new Vector3(-8f, Slab + OriginAboveFeet, -29.7f + Z));
            Visited.Clear();
            Keys(Key.W);
            yield return 0.15f;
            yield return Tap(Key.Space, Key.W);
            Keys();
            yield return 1.6f;
            Check(!Visited.Contains(_movement.VaultState) && Visited.Contains(_movement.JumpState), "Obstáculo de 1.6 m: salta en vez de hacer vault");

            // 10. Slide under the 1.2 m bar (x = −3)
            yield return Teleport(new Vector3(-3f, Slab + OriginAboveFeet, -9f + Z));
            Visited.Clear();
            Keys(Key.LeftShift, Key.W);
            yield return 0.7f;
            Debug.Log($"{Tag} (diag) antes de C: estado={Current.GetType().Name}, IsSprint={_movement.IsSprint}, CanSprint={_movement.CanSprint}, velocidad={Speed:F2}, z={_movement.transform.position.z:F2}");
            yield return Tap(Key.C, Key.LeftShift, Key.W);
            yield return 1.3f;
            float slideZ = _movement.transform.position.z;
            Keys();
            Check(Visited.Contains(_movement.SlideState), "Shift + C inicia el slide");
            Check(slideZ < -15f + Z, $"Pasa bajo la barra (z = {slideZ:F2}, la barra termina en {-14.5f + Z})");
            yield return 1f;

            // 11. Auto step up the stairs (x = 2)
            yield return Teleport(new Vector3(2f, Slab + OriginAboveFeet, -9f + Z));
            Keys(Key.W);
            yield return 1.45f;
            float stepFeet = Feet - Slab;
            Keys();
            Check(Mathf.Abs(stepFeet - 1.25f) < 0.12f, $"Auto step sube a la plataforma de 1.25 m (pies a {stepFeet:F2} m de la losa)");
            yield return 1f;

            // 12. Ledge grab and climb (x = 7, wall of 3.0 m)
            yield return Teleport(new Vector3(7f, Slab + OriginAboveFeet, -12.2f + Z));
            Visited.Clear();
            Keys(Key.W);
            yield return 0.15f;
            yield return Tap(Key.Space, Key.W);
            yield return 2f;
            bool grabbed = Current == _movement.LedgeGrabState;
            Check(grabbed, "Se agarra de la cornisa al caer junto al muro");
            Keys();
            if (grabbed)
            {
                yield return Tap(Key.Space);
                yield return 1.3f;
                Check(Visited.Contains(_movement.LedgeClimbState) && Mathf.Abs(Feet - Slab - 3.0f) < 0.15f,
                      $"Sube a la cornisa y queda de pie arriba (pies a {Feet - Slab:F2} m de la losa)");
            }
            yield return 1f;

            // 13. Combat: J, J (buffered during the strike) → K finisher, with lunge
            yield return Teleport(Corridor);
            Visited.Clear();
            Vector3 c0 = _movement.transform.position;
            yield return Tap(Key.J);
            yield return 0.07f;
            yield return Tap(Key.J);
            yield return 0.3f;
            yield return Tap(Key.K);
            yield return 1.6f;
            int lights = Count(_movement.LightAttackState);
            float lunge = Vector3.Dot(_movement.transform.position - c0, _movement.transform.forward);
            Check(lights >= 2, $"El segundo J pulsado durante el golpe se encadena ({lights} golpes ligeros)");
            Check(Visited.Contains(_movement.HeavyAttackState), "K cierra el combo con el ataque fuerte");
            Check(lunge > 0.3f, $"Los ataques avanzan (impulso de {lunge:F2} m)");

            // 14. Dodge backward (Q + S) keeps facing and rolls back
            yield return Teleport(new Vector3(CorridorX, Slab + OriginAboveFeet, -20f + Z));
            Visited.Clear();
            Keys(Key.S);
            yield return 0.2f;
            yield return Tap(Key.Q, Key.S);
            Check(Visited.Contains(_movement.DodgeState) && _movement.DodgeState.LocalDirection.y < -0.5f,
                  $"Q + S: esquiva hacia atrás (dirección local {_movement.DodgeState.LocalDirection})");
            Keys();
            yield return 1f;

            // 15. Block: −70 % only from the front
            yield return Teleport(Corridor);
            var health = _movement.GetComponent<HealthSystem>();
            Keys(Key.L);
            yield return 0.6f;
            Check(Current == _movement.BlockState, "L mantenido bloquea");
            int h0 = health.CurrentHealth;
            health.TakeDamage(20, _movement.transform.position + _movement.transform.forward * 2f);
            int frontal = h0 - health.CurrentHealth;
            yield return 0.6f; // i-frames
            int h1 = health.CurrentHealth;
            health.TakeDamage(20, _movement.transform.position - _movement.transform.forward * 2f);
            int back = h1 - health.CurrentHealth;
            Check(frontal == 6 && back == 20, $"Bloqueo: golpe frontal de 20 quita {frontal}, por la espalda quita {back}");
            Keys();
            yield return 0.5f;
        }

        private static IEnumerator VaultCase(float startZ, float mustPassZ, string label)
        {
            yield return Teleport(new Vector3(-8f, Slab + OriginAboveFeet, startZ));
            Visited.Clear();
            Keys(Key.W);
            yield return 0.15f;
            yield return Tap(Key.Space, Key.W);
            Keys();
            bool sawVaultAnim = false;
            for (float t = 0f; t < 1.2f; t += 0.02f)
            {
                sawVaultAnim |= AnimIs(PlayerAnimatorIds.Vault);
                yield return 0.02f;
            }
            float z = _movement.transform.position.z;
            Check(Visited.Contains(_movement.VaultState) && sawVaultAnim, $"Vault {label}: Espacio cerca del obstáculo inicia el vault con su animación");
            Check(z < mustPassZ && Mathf.Abs(Feet - Slab) < 0.15f, $"Vault {label}: aterriza detrás del obstáculo (z = {z:F2}, pies a {Feet - Slab:F2} m de la losa)");
            yield return 0.8f;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static PlayerState Current => _movement.StateMachine.CurrentState;
        private static Vector3 Corridor => new Vector3(CorridorX, Slab + OriginAboveFeet, -9f + Z);
        private static float Feet => _movement.FeetY;
        private static float AnimSpeed => _animator.GetFloat(PlayerAnimatorIds.SpeedParam);

        private static float Speed
        {
            get
            {
                Vector3 v = _movement.Rb.linearVelocity;
                return new Vector2(v.x, v.z).magnitude;
            }
        }

        private static float GroundBelow()
        {
            Vector3 p = _movement.transform.position;
            return Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3f, LayerMask.GetMask("Ground", "Obstacle"), QueryTriggerInteraction.Ignore)
                ? hit.point.y : float.NaN;
        }

        private static bool AnimIs(int stateHash)
        {
            return _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash ||
                   (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash);
        }

        private static int Count(PlayerState state)
        {
            int n = 0;
            foreach (PlayerState s in Visited) if (s == state) n++;
            return n;
        }

        private static void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        /// <summary>Presses <paramref name="key"/> for a short moment while keeping <paramref name="held"/> down.</summary>
        private static IEnumerator Tap(Key key, params Key[] held)
        {
            var down = new Key[held.Length + 1];
            held.CopyTo(down, 0);
            down[held.Length] = key;
            Keys(down);
            yield return 0.06f;
            Keys(held);
            yield return 0.02f;
        }

        /// <summary>Places the player standing at <paramref name="position"/> facing −Z (toward the circuit) and lets the camera settle.</summary>
        private static IEnumerator Teleport(Vector3 position)
        {
            Keys();
            for (float t = 0f; t < 3f && Current != _movement.IdleState; t += 0.1f)
                yield return 0.1f;

            Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
            _movement.Rb.linearVelocity = Vector3.zero;
            _movement.Rb.position = position;
            _movement.Rb.rotation = facing;
            _movement.transform.SetPositionAndRotation(position, facing);
            Physics.SyncTransforms();
            Object.FindAnyObjectByType<CameraFollow>()?.SnapToTarget();
            yield return 0.6f;
        }

        private static bool Check(bool condition, string label)
        {
            if (condition) Debug.Log($"{Tag} OK    {label}");
            else
            {
                Debug.LogError($"{Tag} FALLA {label} | {Diag()}");
                _fails++;
            }
            return condition;
        }

        /// <summary>Context printed with every failure.</summary>
        private static string Diag()
        {
            if (_movement == null) return "sin Player";
            Vector3 p = _movement.transform.position;
            string under = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                ? $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, y={hit.point.y:F2})"
                : "nada";
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            return $"estado={Current.GetType().Name}, enSuelo={_movement.IsGrounded}, pos={p}, yaw={_movement.transform.eulerAngles.y:F0}, " +
                   $"camForward={(cam != null ? cam.forward.ToString("F2") : "-")}, debajo={under}";
        }
    }
}
