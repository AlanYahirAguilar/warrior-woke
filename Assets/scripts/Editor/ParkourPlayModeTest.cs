using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// ground, facing the wall, landing weight and momentum (docs/features.md F32). The vaults are
    /// also drawn as storyboards of the real game (Logs/PlayModeVaults) for visual review, and the
    /// combat's impacts on the training dummy as a sheet (Logs/PlayModeCombat).
    /// The whole run is bounded by TimeoutSeconds of real time.
    /// Menu: Tools → Warrior Woke → Probar Personaje en Play Mode.
    /// Batch: -executeMethod WarriorWoke.EditorTools.ParkourPlayModeTest.RunBatch (do not pass -quit);
    /// add "-wwSections Vaults,Slide" to run only some sections.
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
        private static Transform      _hips, _kneeL, _kneeR, _toeL, _toeR, _armL, _armR;
        private static double         _startedAt;
        private static string         _feetDiag = "";
        private static PlayerAnimator _playerAnimator;
        private static Vector3        _stepPos;
        private static float          _stepTime, _lastStepDt;

        // Highest speed the body may show between two samples: anything faster is a visible teleport
        private const float TeleportSpeed = 13f;

        // Real seconds the whole Play Mode test may take before it is stopped as failed
        private const double TimeoutSeconds = 1500.0;

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
                _startedAt = EditorApplication.timeSinceStartup;
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
            if (EditorApplication.timeSinceStartup - _startedAt > TimeoutSeconds)
            {
                Debug.LogError($"{Tag} TIEMPO AGOTADO: la prueba superó {TimeoutSeconds:F0} s reales y se detiene");
                _fails++;
                Finish();
                return;
            }
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
            _hips  = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _kneeL = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _kneeR = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _toeL  = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
            _toeR  = _animator.GetBoneTransform(HumanBodyBones.RightToes);
            _armL  = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _armR  = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _playerAnimator = _movement.GetComponent<PlayerAnimator>();
            _movement.StateChanged += s => Visited.Add(s);

            yield return Spawn();
            if (Runs("Locomotion"))     yield return Locomotion();
            if (Runs("JumpAndLandings")) yield return JumpAndLandings();
            if (Runs("Vaults"))         yield return Vaults();
            if (Runs("Slide"))          yield return Slide();
            if (Runs("LedgeGrab"))      yield return LedgeGrab();
            if (Runs("Climb"))          yield return Climb();
            if (Runs("Combined"))       yield return Combined();
            if (Runs("Flow"))           yield return Flow();
            if (Runs("Mantles"))        yield return Mantles();
            if (Runs("Gaits"))          yield return Gaits();
            if (Runs("DropAndJumpOff")) yield return DropAndJumpOff();
            if (Runs("Rig"))            yield return Rig();
            if (Runs("Combat"))         yield return Combat();
        }

        /// <summary>
        /// Sections to run: all, or only those listed after -wwSections on the command line
        /// (comma-separated, e.g. "-wwSections Vaults,Flow"), to iterate on one mechanic in batch.
        /// </summary>
        private static bool Runs(string section)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-wwSections") continue;
                foreach (string name in args[i + 1].Split(','))
                    if (name.Trim() == section) return true;
                return false;
            }
            return true;
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
            yield return 1.0f;
            // The mocap's sprint takes run at 4.4–5.4 m/s (PlayerMxMLocomotion slows the fastest down to 88 %, T27).
            // Its start-from-standing take varies between runs (motion matching is not deterministic across
            // scenarios: 3.8–5.3 m/s at 1.4 s), so the check is the speed the sprint holds from 1 to 2.4 s
            float sprintSum = 0f;
            string sprintSeries = "";
            for (int i = 0; i < 70; i++)
            {
                sprintSum += Speed;
                if (i % 10 == 0) sprintSeries += $"{Speed:F1} ";
                yield return 0.02f;
            }
            float sprintMean = sprintSum / 70f;
            Check(Mathf.Abs(sprintMean - _movement.SprintSpeed) < 0.6f, $"Sprint a {sprintMean:F2} m/s de media (SprintSpeed {_movement.SprintSpeed}; de 1.0 a 2.4 s cada 0.2 s: {sprintSeries.Trim()})");
            Keys();
            yield return 1.2f;

            // Curbs up (auto step) and stairs down (step down) without falling
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LocomotionX, Floor + OriginAboveFeet, -4f));
            Visited.Clear();
            Keys(Key.W);
            // 12 m at the run of P33 (3.4 m/s) plus the start from standing
            string fallDiag = "";
            for (float t = 0f; t < 5f && _movement.transform.position.z > -16f; t += 0.02f)
            {
                if (fallDiag.Length == 0 && Current == _movement.FallState)
                    fallDiag = $"; cae en z = {_movement.transform.position.z:F2}, pies {_movement.FeetY:F2}, suelo debajo {GroundUnder(_movement.transform.position):F2}, vy {_movement.Velocity.y:F2}";
                yield return 0.02f;
            }
            Keys();
            Check(_movement.transform.position.z < -15.5f && !Visited.Contains(_movement.FallState),
                  $"Sube los bordillos de 0.15–0.35 m sin detenerse ni caer (z = {_movement.transform.position.z:F2}{fallDiag})");
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
            yield return 0.2f; // the ground check reports ground up to 0.15 m below the feet: let the body settle
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

        // ── S02–S04: vaults by context (P36) ────────────────────────────────────────────────────────
        // Each standard vault from a standstill, walking, running and sprinting, from several distances,
        // lateral offsets and angles, and other depths; and what must NOT be vaulted (too high, too deep
        // for a slow approach, too close or too far standing, a blocked landing): the action the geometry
        // allows happens instead and nothing goes through the obstacle. Every vault is measured on the
        // animated skeleton and drawn in a storyboard (Logs/PlayModeVaults) for visual review.
        private static IEnumerator Vaults()
        {
            VaultCatalog catalog = _movement.VaultCatalog;
            int clips = catalog != null && catalog.Variants != null ? catalog.Variants.Length : 0;
            if (!Check(clips > 0, $"El Player tiene el catálogo de vaults ({clips} clips)")) yield break;
            Directory.CreateDirectory(VaultSheetFolder);

            float front = ParkourTestCircuitBuilder.VaultFront;
            ParkourObstacleSpec lowSpec  = ParkourStandard.Spec(ParkourObstacleType.LowVault);
            ParkourObstacleSpec midSpec  = ParkourStandard.Spec(ParkourObstacleType.MediumVault);
            ParkourObstacleSpec highSpec = ParkourStandard.Spec(ParkourObstacleType.HighVault);
            float lowX = ParkourTestCircuitBuilder.LowVaultX, midX = ParkourTestCircuitBuilder.MediumVaultX, highX = ParkourTestCircuitBuilder.HighVaultX;
            var low  = new Box(lowX, front, lowSpec.Height, lowSpec.Depth);
            var mid  = new Box(midX, front, midSpec.Height, midSpec.Depth);
            var deep = new Box(midX, ParkourTestCircuitBuilder.MediumDeepFront, midSpec.Height, ParkourTestCircuitBuilder.MediumDeepDepth);
            var high = new Box(highX, front, highSpec.Height, highSpec.Depth);

            // Medium vault, the reference case: every approach
            var sheet = new VaultSheet("1_medio", 12);
            VaultResults.Clear();
            yield return VaultCase(sheet, mid, Gait.Stand, "medio desde parado a 1.5 m", standAt: 1.5f);
            yield return VaultCase(sheet, mid, Gait.Walk, "medio caminando", pressAt: 2.0f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo", pressAt: 2.4f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo, Espacio anticipado (3.2 m)", pressAt: 3.2f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo, Espacio tardío (1.4 m)", pressAt: 1.4f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo, 1.2 m a la derecha", pressAt: 2.4f, lateral: -1.2f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo en ángulo de 20°", pressAt: 2.3f, yaw: -20f);
            yield return VaultCase(sheet, mid, Gait.Run, "medio corriendo en ángulo de 35°", pressAt: 2.0f, yaw: 35f);
            yield return VaultCase(sheet, mid, Gait.Sprint, "medio esprintando", pressAt: 2.8f);
            CheckConsistency("medio");
            yield return VaultCase(sheet, deep, Gait.Run, "medio de 1.4 m de fondo corriendo", pressAt: 2.4f);
            yield return VaultCase(sheet, deep, Gait.Sprint, "medio de 1.4 m de fondo esprintando", pressAt: 2.8f);
            GameObject temp = TempBox(new Box(midX, -15f, midSpec.Height, 0.6f));
            yield return VaultCase(sheet, new Box(midX, -15f, midSpec.Height, 0.6f), Gait.Run, "medio de 0.6 m de fondo corriendo", pressAt: 2.4f);
            Object.Destroy(temp);
            sheet.Save();

            // Low vault
            sheet = new VaultSheet("2_bajo", 6);
            VaultResults.Clear();
            yield return VaultCase(sheet, low, Gait.Stand, "bajo desde parado a 1.0 m", standAt: 1.0f);
            yield return VaultCase(sheet, low, Gait.Walk, "bajo caminando", pressAt: 1.4f);
            yield return VaultCase(sheet, low, Gait.Run, "bajo corriendo", pressAt: 2.2f);
            yield return VaultCase(sheet, low, Gait.Run, "bajo corriendo, 1.2 m a la izquierda", pressAt: 2.2f, lateral: 1.2f);
            yield return VaultCase(sheet, low, Gait.Run, "bajo corriendo en ángulo de 20°", pressAt: 2.2f, yaw: 20f);
            yield return VaultCase(sheet, low, Gait.Sprint, "bajo esprintando", pressAt: 2.8f);
            CheckConsistency("bajo");
            sheet.Save();

            // High vault: only a run's momentum carries the body over 1.1 m (no slow clip reaches it)
            sheet = new VaultSheet("3_alto", 3);
            VaultResults.Clear();
            yield return VaultCase(sheet, high, Gait.Run, "alto corriendo", pressAt: 2.4f);
            yield return VaultCase(sheet, high, Gait.Run, "alto corriendo, 1.2 m a la izquierda", pressAt: 2.4f, lateral: 1.2f);
            yield return VaultCase(sheet, high, Gait.Sprint, "alto esprintando", pressAt: 2.8f);
            CheckConsistency("alto");
            sheet.Save();

            // Not vaulted: no clip fits, so the geometry's other action happens (or the jump), never a vault
            yield return NoVaultCase(high, Gait.Stand, "alto desde parado a 1.5 m (ningún clip lento llega a 1.1 m)", standAt: 1.5f, expect: _movement.JumpState);
            yield return NoVaultCase(high, Gait.Walk, "alto caminando", pressAt: 2.0f, expect: _movement.JumpState);
            yield return NoVaultCase(mid, Gait.Stand, "medio pegado al obstáculo (0.5 m: sin espacio para la carrera del clip)", standAt: 0.5f, expect: _movement.JumpState);
            yield return NoVaultCase(mid, Gait.Stand, "medio desde parado lejos (3.0 m: más de dos pasos)", standAt: 3.0f, expect: _movement.JumpState);
            yield return NoVaultCase(deep, Gait.Walk, "medio de 1.4 m de fondo caminando: sube con mantle", pressAt: 0.9f, expect: _movement.MantleState);

            var tooHigh = new Box(highX, -15f, 1.2f, 0.3f);
            temp = TempBox(tooHigh);
            yield return NoVaultCase(tooHigh, Gait.Run, "de 1.2 m corriendo (más alto que el vault)", pressAt: 2.4f, expect: null);
            Object.Destroy(temp);

            var walkDeep = new Box(midX, -15f, midSpec.Height, 0.6f);
            temp = TempBox(walkDeep);
            yield return NoVaultCase(walkDeep, Gait.Walk, "medio de 0.6 m de fondo caminando (ningún clip lento lo cubre)", pressAt: 2.0f, expect: _movement.JumpState);
            Object.Destroy(temp);

            // A wall right behind the medium vault: no room to land
            var wall = new Box(midX, mid.BackZ - 0.6f, 3f, 2f);
            temp = TempBox(wall);
            yield return NoVaultCase(mid, Gait.Run, "medio con un muro detrás (aterrizaje bloqueado)", pressAt: 2.4f, expect: null, extra: wall);
            Object.Destroy(temp);
            yield return 0.5f;
        }

        private enum Gait { Stand, Walk, Run, Sprint }

        private static Key[] GaitKeys(Gait gait) =>
            gait == Gait.Walk   ? new[] { Key.LeftCtrl, Key.W } :
            gait == Gait.Run    ? new[] { Key.W } :
            gait == Gait.Sprint ? new[] { Key.LeftShift, Key.W } : new Key[0];

        /// <summary>A box obstacle of a vault lane: its approach face at FrontZ, facing +Z (the lanes run toward −Z).</summary>
        private readonly struct Box
        {
            public readonly float LaneX, FrontZ, Height, Depth;
            public Box(float laneX, float frontZ, float height, float depth) { LaneX = laneX; FrontZ = frontZ; Height = height; Depth = depth; }
            public float BackZ => FrontZ - Depth;

            /// <summary>How deep (m) a point is inside the box (≤ 0: outside).</summary>
            public float Inside(Vector3 p) => Mathf.Min(Mathf.Min(FrontZ - p.z, p.z - BackZ),
                                                        Mathf.Min(Floor + Height - p.y, ParkourStandard.PrefabWidth * 0.5f - Mathf.Abs(p.x - LaneX)));
        }

        /// <summary>A box on layer Obstacle, built only for one check and removed afterwards (the scene keeps standard obstacles only).</summary>
        private static GameObject TempBox(Box o)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PruebaTemporal";
            go.layer = LayerMask.NameToLayer("Obstacle");
            go.transform.SetPositionAndRotation(new Vector3(o.LaneX, Floor + o.Height * 0.5f, o.FrontZ - o.Depth * 0.5f), Quaternion.identity);
            go.transform.localScale = new Vector3(ParkourStandard.PrefabWidth, o.Height, o.Depth);
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>Deepest point of the body inside the obstacle (m): soles, toes, knees, hips and hands.</summary>
        private static float BodyInside(Box o, out string part)
        {
            float worst = float.MinValue;
            string worstPart = "";
            void Probe(Vector3 p, string name)
            {
                float d = o.Inside(p);
                if (d > worst) { worst = d; worstPart = name; }
            }
            Probe(_footL.position + Vector3.down * _animator.leftFeetBottomHeight, "suela izq");
            Probe(_footR.position + Vector3.down * _animator.rightFeetBottomHeight, "suela der");
            if (_toeL != null) Probe(_toeL.position, "dedos izq");
            if (_toeR != null) Probe(_toeR.position, "dedos der");
            Probe(_kneeL.position, "rodilla izq");
            Probe(_kneeR.position, "rodilla der");
            Probe(_hips.position, "cadera");
            Probe(_handL.position, "mano izq");
            Probe(_handR.position, "mano der");
            part = worstPart;
            return worst;
        }

        private struct VaultResult { public string Label, Clip; public float HandError, LandingBehind; }
        private static readonly List<VaultResult> VaultResults = new List<VaultResult>();

        /// <summary>
        /// The same obstacle must behave the same from any running position: the hand on its point and,
        /// with the same clip, a landing at the same distance behind it (sprinting lands farther on purpose).
        /// </summary>
        private static void CheckConsistency(string type)
        {
            float maxHand = 0f;
            var landings = new Dictionary<string, Vector2>();
            int n = 0;
            foreach (VaultResult r in VaultResults)
            {
                if (!r.Label.Contains("corriendo") || r.Label.Contains("fondo")) continue;
                maxHand = Mathf.Max(maxHand, r.HandError);
                string clip = r.Clip.Replace("_M", "");
                landings[clip] = landings.TryGetValue(clip, out Vector2 range)
                    ? new Vector2(Mathf.Min(range.x, r.LandingBehind), Mathf.Max(range.y, r.LandingBehind))
                    : new Vector2(r.LandingBehind, r.LandingBehind);
                n++;
            }
            if (n < 2) return;
            float spread = 0f;
            string summary = "";
            foreach (KeyValuePair<string, Vector2> kv in landings)
            {
                spread = Mathf.Max(spread, kv.Value.y - kv.Value.x);
                summary += $" {kv.Key}: {kv.Value.x:F2}–{kv.Value.y:F2} m";
            }
            Check(spread < 0.35f && maxHand < VaultHandTolerance,
                  $"Vault {type}: resultado consistente desde todas las posiciones corriendo (aterrizaje tras la cara trasera por clip:{summary}; mano a ≤ {maxHand * 100f:F1} cm)");
        }

        /// <summary>A planted palm may be this far (m) from its plant point on the top while it is down.</summary>
        private const float VaultHandTolerance = 0.07f;

        /// <summary>
        /// One vault: placed <paramref name="standAt"/> m from the face (standing), or running up in the
        /// gait and pressing Space <paramref name="pressAt"/> m from it (offset <paramref name="lateral"/> m,
        /// at <paramref name="yaw"/>° to the face). Measures the plan, the contacts, the trajectory and
        /// the hand-over to the locomotion, and draws one storyboard row.
        /// </summary>
        private static IEnumerator VaultCase(VaultSheet sheet, Box o, Gait gait, string label,
                                             float pressAt = 0f, float standAt = 0f, float lateral = 0f, float yaw = 0f)
        {
            label = $"{label} ({o.Height:F2} × {o.Depth:F2} m)";
            bool moving = gait != Gait.Stand;
            float runUp = moving ? pressAt + (gait == Gait.Walk ? 2f : gait == Gait.Run ? 3.5f : 5f) : standAt;
            float startX = o.LaneX + lateral + Mathf.Tan(yaw * Mathf.Deg2Rad) * runUp;
            yield return Teleport(new Vector3(startX, Floor + OriginAboveFeet, o.FrontZ + runUp), 180f + yaw);
            Visited.Clear();
            Key[] held = GaitKeys(gait);
            Keys(held);
            for (float t = 0f; t < 5f && moving && _movement.transform.position.z - o.FrontZ > pressAt; t += 0.02f) yield return 0.02f;
            float approach = Speed, pressed = _movement.transform.position.z - o.FrontZ;
            yield return Tap(Key.Space, held);

            sheet.NextRow(label);
            VaultVariant v = null;
            VaultPlan plan = null;
            float entryTime = 0f, entrySpeed = 0f, entryDistance = 0f, startedAt = 0f;
            float handWindow = -1f, handAtPlant = float.MaxValue, handNear = float.MaxValue;
            float secondWindow = -1f, secondAtPlant = float.MaxValue, secondNear = float.MaxValue;
            float maxInside = float.MinValue, maxStep = 0f, maxLean = 0f, rootSpeed = 0f, minFloor = float.MaxValue;
            float takeoff = float.NaN, landBehind = float.NaN, apex = float.MinValue, landSole = float.MaxValue;
            float exitSpeed = -1f, exitYaw = -1f, exitAt = 0f, runOutMin = float.MaxValue, speedJump = 0f;
            string insideAt = "", stepAt = "", floorAt = "", runOut = "", handWindowAt = "";
            float nextRunOutSample = 0f;
            bool sawState = false;
            var descent = new List<Vector2>();
            int column = 0;
            ResetStep();
            for (float t = 0f; t < 5f; t += 0.02f)
            {
                float step = StepSpeed();
                if (step > maxStep) { maxStep = step; stepAt = $"[{Current.GetType().Name} clip={_movement.VaultState.ClipTime:F2} dt={_lastStepDt:F3}]"; }

                if (Current == _movement.VaultState)
                {
                    if (v == null)
                    {
                        plan = _movement.VaultState.Plan;
                        v = plan.Variant;
                        entryTime = plan.EntryTime;
                        entrySpeed = _movement.VaultState.ApproachSpeed;
                        entryDistance = Vector3.Dot(plan.FrontEdge - _movement.transform.position, plan.Direction);
                        startedAt = Time.time;
                    }
                    if (v == null) break;
                    float ct = _movement.VaultState.ClipTime;
                    sawState |= AnimIs(Animator.StringToHash(v.State));
                    rootSpeed = Vector3.Dot(_movement.RootMotionVelocity, _movement.transform.forward);
                    maxLean = Mathf.Max(maxLean, _playerAnimator.Lean.magnitude);

                    // Palms on their plant points while the clip has them down
                    Transform first = v.RightHand ? _handR : _handL;
                    float e1 = Vector3.Distance(first.position, plan.PlantPoint);
                    if (ct >= v.Plant && ct <= plan.PlantRelease && e1 > handWindow)
                    {
                        handWindow = e1;
                        Vector3 sh = (v.RightHand ? _armR : _armL).position;
                        handWindowAt = $"[clip={ct:F2} (apoyo {v.Plant:F2}–{plan.PlantRelease:F2}, clip hasta {v.PlantRelease:F2}), hombro a {Vector3.Distance(sh, plan.PlantPoint):F2} m del punto, mano {first.position - plan.PlantPoint}]";
                    }
                    if (Mathf.Abs(ct - v.Plant) < handNear) { handNear = Mathf.Abs(ct - v.Plant); handAtPlant = e1; }
                    if (plan.HasSecondPlant)
                    {
                        Transform second = v.SecondRightHand ? _handR : _handL;
                        float e2 = Vector3.Distance(second.position, plan.SecondPlantPoint);
                        if (ct >= v.SecondPlant && ct <= plan.SecondRelease) secondWindow = Mathf.Max(secondWindow, e2);
                        if (Mathf.Abs(ct - v.SecondPlant) < secondNear) { secondNear = Mathf.Abs(ct - v.SecondPlant); secondAtPlant = e2; }
                    }

                    float inside = BodyInside(o, out string part);
                    if (inside > maxInside) { maxInside = inside; insideAt = $"[{part} clip={ct:F2}]"; }
                    float floor = SoleClearance;
                    if (floor < minFloor) { minFloor = floor; floorAt = $"[clip={ct:F2}]"; }
                    apex = Mathf.Max(apex, _hips.position.y);
                    float along = Vector3.Dot(_movement.transform.position - plan.FrontEdge, plan.Direction);
                    if (float.IsNaN(takeoff) && ct >= v.Takeoff) takeoff = -along;
                    if (float.IsNaN(landBehind) && ct >= v.Land) landBehind = along - plan.Depth;
                    if (ct >= v.Land && ct <= v.Land + 0.2f) landSole = Mathf.Min(landSole, SoleClearance);
                    if (ct > v.Release + 0.02f && ct < v.Land - 0.02f) descent.Add(new Vector2(Time.time, _hips.position.y));

                    // Storyboard: entry, take-off, plant, release, landing
                    float[] moments = { entryTime, v.Takeoff, v.Plant, v.Release, v.Land };
                    while (column < moments.Length && ct >= moments[column])
                        sheet.Capture(column++, o.LaneX, plan.PlantPoint);
                }
                else if (v != null)
                {
                    if (exitSpeed < 0f)
                    {
                        // A clip that hands over on its landing frame lands here
                        if (float.IsNaN(landBehind)) landBehind = Vector3.Dot(_movement.transform.position - plan.FrontEdge, plan.Direction) - plan.Depth;
                        exitSpeed = Speed;
                        exitYaw = Mathf.Abs(Mathf.DeltaAngle(Yaw, Quaternion.LookRotation(plan.Direction).eulerAngles.y));
                        exitAt = Time.time;
                        sheet.Capture(5, o.LaneX, plan.PlantPoint);
                    }
                    float since = Time.time - exitAt;
                    if (since <= 0.6f) runOutMin = Mathf.Min(runOutMin, Speed);
                    if (since >= nextRunOutSample && since <= 0.6f)
                    {
                        // Speed / motion matching weight after the hand-over, every 0.1 s (diagnostic)
                        runOut += $"{Speed:F1}/{_movement.Locomotion.Weight:F1} ";
                        nextRunOutSample += 0.1f;
                    }
                    if (since <= 0.2f && landSole == float.MaxValue) landSole = SoleClearance;
                    if (since >= 0.2f && speedJump == 0f) speedJump = Mathf.Max(0.001f, exitSpeed - Speed);
                    if (since >= 0.4f && column < 6) { sheet.Capture(6, o.LaneX, null); column = 6; }
                    if (since >= 0.6f) break;
                }
                yield return 0.02f;
            }
            Keys();
            yield return 0.4f;

            string clip = v != null ? $"{v.Id}{(v.Mirror ? " (espejo)" : "")}, {v.Style}" : "ninguno";
            if (!Check(Visited.Contains(_movement.VaultState) && v != null && sawState,
                       $"Vault {label}: Espacio a {pressed:F2} m inicia el vault con su clip ({clip}; aproximación {approach:F1} m/s; motivo si no: '{_movement.VaultState.LastReason}', medido {_movement.VaultState.LastEvaluation.MeasuredHeight:F3} × {_movement.VaultState.LastEvaluation.MeasuredDepth:F3} m a {_movement.VaultState.LastEvaluation.MeasuredSpeed:F2} m/s)"))
            {
                yield return 0.6f;
                yield break;
            }
            // (palm error: the worst while it is down, or at the plant if no sample fell inside that window)
            float handErr = handWindow >= 0f ? handWindow : handAtPlant;
            float secondErr = secondWindow >= 0f ? secondWindow : secondAtPlant;
            string planInfo = $"entra en t={entryTime:F2} s a {entryDistance:F2} m de la cara (corrección {plan.EntryError * 100f:F0} cm), lift {plan.Lift * 100f:F0} cm, estiramiento {plan.Stretch * 100f:F0} cm, ritmo {plan.ApproachRate:F2}/{plan.AirRate:F2}";
            string states = "";
            foreach (PlayerState st in Visited) states += st.GetType().Name.Replace("Player", "").Replace("State", "") + " ";
            Debug.Log($"{Tag} INFO  Vault {label}: clip {clip}; {planInfo}; estados {states.Trim()}");

            Check(handErr < VaultHandTolerance, $"Vault {label}: la mano se apoya en la cima ({handErr * 100f:F1} cm del punto de apoyo; al apoyar {handAtPlant * 100f:F1} cm) {handWindowAt}");
            if (plan.HasSecondPlant)
                Check(secondErr < VaultHandTolerance, $"Vault {label}: la segunda mano se apoya en la cima ({secondErr * 100f:F1} cm del punto)");
            Check(maxInside < Penetration, $"Vault {label}: ni pies, rodillas, cadera ni manos atraviesan el obstáculo (máximo {Mathf.Max(0f, maxInside) * 100f:F1} cm dentro) {insideAt}");
            Check(minFloor > -Penetration, $"Vault {label}: los pies nunca se hunden en el suelo ni en la cima (mínimo {minFloor * 100f:F1} cm) {floorAt}");
            Check(maxStep < TeleportSpeed, $"Vault {label}: sin teleport (velocidad máxima entre muestras {maxStep:F1} m/s) {stepAt}");
            Check(maxLean < 0.01f, $"Vault {label}: la inclinación procedural está apagada durante el vault ({maxLean:F2}°)");

            // Take-off where the clip takes off (not a far jump), flight with gravity, landing close behind
            float natural = NaturalTakeoff(v, plan);
            Check(!float.IsNaN(takeoff) && Mathf.Abs(takeoff - natural) < 0.35f && takeoff < 2.0f,
                  $"Vault {label}: despega a {takeoff:F2} m de la cara (el clip despega a {natural:F2} m)");
            float g = descent.Count >= 5 && descent[descent.Count - 1].x - descent[0].x >= 0.1f ? FittedGravity(descent) : float.NaN;
            if (v.Style != VaultStyle.Lazy && !float.IsNaN(g))
                Check(g > 5f && g < 18f, $"Vault {label}: el vuelo cae con gravedad natural ({g:F1} m/s²), sin cámara lenta ni flotación");
            else
                Debug.Log($"{Tag} INFO  Vault {label}: gravedad del vuelo {(float.IsNaN(g) ? "sin vuelo medible" : g.ToString("F1") + " m/s²")} (estilo {v.Style})");
            Check(!float.IsNaN(landBehind) && landBehind > 0.15f && landBehind < 2.0f,
                  $"Vault {label}: aterriza a {landBehind:F2} m tras la cara trasera (cimas a {apex - Floor:F2} m de cadera)");
            Check(landSole > -Penetration && landSole < 0.08f, $"Vault {label}: al aterrizar el pie apoya en el suelo, sin flotar ni hundirse ({landSole * 100f:F1} cm)");

            // Hand-over to the locomotion: facing the obstacle's direction, momentum kept, no stop
            Check(exitYaw >= 0f && exitYaw < 5f, $"Vault {label}: termina alineado con la dirección del vault (desvío {exitYaw:F1}°)");
            Check(Mathf.Abs(exitSpeed - rootSpeed) < 1.0f, $"Vault {label}: sale a la velocidad que llevaba el clip ({rootSpeed:F2} → {exitSpeed:F2} m/s)");
            if (gait == Gait.Run || gait == Gait.Sprint)
            {
                // Against the gait's speed: some sprint takes of the locomotion overshoot it (T27). No
                // surge, and the momentum kept (a dive's landing absorbs some of it, then the run goes on)
                float gaitSpeed = gait == Gait.Sprint ? _movement.SprintSpeed : _movement.BaseSpeed;
                float reference = Mathf.Min(entrySpeed, gaitSpeed + 0.3f);
                Check(exitSpeed < reference + 0.4f && exitSpeed > reference * 0.75f,
                      $"Vault {label}: sale sin acelerón y con el impulso de la carrera ({entrySpeed:F2} → {exitSpeed:F2} m/s; marcha {gaitSpeed:F1})");
                Check(runOutMin > entrySpeed * 0.6f && speedJump < 0.8f,
                      $"Vault {label}: sigue corriendo sin pararse (mínimo {runOutMin:F2} m/s en 0.6 s, caída {speedJump:F2} m/s; velocidad/peso MxM cada 0.1 s: {runOut.Trim()})");
            }
            VaultResults.Add(new VaultResult { Label = label, Clip = v.Id, HandError = handErr, LandingBehind = landBehind });

            float after = float.MaxValue;
            for (float t = 0f; t < 0.4f; t += 0.02f) { after = Mathf.Min(after, SoleClearance); yield return 0.02f; }
            Check(after > -Penetration && after < SoleOnGround, $"Vault {label}: después del vault los pies pisan el suelo ({after * 100f:F1} cm)");
            yield return 0.6f;
        }

        /// <summary>Distance (m) from the face at which the clip itself takes off for this plan (no approach correction).</summary>
        private static float NaturalTakeoff(VaultVariant v, VaultPlan plan)
        {
            float sPlant = VaultWarpProfile.Stretch(v, v.Plant);
            float toHand = v.PathAt(v.Plant).y - v.PathAt(v.Takeoff).y + v.HandOffset.z + plan.Stretch * sPlant;
            return toHand - plan.Inset;
        }

        /// <summary>Acceleration (m/s², downward positive) of the parabola that best fits (time, height) samples.</summary>
        private static float FittedGravity(List<Vector2> samples)
        {
            double t0 = samples[0].x, s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, y0 = 0, y1 = 0, y2 = 0;
            foreach (Vector2 p in samples)
            {
                double t = p.x - t0, tt = t * t;
                s0 += 1; s1 += t; s2 += tt; s3 += tt * t; s4 += tt * tt;
                y0 += p.y; y1 += t * p.y; y2 += tt * p.y;
            }
            // Normal equations of y = a t² + b t + c, solved for a by Cramer's rule
            double det = s4 * (s2 * s0 - s1 * s1) - s3 * (s3 * s0 - s1 * s2) + s2 * (s3 * s1 - s2 * s2);
            if (System.Math.Abs(det) < 1e-12) return float.NaN;
            double detA = y2 * (s2 * s0 - s1 * s1) - s3 * (y1 * s0 - s1 * y0) + s2 * (y1 * s1 - s2 * y0);
            return (float)(-2.0 * detA / det);
        }

        /// <summary>
        /// An obstacle that must not be vaulted from this approach: Space does the action the geometry
        /// allows (<paramref name="expect"/>, if given) and nothing of the body goes through it (nor through
        /// <paramref name="extra"/>, a second box such as a wall behind).
        /// </summary>
        private static IEnumerator NoVaultCase(Box o, Gait gait, string label, PlayerState expect,
                                               float pressAt = 0f, float standAt = 0f, Box? extra = null)
        {
            label = $"{label} ({o.Height:F2} × {o.Depth:F2} m)";
            bool moving = gait != Gait.Stand;
            float runUp = moving ? pressAt + (gait == Gait.Walk ? 2f : gait == Gait.Run ? 3.5f : 5f) : standAt;
            yield return Teleport(new Vector3(o.LaneX, Floor + OriginAboveFeet, o.FrontZ + runUp));
            Visited.Clear();
            Key[] held = GaitKeys(gait);
            Keys(held);
            for (float t = 0f; t < 5f && moving && _movement.transform.position.z - o.FrontZ > pressAt; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.Space, held);
            string reason = _movement.VaultState.LastReason;
            float maxInside = float.MinValue, maxStep = 0f;
            string insideAt = "";
            ResetStep();
            for (float t = 0f; t < 2.0f; t += 0.02f)
            {
                float inside = BodyInside(o, out string part);
                if (extra.HasValue)
                {
                    float other = BodyInside(extra.Value, out string otherPart);
                    if (other > inside) { inside = other; part = otherPart + " (segundo obstáculo)"; }
                }
                if (inside > maxInside)
                {
                    maxInside = inside;
                    insideAt = $"[{part} {Current.GetType().Name} t={t:F2} s, centro a {_movement.transform.position.z - o.FrontZ:F2} m de la cara, " +
                               $"contra la pared={_movement.IsAgainstWall}, peso MxM {_movement.Locomotion.Weight:F1}, {Speed:F1} m/s, " +
                               $"salvaguardas de mano {_playerAnimator.HandGuards}, hombro izq a {_armL.position.z - o.FrontZ:F2} m de la cara y {_armL.position.y - Floor:F2} m de alto, mano a {_handL.position.y - Floor:F2} m]";
                }
                maxStep = Mathf.Max(maxStep, StepSpeed());
                yield return 0.02f;
            }
            Keys();
            yield return 0.8f;
            string seen = "";
            foreach (PlayerState s in Visited) if (!seen.Contains(s.GetType().Name)) seen += s.GetType().Name.Replace("Player", "").Replace("State", "") + " ";
            Check(!Visited.Contains(_movement.VaultState) && (expect == null || Visited.Contains(expect)),
                  $"Sin vault: {label}: {(expect == null ? "no hace el vault" : "hace " + expect.GetType().Name.Replace("Player", "").Replace("State", ""))} (estados: {seen.Trim()}; motivo del rechazo: '{reason}')");
            Check(maxInside < Penetration && maxStep < TeleportSpeed,
                  $"Sin vault: {label}: nada atraviesa el obstáculo ni se teletransporta (máximo {Mathf.Max(0f, maxInside) * 100f:F1} cm dentro {insideAt}, {maxStep:F1} m/s)");
            yield return 0.4f;
        }

        private const string VaultSheetFolder = "Logs/PlayModeVaults";

        /// <summary>
        /// Storyboard of a group of vaults, rendered from the game's scene: one row per vault, one column
        /// per key moment, seen from the side at the obstacle's lane (only that lane's slice of the scene,
        /// so the other lanes do not hide the body). The red dot is the planned plant point of the hand.
        /// </summary>
        private sealed class VaultSheet
        {
            public static readonly string[] Moments = { "entrada", "despegue", "apoyo", "suelta", "aterrizaje", "salida", "+0.4 s" };
            private readonly PoseSheetRenderer _renderer;
            private readonly string _name;
            private readonly int _rows;
            private readonly List<string> _labels = new List<string>();

            public VaultSheet(string name, int rows)
            {
                _name = name;
                _rows = rows;
                _renderer = new PoseSheetRenderer(300, 210, 1.4f, studio: false);
                _renderer.SetDepthRange(8f - 2.6f, 8f + 2.6f);
                _renderer.Begin(Moments.Length, rows);
            }

            public void NextRow(string label) => _labels.Add(label);

            public void Capture(int column, float laneX, Vector3? marker)
            {
                int row = Mathf.Clamp(_labels.Count - 1, 0, _rows - 1);
                _renderer.SetMarker(marker);
                _renderer.Capture(row, column, new Vector3(laneX, Floor + 1.1f, _movement.transform.position.z), Vector3.right, Floor);
            }

            public void Save()
            {
                string path = $"{VaultSheetFolder}/{_name}.png";
                _renderer.Save(path);
                var text = new StringBuilder("Columnas: " + string.Join(" · ", Moments) + "\n");
                for (int i = 0; i < _labels.Count; i++) text.AppendLine($"fila {i + 1}: {_labels[i]}");
                File.WriteAllText(Path.ChangeExtension(path, ".txt"), text.ToString());
                _renderer.Dispose();
                Debug.Log($"{Tag} INFO  Storyboard de vaults: {path}");
            }
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

            // C pressed early, out of the slide's reach: the run keeps the intention and the slide starts
            // where it carries the body under the bar
            yield return EarlySlideCase(new[] { Key.LeftShift, Key.W }, 3.5f, "esprintando");
            yield return EarlySlideCase(new[] { Key.W }, 3.0f, "corriendo");

            // No headroom: a slab whose underside (0.6 m) is lower than a sliding body. The slide is
            // refused (the body crouches) and nothing goes into the slab
            float slabFront = -30f;
            GameObject slab = TempSlab(ParkourTestCircuitBuilder.SlideX, slabFront, 0.6f, 1.4f, 2f);
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.SlideX, Floor + OriginAboveFeet, slabFront + 6f));
            Visited.Clear();
            Keys(Key.W);
            for (float t = 0f; t < 3f && _movement.transform.position.z > slabFront + 1.5f; t += 0.02f) yield return 0.02f;
            yield return Tap(Key.C, Key.W);
            float maxInSlab = float.MinValue;
            string inSlabAt = "";
            Bounds b = slab.GetComponent<Collider>().bounds;
            for (float t = 0f; t < 2f; t += 0.02f)
            {
                foreach ((Transform bone, string name) in new[] { (_head, "cabeza"), (_hips, "cadera"), (_handL, "mano izq"), (_handR, "mano der"), (_kneeL, "rodilla izq"), (_kneeR, "rodilla der") })
                {
                    Vector3 q = bone.position;
                    float d = Mathf.Min(Mathf.Min(q.x - b.min.x, b.max.x - q.x), Mathf.Min(Mathf.Min(q.y - b.min.y, b.max.y - q.y), Mathf.Min(q.z - b.min.z, b.max.z - q.z)));
                    if (d > maxInSlab) { maxInSlab = d; inSlabAt = $"[{name} {Current.GetType().Name}]"; }
                }
                yield return 0.02f;
            }
            Keys();
            Object.Destroy(slab);
            if (Current == _movement.CrouchState) yield return Tap(Key.C); // stand up for the next cases
            string seen = "";
            foreach (PlayerState st in Visited) if (!seen.Contains(st.GetType().Name)) seen += st.GetType().Name.Replace("Player", "").Replace("State", "") + " ";
            Check(!Visited.Contains(_movement.SlideState), $"Slide sin altura libre (techo a 0.6 m): C no desliza bajo él (estados: {seen.Trim()})");
            Check(maxInSlab < Penetration, $"Slide sin altura libre: nada del cuerpo entra bajo el techo bajo (máximo {Mathf.Max(0f, maxInSlab) * 100f:F1} cm) {inSlabAt}");
            yield return 1.0f;
        }

        /// <summary>
        /// C pressed <paramref name="pressAt"/> m before the slide bar, farther than the slide reaches:
        /// the slide must still pass under the bar (it starts where the momentum carries it there).
        /// </summary>
        private static IEnumerator EarlySlideCase(Key[] held, float pressAt, string label)
        {
            float barFront = ParkourTestCircuitBuilder.SlideBarFront;
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.SlideX, Floor + OriginAboveFeet, barFront + pressAt + 5f));
            Visited.Clear();
            Keys(held);
            for (float t = 0f; t < 4f && _movement.transform.position.z > barFront + pressAt; t += 0.02f) yield return 0.02f;
            float speed = Speed;
            yield return Tap(Key.C, held);
            float startedAt = float.NaN, maxHead = float.MinValue;
            for (float t = 0f; t < 3f && _movement.transform.position.z > barFront - 1.5f; t += 0.02f)
            {
                if (float.IsNaN(startedAt) && Current == _movement.SlideState) startedAt = _movement.transform.position.z - barFront;
                float z = _movement.transform.position.z;
                if (z < barFront + 0.1f && z > barFront - ParkourStandard.SlideBarThickness - 0.1f) maxHead = Mathf.Max(maxHead, _head.position.y);
                yield return 0.02f;
            }
            Keys();
            Check(Visited.Contains(_movement.SlideState) && _movement.transform.position.z < barFront - 1f && maxHead < 1.2f,
                  $"Slide con C anticipado {label} ({pressAt:F1} m antes de la barra, {speed:F1} m/s): empieza a {startedAt:F2} m y pasa bajo ella (cabeza a {maxHead:F2} m, z = {_movement.transform.position.z:F2})");
            yield return 1.0f;
        }

        /// <summary>A horizontal slab on layer Obstacle with its underside <paramref name="bottom"/> m above the floor (a ceiling), for one check.</summary>
        private static GameObject TempSlab(float laneX, float frontZ, float bottom, float top, float depth)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PruebaTemporal";
            go.layer = LayerMask.NameToLayer("Obstacle");
            go.transform.SetPositionAndRotation(new Vector3(laneX, Floor + (bottom + top) * 0.5f, frontZ - depth * 0.5f), Quaternion.identity);
            go.transform.localScale = new Vector3(ParkourStandard.PrefabWidth, top - bottom, depth);
            Physics.SyncTransforms();
            return go;
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
                // Sprinting, Space about half a second before each vault (the clips take off 0.7–1.7 m before it)
                if (!vault1 && free && z - vaultA < 2.6f) { vault1 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (vault1 && !slide && free && z < vaultA - 2f && z - bar < ParkourStandard.SlideEntryDistance + 1f) { slide = true; yield return Tap(Key.C, Key.LeftShift, Key.W); continue; }
                if (slide && !grab && free && z < bar - 1.5f && z - wall < 0.9f) { grab = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (grab && !climbTap && s == _movement.LedgeGrabState) { climbTap = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
                if (climbTap && !vault2 && free && z < wall - 4f && z - vaultB < 2.6f && _movement.IsGrounded) { vault2 = true; yield return Tap(Key.Space, Key.LeftShift, Key.W); continue; }
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
            yield return 1.45f; // the strafe takes' stop ends in 1.0–1.5 s (motion matching's choice varies, T27)
            // (the strafe takes sway ±10°, as in the gaits below)
            Check(braking > 0.2f && braking < _movement.BackpedalSpeed && Speed < 0.05f && Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw)) < 10f,
                  $"Caminar hacia atrás → parar: frena gradualmente sin girar ({braking:F2} m/s a los 0.06 s, {Speed:F2} m/s a los 1.51 s, giró {Mathf.Abs(Mathf.DeltaAngle(yaw0, Yaw)):F1}°)");

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

            float handErr = float.MaxValue, minFootOver = float.MaxValue, maxStep = 0f, maxInside = float.MinValue;
            string handDiag = "", insideAt = "";
            bool poseOk = true;
            var block = new Box(laneX, frontZ, top, ParkourStandard.Spec(ParkourObstacleType.Mantle).Depth);
            ResetStep();
            for (float t = 0f; t < 2.5f && !(Visited.Contains(_movement.MantleState) && Current != _movement.MantleState); t += 0.02f)
            {
                maxStep = Mathf.Max(maxStep, StepSpeed());
                if (Current == _movement.MantleState)
                {
                    float n = _movement.ParkourProgress;
                    poseOk &= PoseFacesBody();
                    float inside = BodyInside(block, out string part);
                    if (inside > maxInside) { maxInside = inside; insideAt = $"[{part} n={n:F2}, centro a {_movement.transform.position.z - frontZ:F2} m de la cara]"; }
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
            Check(maxInside < Penetration, $"{label}: ni rodillas, cadera ni manos atraviesan el bloque (máximo {Mathf.Max(0f, maxInside) * 100f:F1} cm dentro) {insideAt}");
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

        // ── S12: unarmed combat on the training dummy (P37), dodge, block, hit reactions ───────────────
        private const string CombatSheetFolder = "Logs/PlayModeCombat";
        // ── Animation Rigging (P31): feet on the ground and on the curbs, foot lock, look ─────────────
        private static IEnumerator Rig()
        {
            var builder = _animator.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            var feet = _animator.GetComponentInChildren<GroundContactConstraint>();
            var look = _animator.GetComponentInChildren<HeadLookConstraint>();
            if (!Check(builder != null && builder.graph.IsValid() && feet != null && look != null,
                       "El rig de Animation Rigging del jugador está construido (pies y mirada)")) yield break;

            // Running on flat ground: the stance foot locks, does not skate and nothing of the feet sinks
            yield return Teleport(Corridor(5f));
            Keys(Key.W);
            yield return 1.0f;
            int locks = feet.LockCount;
            var contact = new List<Vector3>();
            var times = new List<float>();
            float minClear = float.MaxValue, minHeight = float.MaxValue, minSpeed = float.MaxValue;
            for (float t = 0f; t < 1.5f; t += 0.01f)
            {
                contact.Add(LowestFootPoint);
                times.Add(Time.time);
                minClear = Mathf.Min(minClear, FootClearance);
                Vector2 cl = feet.Contact(true), cr = feet.Contact(false);
                minHeight = Mathf.Min(minHeight, Mathf.Min(cl.x, cr.x));
                if (cl.x < 0.03f) minSpeed = Mathf.Min(minSpeed, cl.y);
                if (cr.x < 0.03f) minSpeed = Mathf.Min(minSpeed, cr.y);
                yield return 0.01f;
            }
            Keys();
            float skate = Skating(contact, times);
            Check(feet.LockCount > locks, $"Corriendo, el pie de apoyo se bloquea en el suelo ({feet.LockCount - locks} bloqueos en 1.5 s; " +
                                          $"apoyo más bajo {minHeight * 100f:F1} cm, más lento {minSpeed:F2} m/s; peso {feet.weight:F2}, bloqueo permitido {feet.LockAllowed}, Foot IK {feet.FootIKWeight:F2})");
            Check(skate <= 0.24f, $"Corriendo, el pie de apoyo no patina (mediana {skate:F3} m/s; el mocap solo: 0.16)");
            Check(minClear > -0.02f, $"Corriendo, ni las suelas ni las puntas se hunden (mínimo {minClear * 100f:F1} cm)");
            yield return 1.0f;

            // Up the curbs: each foot stands on the surface under it
            yield return Teleport(new Vector3(ParkourTestCircuitBuilder.LocomotionX, Floor + OriginAboveFeet, -4f));
            Keys(Key.W);
            float curbMin = float.MaxValue;
            string curbDiag = "";
            for (float t = 0f; t < 5f && _movement.transform.position.z > -16f; t += 0.02f)
            {
                float c = FootClearance;
                if (c < curbMin)
                {
                    curbMin = c;
                    Vector3 lp = LowestFootPointUnder(out string part);
                    curbDiag = $"{part} a z {lp.z:F2}, {(lp.y - GroundUnder(lp)) * 100f:F1} cm sobre su suelo; cuerpo a z {_movement.transform.position.z:F2}";
                }
                yield return 0.02f;
            }
            Keys();
            Check(curbMin > -Penetration, $"Subiendo bordillos, cada pie pisa la superficie bajo él sin atravesarla (mínimo {curbMin * 100f:F1} cm: {curbDiag})");
            yield return 0.8f;

            // In the air the feet are the clip's (the rig fades out)
            yield return Teleport(Corridor(5f));
            yield return Tap(Key.Space);
            yield return 0.3f;
            Check((Current == _movement.JumpState || Current == _movement.FallState) && feet.weight < 0.2f,
                  $"En el aire el rig suelta los pies (peso {feet.weight:F2}, estado {Current.GetType().Name})");
            yield return 1.2f;

            // Standing near the dummy, turned 50° away from it: the head (with the neck and the chest) looks at it
            _dummy = Object.FindAnyObjectByType<TrainingDummy>();
            if (_dummy != null)
            {
                yield return FaceDummy(2.0f, 50f);
                yield return 1.0f;
                Vector3 face = _head.rotation * look.data.headForward;
                float body = AngleToDummy(_movement.transform.forward), head = AngleToDummy(face);
                Check(look.weight > 0.9f && head < body - 25f,
                      $"De pie junto al muñeco, la cabeza lo mira (el cuerpo a {body:F0}°, la cara a {head:F0}°; peso {look.weight:F2})");
            }
        }

        /// <summary>Horizontal angle (°) between <paramref name="direction"/> and the way from the head to the dummy.</summary>
        private static float AngleToDummy(Vector3 direction)
        {
            Vector3 to = _dummy.transform.position - _head.position;
            to.y = 0f;
            direction.y = 0f;
            return Vector3.Angle(direction, to);
        }

        /// <summary>Lowest point of the feet: the soles (foot minus its bottom height) and the toe joints.</summary>
        private static Vector3 LowestFootPoint
        {
            get
            {
                Vector3 best = _footL.position + Vector3.down * _animator.leftFeetBottomHeight;
                Vector3 heelR = _footR.position + Vector3.down * _animator.rightFeetBottomHeight;
                if (heelR.y < best.y) best = heelR;
                if (_toeL != null && _toeL.position.y < best.y) best = _toeL.position;
                if (_toeR != null && _toeR.position.y < best.y) best = _toeR.position;
                return best;
            }
        }

        /// <summary>The sole or toe joint lowest relative to the ground right under it, and which one.</summary>
        private static Vector3 LowestFootPointUnder(out string part)
        {
            part = "suela izq";
            Vector3 best = _footL.position + Vector3.down * _animator.leftFeetBottomHeight;
            float bc = best.y - GroundUnder(best);
            Vector3 p = _footR.position + Vector3.down * _animator.rightFeetBottomHeight;
            if (p.y - GroundUnder(p) < bc) { best = p; bc = p.y - GroundUnder(p); part = "suela der"; }
            p = _toeL.position;
            if (p.y - GroundUnder(p) < bc) { best = p; bc = p.y - GroundUnder(p); part = "punta izq"; }
            p = _toeR.position;
            if (p.y - GroundUnder(p) < bc) { best = p; bc = p.y - GroundUnder(p); part = "punta der"; }
            return best;
        }

        /// <summary>Lowest gap between a sole or a toe joint and the ground right under it (negative = sunk).</summary>
        private static float FootClearance
        {
            get
            {
                float c = SoleClearance;
                if (_toeL != null) c = Mathf.Min(c, _toeL.position.y - GroundUnder(_toeL.position));
                if (_toeR != null) c = Mathf.Min(c, _toeR.position.y - GroundUnder(_toeR.position));
                return c;
            }
        }

        /// <summary>
        /// Skating of the planted foot, with MocapRetargetProbe's metric: the lowest point of the feet, within
        /// 2 cm of its p5 height in two consecutive samples, is planted; the median of its horizontal speed
        /// (above 6 m/s it is the contact switching feet, not sliding).
        /// </summary>
        private static float Skating(List<Vector3> contact, List<float> times)
        {
            var heights = new List<float>(contact.Count);
            foreach (Vector3 c in contact) heights.Add(c.y);
            heights.Sort();
            float floor = heights[Mathf.Clamp(Mathf.FloorToInt(0.05f * (heights.Count - 1)), 0, heights.Count - 1)];
            var speeds = new List<float>();
            for (int i = 1; i < contact.Count; i++)
            {
                if (contact[i].y > floor + 0.02f || contact[i - 1].y > floor + 0.02f || times[i] <= times[i - 1]) continue;
                Vector3 d = contact[i] - contact[i - 1];
                d.y = 0f;
                float v = d.magnitude / (times[i] - times[i - 1]);
                if (v < 6f) speeds.Add(v);
            }
            if (speeds.Count == 0) return 0f;
            speeds.Sort();
            return speeds[speeds.Count / 2];
        }

        private static TrainingDummy _dummy;
        private static CapsuleCollider _dummyBody;
        private static PoseSheetRenderer _combatSheet;

        private static IEnumerator Combat()
        {
            _dummy = Object.FindAnyObjectByType<TrainingDummy>();
            if (!Check(_dummy != null, "El muñeco de entrenamiento está en el área (S12)")) yield break;
            foreach (CapsuleCollider c in _dummy.GetComponentsInChildren<CapsuleCollider>())
                if (c.gameObject.layer == LayerMask.NameToLayer("Enemy")) _dummyBody = c;
            Directory.CreateDirectory(CombatSheetFolder);
            _combatSheet = new PoseSheetRenderer(260, 300, 1.25f, studio: false);
            _combatSheet.SetDepthRange(8f - 2.6f, 8f + 2.6f);
            _combatSheet.Begin(4, 2);
            var light = _movement.LightAttackState;
            var heavy = _movement.HeavyAttackState;

            // Light chain J → J → J: three presses inside the first blow (buffered)
            yield return FaceDummy(1.0f);
            var log = new CombatLog { Columns = new Dictionary<string, int> { ["Jab"] = 0, ["Cross"] = 1, ["Hook"] = 2 } };
            yield return CombatRun(1.8f, log, null, (0f, Key.J), (0.12f, Key.J), (0.26f, Key.J));
            Check(Count(light) == 3 && log.Anims.Contains(PlayerAnimatorIds.LightAttack1) && log.Anims.Contains(PlayerAnimatorIds.LightAttack2) &&
                  log.Anims.Contains(PlayerAnimatorIds.LightAttack3),
                  $"J → J → J: jab, cross y gancho, uno tras otro ({Count(light)} golpes ligeros)");
            Check(log.Hits.Count == 3 && log.Hits.TrueForAll(h => h.Damage == 10),
                  $"Cada golpe ligero conecta con el muñeco y hace 10 de daño ({log.Describe()})");
            Check(log.Hits.Count == 3 && log.Hits[0].Time < 0.35f && log.Intervals(0.15f, 0.5f),
                  $"Ritmo de la cadena: primer impacto a {log.FirstHit:F2} s, impactos cada {log.IntervalText} s (GDD: ~0.25 s)");
            Check(log.SawHitStop, "Hit stop: el atacante se congela un instante al conectar");
            Check(log.MaxSway >= 2f, $"El muñeco reacciona: se inclina {log.MaxSway:F1}° con los golpes");
            Check(log.MinDistance >= ParkourTestCircuitBuilder.DummyPostRadius + 0.3f,
                  $"El cuerpo nunca entra en el muñeco (distancia mínima al eje {log.MinDistance:F2} m)");
            Check(log.MaxLimbDepth <= 0.12f, $"Puños y pies no atraviesan el muñeco (lo más hondo: {log.MaxLimbDepth * 100f:F0} cm, {log.DeepestLimb})");
            Check(log.MaxStepSpeed < TeleportSpeed, $"Sin teleport durante la cadena (máx {log.MaxStepSpeed:F1} m/s)");
            Check(log.MinSole > -0.04f, $"Las suelas no se hunden durante los golpes (mín {log.MinSole * 100f:F1} cm)");
            Check(log.EndedIdleAt > 0f && log.EndedIdleAt - log.LastHitTime < 1.2f,
                  $"Tras el gancho vuelve a la locomoción ({log.EndedIdleAt - log.LastHitTime:F2} s después del último impacto)");
            yield return SettleDummy();
            Check(_dummy.Sway < 1f && _dummy.Displacement < 0.03f, $"El muñeco se recupera (inclinación {_dummy.Sway:F1}°)");

            // Combo J → J → K (GDD §5.9): the kick ends it and knocks the dummy back
            yield return FaceDummy(1.0f);
            log = new CombatLog { Columns = new Dictionary<string, int> { ["Kick"] = 3 } };
            yield return CombatRun(2.4f, log, null, (0f, Key.J), (0.2f, Key.J), (0.42f, Key.K));
            Check(log.Hits.Count == 3 && log.Hits[2].Attack == "Kick" && log.Hits[2].Damage == 20 && log.SawCombo,
                  $"J → J → K: dos golpes y la patada de remate de 20 ({log.Describe()})");
            Check(log.MaxDisplacement >= 0.15f, $"La patada hace retroceder al muñeco ({log.MaxDisplacement:F2} m)");
            Check(log.MaxStepSpeed < TeleportSpeed && log.MinDistance >= ParkourTestCircuitBuilder.DummyPostRadius + 0.3f,
                  $"La patada avanza sin teleport ni meterse en el muñeco (máx {log.MaxStepSpeed:F1} m/s, distancia mínima {log.MinDistance:F2} m)");
            Check(log.MaxLimbDepth <= 0.15f, $"El pie de la patada no atraviesa el muñeco ({log.MaxLimbDepth * 100f:F0} cm, {log.DeepestLimb})");
            yield return SettleDummy();
            Check(_dummy.Displacement < 0.05f, $"El muñeco vuelve a su sitio ({_dummy.Displacement:F2} m)");

            // Target orientation: facing 45° away from the dummy, J turns the body toward it
            yield return FaceDummy(1.0f, 45f);
            log = new CombatLog();
            yield return CombatRun(1.0f, log, null, (0f, Key.J));
            Check(log.Hits.Count == 1 && log.Hits[0].YawError < 12f,
                  $"El golpe gira hacia el muñeco ({(log.Hits.Count > 0 ? log.Hits[0].YawError : -1f):F0}° de error al impactar, empezando a 45°)");

            // Approach: from 1.6 m the jab steps in; from 2.0 m the kick does
            yield return FaceDummy(1.6f);
            log = new CombatLog();
            yield return CombatRun(1.0f, log, null, (0f, Key.J));
            Check(log.Hits.Count == 1 && log.Hits[0].Distance < 1.05f && log.MinDistance > 0.6f,
                  $"Desde 1.6 m el jab da un paso y conecta ({log.Describe()})");
            yield return SettleDummy();
            yield return FaceDummy(2.0f);
            log = new CombatLog();
            yield return CombatRun(1.4f, log, null, (0f, Key.K));
            Check(log.Hits.Count == 1 && log.Hits[0].Attack == "Kick",
                  $"Desde 2.0 m la patada avanza y conecta ({log.Describe()})");
            yield return SettleDummy();

            // Out of reach: from 3.5 m nothing is struck and the body does not lunge
            yield return FaceDummy(3.5f);
            Vector3 p0 = _movement.transform.position;
            log = new CombatLog();
            yield return CombatRun(1.0f, log, null, (0f, Key.J));
            float moved = Vector3.Distance(p0, _movement.transform.position);
            Check(log.Hits.Count == 0 && moved < 0.12f, $"Fuera de alcance: el jab no conecta ni se lanza ({moved:F2} m)");

            // Recovery cancel: holding W the jab still lands, then the run takes over before the clip ends
            yield return FaceDummy(1.0f);
            log = new CombatLog();
            yield return CombatRun(1.2f, log, new[] { Key.W }, (0f, Key.J));
            AttackData jab = CombatTimings.Jab;
            Check(log.Hits.Count == 1 && log.RunAt >= jab.SecondsTo(jab.HitEnd) && log.RunAt <= jab.SecondsTo(0.97f),
                  $"Con W mantenido el jab conecta y la carrera lo interrumpe en la recuperación (a {log.RunAt:F2} s; golpe hasta {jab.SecondsTo(jab.HitEnd):F2} s)");
            yield return SettleDummy();

            // A kick that misses leaves the player open (GDD §5.7): moving cannot cut its recovery
            yield return FaceDummy(4.5f);
            log = new CombatLog();
            yield return CombatRun(1.4f, log, new[] { Key.W }, (0f, Key.K));
            AttackData kick = CombatTimings.Kick;
            Check(log.Hits.Count == 0 && log.HeavyUntil >= kick.SecondsTo(0.9f) - 0.05f,
                  $"La patada fallada no se cancela al moverse (dura {log.HeavyUntil:F2} s; con impacto se podría a los {kick.SecondsTo(kick.MoveCancel):F2} s)");

            // The chain restarts after 0.5 s without a press (GDD §5.9)
            yield return FaceDummy(1.0f);
            log = new CombatLog();
            yield return CombatRun(2.0f, log, null, (0f, Key.J), (1.0f, Key.J));
            Check(log.Hits.Count == 2 && log.MaxChain == 1, $"Pasados 0.5 s sin pulsar, la cadena vuelve al jab (golpes: {log.Describe()})");
            yield return SettleDummy();

            // Hit reactions: the body reacts and cannot dodge until the reaction ends (GDD §5.5)
            var health = _movement.GetComponent<HealthSystem>();
            yield return Teleport(Corridor(-30f));
            Visited.Clear();
            Vector3 h0 = _movement.transform.position;
            health.TakeDamage(15, h0 + _movement.transform.forward * 2f);
            bool hurt = Current == _movement.HurtState;
            yield return 0.06f;
            bool hurtAnim = AnimIs(PlayerAnimatorIds.Hurt);
            yield return Tap(Key.Q);
            bool dodged = Visited.Contains(_movement.DodgeState);
            float pushed = 0f;
            for (float t = 0f; t < 1.2f && Current == _movement.HurtState; t += 0.02f)
            {
                pushed = Mathf.Max(pushed, Vector3.Distance(h0, _movement.transform.position));
                yield return 0.02f;
            }
            Check(hurt && hurtAnim && !dodged && Current == _movement.IdleState && pushed > 0.1f && pushed < 0.45f,
                  $"Recibir un golpe: reacción, empujón de {pushed:F2} m, sin esquivar durante ella y de vuelta a Idle");
            yield return 0.5f; // i-frames
            health.TakeDamage(25, _movement.transform.position + _movement.transform.forward * 2f);
            yield return 0.06f;
            Check(Current == _movement.HurtState && AnimIs(PlayerAnimatorIds.HurtHead), "Un golpe fuerte (25) usa la reacción a la cabeza");
            yield return 1f;

            // Dodge: a roll of ~2.6 m that slows down (it was a 6 m dash), then an attack right out of it
            yield return Teleport(Corridor(-30f));
            Visited.Clear();
            Vector3 d0 = _movement.transform.position;
            Keys(Key.S);
            yield return 0.15f;
            yield return Tap(Key.Q, Key.S);
            Keys();
            yield return 0.7f;
            float rolled = Vector3.Distance(d0, _movement.transform.position);
            Check(Visited.Contains(_movement.DodgeState) && _movement.DodgeState.LocalDirection.y < -0.5f && rolled > 2.0f && rolled < 3.4f,
                  $"Q + S: esquiva hacia atrás de {rolled:F2} m (dirección local {_movement.DodgeState.LocalDirection})");
            yield return Teleport(Corridor(-30f));
            yield return 0.5f; // dodge cooldown
            Visited.Clear();
            yield return Tap(Key.Q);
            yield return 0.25f;
            yield return Tap(Key.J);
            yield return 0.3f;
            int dodge = Visited.IndexOf(_movement.DodgeState);
            Check(dodge >= 0 && Visited.IndexOf(light) > dodge, "Después de la esquiva J responde con un ataque (GDD §5.5)");
            yield return 1f;

            // Block: −70 % from the front only, and a hit from behind breaks the guard
            yield return Teleport(Corridor(-30f));
            Keys(Key.L);
            yield return 0.6f;
            Check(Current == _movement.BlockState, "L mantenido bloquea");
            int b0 = health.CurrentHealth;
            health.TakeDamage(20, _movement.transform.position + _movement.transform.forward * 2f);
            int frontal = b0 - health.CurrentHealth;
            bool held = Current == _movement.BlockState;
            yield return 0.6f; // i-frames
            int b1 = health.CurrentHealth;
            health.TakeDamage(20, _movement.transform.position - _movement.transform.forward * 2f);
            int back = b1 - health.CurrentHealth;
            Check(frontal == 6 && back == 20, $"Bloqueo: golpe frontal de 20 quita {frontal}, por la espalda quita {back}");
            Check(held && Current == _movement.HurtState, "El golpe bloqueado no rompe la guardia; el de la espalda sí");
            Keys();
            yield return 1f;
            health.InitializeHealth(health.MaxHealth);

            string path = $"{CombatSheetFolder}/impactos.png";
            _combatSheet.Save(path);
            _combatSheet.Dispose();
            _combatSheet = null;
            File.WriteAllText(Path.ChangeExtension(path, ".txt"),
                "Columnas: jab · cross · gancho · patada (en el momento del impacto). Fila 1: de lado; fila 2: en diagonal.\n");
            Debug.Log($"{Tag} INFO  Hoja de impactos del combate: {path}");
        }

        /// <summary>One strike that connected with the dummy, as the test saw it.</summary>
        private sealed class CombatHit
        {
            public float Time, Distance, YawError;
            public int Damage;
            public string Attack;
        }

        /// <summary>What happened during a combat run, sampled every 10 ms.</summary>
        private sealed class CombatLog
        {
            public readonly List<CombatHit> Hits = new List<CombatHit>();
            public readonly HashSet<int> Anims = new HashSet<int>();
            public float MinDistance = float.MaxValue, MaxLimbDepth, MaxStepSpeed, MinSole = float.MaxValue, MaxSway, MaxDisplacement;
            public float EndedIdleAt = -1f, RunAt = -1f, HeavyUntil;
            public bool SawHitStop, SawCombo;
            public int MaxChain;
            public string DeepestLimb = "-";
            /// <summary>Closest the striking limb came to the dummy's surface during a strike (m; negative = inside), and the body's distance then.</summary>
            public float MinStrikeGap = float.MaxValue, StrikeBodyDistance = -1f;
            /// <summary>Logs every sample of the first 0.45 s (diagnostic).</summary>
            public bool Trace;
            /// <summary>Impacts drawn on the combat sheet: attack name → column.</summary>
            public Dictionary<string, int> Columns;

            public float FirstHit => Hits.Count > 0 ? Hits[0].Time : -1f;
            public float LastHitTime => Hits.Count > 0 ? Hits[Hits.Count - 1].Time : -1f;

            public bool Intervals(float min, float max)
            {
                for (int i = 1; i < Hits.Count; i++)
                {
                    float d = Hits[i].Time - Hits[i - 1].Time;
                    if (d < min || d > max) return false;
                }
                return true;
            }

            public string IntervalText
            {
                get
                {
                    var s = new StringBuilder();
                    for (int i = 1; i < Hits.Count; i++) s.Append(i > 1 ? " / " : "").Append((Hits[i].Time - Hits[i - 1].Time).ToString("F2"));
                    return s.Length > 0 ? s.ToString() : "-";
                }
            }

            public string Describe()
            {
                var s = new StringBuilder();
                foreach (CombatHit h in Hits) s.Append($"{h.Attack} {h.Damage} a {h.Time:F2} s ({h.Distance:F2} m, {h.YawError:F0}°); ");
                return s.Length > 0 ? s.ToString() : $"ninguno; el miembro pasó a {MinStrikeGap:F2} m de la superficie con el cuerpo a {StrikeBodyDistance:F2} m del eje";
            }
        }

        /// <summary>Places the player <paramref name="distance"/> m in front of the dummy, facing it (turned by <paramref name="yawOffset"/>).</summary>
        private static IEnumerator FaceDummy(float distance, float yawOffset = 0f)
        {
            yield return SettleDummy();
            Vector3 d = _dummy.transform.position;
            // The dummy stands at the entrance strip; the player comes from +Z (yaw 180 faces it)
            yield return Teleport(new Vector3(d.x, Floor + OriginAboveFeet, d.z + distance), 180f + yawOffset);
            Visited.Clear();
        }

        /// <summary>Waits (up to 4 s) for the dummy to stop swaying and return to its place.</summary>
        private static IEnumerator SettleDummy()
        {
            for (float t = 0f; t < 4f && (_dummy.Sway > 0.5f || _dummy.Displacement > 0.02f); t += 0.1f)
                yield return 0.1f;
        }

        /// <summary>
        /// Presses keys at the given times (each held 60 ms) while <paramref name="held"/> stays down, and
        /// samples the fight every 10 ms for <paramref name="duration"/> seconds.
        /// </summary>
        private static IEnumerator CombatRun(float duration, CombatLog log, Key[] held, params (float At, Key Key)[] presses)
        {
            held ??= new Key[0];
            Keys(held);
            float t0 = Time.time, release = -1f;
            int next = 0, hits = _dummy.Hits;
            ResetStep();
            while (Time.time - t0 < duration)
            {
                float t = Time.time - t0;
                if (next < presses.Length && t >= presses[next].At)
                {
                    var down = new Key[held.Length + 1];
                    held.CopyTo(down, 0);
                    down[held.Length] = presses[next].Key;
                    Keys(down);
                    release = t + 0.06f;
                    next++;
                }
                else if (release > 0f && t >= release)
                {
                    Keys(held);
                    release = -1f;
                }
                SampleCombat(log, t, ref hits);
                yield return 0.01f;
            }
            Keys();
        }

        private static void SampleCombat(CombatLog log, float t, ref int hits)
        {
            PlayerState s = Current;
            Vector3 p = _movement.transform.position, d = _dummy.transform.position;
            Vector3 to = new Vector3(d.x - p.x, 0f, d.z - p.z);
            float distance = to.magnitude;
            log.MinDistance = Mathf.Min(log.MinDistance, distance);
            log.MaxStepSpeed = Mathf.Max(log.MaxStepSpeed, StepSpeed());
            log.MaxSway = Mathf.Max(log.MaxSway, _dummy.Sway);
            log.MaxDisplacement = Mathf.Max(log.MaxDisplacement, _dummy.Displacement);
            if (_movement.IsGrounded) log.MinSole = Mathf.Min(log.MinSole, SoleClearance);
            if (_playerAnimator.IsHitStopped) log.SawHitStop = true;
            AnimatorStateInfo info = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0) : _animator.GetCurrentAnimatorStateInfo(0);
            log.Anims.Add(info.shortNameHash);
            if (s == _movement.LightAttackState) log.MaxChain = Mathf.Max(log.MaxChain, _movement.LightAttackState.ChainIndex);
            if (s == _movement.HeavyAttackState)
            {
                log.HeavyUntil = t;
                if (_movement.HeavyAttackState.FromCombo) log.SawCombo = true;
            }
            if (s == _movement.RunState && log.RunAt < 0f) log.RunAt = t;
            if (log.Trace && t < 0.45f)
                Debug.Log($"{Tag} TRACE t={t:F3} estado={s.GetType().Name} n={(s is PlayerAttackState tr ? tr.Progress : -2f):F3} dist={distance:F3} vel={_movement.Velocity} pesoMxM={(_movement.Locomotion != null ? _movement.Locomotion.Weight : -1f):F2} hitstop={_playerAnimator.IsHitStopped} deltaAnim/s={(Time.deltaTime > 0f ? Vector3.Dot(_animator.deltaPosition, _movement.transform.forward) / Time.deltaTime : 0f):F2} pos={_movement.transform.position.z:F3}");
            if (s is PlayerAttackState striking && striking.Progress >= striking.Data.HitStart && striking.Progress <= striking.Data.HitEnd)
            {
                Transform limbBone = _animator.GetBoneTransform(striking.Data.Limb);
                float gap = -SignedDepthInDummy(limbBone.position);
                if (gap < log.MinStrikeGap) { log.MinStrikeGap = gap; log.StrikeBodyDistance = distance; }
            }
            if (s == _movement.IdleState && log.Hits.Count > 0 && log.EndedIdleAt < 0f) log.EndedIdleAt = t;

            foreach ((Transform limb, string name) in new[] { (_handL, "mano izq"), (_handR, "mano der"), (_footL, "pie izq"), (_footR, "pie der") })
            {
                float depth = DepthInDummy(limb.position);
                if (depth > log.MaxLimbDepth) { log.MaxLimbDepth = depth; log.DeepestLimb = name; }
            }

            if (_dummy.Hits == hits) return;
            hits = _dummy.Hits;
            var attack = s as PlayerAttackState;
            float yawError = distance > 0.01f ? Vector3.Angle(_movement.transform.forward, to) : 0f;
            log.Hits.Add(new CombatHit
            {
                Time = t, Distance = distance, YawError = yawError, Damage = _dummy.LastDamage,
                Attack = attack != null ? attack.Data.Name : s.GetType().Name,
            });
            string attackName = log.Hits[log.Hits.Count - 1].Attack;
            Debug.Log($"{Tag} INFO  Impacto: {attackName} de {_dummy.LastDamage} a {t:F2} s, a {distance:F2} m del eje, {yawError:F0}° de la mira");
            if (_combatSheet != null && log.Columns != null && log.Columns.TryGetValue(attackName, out int column))
                CaptureCombat(column);
        }

        /// <summary>How deep (m) a point is inside the dummy's body capsule (0 if outside).</summary>
        private static float DepthInDummy(Vector3 point) => Mathf.Max(0f, SignedDepthInDummy(point));

        /// <summary>How deep (m) a point is inside the dummy's body capsule (negative: how far outside).</summary>
        private static float SignedDepthInDummy(Vector3 point)
        {
            if (_dummyBody == null) return -1f;
            Transform b = _dummyBody.transform;
            Vector3 axisBottom = b.TransformPoint(_dummyBody.center - Vector3.up * (_dummyBody.height * 0.5f - _dummyBody.radius));
            Vector3 axisTop = b.TransformPoint(_dummyBody.center + Vector3.up * (_dummyBody.height * 0.5f - _dummyBody.radius));
            Vector3 axis = axisTop - axisBottom;
            float k = Mathf.Clamp01(Vector3.Dot(point - axisBottom, axis) / axis.sqrMagnitude);
            return _dummyBody.radius - Vector3.Distance(point, axisBottom + axis * k);
        }

        /// <summary>Draws the pose of this moment (an impact) in column <paramref name="column"/> of the combat sheet: from the side and in diagonal.</summary>
        private static void CaptureCombat(int column)
        {
            Vector3 focus = Vector3.Lerp(_movement.transform.position, _dummy.transform.position, 0.5f);
            focus.y = Floor + 1.0f;
            _combatSheet.Capture(0, column, focus, Vector3.right, Floor);
            _combatSheet.Capture(1, column, focus, new Vector3(1f, 0f, -1f), Floor);
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

            // The cases measure distances from where the body stands: if the end of the previous
            // motion still moved it, place it again now that it is at rest
            Vector3 drift = _movement.transform.position - position;
            if (new Vector2(drift.x, drift.z).magnitude > 0.02f)
            {
                _movement.Teleport(position, facing);
                yield return 0.3f;
            }
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
