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
        private const float  OriginAboveFeet = 1.03f;                                 // CharacterController half height + skin width of Player.prefab
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
        private static PlayerAnimator _playerAnimator;
        private static Vector3        _stepPos;
        private static float          _stepTime, _lastStepDt;

        // Highest speed the body may show between two samples: anything faster is a visible teleport
        private const float TeleportSpeed = 13f;

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
            _playerAnimator = _movement.GetComponent<PlayerAnimator>();
            _movement.StateChanged += s => Visited.Add(s);

            yield return Spawn();
            yield return Locomotion();
            yield return JumpAndLandings();
            yield return Vaults();
            yield return Slide();
            yield return LedgeGrab();
            yield return Climb();
            yield return Combined();
            yield return Flow();
            yield return Mantles();
            yield return Gaits();
            yield return DropAndJumpOff();
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
            Check(AnimSpeed > 0.8f * _movement.BaseSpeed, $"Parámetro MoveZ del Animator en carrera: {AnimSpeed:F2} m/s");
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
            Check(AnimSpeed < -1f, $"MoveZ negativo en el Animator (blend hacia atrás): {AnimSpeed:F2} m/s");
            Keys();
            yield return 0.8f;

            // Turn 90° while running (open area at the entrance: the corridor turns into the perimeter)
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.SpawnPosition.x, Floor + OriginAboveFeet, 6f));
            Keys(Key.W);
            yield return 1.0f;
            float yawStart = Yaw;
            Keys(Key.W, Key.D);
            yield return 0.1f;
            float earlyTurn = Mathf.Abs(Mathf.DeltaAngle(yawStart, Yaw));
            float minAnimSpeed = float.MaxValue, maxSkid = 0f, maxLean = 0f;
            for (float t = 0f; t < 0.8f; t += 0.02f)
            {
                minAnimSpeed = Mathf.Min(minAnimSpeed, AnimSpeed);
                maxSkid = Mathf.Max(maxSkid, Skid);
                maxLean = Mathf.Max(maxLean, Mathf.Abs(_playerAnimator.Lean.x));
                yield return 0.02f;
            }
            Check(maxSkid < 25f, $"Giro sin patinar: la velocidad sigue al cuerpo (desvío máximo {maxSkid:F0}°)");
            Check(maxLean > 2f, $"Giro con peso: el torso se inclina hacia la curva ({maxLean:F1}°)");
            float finalTurn = Mathf.Abs(Mathf.DeltaAngle(yawStart, Yaw));
            Keys();
            Check(earlyTurn < 30f && finalTurn > 30f, $"Giro progresivo: {earlyTurn:F0}° a los 0.1 s, {finalTurn:F0}° a los 0.9 s");
            Check(minAnimSpeed > -0.25f, $"Durante el giro no aparece la caminata hacia atrás (MoveZ mínimo {minAnimSpeed:F2} m/s)");
            yield return 1f;

            // Sprint (+40 %)
            yield return Teleport(Corridor(5f));
            Keys(Key.LeftShift, Key.W);
            yield return 1.4f;
            // The mocap's sprint takes run at 4.4–5.4 m/s (PlayerMxMLocomotion slows the fastest down to 88 %, T27)
            Check(Mathf.Abs(Speed - _movement.SprintSpeed) < 0.6f, $"Sprint a {Speed:F2} m/s (SprintSpeed {_movement.SprintSpeed})");
            Keys();
            yield return 1.2f;

            // Curbs up (auto step) and stairs down (step down) without falling
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LocomotionX, Floor + OriginAboveFeet, -4f));
            Visited.Clear();
            Keys(Key.W);
            // 12 m at the run of P33 (3.4 m/s) plus the start from standing
            for (float t = 0f; t < 5f && _movement.transform.position.z > -16f; t += 0.02f) yield return 0.02f;
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

        // ── Jump, gap jump and landings of 1, 2 and 3 m (S08) ──────────────────────────────────────
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
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.JumpX, 1.0f + OriginAboveFeet, -8.6f));
            Keys(Key.LeftShift, Key.W);
            for (float t = 0f; t < 2f && _movement.transform.position.z > -11.4f; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.Space, Key.LeftShift, Key.W);
            for (float t = 0f; t < 1.5f && !(_movement.IsGrounded && Current != _movement.JumpState); t += 0.02f) yield return 0.02f;
            Keys();
            Check(_movement.transform.position.z < -14f && Mathf.Abs(_movement.FeetY - 1.0f) < 0.08f,
                  $"Salta el hueco de 2 m con el impulso de la carrera (z = {_movement.transform.position.z:F2}, pies a {_movement.FeetY:F2} m)");
            yield return 0.8f;

            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.JumpX, 1.0f + OriginAboveFeet, -16.5f), "1 m", false);
            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.JumpX, 2.0f + OriginAboveFeet, -27.5f), "2 m", false);
            yield return LandingCase(new Vector3(ParkourTestCircuitBuilder.JumpX, 3.0f + OriginAboveFeet, -39.9f), "3 m", true);
        }

        private static IEnumerator LandingCase(Vector3 start, string label, bool expectHard)
        {
            yield return Teleport(start);
            Visited.Clear();
            Keys(Key.W);
            bool sawHard = false, sawSoft = false, sawRoll = false, rollPose = true;
            float minScale = 1f, minSole = float.MaxValue;
            string minAt = "";
            float landedAt = -1f;
            for (float t = 0f; t < 3f; t += 0.02f)
            {
                sawHard |= AnimIs(PlayerAnimatorIds.LandHard);
                sawRoll |= AnimIs(PlayerAnimatorIds.LandRoll);
                if (AnimIs(PlayerAnimatorIds.LandRoll)) rollPose &= PoseFacesBody();
                sawSoft |= AnimIs(PlayerAnimatorIds.Land) || AnimIs(PlayerAnimatorIds.LandRun);
                if (Visited.Contains(_movement.FallState) && _movement.IsGrounded)
                {
                    if (landedAt < 0f) landedAt = t;
                    minScale = Mathf.Min(minScale, _movement.RecoverySpeedScale);
                    float sole = SoleClearance;
                    if (sole < minSole)
                    {
                        minSole = sole;
                        minAt = $"[t={t - landedAt:F2} s tras aterrizar, z={_movement.transform.position.z:F2}, estado={Current.GetType().Name}, anim={_animator.GetCurrentAnimatorStateInfo(0).shortNameHash}, pie izq={_footL.position.y:F2} der={_footR.position.y:F2}, suelo izq={GroundUnder(_footL.position):F2} der={GroundUnder(_footR.position):F2}]";
                    }
                }
                // P23 recovery (up to 0.7 s) plus the mocap's own acceleration back to the run (~1.5 s
                // from a heavy landing's crouch, MxMLocomotionProbe)
                if (landedAt >= 0f && t - landedAt > 2.5f) break;
                yield return 0.02f;
            }
            float recovered = Speed;
            Keys();
            Check(landedAt >= 0f, $"Caída de {label}: cae y aterriza");
            // Running off the edge (input held): a heavy landing at speed is rolled out
            Check(expectHard ? (sawRoll && _movement.LandedWithRoll) : (sawSoft && !sawHard && !sawRoll),
                  $"Caída de {label} corriendo: {(expectHard ? "aterrizaje fuerte absorbido con roll" : "aterrizaje suave")} (severidad {_movement.LandingSeverity:F2}, roll visto: {sawRoll})");
            if (expectHard)
            {
                Check(minScale < 0.7f, $"Caída de {label}: el impacto absorbe la velocidad (escala mínima {minScale:F2})");
                // (the mocap accelerates from a heavy landing's crouch in ~1.5–2 s: 2.6–3 m/s at 2.5 s, T27)
                Check(recovered > _movement.BaseSpeed * 0.75f, $"Caída de {label}: recupera la carrera ({recovered:F2} m/s a los 2.5 s)");
            }
            if (expectHard) Check(rollPose, $"Caída de {label}: el roll mira hacia donde avanza el cuerpo");
            Check(minSole > -Penetration, $"Caída de {label}: los pies no atraviesan el suelo al aterrizar (mínimo {minSole * 100f:F1} cm) {minAt}");
            yield return 0.8f;
        }

        // ── S02–S04: each standard vault from several positions, speeds and angles ─────────────────
        private static IEnumerator Vaults()
        {
            float front = ParkourTestCircuitBuilder.VaultFront;
            var low  = ParkourStandard.Spec(ParkourObstacleType.LowVault);
            var mid  = ParkourStandard.Spec(ParkourObstacleType.MediumVault);
            var high = ParkourStandard.Spec(ParkourObstacleType.HighVault);
            float lowX = ParkourTestCircuitBuilder.LowVaultX, midX = ParkourTestCircuitBuilder.MediumVaultX, highX = ParkourTestCircuitBuilder.HighVaultX;

            VaultResults.Clear();
            yield return VaultCase(lowX, front, low.Height, low.Depth, false, "bajo corriendo");
            yield return VaultCase(lowX, front, low.Height, low.Depth, false, "bajo corriendo, 1.2 m a la izquierda", lateral: 1.2f);
            yield return VaultCase(lowX, front, low.Height, low.Depth, false, "bajo corriendo en ángulo de 20°", yaw: 20f);
            yield return VaultCase(lowX, front, low.Height, low.Depth, true, "bajo esprintando");
            CheckConsistency("bajo");
            yield return VaultCase(lowX, front, low.Height, low.Depth, false, "bajo desde parado", standing: true);
            yield return VaultCase(lowX, front, low.Height, low.Depth, false, "bajo arrancando a caminar", walk: true);

            // Out of range (2 m from the face): Space is a jump, nothing is triggered by the raycast alone
            yield return Teleport(new Vector3(lowX, Floor + OriginAboveFeet, front + 2f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 1.2f;
            Check(!Visited.Contains(_movement.VaultState) && Visited.Contains(_movement.JumpState),
                  "Vault bajo fuera de alcance (2 m): Espacio salta, no dispara el vault");

            VaultResults.Clear();
            yield return VaultCase(midX, front, mid.Height, mid.Depth, false, "medio corriendo");
            yield return VaultCase(midX, front, mid.Height, mid.Depth, false, "medio corriendo, 1.2 m a la derecha", lateral: -1.2f);
            yield return VaultCase(midX, front, mid.Height, mid.Depth, false, "medio corriendo en ángulo de 20°", yaw: -20f);
            yield return VaultCase(midX, front, mid.Height, mid.Depth, true, "medio esprintando");
            CheckConsistency("medio");
            yield return VaultCase(midX, front, mid.Height, mid.Depth, false, "medio desde parado a 1.0 m", standing: true, standDistance: 1.0f);
            yield return VaultCase(midX, ParkourTestCircuitBuilder.MediumDeepFront, mid.Height, ParkourTestCircuitBuilder.MediumDeepDepth, false, "medio de 1.4 m de fondo");

            VaultResults.Clear();
            yield return VaultCase(highX, front, high.Height, high.Depth, false, "alto corriendo");
            yield return VaultCase(highX, front, high.Height, high.Depth, false, "alto corriendo, 1.2 m a la izquierda", lateral: 1.2f);
            yield return VaultCase(highX, front, high.Height, high.Depth, true, "alto esprintando");
            CheckConsistency("alto");
            yield return VaultCase(highX, front, high.Height, high.Depth, false, "alto desde parado a 1.0 m", standing: true, standDistance: 1.0f);

            // Too close for a tall vault's take-off (< 0.91 m): Space jumps instead of cutting through it
            yield return Teleport(new Vector3(highX, Floor + OriginAboveFeet, front + 0.6f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 1.4f;
            Check(!Visited.Contains(_movement.VaultState) && Visited.Contains(_movement.JumpState),
                  "Vault alto pegado al obstáculo (0.6 m): sin espacio para el despegue, salta en vez de atravesarlo");

            // Above the vault range (1.6 m, in the barrier band, on layer Obstacle): Space jumps.
            // Built only for this check and removed afterwards, so the scene keeps standard obstacles only.
            GameObject tall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tall.layer = LayerMask.NameToLayer("Obstacle");
            tall.transform.SetPositionAndRotation(new Vector3(highX, 0.8f, -20.25f), Quaternion.identity);
            tall.transform.localScale = new Vector3(4f, 1.6f, 0.5f);
            Physics.SyncTransforms();
            yield return Teleport(new Vector3(highX, Floor + OriginAboveFeet, -20f + 0.9f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 1.4f;
            Check(!Visited.Contains(_movement.VaultState) && !Visited.Contains(_movement.LedgeGrabState) && Visited.Contains(_movement.JumpState),
                  "Obstáculo de 1.6 m (fuera del rango del vault): salta en vez de hacer vault");
            Object.Destroy(tall);
            yield return 0.5f;
        }

        private struct VaultResult { public string Label; public float HandError, LandingBehind; }
        private static readonly List<VaultResult> VaultResults = new List<VaultResult>();

        /// <summary>
        /// The same obstacle must behave the same from any running position: hand on its point, and a
        /// landing at the same distance behind it (the approach speed is the only valid difference).
        /// </summary>
        private static void CheckConsistency(string type)
        {
            float minLand = float.MaxValue, maxLand = float.MinValue, maxHand = 0f;
            int n = 0;
            foreach (VaultResult r in VaultResults)
            {
                if (r.Label.Contains("esprintando")) continue; // faster: lands farther on purpose
                minLand = Mathf.Min(minLand, r.LandingBehind);
                maxLand = Mathf.Max(maxLand, r.LandingBehind);
                maxHand = Mathf.Max(maxHand, r.HandError);
                n++;
            }
            if (n < 2) return;
            Check(maxLand - minLand < 0.3f && maxHand < HandOnTarget,
                  $"Vault {type}: resultado consistente desde todas las posiciones corriendo (termina {minLand:F2}–{maxLand:F2} m detrás, mano a ≤ {maxHand * 100f:F1} cm)");
        }

        private static IEnumerator VaultCase(float laneX, float frontZ, float height, float depth, bool sprint, string label,
                                             bool standing = false, float lateral = 0f, float yaw = 0f, float standDistance = 0.85f, bool walk = false)
        {
            float backZ = frontZ - depth;
            label = $"{label} ({height:F2} m)";
            // An angled run-up starts off-axis so it reaches the obstacle near the lane's center
            float runUp = standing ? standDistance : walk ? 1.35f : 4f;
            float startX = laneX + lateral + Mathf.Tan(yaw * Mathf.Deg2Rad) * runUp;
            yield return Teleport(new Vector3(startX, Floor + OriginAboveFeet, frontZ + runUp), 180f + yaw);
            Visited.Clear();
            Key[] held = standing ? new Key[0] : sprint ? new[] { Key.LeftShift, Key.W } : new[] { Key.W };
            Keys(held);
            // Running, Space is pressed early (as a player does): the run keeps the intention until the
            // take-off point. Walking, it is pressed within reach.
            float pressAt = walk ? 1.0f : 2.2f;
            for (float t = 0f; t < 3f && !standing && _movement.transform.position.z - frontZ > pressAt; t += 0.02f) yield return 0.02f;
            float approach = Speed;
            yield return Tap(Key.Space, held);

            bool sawAnim = false;
            float handError = float.MaxValue, minFeetOver = float.MaxValue, exitSpeed = -1f, facingErr = -1f, exitZ = 0f;
            float rootSpeed = 0f, maxStep = 0f, maxLean = 0f;
            string stepAt = "";
            ResetStep();
            for (float t = 0f; t < 2f; t += 0.02f)
            {
                sawAnim |= AnimIs(PlayerAnimatorIds.Vault);
                float stepNow = StepSpeed();
                if (stepNow > maxStep)
                {
                    maxStep = stepNow;
                    stepAt = $"[estado={Current.GetType().Name} n={_movement.ParkourProgress:F2} dt={_lastStepDt:F3} rootMotion={_movement.IsRootMotionDriven} pos={_movement.transform.position}]";
                }
                if (Current == _movement.VaultState)
                {
                    rootSpeed = Vector3.Dot(_movement.RootMotionVelocity, _movement.transform.forward);
                    maxLean = Mathf.Max(maxLean, _playerAnimator.Lean.magnitude);
                    if (_animator.GetFloat(PlayerAnimatorIds.LHandCurveParam) > 0.9f)
                        handError = Mathf.Min(handError, Vector3.Distance(_handL.position, _movement.VaultState.HandTarget));
                    foreach (Transform foot in new[] { _footL, _footR })
                    {
                        float z = foot.position.z;
                        if (z < frontZ && z > backZ && Mathf.Abs(foot.position.x - laneX) < 1.9f &&
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
                    exitZ = _movement.transform.position.z; // where the vault itself ends (the run after it follows the input)
                    facingErr = Mathf.Abs(Mathf.DeltaAngle(Yaw, 180f)); // at the end of the vault, before the run turns it
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
            if (yaw != 0f)
                Check(facingErr >= 0f && facingErr < 5f, $"Vault {label}: el cuerpo se alinea perpendicular al obstáculo (desvío {facingErr:F1}°)");
            Check(maxStep < TeleportSpeed, $"Vault {label}: sin teleport (velocidad máxima entre muestras {maxStep:F1} m/s) {stepAt}");
            Check(Mathf.Abs(exitSpeed - rootSpeed) < 1.0f, $"Vault {label}: sale a la velocidad que llevaba el clip ({rootSpeed:F2} → {exitSpeed:F2} m/s)");
            Check(maxLean < 0.01f, $"Vault {label}: la inclinación procedural está apagada durante el parkour ({maxLean:F2}°)");
            if (!standing && !walk)
            {
                Check(exitSpeed > _movement.BaseSpeed * 0.7f, $"Vault {label}: mantiene el impulso al salir ({exitSpeed:F2} m/s)");
                float entry = _movement.VaultState.ApproachSpeed;
                Check(Mathf.Abs(exitSpeed - entry) < entry * 0.15f, $"Vault {label}: entra y sale a la misma velocidad, sin acelerones ({entry:F2} → {exitSpeed:F2} m/s)");
                VaultResults.Add(new VaultResult { Label = label, HandError = handError, LandingBehind = backZ - exitZ });
            }
            float after = float.MaxValue;
            for (float t = 0f; t < 0.4f; t += 0.02f) { after = Mathf.Min(after, SoleClearance); yield return 0.02f; }
            Check(after > -Penetration && after < SoleOnGround, $"Vault {label}: después del vault los pies pisan el suelo ({after * 100f:F1} cm)");
            yield return 0.6f;
        }

        // ── S05: slide under the standard bar and through the tunnel ────────────────────────────────────────
        private static IEnumerator Slide()
        {
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.SlideX, Floor + OriginAboveFeet, -1f));
            Visited.Clear();
            Keys(Key.LeftShift, Key.W);
            // C within ParkourStandard.SlideEntryDistance of the bar (the slides of P33 speeds are
            // 1.8–4 m), and under the bar the ceiling keeps it going
            float barFront = ParkourTestCircuitBuilder.SlideBarFront;
            for (float t = 0f; t < 4f && _movement.transform.position.z > barFront + ParkourStandard.SlideEntryDistance; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.C, Key.LeftShift, Key.W);
            float maxColliderGap = 0f, minSole = float.MaxValue, maxHeadUnderBar = float.MinValue;
            bool slidePose = true;
            for (float t = 0f; t < 2.0f; t += 0.02f)
            {
                if (Current == _movement.SlideState)
                {
                    slidePose &= PoseFacesBody();
                    maxColliderGap = Mathf.Max(maxColliderGap, Mathf.Abs(_movement.FeetY - Floor));
                    if (LowestPoint - Floor < minSole) { minSole = LowestPoint - Floor; _feetDiag = LowestPart; }
                    float z = _movement.transform.position.z;
                    if (z < -9.4f && z > -10.6f) maxHeadUnderBar = Mathf.Max(maxHeadUnderBar, _head.position.y);
                }
                yield return 0.02f;
            }
            Check(Visited.Contains(_movement.SlideState), "Shift + C inicia el slide");
            Check(_movement.transform.position.z < -11f, $"Pasa bajo la barra (z = {_movement.transform.position.z:F2})");
            Check(maxColliderGap < 0.05f, $"Durante el slide el collider no flota ni se hunde (máximo {maxColliderGap * 100f:F1} cm)");
            Check(slidePose, "Durante el slide la pose mira hacia donde se desliza el cuerpo");
            Check(minSole > -Penetration, $"Durante el slide el cuerpo no atraviesa el suelo (mínimo {minSole * 100f:F1} cm, {_feetDiag})");
            Check(maxHeadUnderBar < 1.2f, $"Bajo la barra la cabeza queda por debajo de ella ({maxHeadUnderBar:F2} m < 1.2 m)");

            // Tunnel: the slide keeps going while there is a ceiling
            // C within SlideEntryDistance of the tunnel too (see the bar above)
            float tunnelFront = ParkourTestCircuitBuilder.TunnelFront;
            for (float t = 0f; t < 5f && _movement.transform.position.z > tunnelFront + ParkourStandard.SlideEntryDistance; t += 0.02f) yield return 0.02f;
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

        // ── S06: the standard ledge from the ground, running, chained, off-center and angled ───────
        private static IEnumerator LedgeGrab()
        {
            float x = ParkourTestCircuitBuilder.LedgeX, front = ParkourTestCircuitBuilder.LedgeFront;
            float h = ParkourStandard.Spec(ParkourObstacleType.Ledge).Height;
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, front + 0.75f), h, "desde parado", run: false, climb: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, front + 4f), h, "corriendo", run: true, climb: false, drop: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, front + 0.75f), h, "con Espacio doble (agarre → subida)", run: false, climb: true, chain: true);
            yield return LedgeCase(new Vector3(x + 1.2f, Floor + OriginAboveFeet, front + 0.75f), h, "1.2 m a la izquierda", run: false, climb: true);

            // Same ledge turned 30°: its pivot is on the face, and the face looks along −(prefab forward)
            Quaternion rot = Quaternion.Euler(0f, 180f + ParkourTestCircuitBuilder.LedgeAngle, 0f);
            Vector3 normal = -(rot * Vector3.forward);
            Vector3 start = new Vector3(x, 0f, ParkourTestCircuitBuilder.LedgeAngledFront) + normal * 0.8f;
            yield return LedgeCase(new Vector3(start.x, Floor + OriginAboveFeet, start.z), h, "en ángulo de 30°", run: false, climb: true);
        }

        private static IEnumerator LedgeCase(Vector3 start, float topY, string label, bool run, bool climb, bool chain = false, bool drop = false)
        {
            label = $"{topY:F1} m {label}";
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

        // ── S07: the standard climb wall (jump + grab), then a chained climb, a drop and stairs down ─
        private static IEnumerator Climb()
        {
            float x = ParkourTestCircuitBuilder.ClimbX;
            float wall = ParkourStandard.Spec(ParkourObstacleType.ClimbWall).Height;
            float ledge = ParkourStandard.Spec(ParkourObstacleType.Ledge).Height;
            float front = ParkourTestCircuitBuilder.ClimbWallFront;
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, front + 0.75f), wall, "saltando desde parado", run: false, climb: true);
            yield return LedgeCase(new Vector3(x, Floor + OriginAboveFeet, front + 4f), wall, "saltando corriendo", run: true, climb: true);

            float stack = ParkourTestCircuitBuilder.StackFront, upper = ParkourTestCircuitBuilder.StackUpperFront;
            yield return Teleport(new Vector3(x, Floor + OriginAboveFeet, stack + 0.75f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 0.15f;
            yield return Tap(Key.Space); // chained: grab → climb
            for (float t = 0f; t < 4f && !(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.IdleState); t += 0.02f) yield return 0.02f;
            Check(Mathf.Abs(_movement.FeetY - ledge) < 0.06f, $"Escalada: sube la cornisa de {ledge:F1} m (pies a {_movement.FeetY:F2} m)");

            // Walk to the second ledge (standing on the terrace) and climb it from its base
            Keys(Key.W);
            for (float t = 0f; t < 2f && _movement.transform.position.z > upper + 0.8f; t += 0.02f) yield return 0.02f;
            Keys();
            yield return 0.3f;
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 0.15f;
            yield return Tap(Key.Space);
            for (float t = 0f; t < 4f && !(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.IdleState); t += 0.02f) yield return 0.02f;
            Check(Mathf.Abs(_movement.FeetY - 2f * ledge) < 0.06f, $"Escalada: desde la terraza sube la segunda cornisa hasta {2f * ledge:F1} m (pies a {_movement.FeetY:F2} m)");

            // Drop onto the terrace and walk down the stairs
            float bottom = stack - ParkourTestCircuitBuilder.StackDepth - 9 * 0.45f;
            Keys(Key.W);
            for (float t = 0f; t < 8f && _movement.transform.position.z > bottom - 1f; t += 0.02f) yield return 0.02f;
            Keys();
            yield return 0.6f;
            Check(_movement.transform.position.z < bottom - 0.5f && Mathf.Abs(_movement.FeetY - Floor) < 0.06f,
                  $"Escalada: baja a la terraza y por la escalera hasta el suelo (z = {_movement.transform.position.z:F2})");
            yield return 0.5f;
        }

        // ── S09: the standard combined course in one run ─────────────────────────────────────────────────────────────
        private static IEnumerator Combined()
        {
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.ComboX, Floor + OriginAboveFeet, 3f));
            Visited.Clear();
            // Front faces along the lane (the course runs toward −Z from the lane start)
            float vaultA = -ComboFront(ParkourObstacleType.MediumVault), bar = -ComboFront(ParkourObstacleType.SlideBar);
            float wall = -ComboFront(ParkourObstacleType.Ledge), vaultB = -ComboFront(ParkourObstacleType.LowVault);
            float block = -ComboFront(ParkourObstacleType.Mantle);
            float blockTop = ParkourStandard.Spec(ParkourObstacleType.Mantle).Height;
            bool vault1 = false, slide = false, grab = false, climbTap = false, vault2 = false, mantle = false;
            Keys(Key.LeftShift, Key.W);
            for (float t = 0f; t < 22f && !(Visited.Contains(_movement.MantleState) && Current != _movement.MantleState); t += 0.02f)
            {
                float z = _movement.transform.position.z;
                PlayerState s = Current;
                bool free = s == _movement.RunState || s == _movement.IdleState;
                if (!vault1 && free && z - vaultA < 1.0f) { vault1 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (vault1 && !slide && free && z < vaultA - 2f && z - bar < ParkourStandard.SlideEntryDistance + 1f) { slide = true; yield return Tap(Key.C, Key.LeftShift, Key.W); continue; }
                if (slide && !grab && free && z < bar - 1.5f && z - wall < 0.9f) { grab = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (grab && !climbTap && s == _movement.LedgeGrabState) { climbTap = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (climbTap && !vault2 && free && z < wall - 4f && z - vaultB < 1.0f && _movement.IsGrounded) { vault2 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (vault2 && !mantle && free && z < vaultB - 2f && z - block < 2.2f && _movement.IsGrounded) { mantle = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                yield return 0.02f;
            }
            Keys();
            int vaults = Count(_movement.VaultState);
            Check(vaults >= 2 && Visited.Contains(_movement.SlideState) && Visited.Contains(_movement.LedgeGrabState) && Visited.Contains(_movement.LedgeClimbState),
                  $"Circuito combinado: vault ({vaults}), slide, agarre y subida en una sola carrera");
            Check(Visited.Contains(_movement.MantleState) && Mathf.Abs(_movement.FeetY - blockTop) < 0.06f,
                  $"Circuito combinado: corriendo, Espacio anticipado sube al bloque final con mantle (pies a {_movement.FeetY:F2} m)");
            yield return 1f;
        }

        // ── S10 / corridor: movement quality (slide by context, chaining, stops, reversals, climb → run) ─
        private static IEnumerator Flow()
        {
            // A. Sprint → Slide → Run
            SlideRun sprint = new SlideRun();
            yield return SlideCase(Corridor(5f), true, true, 0f, false, sprint, "sprint → slide → correr");
            Check(sprint.Entries == 1 && sprint.AnimRestarts == 0, $"Slide (sprint): un solo slide y la pose nunca se reinicia (entradas {sprint.Entries}, reinicios {sprint.AnimRestarts})");
            Check(sprint.EndReason == "momentum" && sprint.Exit == _movement.RunState, $"Slide (sprint): termina al gastar el momentum y sigue corriendo (motivo '{sprint.EndReason}')");
            Check(sprint.SpeedJump < 0.8f, $"Slide (sprint): la carrera continúa sin saltos de velocidad (cambio {sprint.SpeedJump:F2} m/s)");
            Check(sprint.MaxStep < TeleportSpeed, $"Slide (sprint): sin teleport ({sprint.MaxStep:F1} m/s)");

            // M. Entry speed changes the slide: a run slide is shorter than a sprint slide
            SlideRun run = new SlideRun();
            yield return SlideCase(Corridor(5f), false, true, 0f, false, run, "correr → slide");
            Check(run.Entries == 1 && run.Distance < sprint.Distance * 0.75f && run.Duration < sprint.Duration,
                  $"Slide: la velocidad de entrada cambia el slide (corriendo {run.Distance:F2} m en {run.Duration:F2} s, esprintando {sprint.Distance:F2} m en {sprint.Duration:F2} s)");

            // B. Sprint → Slide → Stop (input released: the legs brake the slide)
            SlideRun stop = new SlideRun();
            yield return SlideCase(Corridor(5f), true, false, 0f, false, stop, "sprint → slide → detenerse");
            Check(stop.Entries == 1 && stop.Exit == _movement.IdleState && stop.Duration < sprint.Duration,
                  $"Slide soltando el input: frena antes y se detiene ({stop.Duration:F2} s frente a {sprint.Duration:F2} s, sale a {stop.Exit?.GetType().Name})");
            Check(stop.AnimRestarts == 0 && stop.MaxStep < TeleportSpeed, $"Slide soltando el input: sin reinicios ni teleport ({stop.AnimRestarts}, {stop.MaxStep:F1} m/s)");

            // C. Sprint → Slide → Jump (Space chains, the jump keeps the slide's speed)
            SlideRun jump = new SlideRun();
            yield return SlideCase(Corridor(5f), true, true, 0.45f, false, jump, "sprint → slide → salto");
            Check(jump.Chained == _movement.JumpState && jump.JumpSpeedKept > -0.4f,
                  $"Slide → salto: Espacio encadena el salto con la velocidad del slide (diferencia {jump.JumpSpeedKept:F2} m/s)");

            // H. Walk → Slide: no momentum to spend, C does nothing
            yield return Teleport(Corridor(5f));
            Visited.Clear();
            Keys(Key.W);
            yield return 0.12f;
            float walkSpeed = Speed;
            yield return Tap(Key.C, Key.W);
            yield return 0.5f;
            Keys();
            Check(!Visited.Contains(_movement.SlideState), $"Caminar → slide: a {walkSpeed:F1} m/s no hay momentum, C no dispara el slide");
            yield return 0.8f;

            // Short space: sliding toward an obstacle stops short of it instead of running into it
            float fx = ParkourTestCircuitBuilder.FlowX, ffront = ParkourTestCircuitBuilder.FlowVaultFront;
            SlideRun blocked = new SlideRun();
            yield return SlideCase(new Vector3(fx, Floor + OriginAboveFeet, -1f), true, true, 0f, false, blocked, "slide hacia un obstáculo", tapAtZ: ffront + 4.2f);
            float gap = blocked.ExitZ - ffront;
            Check(blocked.Entries == 1 && blocked.EndReason == "obstáculo" && gap > 0.45f && !Visited.Contains(_movement.VaultState),
                  $"Slide con poco espacio: frena y se levanta antes del obstáculo (motivo '{blocked.EndReason}', a {gap:F2} m de la cara)");

            // Obstacle at the end of the slide + Space: the slide chains into the vault
            SlideRun chain = new SlideRun();
            yield return SlideCase(new Vector3(fx, Floor + OriginAboveFeet, -1f), true, true, 0f, true, chain, "slide → vault", tapAtZ: ffront + 4.2f);
            Check(chain.Exit == _movement.VaultState && _movement.transform.position.z < ffront - 0.5f,
                  $"Slide → vault: Espacio cerca del obstáculo encadena el vault (z = {_movement.transform.position.z:F2}, encadenó {chain.Chained?.GetType().Name}, motivo '{chain.EndReason}', salida {chain.Exit?.GetType().Name} en z = {chain.ExitZ:F2})");

            // I. Backward walk → stop
            yield return Teleport(Corridor(-10f));
            float yaw0 = Yaw;
            Keys(Key.S);
            yield return 1.0f;
            Keys();
            yield return 0.06f;
            float braking = Speed;
            yield return 1.2f; // the strafe takes' stop ends in ~1 s
            // (the strafe takes sway ±10°, as in the gaits below)
            Check(braking > 0.2f && braking < _movement.BackpedalSpeed && Speed < 0.05f && Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw)) < 10f,
                  $"Caminar hacia atrás → parar: frena gradualmente sin girar ({braking:F2} m/s a los 0.06 s, {Speed:F2} m/s a los 1.26 s, giró {Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw)):F1}°)");

            // Reversal while sprinting (S + Shift): the body brakes and pivots progressively, the camera stays put
            yield return Teleport(Corridor(-5f), 0f);
            var cam = Object.FindAnyObjectByType<CameraFollow>();
            Keys(Key.LeftShift, Key.W);
            yield return 1.2f;
            float camYaw0 = cam.Yaw, bodyYaw0 = Yaw;
            Keys(Key.LeftShift, Key.S);
            float minSpeed = float.MaxValue, skid = 0f, maxTurnStep = 0f, prevYaw = Yaw;
            for (float t = 0f; t < 2.4f; t += 0.02f)
            {
                minSpeed = Mathf.Min(minSpeed, Speed);
                if (Speed > 1.5f) skid = Mathf.Max(skid, Skid);
                maxTurnStep = Mathf.Max(maxTurnStep, Mathf.Abs(Mathf.DeltaAngle(prevYaw, Yaw)));
                prevYaw = Yaw;
                yield return 0.02f;
            }
            float turned = Mathf.Abs(Mathf.DeltaAngle(bodyYaw0, Yaw));
            float camMoved = Mathf.Abs(Mathf.DeltaAngle(camYaw0, cam.Yaw));
            Keys();
            // Under motion matching the mocap's own reversal ends 15–20° short of the input after 2.4 s
            // and closes the rest while running (T27)
            Check(turned > 158f && minSpeed < 2.5f && maxTurnStep < 30f && camMoved < 1f,
                  $"Media vuelta esprintando: frena y gira progresivamente sin mover la cámara (giró {turned:F0}°, paso máximo {maxTurnStep:F0}°, mínimo {minSpeed:F1} m/s, cámara {camMoved:F1}°)");
            // Motion matching plays the mocap's plant turn: the hips lead the travel by up to ~60° while
            // the foot is planted (the feet themselves are measured by MxMLocomotionProbe)
            Check(skid < 70f, $"Media vuelta esprintando: la velocidad no patina de lado (desvío {skid:F0}°)");
            yield return 1f;

            // Backpedal: the camera does not turn with the body
            yield return Teleport(Corridor(-10f));
            float camBack0 = cam.Yaw;
            Keys(Key.S);
            yield return 1.0f;
            Keys();
            Check(Mathf.Abs(Mathf.DeltaAngle(camBack0, cam.Yaw)) < 1f, "Caminar hacia atrás: la cámara se queda quieta");
            yield return 0.6f;

            // Sprint → stop: the momentum runs out gradually and the torso leans back against it
            yield return Teleport(Corridor(5f));
            Keys(Key.LeftShift, Key.W);
            yield return 1.4f;
            float cruise = Speed;
            Keys();
            yield return 0.2f; // motion matching picks the stop take and blends into it in ~0.1–0.2 s
            float early = Speed;
            float minPitch = 0f;
            // The mocap's actor stops a sprint in ~1.3–1.6 s (T27)
            for (float t = 0f; t < 2.0f; t += 0.02f) { minPitch = Mathf.Min(minPitch, _playerAnimator.Lean.y); yield return 0.02f; }
            Check(early > cruise * 0.3f && early < cruise && Speed < 0.05f && minPitch < -1f,
                  $"Sprint → parar: frena con inercia ({cruise:F1} → {early:F1} m/s a los 0.2 s) y el torso se echa atrás ({minPitch:F1}°)");
            yield return 0.5f;

            // K. Ledge grab → climb → run, as one sequence
            float lx = ParkourTestCircuitBuilder.LedgeX, lfront = ParkourTestCircuitBuilder.LedgeFront;
            float top = ParkourStandard.Spec(ParkourObstacleType.Ledge).Height;
            yield return Teleport(new Vector3(lx, Floor + OriginAboveFeet, lfront + 0.75f));
            Visited.Clear();
            yield return Tap(Key.Space);
            yield return 0.15f;
            yield return Tap(Key.Space);
            Keys(Key.W);
            float step = 0f;
            ResetStep();
            for (float t = 0f; t < 4f && !(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.RunState && Speed > 1.5f); t += 0.02f)
            {
                step = Mathf.Max(step, StepSpeed());
                yield return 0.02f;
            }
            Keys();
            Check(Visited.Contains(_movement.LedgeClimbState) && Current == _movement.RunState && Mathf.Abs(_movement.FeetY - top) < 0.06f,
                  $"Agarre → subida → correr: encadena hasta correr sobre la cima (pies a {_movement.FeetY - top:F2} m)");
            Check(step < TeleportSpeed, $"Agarre → subida → correr: sin teleport ({step:F1} m/s)");
            yield return 1f;
        }

        private class SlideRun
        {
            public int Entries, AnimRestarts;
            public float Distance, Duration, SpeedJump, MaxStep, JumpSpeedKept, ExitZ;
            public string EndReason = "";
            public PlayerState Exit, Chained;
        }

        /// <summary>
        /// Runs into a slide (sprinting or running), optionally releasing the input, pressing Space after
        /// <paramref name="spaceAfter"/> s or when close to an obstacle, and measures the slide.
        /// </summary>
        private static IEnumerator SlideCase(Vector3 start, bool sprint, bool keepInput, float spaceAfter, bool spaceNearObstacle,
                                             SlideRun r, string label, float tapAtZ = float.NaN)
        {
            yield return Teleport(start);
            Visited.Clear();
            Key[] held = sprint ? new[] { Key.LeftShift, Key.W } : new[] { Key.W };
            Keys(held);
            for (float t = 0f; t < 3f; t += 0.02f)
            {
                if (float.IsNaN(tapAtZ) ? t >= 1.3f : _movement.transform.position.z < tapAtZ) break;
                yield return 0.02f;
            }
            yield return Tap(Key.C, held);
            if (!keepInput) Keys();
            Key[] afterKeys = keepInput ? held : new Key[0];

            float enterZ = float.NaN, enterTime = 0f, lastSlideSpeed = 0f, lastNorm = -1f;
            int prevHash = 0;
            bool spaced = false;
            ResetStep();
            for (float t = 0f; t < 4f; t += 0.02f)
            {
                PlayerState cur = Current;
                r.MaxStep = Mathf.Max(r.MaxStep, StepSpeed());
                int hash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
                if (cur == _movement.SlideState)
                {
                    if (float.IsNaN(enterZ)) { enterZ = _movement.transform.position.z; enterTime = Time.time; }
                    lastSlideSpeed = Speed;
                    if (hash == PlayerAnimatorIds.SlideLoop)
                    {
                        float n = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        if (n + 0.01f < lastNorm) r.AnimRestarts++;
                        lastNorm = n;
                    }
                    if (hash == PlayerAnimatorIds.Slide && prevHash != PlayerAnimatorIds.Slide && prevHash != 0 && lastNorm >= 0f) r.AnimRestarts++;

                    bool nearObstacle = spaceNearObstacle && _movement.transform.position.z - ParkourTestCircuitBuilder.FlowVaultFront < 2.2f;
                    if (!spaced && ((spaceAfter > 0f && Time.time - enterTime >= spaceAfter) || nearObstacle))
                    {
                        spaced = true;
                        float before = Speed;
                        yield return Tap(Key.Space, afterKeys);
                        r.Chained = Current;
                        r.JumpSpeedKept = Speed - before;
                        continue;
                    }
                }
                else if (!float.IsNaN(enterZ) && r.Exit == null)
                {
                    r.Exit = cur;
                    r.Duration = Time.time - enterTime;
                    r.Distance = enterZ - _movement.transform.position.z;
                    r.ExitZ = _movement.transform.position.z;
                    r.EndReason = _movement.SlideState.EndReason;
                    yield return 0.1f;
                    r.SpeedJump = Mathf.Max(0f, lastSlideSpeed - Speed);
                    if (r.Chained == null) yield return 1.0f;
                    break;
                }
                prevHash = hash;
                yield return 0.02f;
            }
            r.Entries = Count(_movement.SlideState);
            Keys();
            Check(r.Entries >= 1, $"Slide {label}: C inicia el slide");
            yield return 1.0f;
        }

        // ── S11: mantle onto blocks of 0.9, 1.3 and 1.5 m, from a standstill and running ─────────
        private static IEnumerator Mantles()
        {
            float x = ParkourTestCircuitBuilder.MantleX;
            float std = ParkourStandard.Spec(ParkourObstacleType.Mantle).Height;
            yield return MantleCase(x, ParkourTestCircuitBuilder.MantleFront, std, false, "estándar desde parado");
            yield return MantleCase(x, ParkourTestCircuitBuilder.MantleLowFront, ParkourTestCircuitBuilder.MantleLowHeight, false, "bajo desde parado");
            yield return MantleCase(x, ParkourTestCircuitBuilder.MantleHighFront, ParkourTestCircuitBuilder.MantleHighHeight, false, "alto desde parado");
            yield return MantleCase(x, ParkourTestCircuitBuilder.MantleFront, std, true, "estándar corriendo, Espacio anticipado");
        }

        private static IEnumerator MantleCase(float laneX, float frontZ, float top, bool run, string label)
        {
            label = $"Mantle {label} ({top:F2} m)";
            yield return Teleport(new Vector3(laneX, Floor + OriginAboveFeet, frontZ + (run ? 5f : 0.7f)));
            Visited.Clear();
            if (run)
            {
                Keys(Key.W);
                for (float t = 0f; t < 3f && _movement.transform.position.z - frontZ > 2.2f; t += 0.02f) yield return 0.02f;
                yield return Tap(Key.Space, Key.W); // the run is held: the intention waits for the mantle's start
                for (float t = 0f; t < 1.5f && !Visited.Contains(_movement.MantleState); t += 0.02f) yield return 0.02f;
                Keys();
            }
            else
            {
                yield return Tap(Key.Space);
            }

            float handErr = float.MaxValue, minFootOver = float.MaxValue, maxStep = 0f;
            string handDiag = "";
            bool poseOk = true;
            ResetStep();
            for (float t = 0f; t < 2.5f && !(Visited.Contains(_movement.MantleState) && Current != _movement.MantleState); t += 0.02f)
            {
                maxStep = Mathf.Max(maxStep, StepSpeed());
                if (Current == _movement.MantleState)
                {
                    float n = _movement.ParkourProgress;
                    poseOk &= PoseFacesBody();
                    if (handDiag == "" && n > 0.33f)
                        handDiag = $"[n={n:F2} manoIzq={_handL.position} manoDer={_handR.position} cara z={frontZ}]";
                    if (n > ParkourTimings.MantleHandPlant + 0.06f && n < ParkourTimings.MantleHandRelease - 0.06f && _handL.position.z < frontZ)
                        handErr = Mathf.Min(handErr, Mathf.Abs(_handL.position.y - (top + ParkourTimings.WristAboveSurface)));
                    foreach (Transform foot in new[] { _footL, _footR })
                    {
                        float bottom = foot == _footL ? _animator.leftFeetBottomHeight : _animator.rightFeetBottomHeight;
                        if (foot.position.z < frontZ - 0.05f && Mathf.Abs(foot.position.x - laneX) < 1.9f)
                            minFootOver = Mathf.Min(minFootOver, foot.position.y - bottom - top);
                    }
                }
                yield return 0.02f;
            }
            Check(Visited.Contains(_movement.MantleState), $"{label}: Espacio frente al bloque hace mantle");
            Check(Mathf.Abs(_movement.FeetY - top) < 0.06f, $"{label}: queda de pie sobre la cima (pies a {_movement.FeetY - top:F2} m)");
            Check(handErr < HandOnTarget, $"{label}: la mano de apoyo descansa sobre la cima (a {handErr * 100f:F1} cm) {handDiag}");
            Check(minFootOver == float.MaxValue || minFootOver > -Penetration, $"{label}: los pies no atraviesan el bloque (mínimo {(minFootOver == float.MaxValue ? 0f : minFootOver) * 100f:F1} cm)");
            Check(maxStep < TeleportSpeed, $"{label}: sin teleport ({maxStep:F1} m/s)");
            Check(poseOk, $"{label}: la pose mira hacia donde sube el cuerpo");
            yield return 0.8f;
        }

        // ── Gaits (corridor): walk, strafe, walk/run backward, diagonals, back ↔ forward, sprint, crouch ─
        private static IEnumerator Gaits()
        {
            yield return GaitCase(new[] { Key.LeftCtrl, Key.W }, _movement.WalkSpeed, "caminar (Ctrl + W)", 0f, 1f);
            yield return GaitCase(new[] { Key.LeftCtrl, Key.D }, _movement.WalkSpeed, "strafe a la derecha caminando (Ctrl + D)", 1f, 0f);
            yield return GaitCase(new[] { Key.LeftCtrl, Key.S }, _movement.WalkSpeed, "caminar hacia atrás (Ctrl + S)", 0f, -1f);
            yield return GaitCase(new[] { Key.S, Key.A }, _movement.BackpedalSpeed, "diagonal hacia atrás corriendo (S + A)", -1f, -1f);

            // Backward → forward: no snap turn, the velocity reverses through zero
            yield return Teleport(Corridor(-10f));
            float yaw0 = Yaw;
            Keys(Key.S);
            yield return 0.8f;
            Keys(Key.W);
            float maxYaw = 0f, prevZ = _movement.Velocity.z, maxJump = 0f;
            for (float t = 0f; t < 1.0f; t += 0.02f)
            {
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw)));
                maxJump = Mathf.Max(maxJump, Mathf.Abs(_movement.Velocity.z - prevZ));
                prevZ = _movement.Velocity.z;
                yield return 0.02f;
            }
            Keys();
            Check(maxYaw < 10f && maxJump < 0.6f, $"Atrás → adelante: sin giro (máximo {maxYaw:F1}°) y la velocidad cambia de sentido gradualmente (salto máximo {maxJump:F2} m/s por muestra)");
            yield return 0.8f;

            // Sprint: motion matching carries the body with the mocap's own sprint (no clip played faster)
            yield return Teleport(Corridor(5f));
            Keys(Key.LeftShift, Key.W);
            yield return 1.4f;
            float weight = _movement.Locomotion != null ? _movement.Locomotion.Weight : 0f;
            Keys();
            Check(weight > 0.99f, $"Sprint: el motion matching lleva el cuerpo (peso {weight:F2})");
            yield return 1.2f;

            // Crouch: C toggles it, the collider lowers, it moves slowly, C stands up
            yield return Teleport(Corridor(-20f));
            Visited.Clear();
            float standing = _movement.Controller.height;
            yield return Tap(Key.C);
            yield return 0.3f;
            float crouched = _movement.Controller.height;
            Keys(Key.W);
            yield return 1.2f;
            float crouchSpeed = Speed;
            bool crouchPose = PoseFacesBody();
            Keys();
            yield return 0.4f;
            yield return Tap(Key.C);
            yield return 0.3f;
            Check(Visited.Contains(_movement.CrouchState) && crouched < standing * 0.7f && Mathf.Abs(crouchSpeed - _movement.CrouchSpeed) < 0.2f &&
                  Current == _movement.IdleState && Mathf.Abs(_movement.Controller.height - standing) < 0.01f,
                  $"Agacharse: C baja el collider ({crouched:F2} de {standing:F2} m), camina a {crouchSpeed:F2} m/s y C se levanta");
            Check(crouchPose, "Agachado caminando, la pose mira hacia donde avanza");
            yield return 0.5f;
        }

        private static IEnumerator GaitCase(Key[] keys, float expectedSpeed, string label, float expectX, float expectZ)
        {
            yield return Teleport(Corridor(-10f));
            float yaw0 = Yaw;
            Keys(keys);
            yield return 1.2f;
            float speed = Speed, yaw = Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw));
            float mx = _animator.GetFloat(PlayerAnimatorIds.MoveXParam), mz = _animator.GetFloat(PlayerAnimatorIds.MoveZParam);
            Keys();
            bool dirOk = (expectX == 0f || (Mathf.Sign(mx) == Mathf.Sign(expectX) && Mathf.Abs(mx) > 0.8f)) &&
                         (expectZ == 0f || (Mathf.Sign(mz) == Mathf.Sign(expectZ) && Mathf.Abs(mz) > 0.8f));
            // (the strafe takes sway ±10° and the motor corrects the facing under them)
            // (the 100STYLE takes run a little slower in the diagonals: ±0.35 m/s, P34)
            Check(Mathf.Abs(speed - expectedSpeed) < 0.35f && yaw < 10f && dirOk,
                  $"Marcha: {label} a {speed:F2} m/s (esperado {expectedSpeed:F2}), cuerpo sin girar ({yaw:F1}°), blend en ({mx:F2}, {mz:F2})");
            yield return 0.8f;
        }

        // ── S06: lower from the top onto a hang (C at the edge), then jump off the wall ─────────────
        private static IEnumerator DropAndJumpOff()
        {
            float x = ParkourTestCircuitBuilder.LedgeX, front = ParkourTestCircuitBuilder.LedgeFront;
            var spec = ParkourStandard.Spec(ParkourObstacleType.Ledge);
            float back = front - spec.Depth;
            yield return Teleport(new Vector3(x, spec.Height + OriginAboveFeet, back + 0.5f));
            Visited.Clear();
            yield return Tap(Key.C);
            for (float t = 0f; t < 3f && !(Current == _movement.LedgeGrabState && _movement.LedgeGrabState.IsAttached); t += 0.02f) yield return 0.02f;
            yield return 0.5f;
            LedgeInfo ledge = _movement.CurrentLedge;
            float handErr = Mathf.Max(HandEdgeError(ledge, _handL), HandEdgeError(ledge, _handR));
            float footIn = Mathf.Min(FaceDistance(ledge, _footL), FaceDistance(ledge, _footR));
            float facing = Vector3.Angle(_movement.transform.forward, -ledge.Normal);
            Check(Visited.Contains(_movement.LedgeDropState) && Current == _movement.LedgeGrabState,
                  "Drop: C en el borde baja hasta quedar colgado");
            Check(handErr < HandOnTarget * 1.5f && footIn > -Penetration && facing < 10f,
                  $"Drop: manos en el borde ({handErr * 100f:F1} cm), pies fuera del muro ({footIn * 100f:F1} cm), de frente al muro ({facing:F0}°)");

            // Space + the direction away from the wall (W: the camera still looks that way): jump off
            Visited.Clear();
            Keys(Key.W);
            yield return Tap(Key.Space, Key.W);
            for (float t = 0f; t < 2.5f && !(Visited.Contains(_movement.FallState) && _movement.IsGrounded); t += 0.02f) yield return 0.02f;
            Keys();
            yield return 0.3f;
            float away = Vector3.Dot(_movement.transform.position - ledge.Edge, ledge.Normal);
            Check(Visited.Contains(_movement.FallState) && _movement.IsGrounded && away > 1.0f && Mathf.Abs(_movement.FeetY - Floor) < 0.06f,
                  $"Salto desde la cornisa: se impulsa lejos del muro y aterriza ({away:F2} m del muro)");
            yield return 0.8f;
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

        /// <summary>Distance of an obstacle's front face from the start of the combined course (prefab layout).</summary>
        private static float ComboFront(ParkourObstacleType type)
        {
            foreach ((ParkourObstacleType t, float front) in ParkourObstaclePrefabs.CombinedLayout)
                if (t == type) return front;
            return 0f;
        }

        // ─── Measurements ────────────────────────────────────────────────────────────

        private static PlayerState Current => _movement.StateMachine.CurrentState;
        private static Vector3 Corridor(float z) => new Vector3(CorridorX, Floor + OriginAboveFeet, z);
        /// <summary>Forward velocity the locomotion blend reads (MoveZ, m/s).</summary>
        private static float AnimSpeed => _animator.GetFloat(PlayerAnimatorIds.MoveZParam);

        /// <summary>
        /// The animated pose faces where the body faces: its right hip is on the body's right. A clip
        /// imported with the wrong orientation shows a character moving one way while facing the other.
        /// </summary>
        private static bool PoseFacesBody()
        {
            Vector3 hips = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            return Vector3.Dot(hips, _movement.transform.right) > 0f;
        }

        /// <summary>Angle (°) between the horizontal velocity and the facing while moving: sideways skating.</summary>
        private static float Skid
        {
            get
            {
                Vector3 v = _movement.Velocity;
                v.y = 0f;
                if (v.magnitude < 1f) return 0f;
                float a = Vector3.Angle(v, _movement.transform.forward);
                return _movement.IsBackpedaling ? Mathf.Abs(180f - a) : a;
            }
        }

        private static void ResetStep()
        {
            _stepPos = _movement.transform.position;
            _stepTime = Time.time;
        }

        /// <summary>Speed of the body since the last sample (m/s); a jump in position shows as a huge value.</summary>
        private static float StepSpeed()
        {
            float dt = Time.time - _stepTime;
            Vector3 p = _movement.transform.position;
            float v = dt > 0.005f ? Vector3.Distance(p, _stepPos) / dt : 0f;
            if (dt > 0.005f) _lastStepDt = dt;
            if (dt > 0.005f) { _stepPos = p; _stepTime = Time.time; }
            return v;
        }
        private static float Yaw => _movement.transform.eulerAngles.y;

        private static float Speed
        {
            get
            {
                Vector3 v = _movement.Velocity;
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

        /// <summary>Which part is the lowest point (diagnostic).</summary>
        private static string LowestPart
        {
            get
            {
                float hand = Mathf.Min(_handL.position.y, _handR.position.y) - 0.04f, head = _head.position.y - 0.1f;
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                string anim = $"anim={info.shortNameHash} n={info.normalizedTime:F2} transición={_animator.IsInTransition(0)} pesoIK={_animator.GetIKPositionWeight(AvatarIKGoal.LeftHand):F2}";
                return (Sole <= hand && Sole <= head ? $"suela {Sole:F2}" : hand <= head ? $"mano izq {_handL.position.y:F3} der {_handR.position.y:F3}" : $"cabeza {head + 0.1f:F2}") + " " + anim;
            }
        }

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

        /// <summary>Places the player standing at <paramref name="position"/> facing <paramref name="yaw"/> (−Z, toward the lanes, by default) and lets the camera settle.</summary>
        private static IEnumerator Teleport(Vector3 position, float yaw = 180f)
        {
            Keys();
            for (float t = 0f; t < 4f && Current != _movement.IdleState; t += 0.1f)
                yield return 0.1f;

            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            _movement.Teleport(position, facing);
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
