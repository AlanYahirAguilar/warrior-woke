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
    /// Automatic Play Mode test of the player in the Parkour Test Area (Level-1). It enters Play Mode,
    /// adds a virtual keyboard and drives the real input path (Input System → PlayerInputHandler →
    /// FSM). Besides the state flow, it measures physical contact on the animated skeleton: hands on
    /// the obstacle and on the ledge, hands and feet never inside walls or the floor, soles on the
    /// ground, facing the wall, landing weight and momentum (docs/features.md F32).
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
        private const float  OriginAboveFeet = 0.99f;                                 // CapsuleCollider half height of Player.prefab
        private const float  Floor           = ParkourTestCircuitBuilder.FloorTop;
        private const float  CorridorX       = -31f;                                  // free strip along the whole area

        // Contact tolerances (m)
        private const float HandOnTarget   = 0.06f; // IK goal reached
        private const float Penetration    = 0.03f; // a limb may not go deeper than this into a surface
        private const float SoleOnGround   = 0.04f; // standing soles within this of the ground

        private static readonly Stack<IEnumerator> Routines = new Stack<IEnumerator>();
        private static readonly List<PlayerState>  Visited  = new List<PlayerState>();
        private static float          _waitUntil;
        private static int            _fails;
        private static int            _errors;
        private static Keyboard       _keyboard;
        private static PlayerMovement _movement;
        private static Animator       _animator;
        private static Transform      _handL, _handR, _footL, _footR, _head;
        private static string         _feetDiag = "";

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
            _handL = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _handR = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _footL = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _footR = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _head  = _animator.GetBoneTransform(HumanBodyBones.Head);
            _movement.StateChanged += s => Visited.Add(s);

            yield return Spawn();
            yield return Locomotion();
            yield return JumpAndLandings();
            yield return Vaults();
            yield return Slide();
            yield return LedgeGrab();
            yield return Climb();
            yield return Combined();
            yield return Combat();
        }

        private static IEnumerator Spawn()
        {
            Vector3 spawn = _movement.transform.position;
            Check(Current == _movement.IdleState && _movement.IsGrounded, "Idle apoyado en el suelo al aparecer");
            Check((new Vector2(spawn.x, spawn.z) - new Vector2(ParkourTestCircuitBuilder.SpawnPosition.x, ParkourTestCircuitBuilder.SpawnPosition.z)).sqrMagnitude < 1f,
                  $"El jugador aparece en la entrada del área ({spawn})");
            Check(Mathf.Abs(Sole - Floor) < SoleOnGround, $"De pie, las suelas tocan el suelo (a {(Sole - Floor) * 100f:F1} cm)");
            yield return null;
        }

        // ── S01 / corridor: acceleration, cruise, braking, backpedal, turn, sprint, steps ──────────
        private static IEnumerator Locomotion()
        {
            yield return Teleport(Corridor(5f));
            Keys(Key.W);
            yield return 0.1f;
            float early = Speed;
            float minSole = float.MaxValue, maxSole = float.MinValue;
            for (float t = 0f; t < 1.2f; t += 0.02f)
            {
                if (t > 0.6f) { minSole = Mathf.Min(minSole, SoleClearance); maxSole = Mathf.Max(maxSole, SoleClearance); }
                yield return 0.02f;
            }
            float cruise = Speed;
            Check(early > 0.2f && early < 2.6f, $"Arranque gradual: a 0.1 s va a {early:F2} m/s (zona Walk/Jog del blend)");
            Check(Mathf.Abs(cruise - _movement.BaseSpeed) < 0.3f, $"Correr a {cruise:F2} m/s (BaseSpeed {_movement.BaseSpeed})");
            Check(AnimSpeed > 0.8f, $"Parámetro Speed del Animator en carrera: {AnimSpeed:F2}");
            Check(minSole > -Penetration && minSole < SoleOnGround, $"Corriendo, los pies pisan el suelo sin atravesarlo (mínimo {minSole * 100f:F1} cm)");
            Keys();
            yield return 0.12f;
            float braking = Speed;
            yield return 1.2f;
            Check(braking > 0.3f && braking < cruise, $"Frenado gradual: a 0.12 s va a {braking:F2} m/s");
            Check(Current == _movement.IdleState && Speed < 0.05f, "Vuelve a Idle y se detiene");

            // Backpedal: walks backward facing forward
            yield return Teleport(Corridor(-10f));
            float yaw0 = Yaw;
            Vector3 p0 = _movement.transform.position;
            Keys(Key.S);
            yield return 1.2f;
            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw));
            float backDist = Vector3.Dot(_movement.transform.position - p0, -_movement.transform.forward);
            Check(yawDelta < 10f, $"Caminar hacia atrás conserva la orientación (giró {yawDelta:F1}°)");
            Check(backDist > 1.2f, $"Retrocede {backDist:F2} m");
            Check(Mathf.Abs(Speed - _movement.BackpedalSpeed) < 0.2f, $"Velocidad hacia atrás {Speed:F2} m/s");
            Check(AnimSpeed < -0.1f, $"Speed negativo en el Animator (blend WalkBackward): {AnimSpeed:F2}");
            Keys();
            yield return 0.8f;

            // Turn 90° while running: smooth turn, never the backward walk
            yield return Teleport(Corridor(5f));
            Keys(Key.W);
            yield return 1.0f;
            float yawStart = Yaw;
            Keys(Key.W, Key.D);
            yield return 0.1f;
            float earlyTurn = Mathf.Abs(Mathf.DeltaAngle(yawStart, Yaw));
            float minAnimSpeed = float.MaxValue;
            for (float t = 0f; t < 0.8f; t += 0.02f)
            {
                minAnimSpeed = Mathf.Min(minAnimSpeed, AnimSpeed);
                yield return 0.02f;
            }
            float finalTurn = Mathf.Abs(Mathf.DeltaAngle(yawStart, Yaw));
            Keys();
            Check(earlyTurn < 30f && finalTurn > 30f, $"Giro progresivo: {earlyTurn:F0}° a los 0.1 s, {finalTurn:F0}° a los 0.9 s");
            Check(minAnimSpeed > -0.05f, $"Durante el giro no aparece la caminata hacia atrás (Speed mínimo {minAnimSpeed:F2})");
            yield return 1f;

            // Sprint (+40 %)
            yield return Teleport(Corridor(5f));
            Keys(Key.LeftShift, Key.W);
            yield return 1.4f;
            Check(Mathf.Abs(Speed - _movement.SprintSpeed) < 0.3f, $"Sprint a {Speed:F2} m/s (SprintSpeed {_movement.SprintSpeed})");
            Keys();
            yield return 1.2f;

            // Curbs up (auto step) and stairs down (step down) without falling
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LocomotionX, Floor + OriginAboveFeet, -4f));
            Visited.Clear();
            Keys(Key.W);
            for (float t = 0f; t < 3f && _movement.transform.position.z > -16f; t += 0.02f) yield return 0.02f;
            Keys();
            Check(_movement.transform.position.z < -15.5f && !Visited.Contains(_movement.FallState),
                  $"Sube los bordillos de 0.15–0.35 m sin detenerse ni caer (z = {_movement.transform.position.z:F2})");
            yield return 0.6f;

            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LocomotionX, 1.0f + OriginAboveFeet, -44.5f));
            Visited.Clear();
            Keys(Key.W);
            float stairsMinSole = float.MaxValue;
            for (float t = 0f; t < 3f && _movement.transform.position.z > -50f; t += 0.02f)
            {
                if (_movement.transform.position.z < -46.4f) stairsMinSole = Mathf.Min(stairsMinSole, SoleClearance);
                yield return 0.02f;
            }
            Keys();
            Check(_movement.transform.position.z < -49.5f && Mathf.Abs(_movement.FeetY - Floor) < 0.05f && !Visited.Contains(_movement.FallState),
                  $"Baja la escalera pisando cada escalón, sin caer (z = {_movement.transform.position.z:F2})");
            Check(stairsMinSole > -Penetration, $"Bajando escalones los pies no atraviesan los peldaños (mínimo {stairsMinSole * 100f:F1} cm)");
            yield return 0.8f;
        }

        // ── Jump, gap jump and landings of 1, 2 and 3 m (S06) ──────────────────────────────────────
        private static IEnumerator JumpAndLandings()
        {
            // Jump in place: human height, visual body on the collider
            yield return Teleport(Corridor(-20f));
            Visited.Clear();
            float startFeet = _movement.FeetY;
            float peak = startFeet, worstAirGap = 0f;
            yield return Tap(Key.Space);
            for (float t = 0f; t < 1.6f; t += 0.02f)
            {
                peak = Mathf.Max(peak, _movement.FeetY);
                if (!_movement.IsGrounded) worstAirGap = Mathf.Max(worstAirGap, LowestSole - _movement.FeetY);
                yield return 0.02f;
            }
            float rise = peak - startFeet;
            Check(Visited.Contains(_movement.JumpState) && Visited.Contains(_movement.FallState), "Salto → caída");
            Check(rise > 0.7f && rise < 1.3f, $"Salto de altura humana: sube {rise:F2} m");
            Check(worstAirGap < 0.25f, $"En el aire el cuerpo visible sigue al collider (separación máxima de las suelas {worstAirGap:F2} m)");
            Check(Current == _movement.IdleState && Mathf.Abs(Sole - Floor) < SoleOnGround, $"Aterriza en Idle con las suelas en el suelo ({(Sole - Floor) * 100f:F1} cm)");

            // Gap of 2 m between the 1 m platforms: the jump keeps the run's momentum
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LandingX, 1.0f + OriginAboveFeet, -8.6f));
            Keys(Key.LeftShift, Key.W);
            for (float t = 0f; t < 2f && _movement.transform.position.z > -11.4f; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.Space, Key.LeftShift, Key.W);
            for (float t = 0f; t < 1.5f && !(_movement.IsGrounded && Current != _movement.JumpState); t += 0.02f) yield return 0.02f;
            Keys();
            Check(_movement.transform.position.z < -14f && Mathf.Abs(_movement.FeetY - 1.0f) < 0.08f,
                  $"Salta el hueco de 2 m con el impulso de la carrera (z = {_movement.transform.position.z:F2}, pies a {_movement.FeetY:F2} m)");
            yield return 0.8f;

            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.LandingX, 1.0f + OriginAboveFeet, -16.5f), "1 m", false);
            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.LandingX, 2.0f + OriginAboveFeet, -27.5f), "2 m", false);
            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.LandingX, 3.0f + OriginAboveFeet, -39.9f), "3 m", true);
        }

        private static IEnumerator LandingCase(Vector3 start, string label, bool expectHard)
        {
            yield return Teleport(start);
            Visited.Clear();
            Keys(Key.W);
            bool sawHard = false, sawSoft = false;
            float minScale = 1f, minSole = float.MaxValue;
            float landedAt = -1f;
            for (float t = 0f; t < 3f; t += 0.02f)
            {
                sawHard |= AnimIs(PlayerAnimatorIds.LandHard);
                sawSoft |= AnimIs(PlayerAnimatorIds.Land) || AnimIs(PlayerAnimatorIds.LandRun);
                if (Visited.Contains(_movement.FallState) && _movement.IsGrounded)
                {
                    if (landedAt < 0f) landedAt = t;
                    minScale = Mathf.Min(minScale, _movement.RecoverySpeedScale);
                    minSole = Mathf.Min(minSole, SoleClearance);
                }
                if (landedAt >= 0f && t - landedAt > 1.5f) break;
                yield return 0.02f;
            }
            float recovered = Speed;
            Keys();
            Check(landedAt >= 0f, $"Caída de {label}: cae y aterriza");
            Check(expectHard ? sawHard : (sawSoft && !sawHard),
                  $"Caída de {label}: aterrizaje {(expectHard ? "fuerte" : "suave")} (severidad {_movement.LandingSeverity:F2}, LandHard visto: {sawHard})");
            if (expectHard)
            {
                Check(minScale < 0.7f, $"Caída de {label}: el impacto absorbe la velocidad (escala mínima {minScale:F2})");
                Check(recovered > _movement.BaseSpeed * 0.9f, $"Caída de {label}: recupera la carrera ({recovered:F2} m/s a los 1.5 s)");
            }
            Check(minSole > -Penetration, $"Caída de {label}: los pies no atraviesan el suelo al aterrizar (mínimo {minSole * 100f:F1} cm)");
            yield return 0.8f;
        }

        // ── S02: vault heights, approach speeds, standing vault, too-high obstacle ─────────────────
        private static IEnumerator Vaults()
        {
            yield return VaultCase(-8f, 0.5f, 0.3f, false, "0.5 m corriendo");
            yield return VaultCase(-16f, 0.75f, 0.4f, false, "0.75 m corriendo");
            yield return VaultCase(-24f, 1.0f, 0.5f, false, "1.0 m corriendo");
            yield return VaultCase(-24f, 1.0f, 0.5f, true, "1.0 m esprintando");
            yield return VaultCase(-32f, 1.2f, 0.6f, false, "1.2 m corriendo");
            yield return VaultCase(-40f, 0.9f, 1.4f, false, "0.9 m de 1.4 m de fondo");
            yield return VaultCase(-16f, 0.75f, 0.4f, false, "0.75 m desde parado", standing: true);

            // 1.6 m: too high, Space jumps
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.VaultX, Floor + OriginAboveFeet, -50f + 0.9f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 1.4f;
            Check(!Visited.Contains(_movement.VaultState) && !Visited.Contains(_movement.LedgeGrabState) && Visited.Contains(_movement.JumpState),
                  "Obstáculo de 1.6 m: salta en vez de hacer vault");
        }

        private static IEnumerator VaultCase(float frontZ, float height, float depth, bool sprint, string label, bool standing = false)
        {
            float backZ = frontZ - depth;
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.VaultX, Floor + OriginAboveFeet, frontZ + (standing ? 0.85f : 4f)));
            Visited.Clear();
            Key[] held = standing ? new Key[0] : sprint ? new[] { Key.LeftShift, Key.W } : new[] { Key.W };
            Keys(held);
            for (float t = 0f; t < 3f && !standing && _movement.transform.position.z - frontZ > 1.0f; t += 0.02f) yield return 0.02f;
            float approach = Speed;
            yield return Tap(Key.Space, held);

            bool sawAnim = false;
            float handError = float.MaxValue, minFeetOver = float.MaxValue, exitSpeed = -1f;
            for (float t = 0f; t < 2f; t += 0.02f)
            {
                sawAnim |= AnimIs(PlayerAnimatorIds.Vault);
                if (Current == _movement.VaultState)
                {
                    if (_animator.GetFloat(PlayerAnimatorIds.LHandCurveParam) > 0.9f)
                        handError = Mathf.Min(handError, Vector3.Distance(_handL.position, _movement.VaultState.HandTarget));
                    foreach (Transform foot in new[] { _footL, _footR })
                    {
                        float z = foot.position.z;
                        if (z < frontZ && z > backZ && Mathf.Abs(foot.position.x - ParkourTestCircuitBuilder.VaultX) < 1.9f &&
                            foot.position.y - height < minFeetOver)
                        {
                            minFeetOver = foot.position.y - height;
                            _feetDiag = $"[{foot.name} n={_movement.ParkourProgress:F2} pie={foot.position} cuerpo={_movement.transform.position} manoIzq={_handL.position} objetivo={_movement.VaultState.HandTarget}]";
                        }
                    }
                }
                else if (Visited.Contains(_movement.VaultState) && exitSpeed < 0f)
                {
                    exitSpeed = Speed;
                }
                if (exitSpeed >= 0f && t > 1.2f) break;
                yield return 0.02f;
            }
            Keys();
            yield return 0.4f;
            float z1 = _movement.transform.position.z;
            Check(Visited.Contains(_movement.VaultState) && sawAnim, $"Vault {label}: Espacio cerca del obstáculo inicia el vault con su animación (aproximación {approach:F1} m/s)");
            Check(handError < HandOnTarget, $"Vault {label}: la mano izquierda se apoya en el obstáculo (a {handError * 100f:F1} cm del punto)");
            Check(minFeetOver > -Penetration || minFeetOver == float.MaxValue, $"Vault {label}: los pies no atraviesan el obstáculo (mínimo {(minFeetOver == float.MaxValue ? 0f : minFeetOver) * 100f:F1} cm sobre la cima) {_feetDiag}");
            Check(z1 < backZ - 0.3f && Mathf.Abs(_movement.FeetY - Floor) < 0.06f, $"Vault {label}: aterriza detrás (z = {z1:F2}, pies a {_movement.FeetY - Floor:F2} m)");
            if (!standing)
                Check(exitSpeed > _movement.BaseSpeed * 0.7f, $"Vault {label}: mantiene el impulso al salir ({exitSpeed:F2} m/s)");
            float after = float.MaxValue;
            for (float t = 0f; t < 0.4f; t += 0.02f) { after = Mathf.Min(after, SoleClearance); yield return 0.02f; }
            Check(after > -Penetration && after < SoleOnGround, $"Vault {label}: después del vault los pies pisan el suelo ({after * 100f:F1} cm)");
            yield return 0.6f;
        }

        // ── S03: slide under the bar and through the tunnel ────────────────────────────────────────
        private static IEnumerator Slide()
        {
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.SlideX, Floor + OriginAboveFeet, -1f));
            Visited.Clear();
            Keys(Key.LeftShift, Key.W);
            for (float t = 0f; t < 3f && _movement.transform.position.z > -5.5f; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.C, Key.LeftShift, Key.W);
            float maxColliderGap = 0f, minSole = float.MaxValue, maxHeadUnderBar = float.MinValue;
            for (float t = 0f; t < 1.4f; t += 0.02f)
            {
                if (Current == _movement.SlideState)
                {
                    maxColliderGap = Mathf.Max(maxColliderGap, Mathf.Abs(_movement.FeetY - Floor));
                    minSole = Mathf.Min(minSole, LowestPoint - Floor);
                    float z = _movement.transform.position.z;
                    if (z < -9.4f && z > -10.6f) maxHeadUnderBar = Mathf.Max(maxHeadUnderBar, _head.position.y);
                }
                yield return 0.02f;
            }
            Check(Visited.Contains(_movement.SlideState), "Shift + C inicia el slide");
            Check(_movement.transform.position.z < -11f, $"Pasa bajo la barra (z = {_movement.transform.position.z:F2})");
            Check(maxColliderGap < 0.05f, $"Durante el slide el collider no flota ni se hunde (máximo {maxColliderGap * 100f:F1} cm)");
            Check(minSole > -Penetration, $"Durante el slide el cuerpo no atraviesa el suelo (mínimo {minSole * 100f:F1} cm)");
            Check(maxHeadUnderBar < 1.2f, $"Bajo la barra la cabeza queda por debajo de ella ({maxHeadUnderBar:F2} m < 1.2 m)");

            // Tunnel: the slide keeps going while there is a ceiling
            for (float t = 0f; t < 3f && _movement.transform.position.z > -16f; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.C, Key.LeftShift, Key.W);
            float maxHeadInTunnel = float.MinValue;
            for (float t = 0f; t < 3f && _movement.transform.position.z > -25f; t += 0.02f)
            {
                float z = _movement.transform.position.z;
                if (z < -20.2f && z > -23.8f) maxHeadInTunnel = Mathf.Max(maxHeadInTunnel, _head.position.y);
                yield return 0.02f;
            }
            Keys();
            Check(_movement.transform.position.z < -24.5f, $"Atraviesa el túnel de 4 m deslizándose (z = {_movement.transform.position.z:F2})");
            Check(maxHeadInTunnel < 1.2f, $"En el túnel la cabeza nunca lo atraviesa ({maxHeadInTunnel:F2} m < 1.2 m)");
            yield return 1.2f;
        }

        // ── S04: ledge grab from the ground, from a run, from a jump, angled wall, drop ────────────
        private static IEnumerator LedgeGrab()
        {
            float x = ParkourTestCircuitBuilder.LedgeX;
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, -8f + 0.75f), 2.1f, "2.1 m desde parado", run: false, climb: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, -8f + 4f), 2.1f, "2.1 m corriendo", run: true, climb: false, drop: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, -18f + 0.75f), 2.5f, "2.5 m con Espacio doble (agarre → subida)", run: false, climb: true, chain: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, -28f + 0.75f), 3.0f, "3.0 m saltando", run: false, climb: true);

            // Wall turned 30°: its face center is at the wall center + rotated half depth
            Quaternion rot = Quaternion.Euler(0f, 30f, 0f);
            Vector3 normal = rot * Vector3.forward;
            Vector3 faceCenter = new Vector3(x, 0f, -41.5f) + normal * 1.5f;
            Vector3 start = faceCenter + normal * 0.8f;
            yield return LedgeCase(new Vector3(start.x, Floor + OriginAboveFeet, start.z), 2.4f, "2.4 m en ángulo de 30°", run: false, climb: true);
        }

        private static IEnumerator LedgeCase(Vector3 start, float topY, string label, bool run, bool climb, bool chain = false, bool drop = false)
        {
            yield return Teleport(start);
            Visited.Clear();
            if (run)
            {
                Keys(Key.W);
                for (float t = 0f; t < 3f && _movement.transform.position.z - (start.z - 4f) > 0.9f; t += 0.02f) yield return 0.02f;
                yield return Tap(Key.Space, Key.W);
                Keys();
            }
            else
            {
                yield return Tap(Key.Space);
                if (chain) { yield return 0.15f; yield return Tap(Key.Space); }
            }

            // Wait for the hang (or the climb, when chained)
            for (float t = 0f; t < 2.5f && !(Current == _movement.LedgeGrabState && _movement.LedgeGrabState.IsAttached) && Current != _movement.LedgeClimbState; t += 0.02f)
                yield return 0.02f;
            bool grabbed = Visited.Contains(_movement.LedgeGrabState);
            Check(grabbed, $"Cornisa {label}: se agarra del borde");
            if (!grabbed) { Keys(); yield return 1f; yield break; }

            LedgeInfo ledge = _movement.CurrentLedge;
            Check(Mathf.Abs(ledge.TopY - topY) < 0.02f, $"Cornisa {label}: mide la cima real ({ledge.TopY:F2} m)");

            if (!chain)
            {
                // Hang: hands on the edge, nothing inside the wall, facing it
                float handErr = 0f, handInWall = float.MaxValue, footInWall = float.MaxValue, facingErr = 0f;
                for (float t = 0f; t < 0.5f; t += 0.02f)
                {
                    handErr    = Mathf.Max(handErr, HandEdgeError(ledge, _handL), HandEdgeError(ledge, _handR));
                    handInWall = Mathf.Min(handInWall, FaceDistance(ledge, _handL), FaceDistance(ledge, _handR));
                    footInWall = Mathf.Min(footInWall, FaceDistance(ledge, _footL), FaceDistance(ledge, _footR));
                    facingErr  = Mathf.Max(facingErr, Vector3.Angle(_movement.transform.forward, -ledge.Normal));
                    yield return 0.02f;
                }
                Check(handErr < HandOnTarget, $"Cornisa {label}: ambas manos sobre el borde (error máximo {handErr * 100f:F1} cm)");
                Check(handInWall > -Penetration, $"Cornisa {label}: las manos no atraviesan el muro ({handInWall * 100f:F1} cm)");
                Check(footInWall > -Penetration, $"Cornisa {label}: los pies no atraviesan el muro ({footInWall * 100f:F1} cm)");
                Check(facingErr < 8f, $"Cornisa {label}: el cuerpo mira al muro (desvío {facingErr:F1}°)");
            }

            if (drop)
            {
                Keys(Key.S);
                yield return 0.3f;
                Keys();
                for (float t = 0f; t < 2f && !_movement.IsGrounded; t += 0.02f) yield return 0.02f;
                yield return 0.3f;
                Check(Visited.Contains(_movement.FallState) && _movement.IsGrounded && Mathf.Abs(_movement.FeetY - Floor) < 0.06f,
                      $"Cornisa {label}: S suelta el borde y aterriza");
                yield return 0.8f;
                yield break;
            }

            if (climb)
            {
                if (!chain) yield return Tap(Key.Space);
                float climbHandErr = 0f, minHandOverTop = float.MaxValue;
                for (float t = 0f; t < 2.5f && Current != _movement.IdleState && Current != _movement.RunState; t += 0.02f)
                {
                    if (Current == _movement.LedgeClimbState)
                    {
                        float p = _movement.ParkourProgress;
                        if (p >= 0f && p < ParkourTimings.ClimbHandsRelease)
                            climbHandErr = Mathf.Max(climbHandErr, HandEdgeError(ledge, _handL), HandEdgeError(ledge, _handR));
                        foreach (Transform hand in new[] { _handL, _handR })
                        {
                            if (FaceDistance(ledge, hand) < -0.1f) // over the top surface
                                minHandOverTop = Mathf.Min(minHandOverTop, hand.position.y - ledge.TopY);
                        }
                    }
                    yield return 0.02f;
                }
                yield return 0.5f;
                Check(Visited.Contains(_movement.LedgeClimbState) && Mathf.Abs(_movement.FeetY - topY) < 0.06f,
                      $"Cornisa {label}: sube y queda de pie arriba (pies a {_movement.FeetY - topY:F2} m de la cima)");
                Check(climbHandErr < HandOnTarget * 1.5f, $"Cornisa {label}: las manos siguen en el borde al empezar a subir (error {climbHandErr * 100f:F1} cm)");
                Check(minHandOverTop == float.MaxValue || minHandOverTop > -Penetration,
                      $"Cornisa {label}: las manos no atraviesan la cima ({(minHandOverTop == float.MaxValue ? 0f : minHandOverTop) * 100f:F1} cm)");
                Check(Mathf.Abs(Sole - topY) < SoleOnGround, $"Cornisa {label}: arriba, las suelas tocan la cima ({(Sole - topY) * 100f:F1} cm)");
            }
            Keys();
            yield return 0.8f;
        }

        // ── S05: climb a wall, then a second wall from its top, drop onto the terrace, stairs down ──
        private static IEnumerator Climb()
        {
            float x = ParkourTestCircuitBuilder.ClimbX;
            yield return Teleport(new Vector3(x, Floor + OriginAboveFeet, -8f + 0.75f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 0.15f;
            yield return Tap(Key.Space); // chained: grab → climb
            for (float t = 0f; t < 4f && !(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.IdleState); t += 0.02f) yield return 0.02f;
            Check(Mathf.Abs(_movement.FeetY - 2.2f) < 0.06f, $"Escalada: sube el muro de 2.2 m (pies a {_movement.FeetY:F2} m)");

            // Walk to the second wall (face at z = −12) and climb it from its base
            Keys(Key.W);
            for (float t = 0f; t < 2f && _movement.transform.position.z > -12f + 0.8f; t += 0.02f) yield return 0.02f;
            Keys();
            yield return 0.3f;
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 0.15f;
            yield return Tap(Key.Space);
            for (float t = 0f; t < 4f && !(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.IdleState); t += 0.02f) yield return 0.02f;
            Check(Mathf.Abs(_movement.FeetY - 4.4f) < 0.06f, $"Escalada: desde la cima sube el segundo muro hasta 4.4 m (pies a {_movement.FeetY:F2} m)");

            // Drop 2.2 m onto the terrace and walk down the stairs
            Keys(Key.W);
            for (float t = 0f; t < 8f && _movement.transform.position.z > -24f; t += 0.02f) yield return 0.02f;
            Keys();
            yield return 0.6f;
            Check(_movement.transform.position.z < -23.5f && Mathf.Abs(_movement.FeetY - Floor) < 0.06f,
                  $"Escalada: baja a la terraza y por la escalera hasta el suelo (z = {_movement.transform.position.z:F2})");
            yield return 0.5f;
        }

        // ── S07: everything in one run ─────────────────────────────────────────────────────────────
        private static IEnumerator Combined()
        {
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.ComboX, Floor + OriginAboveFeet, 3f));
            Visited.Clear();
            bool vault1 = false, slide = false, grab = false, climbTap = false, vault2 = false;
            Keys(Key.LeftShift, Key.W);
            for (float t = 0f; t < 20f && _movement.transform.position.z > -47f; t += 0.02f)
            {
                float z = _movement.transform.position.z;
                PlayerState s = Current;
                bool free = s == _movement.RunState || s == _movement.IdleState;
                if (!vault1 && free && z - (-12f) < 1.0f) { vault1 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (vault1 && !slide && free && z < -14f && z - (-20.5f) < 4.0f) { slide = true; yield return Tap(Key.C, Key.LeftShift, Key.W); continue; }
                if (slide && !grab && free && z < -22f && z - (-30f) < 0.9f) { grab = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (grab && !climbTap && s == _movement.LedgeGrabState) { climbTap = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (climbTap && !vault2 && free && z < -34f && z - (-42f) < 1.0f && _movement.IsGrounded) { vault2 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                yield return 0.02f;
            }
            Keys();
            int vaults = Count(_movement.VaultState);
            Check(vaults >= 2 && Visited.Contains(_movement.SlideState) && Visited.Contains(_movement.LedgeGrabState) && Visited.Contains(_movement.LedgeClimbState),
                  $"Circuito combinado: vault ({vaults}), slide, agarre y subida en una sola carrera");
            Check(_movement.transform.position.z < -46.5f, $"Circuito combinado: llega al final (z = {_movement.transform.position.z:F2})");
            yield return 1f;
        }

        // ── Combat (corridor): combo with buffer, dodge, block ─────────────────────────────────────
        private static IEnumerator Combat()
        {
            yield return Teleport(Corridor(-30f));
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

            yield return Teleport(Corridor(-30f));
            Visited.Clear();
            Keys(Key.S);
            yield return 0.2f;
            yield return Tap(Key.Q, Key.S);
            Check(Visited.Contains(_movement.DodgeState) && _movement.DodgeState.LocalDirection.y < -0.5f,
                  $"Q + S: esquiva hacia atrás (dirección local {_movement.DodgeState.LocalDirection})");
            Keys();
            yield return 1f;

            yield return Teleport(Corridor(-30f));
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

        // ─── Measurements ────────────────────────────────────────────────────────────

        private static PlayerState Current => _movement.StateMachine.CurrentState;
        private static Vector3 Corridor(float z) => new Vector3(CorridorX, Floor + OriginAboveFeet, z);
        private static float AnimSpeed => _animator.GetFloat(PlayerAnimatorIds.SpeedParam);
        private static float Yaw => _movement.transform.eulerAngles.y;

        private static float Speed
        {
            get
            {
                Vector3 v = _movement.Rb.linearVelocity;
                return new Vector2(v.x, v.z).magnitude;
            }
        }

        /// <summary>Height of the lower sole (foot bone minus the avatar's foot bottom height).</summary>
        private static float Sole => Mathf.Min(_footL.position.y - _animator.leftFeetBottomHeight,
                                               _footR.position.y - _animator.rightFeetBottomHeight);

        /// <summary>Same as Sole; used where the lowest sole of a moving body matters.</summary>
        private static float LowestSole => Sole;

        /// <summary>Lowest gap between a sole and the ground right under that foot (negative = sunk into it).</summary>
        private static float SoleClearance => Mathf.Min(
            _footL.position.y - _animator.leftFeetBottomHeight - GroundUnder(_footL.position),
            _footR.position.y - _animator.rightFeetBottomHeight - GroundUnder(_footR.position));

        /// <summary>Lowest point among soles, hands and head (for poses lying on the ground).</summary>
        private static float LowestPoint => Mathf.Min(Sole, Mathf.Min(_handL.position.y, _handR.position.y) - 0.04f, _head.position.y - 0.1f);

        private static float GroundUnder(Vector3 p)
        {
            return Physics.Raycast(new Vector3(p.x, p.y + 0.5f, p.z), Vector3.down, out RaycastHit hit, 4f, LayerMask.GetMask("Ground", "Obstacle"), QueryTriggerInteraction.Ignore)
                ? hit.point.y : Floor;
        }

        /// <summary>Distance from a hand (wrist) to its goal on the edge, as PlayerContactIK defines it.</summary>
        private static float HandEdgeError(LedgeInfo ledge, Transform hand)
        {
            Vector3 goal = ledge.EdgeAt(hand.position) + Vector3.up * ParkourTimings.WristAboveSurface + ledge.Normal * ParkourTimings.WristOutOfFace;
            return Vector3.Distance(hand.position, goal);
        }

        /// <summary>Signed distance from a bone to the wall's face plane (negative = inside the wall).</summary>
        private static float FaceDistance(LedgeInfo ledge, Transform bone)
        {
            return Vector3.Dot(bone.position - ledge.Edge, ledge.Normal);
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

        // ─── Input and placement ─────────────────────────────────────────────────────

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

        /// <summary>Places the player standing at <paramref name="position"/> facing −Z (toward the lanes) and lets the camera settle.</summary>
        private static IEnumerator Teleport(Vector3 position)
        {
            Keys();
            for (float t = 0f; t < 4f && Current != _movement.IdleState; t += 0.1f)
                yield return 0.1f;

            Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
            _movement.Rb.linearVelocity = Vector3.zero;
            _movement.Rb.position = position;
            _movement.Rb.rotation = facing;
            _movement.transform.SetPositionAndRotation(position, facing);
            Physics.SyncTransforms();
            Object.FindAnyObjectByType<CameraFollow>()?.SnapToTarget();
            yield return 0.7f;
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
            string under = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)
                ? $"{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, y={hit.point.y:F2})"
                : "nada";
            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
            return $"estado={Current.GetType().Name}, anim={info.shortNameHash} n={info.normalizedTime:F2}, enSuelo={_movement.IsGrounded}, " +
                   $"pos={p}, pies={_movement.FeetY:F2}, suela={Sole:F2}, yaw={Yaw:F0}, rootMotion={_movement.IsRootMotionDriven}, debajo={under}";
        }
    }
}
