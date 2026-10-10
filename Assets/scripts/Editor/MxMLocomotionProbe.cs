using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MxM;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Play Mode probe of the motion matching locomotion (P29) before it reaches the player: Ch45 with an
    /// MxMAnimator over the database of <see cref="MxMLocomotionBuilder"/> and an MxMTrajectoryGenerator
    /// fed with scripted input, on an empty plane (no CharacterController yet: MxM applies the root
    /// motion to the transform). Each scenario measures what the player would feel:
    ///  - speed held at the P33 gaits (walk 1.3, run 3.4, sprint 4.8 m/s; backward and strafe 2.0, P34),
    ///  - how long a 90° or 180° change of direction and a stop take,
    ///  - skating of the planted feet, soles on the ground, facing kept while strafing,
    ///  - pops (a bone jumping faster than any human limb) and which takes MxM picks.
    /// The thresholds are the acceptance criteria of the locomotion; the numbers go to
    /// Logs/MxMProbe/metrics.csv. Since phase 3 (P31) the model carries the player's feet rig
    /// (PlayerRigSetup: ground contact and foot lock with Animation Rigging), so the soles and the skating
    /// are measured as the player shows them; "-wwNoRig" measures the motion matching alone
    /// (Logs/MxMProbe/metrics_norig.csv). Menu: Tools → Warrior Woke → Probar Motion Matching.
    /// Batch: -executeMethod WarriorWoke.EditorTools.MxMLocomotionProbe.RunBatch (do not pass -quit).
    /// </summary>
    [InitializeOnLoad]
    internal static class MxMLocomotionProbe
    {
        private const string RunningKey = "WW_MxMProbe_Running";
        private const string FailsKey   = "WW_MxMProbe_Fails";
        private const string SweepKey   = "WW_MxMProbe_Sweep";
        private static bool _pinRoot = true;
        private const string Tag        = "[MxMProbe]";
        private const string OutFolder  = "Logs/MxMProbe";

        // Gaits (P33, P34)
        private const float Walk = 1.3f, Run = 3.4f, Sprint = 4.8f, Back = 2.0f;

        // Acceptance criteria
        private const float SpeedTolerance = 0.25f; // held speed within ±25 % of the gait
        private const float TurnTime90     = 0.8f;  // s until moving within 20° of the new direction
        private const float TurnTime180    = 1.2f;
        private const float StopTime       = 1.2f;  // s until below 0.2 m/s
        // Skating: at most 50 % above what the mocap itself does on Ch45 with Foot IK (MocapRetargetProbe,
        // same metric): walk 0.04–0.06, jog 0.08–0.16, sprint 0.19, 100STYLE 0.05–0.15 m/s
        private const float SkateMargin = 1.5f;
        private const float SkateWalk = 0.06f, SkateRun = 0.16f, SkateSprint = 0.19f, SkateStyle = 0.15f;
        private const float SoleSink    = 0.02f; // p5 of the soles at most this under the floor (source: ±1 cm)
        private const float StrafeFacing   = 25f;   // ° the body may turn away from the strafe facing
        private const float PopSpeed       = 10f;   // m/s of a limb relative to the hips: faster is a pose jump

        private static readonly Stack<IEnumerator> Routines = new Stack<IEnumerator>();
        private static float _waitUntil;
        private static int _fails, _errors;
        private static GameObject _rig;
        private static MxMAnimator _mxm;
        private static MxMTrajectoryGenerator _traj;
        private static Animator _animator;
        private static Transform[] _ends;   // feet, hands, head: pop detection
        private static Transform _footL, _footR, _toeL, _toeR, _hips;
        private static float _soleL, _soleR;
        private static StringBuilder _csv;

        static MxMLocomotionProbe()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Warrior Woke/Probar Motion Matching")]
        private static void RunMenu() => Start();

        public static void RunBatch() => Start();

        /// <summary>Diagnostic: runs forward under several MxM settings to isolate what makes the feet skate.</summary>
        public static void RunSweepBatch()
        {
            SessionState.SetBool(SweepKey, true);
            Start();
        }

        private static void Start()
        {
            var animData = AssetDatabase.LoadAssetAtPath<MxMAnimData>(MxMLocomotionBuilder.AnimDataPath);
            if (animData == null && !MxMLocomotionBuilder.Build())
            {
                Debug.LogError($"{Tag} No hay datos de motion matching.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            BuildScene();
            SessionState.SetBool(RunningKey, true);
            SessionState.SetInt(FailsKey, 0);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Unsaved scene: ground, light, camera and Ch45 with the MxM components.</summary>
        private static void BuildScene()
        {
            // NewScene unloads unused assets: the data is loaded after it, or the reference dies
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var animData = AssetDatabase.LoadAssetAtPath<MxMAnimData>(MxMLocomotionBuilder.AnimDataPath);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.layer = LayerMask.NameToLayer("Ground"); // what the feet rig stands on
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 6f, -12f), Quaternion.Euler(20f, 0f, 0f));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MxMLocomotionBuilder.Ch45Path);
            GameObject rig = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            rig.name = "MxMProbeRig";
            Animator animator = rig.GetComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Input comes from the probe (m_customInput); facing in world space (no camera)
            var traj = rig.AddComponent<MxMTrajectoryGenerator>();
            var st = new SerializedObject(traj);
            st.FindProperty("m_customInput").boolValue = true;
            st.FindProperty("m_maxSpeed").floatValue = Run;
            st.ApplyModifiedPropertiesWithoutUndo();

            // The trajectory generator requires an MxMAnimator, so it may already be there
            MxMAnimator mxm = rig.GetComponent<MxMAnimator>() ?? rig.AddComponent<MxMAnimator>();
            var sm = new SerializedObject(mxm);
            SerializedProperty data = sm.FindProperty("m_animData");
            data.arraySize = 1;
            data.GetArrayElementAtIndex(0).objectReferenceValue = animData;
            sm.FindProperty("m_rootMotionMode").enumValueIndex = (int)EMxMRootMotion.On;
            sm.FindProperty("m_applyHumanoidFootIK").boolValue = true; // mandatory for this mocap on Ch45
            // Stay on the playing take unless another one is clearly better: switching takes every
            // search blends feet of different phases (skating)
            sm.FindProperty("m_favourCurrentPose").boolValue = true;
            sm.FindProperty("m_nextPoseToleranceTest").boolValue = true;
            sm.ApplyModifiedPropertiesWithoutUndo();

            // The player's feet rig (P31); the root stands on the plane
            if (!NoRig) PlayerRigSetup.Build(animator, 0f, false);
        }

        /// <summary>"-wwNoRig" on the command line: measure the motion matching without the feet rig.</summary>
        private static bool NoRig => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-wwNoRig") >= 0;

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                _fails = 0;
                _errors = 0;
                Application.logMessageReceived += OnLog;
                Routines.Clear();
                Routines.Push(RunAll());
                _waitUntil = 0f;
                EditorApplication.update += Tick;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseBool(RunningKey);
                SessionState.EraseBool(SweepKey);
                int fails = SessionState.GetInt(FailsKey, 1);
                Debug.Log($"{Tag} {(fails == 0 ? "TODAS LAS PRUEBAS OK" : fails + " PRUEBA(S) FALLARON")}");
                if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || Routines.Count == 0) return;
            if (Time.time < _waitUntil) return;

            while (Routines.Count > 0)
            {
                IEnumerator top = Routines.Peek();
                bool more;
                try { more = top.MoveNext(); }
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
            Time.captureFramerate = 0;
            Routines.Clear();
            Check(_errors == 0, $"Sin errores ni excepciones en consola ({_errors})");
            if (_csv != null)
            {
                Directory.CreateDirectory(OutFolder);
                string file = NoRig ? "metrics_norig.csv" : "metrics.csv";
                File.WriteAllText(Path.Combine(OutFolder, file), _csv.ToString());
                Debug.Log($"{Tag} Métricas en {OutFolder}/{file} ({(NoRig ? "sin" : "con")} el rig de pies)");
            }
            SessionState.SetInt(FailsKey, _fails);
            EditorApplication.ExitPlaymode();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.StartsWith(Tag))
                _errors++;
        }

        private static bool Check(bool ok, string what)
        {
            if (ok) Debug.Log($"{Tag} OK   {what}");
            else { Debug.LogError($"{Tag} FALLA {what}"); _fails++; }
            return ok;
        }

        // ─── Scenarios ───────────────────────────────────────────────────────────────

        private static IEnumerator RunAll()
        {
            // Batch mode runs at ~1000 fps: at 1 ms per frame a millimetre of jitter reads as 1 m/s.
            // Game time advances 1/60 s per frame, as in the game.
            Time.captureFramerate = 60;
            _rig = GameObject.Find("MxMProbeRig");
            if (!Check(_rig != null, "El rig de prueba existe")) yield break;
            _mxm = _rig.GetComponent<MxMAnimator>();
            _traj = _rig.GetComponent<MxMTrajectoryGenerator>();
            _animator = _rig.GetComponent<Animator>();
            _footL = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _footR = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _hips  = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _toeL  = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
            _toeR  = _animator.GetBoneTransform(HumanBodyBones.RightToes);
            _soleL = _animator.leftFeetBottomHeight;
            _soleR = _animator.rightFeetBottomHeight;
            _ends = new[]
            {
                _footL, _footR, _animator.GetBoneTransform(HumanBodyBones.LeftHand),
                _animator.GetBoneTransform(HumanBodyBones.RightHand), _animator.GetBoneTransform(HumanBodyBones.Head),
            };
            _csv = new StringBuilder("scenario,target,heldSpeed,responseTime,skateMedian,skateP90,soleP5,soleMin,facingErrMax,pops,takes\n");

            yield return 1.0f;
            Check(_mxm.IsInitialized, "MxMAnimator inicializado con los datos");
            Debug.Log($"{Tag} Datos: {_mxm.CurrentAnimData.Poses.Length} poses, {_mxm.CurrentAnimData.Clips.Length} clips");

            if (SessionState.GetBool(SweepKey, false)) { yield return Sweep(); yield break; }

            yield return Idle();
            if (!NoRig)
            {
                // The rig must act on MxM's pose: a foot standing still in the idle locks
                var feet = _rig.GetComponentInChildren<GroundContactConstraint>();
                Check(feet != null && feet.LockCount > 0,
                      $"El rig de pies actúa sobre la pose de MxM (bloqueos {(feet != null ? feet.LockCount : 0)})");
            }
            yield return Gait("Caminar", Walk);
            yield return Gait("Correr", Run);
            yield return Gait("Sprint", Sprint);
            yield return Turn("Giro 90° corriendo", Run, Vector3.right, TurnTime90);
            yield return Turn("Giro 180° corriendo", Run, Vector3.back, TurnTime180);
            yield return Turn("Giro 180° en sprint", Sprint, Vector3.back, TurnTime180);
            yield return Stop("Frenar corriendo", Run);
            yield return Stop("Frenar en sprint", Sprint);
            yield return Strafe("Retroceso", Vector3.back);
            yield return Strafe("Strafe derecha", Vector3.right);
            yield return Strafe("Strafe izquierda", Vector3.left);
        }

        /// <summary>Samples taken while a scenario runs.</summary>
        private sealed class Recording
        {
            public readonly List<float> T = new List<float>();
            public readonly List<Vector3> Root = new List<Vector3>();
            public readonly List<Vector3> Forward = new List<Vector3>();
            public readonly List<Vector3> Contact = new List<Vector3>(); // lowest point of the feet
            public float SoleMin = float.MaxValue;
            public float SoleP5 => Percentile(Contact.Select(c => c.y).ToList(), 0.05f);
            public int Pops;
            public float LimbMax;   // fastest limb relative to the hips (m/s)
            public readonly Dictionary<string, float> Takes = new Dictionary<string, float>();

            private Vector3[] _lastEnds;
            private float _lastT = -1f;

            public void Sample()
            {
                float t = Time.time;
                if (t - _lastT < 1e-4f) return;
                Transform root = _rig.transform;
                // No CharacterController yet: the ground holds the body as the motor will (the takes carry
                // some vertical root motion that would otherwise lift the body over time)
                if (_pinRoot && Mathf.Abs(root.position.y) > 1e-4f) root.position = new Vector3(root.position.x, 0f, root.position.z);
                float floor = 0f;
                Vector3 hips = _hips.position;
                Vector3 contact = LowestFootPoint();
                SoleMin = Mathf.Min(SoleMin, contact.y - floor);
                Contact.Add(contact);

                if (_lastT >= 0f)
                {
                    float dt = t - _lastT;
                    for (int i = 0; i < _ends.Length; i++)
                    {
                        float v = ((_ends[i].position - hips) - _lastEnds[i]).magnitude / dt;
                        LimbMax = Mathf.Max(LimbMax, v);
                        if (v > PopSpeed) Pops++;
                    }
                    string take = CurrentTake();
                    Takes[take] = (Takes.TryGetValue(take, out float s) ? s : 0f) + dt;
                }
                _lastEnds ??= new Vector3[_ends.Length];
                for (int i = 0; i < _ends.Length; i++) _lastEnds[i] = _ends[i].position - hips;
                _lastT = t;
                T.Add(t);
                Root.Add(root.position);
                Forward.Add(root.forward);
            }

            /// <summary>
            /// Same metric as MocapRetargetProbe, so the numbers compare with the source mocap: the lowest
            /// point of the feet, within 2 cm of its p5 height in two consecutive samples, is planted; its
            /// horizontal speed is the skating (above 6 m/s it is the contact switching feet, not sliding).
            /// </summary>
            public List<float> Skate()
            {
                var heights = Contact.Select(c => c.y).ToList();
                float floor = Percentile(heights, 0.05f);
                var skate = new List<float>();
                for (int i = 1; i < Contact.Count; i++)
                {
                    if (Contact[i].y > floor + 0.02f || Contact[i - 1].y > floor + 0.02f) continue;
                    Vector3 d = Contact[i] - Contact[i - 1];
                    d.y = 0f;
                    float v = d.magnitude / (T[i] - T[i - 1]);
                    if (v < 6f) skate.Add(v);
                }
                return skate;
            }

            /// <summary>Horizontal velocity averaged over the last <paramref name="window"/> seconds before index i.</summary>
            public Vector3 Velocity(int i, float window = 0.2f)
            {
                int j = i;
                while (j > 0 && T[i] - T[j] < window) j--;
                if (j == i) return Vector3.zero;
                Vector3 d = Root[i] - Root[j];
                d.y = 0f;
                return d / (T[i] - T[j]);
            }

            public string TopTakes()
            {
                float total = Takes.Values.Sum();
                return string.Join(" ", Takes.OrderByDescending(k => k.Value).Take(3)
                    .Select(k => $"{k.Key}:{100f * k.Value / Mathf.Max(total, 1e-3f):F0}%"));
            }
        }

        private static Vector3 LowestFootPoint()
        {
            Vector3 best = _footL.position + Vector3.down * _soleL;
            Vector3 heelR = _footR.position + Vector3.down * _soleR;
            if (heelR.y < best.y) best = heelR;
            if (_toeL != null && _toeL.position.y < best.y) best = _toeL.position;
            if (_toeR != null && _toeR.position.y < best.y) best = _toeR.position;
            return best;
        }

        private static string CurrentTake()
        {
            MxMAnimData data = _mxm.CurrentAnimData;
            ref PoseData pose = ref _mxm.DominantPose;
            if (pose.AnimType == EMxMAnimtype.IdleSet) return "Idle";
            if (pose.AnimType == EMxMAnimtype.Composite && data.Composites != null && pose.AnimId < data.Composites.Length)
                return data.Clips[data.Composites[pose.AnimId].ClipIdA].name;
            if (pose.AnimType == EMxMAnimtype.Clip && data.ClipsData != null && pose.AnimId < data.ClipsData.Length)
                return data.Clips[data.ClipsData[pose.AnimId].ClipId].name;
            return pose.AnimType.ToString();
        }

        /// <summary>Holds an input for <paramref name="seconds"/>, sampling every frame.</summary>
        private static IEnumerator Hold(Recording rec, Vector3 input, float seconds)
        {
            _traj.InputVector = input;
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _traj.InputVector = input;
                rec?.Sample();
                yield return 0f;
            }
        }

        private static IEnumerator Settle()
        {
            _traj.TrajectoryMode = ETrajectoryMoveMode.Normal;
            yield return Hold(null, Vector3.zero, 1.5f);
        }

        private static float Median(List<float> v) => Percentile(v, 0.5f);

        private static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0) return 0f;
            float[] s = values.OrderBy(x => x).ToArray();
            return s[Mathf.Clamp(Mathf.RoundToInt(q * (s.Length - 1)), 0, s.Length - 1)];
        }

        /// <param name="skateRef">Skating of the source mocap on Ch45 for this kind of motion.</param>
        private static void Report(string name, float target, float held, float response, Recording rec, float facingErr, float skateRef)
        {
            var ci = CultureInfo.InvariantCulture;
            List<float> skate = rec.Skate();
            float skateMed = Median(skate), skateP90 = Percentile(skate, 0.9f), soleP5 = rec.SoleP5;
            Debug.Log(string.Format(ci,
                "{0} {1,-22} objetivo {2:F1} m/s | sostenida {3:F2} m/s | respuesta {4:F2} s | patinaje {5:F3}/{6:F3} m/s | suela p5 {13:F3} / peor {7:F3} m | error de orientación {8:F0}° | saltos {9} (extremidad más rápida {11:F1} m/s) | tomas {10} | {12:F0} fps",
                Tag, name, target, held, response, skateMed, skateP90, rec.SoleMin, facingErr, rec.Pops, rec.TopTakes(), rec.LimbMax,
                (rec.T.Count - 1) / Mathf.Max(1e-3f, rec.T[rec.T.Count - 1] - rec.T[0]), soleP5));
            _csv.Append(string.Format(ci, "{0},{1:F2},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F1},{9},{10}\n",
                name, target, held, response, skateMed, skateP90, soleP5, rec.SoleMin, facingErr, rec.Pops, rec.TopTakes()));
            float skateMax = skateRef * SkateMargin;
            Check(skateMed <= skateMax, $"{name}: patinaje de los pies (mediana {skateMed:F3} m/s ≤ {skateMax:F2}; el mocap solo: {skateRef})");
            Check(soleP5 >= -SoleSink, $"{name}: las suelas no se hunden (p5 {soleP5 * 100f:F1} cm; peor cuadro {rec.SoleMin * 100f:F1} cm)");
            Check(rec.Pops == 0, $"{name}: sin saltos de pose ({rec.Pops})");
        }

        private static float SkateFor(float speed) => speed <= Walk ? SkateWalk : speed <= Run ? SkateRun : SkateSprint;

        private static IEnumerator Sweep()
        {
            var configs = new (string name, System.Action apply)[]
            {
                ("trayectoria 15/10", () => { }),
                ("trayectoria 25/15", () => { _traj.PositionBias = 25f; _traj.DirectionBias = 15f; }),
            };
            foreach ((string name, System.Action apply) in configs)
            {
                apply();
                yield return Turn($"Giro 90° [{name}]", Run, Vector3.right, TurnTime90);
                yield return Turn($"Giro 180° [{name}]", Sprint, Vector3.back, TurnTime180);
                yield return Stop($"Frenar corriendo [{name}]", Run);
                yield return Stop($"Frenar en sprint [{name}]", Sprint);
            }
        }

        private static IEnumerator Idle()
        {
            yield return Settle();
            var rec = new Recording();
            Vector3 start = _rig.transform.position;
            yield return Hold(rec, Vector3.zero, 2f);
            Vector3 drift = _rig.transform.position - start;
            drift.y = 0f;
            Report("Idle", 0f, drift.magnitude / 2f, 0f, rec, 0f, SkateWalk);
            Check(drift.magnitude < 0.2f, $"Idle: el personaje se queda en su sitio (deriva {drift.magnitude:F2} m en 2 s)");
        }

        private static IEnumerator Gait(string name, float speed)
        {
            yield return Settle();
            _traj.MaxSpeed = speed;
            var rec = new Recording();
            yield return Hold(rec, Vector3.forward, 4f);
            // Held speed: the last 1.5 s
            int from = rec.T.FindIndex(t => t >= rec.T[rec.T.Count - 1] - 1.5f);
            Vector3 d = rec.Root[rec.Root.Count - 1] - rec.Root[from];
            d.y = 0f;
            float held = d.magnitude / (rec.T[rec.T.Count - 1] - rec.T[from]);
            // Response: time to reach 80 % of the gait
            float response = float.NaN;
            for (int i = 0; i < rec.T.Count; i++)
                if (rec.Velocity(i).magnitude >= 0.8f * speed) { response = rec.T[i] - rec.T[0]; break; }
            Report(name, speed, held, response, rec, 0f, SkateFor(speed));
            Check(Mathf.Abs(held - speed) <= SpeedTolerance * speed, $"{name}: velocidad sostenida {held:F2} m/s (objetivo {speed} ±{SpeedTolerance * 100f:F0} %)");
        }

        private static IEnumerator Turn(string name, float speed, Vector3 newDirection, float maxTime)
        {
            yield return Settle();
            _traj.MaxSpeed = speed;
            yield return Hold(null, Vector3.forward, 3f);
            var rec = new Recording();
            yield return Hold(rec, newDirection, 2.5f);
            float response = float.NaN;
            for (int i = 0; i < rec.T.Count; i++)
            {
                Vector3 v = rec.Velocity(i, 0.15f);
                if (v.magnitude > 0.5f * speed && Vector3.Angle(v, newDirection) < 20f) { response = rec.T[i] - rec.T[0]; break; }
            }
            Report(name, speed, rec.Velocity(rec.T.Count - 1, 0.5f).magnitude, response, rec, 0f, SkateFor(speed));
            Check(!float.IsNaN(response) && response <= maxTime, $"{name}: se mueve en la nueva dirección en {response:F2} s (≤ {maxTime})");
        }

        private static IEnumerator Stop(string name, float speed)
        {
            yield return Settle();
            _traj.MaxSpeed = speed;
            yield return Hold(null, Vector3.forward, 3f);
            var rec = new Recording();
            Vector3 start = _rig.transform.position;
            yield return Hold(rec, Vector3.zero, 2.5f);
            float response = float.NaN;
            for (int i = 0; i < rec.T.Count; i++)
                if (rec.T[i] - rec.T[0] > 0.1f && rec.Velocity(i, 0.15f).magnitude < 0.2f) { response = rec.T[i] - rec.T[0]; break; }
            Vector3 slide = _rig.transform.position - start;
            slide.y = 0f;
            Report(name, 0f, slide.magnitude, response, rec, 0f, SkateFor(speed));
            Check(!float.IsNaN(response) && response <= StopTime, $"{name}: se detiene en {response:F2} s (≤ {StopTime}), recorre {slide.magnitude:F2} m");
        }

        /// <summary>Strafe mode facing world forward while moving in <paramref name="direction"/>.</summary>
        private static IEnumerator Strafe(string name, Vector3 direction)
        {
            yield return Settle();
            _traj.MaxSpeed = Back;
            _traj.StrafeDirection = Vector3.forward;
            _traj.TrajectoryMode = ETrajectoryMoveMode.Strafe;
            _mxm.SetRequiredTags(MxMLocomotionBuilder.StrafeTag);
            var rec = new Recording();
            yield return Hold(rec, direction, 4f);
            _traj.TrajectoryMode = ETrajectoryMoveMode.Normal;
            _mxm.ClearRequiredTags();
            int from = rec.T.FindIndex(t => t >= rec.T[rec.T.Count - 1] - 1.5f);
            float facingErr = 0f;
            for (int i = from; i < rec.T.Count; i++)
            {
                Vector3 f = rec.Forward[i];
                f.y = 0f;
                facingErr = Mathf.Max(facingErr, Vector3.Angle(f, Vector3.forward));
            }
            Vector3 d = rec.Root[rec.Root.Count - 1] - rec.Root[from];
            d.y = 0f;
            float held = d.magnitude / (rec.T[rec.T.Count - 1] - rec.T[from]);
            float dirErr = d.sqrMagnitude > 1e-4f ? Vector3.Angle(d, direction) : 180f;
            Report(name, Back, held, 0f, rec, facingErr, SkateStyle);
            Check(Mathf.Abs(held - Back) <= SpeedTolerance * Back, $"{name}: velocidad sostenida {held:F2} m/s (objetivo {Back} ±{SpeedTolerance * 100f:F0} %)");
            Check(dirErr < 20f, $"{name}: se mueve en la dirección pedida (error {dirErr:F0}°)");
            Check(facingErr <= StrafeFacing, $"{name}: mantiene la orientación (se desvía hasta {facingErr:F0}° ≤ {StrafeFacing})");
        }
    }
}
