using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Retarget probe for the motion matching mocap (P29, P34): the Kinematica Demo takes (Unity
    /// Companion License) and the 100STYLE Neutral takes (CC BY 4.0, backward and strafe). Both are
    /// Generic rigs that must retarget cleanly onto Ch45 through Humanoid. For each source:
    ///  1. Imports its actor's skeleton as Humanoid with its own Avatar, and every clip of its Animations
    ///     folder as Humanoid copying that Avatar, root motion kept.
    ///  2. Plays each clip (PlayableGraph, root motion, Foot IK on/off) on the actor and on Ch45 at 30 Hz
    ///     and measures: planted-foot skating, how far the lowest sole is from the ground, travel speed
    ///     and hips height. A retarget that is clean keeps Ch45's numbers close to the actor's.
    ///  3. Renders contact sheets around the action (actor on top, Ch45 with Foot IK below) to
    ///     Logs/MocapProbe/{source}/.
    /// Menu: Tools → Warrior Woke → Probar Retarget del Mocap. Batch: MocapRetargetProbe.RunBatch.
    /// </summary>
    internal static class MocapRetargetProbe
    {
        private const string Ch45Path   = "Assets/Characters/Player/character.fbx";
        private const string OutRoot    = "Logs/MocapProbe";
        private const float  SampleRate = 30f;
        private const int    SheetFrames = 6;
        private const int    CellWidth = 256, CellHeight = 384;

        /// <summary>One mocap source: its actor's skeleton and its takes.</summary>
        private sealed class Source
        {
            public string Name, ActorPath, ClipFolder;
            /// <summary>Explicit Humanoid mapping (human bone → transform); null = Unity's automatic one.</summary>
            public Dictionary<string, string> Mapping;
        }

        private static readonly Source[] Sources =
        {
            new Source
            {
                Name       = "Kinematica",
                ActorPath  = "Assets/ThirdParty/Kinematica/Character/Unit.FBX",
                ClipFolder = "Assets/ThirdParty/Kinematica/Animations",
            },
            new Source
            {
                // Its names would fool the automatic mapping (Collar is the shoulder, Shoulder the upper
                // arm, Hip the upper leg). Chest3 stays as an in-between spine bone.
                Name       = "100STYLE",
                ActorPath  = "Assets/ThirdParty/100STYLE/Character/Neutral_Skeleton.fbx",
                ClipFolder = "Assets/ThirdParty/100STYLE/Animations",
                Mapping = new Dictionary<string, string>
                {
                    ["Hips"] = "Hips", ["Spine"] = "Chest", ["Chest"] = "Chest2", ["UpperChest"] = "Chest4",
                    ["Neck"] = "Neck", ["Head"] = "Head",
                    ["LeftShoulder"]  = "LeftCollar",  ["LeftUpperArm"]  = "LeftShoulder",
                    ["LeftLowerArm"]  = "LeftElbow",   ["LeftHand"]      = "LeftWrist",
                    ["RightShoulder"] = "RightCollar", ["RightUpperArm"] = "RightShoulder",
                    ["RightLowerArm"] = "RightElbow",  ["RightHand"]     = "RightWrist",
                    ["LeftUpperLeg"]  = "LeftHip",     ["LeftLowerLeg"]  = "LeftKnee",
                    ["LeftFoot"]      = "LeftAnkle",   ["LeftToes"]      = "LeftToe",
                    ["RightUpperLeg"] = "RightHip",    ["RightLowerLeg"] = "RightKnee",
                    ["RightFoot"]     = "RightAnkle",  ["RightToes"]     = "RightToe",
                },
            },
        };

        [MenuItem("Tools/Warrior Woke/Probar Retarget del Mocap")]
        private static void RunMenu() => Run();

        public static void RunBatch()
        {
            int code = 1;
            try { code = Run() ? 0 : 1; }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        private static bool Run()
        {
            bool ok = true;
            foreach (Source source in Sources)
            {
                Avatar avatar = ImportActor(source);
                if (avatar == null) { ok = false; continue; }
                List<AnimationClip> clips = ImportClips(source, avatar);
                if (clips.Count == 0) { Debug.LogError($"[MocapProbe] {source.Name}: no se importó ningún clip."); ok = false; continue; }
                Measure(source, clips);
            }
            return ok;
        }

        // ─── Import ──────────────────────────────────────────────────────────────────

        private static Avatar ImportActor(Source source)
        {
            if (!(AssetImporter.GetAtPath(source.ActorPath) is ModelImporter importer))
            {
                Debug.LogError($"[MocapProbe] No se encontró {source.ActorPath}.");
                return null;
            }
            importer.animationType      = ModelImporterAnimationType.Human;
            importer.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation    = false;
            importer.importBlendShapes  = false;
            importer.importCameras      = false;
            importer.importLights       = false;
            importer.SaveAndReimport();

            // An Avatar copied from the actor must only map bones the clips also have (Unit has eyes and
            // a jaw the takes lack), or their import is refused
            HumanDescription description = importer.humanDescription;
            HumanBone[] human = description.human;
            if (source.Mapping != null)
                human = source.Mapping.Select(m => new HumanBone
                {
                    humanName = m.Key, boneName = m.Value, limit = new HumanLimit { useDefaultValues = true },
                }).ToArray();
            HashSet<string> clipBones = ClipSkeleton(source);
            if (clipBones.Count > 0)
            {
                foreach (HumanBone b in human.Where(b => !clipBones.Contains(b.boneName)))
                    Debug.Log($"[MocapProbe] {source.Name}: hueso opcional sin mapear (no está en los clips): {b.humanName} = {b.boneName}");
                human = human.Where(b => clipBones.Contains(b.boneName)).ToArray();
            }
            if (source.Mapping != null || human.Length != description.human.Length)
            {
                description.human = human;
                importer.humanDescription = description;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(source.ActorPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[MocapProbe] {source.Name}: el Avatar Humanoid de {source.ActorPath} no es válido.");
                return null;
            }
            var mapping = new StringBuilder($"[MocapProbe] {source.Name}: mapeo Humanoid:");
            foreach (HumanBone bone in ((ModelImporter)AssetImporter.GetAtPath(source.ActorPath)).humanDescription.human)
                mapping.Append(' ').Append(bone.humanName).Append('=').Append(bone.boneName).Append(';');
            Debug.Log(mapping.ToString());
            return avatar;
        }

        /// <summary>Transform names of the clips' skeleton (from the first clip file).</summary>
        private static HashSet<string> ClipSkeleton(Source source)
        {
            var names = new HashSet<string>();
            string guid = AssetDatabase.FindAssets("t:Model", new[] { source.ClipFolder }).FirstOrDefault();
            if (guid == null) return names;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (model == null) return names;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                names.Add(t.name);
            return names;
        }

        private static List<AnimationClip> ImportClips(Source source, Avatar avatar)
        {
            var clips = new List<AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { source.ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                string name = Path.GetFileNameWithoutExtension(path);

                // A failed Humanoid import leaves the file without takes: read them as Generic first
                if (importer.defaultClipAnimations.Length == 0)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.avatarSetup   = ModelImporterAvatarSetup.NoAvatar;
                    importer.clipAnimations = new ModelImporterClipAnimation[0];
                    importer.SaveAndReimport();
                }

                importer.animationType      = ModelImporterAnimationType.Human;
                importer.avatarSetup        = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar       = avatar;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation    = true;
                importer.importBlendShapes  = false;
                importer.importCameras      = false;
                importer.importLights       = false;

                // Root motion is kept in the clip (motion matching reads the trajectory from it). Only
                // the clip setter is used (T22).
                ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
                if (takes.Length == 0) { Debug.LogWarning($"[MocapProbe] {path} no tiene tomas."); continue; }
                ModelImporterClipAnimation take = takes[0];
                take.name                    = name;
                take.loopTime                = false;
                take.lockRootRotation        = false;
                take.lockRootHeightY         = false;
                take.lockRootPositionXZ      = false;
                take.keepOriginalOrientation = false;
                take.keepOriginalPositionY   = false;
                take.keepOriginalPositionXZ  = false;
                take.heightFromFeet          = true;
                importer.clipAnimations = new[] { take };
                importer.SaveAndReimport();

                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip != null && clip.humanMotion) clips.Add(clip);
                else Debug.LogError($"[MocapProbe] {name}: el clip no quedó como Humanoid.");
            }
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        // ─── Measurement ─────────────────────────────────────────────────────────────
        // Humanoid Foot IK is what pins the retargeted feet at runtime. Outside Play Mode two samplings
        // exist and each one misses something: AnimationMode.SampleAnimationClip writes the pose with
        // the clip's root travel but without Foot IK; a PlayableGraph sampled through AnimationMode
        // applies Foot IK but leaves the body in place. The probe takes both and carries the graph's
        // pose along the clip sampling's hips (position and rotation) to get Foot IK in world space.

        /// <summary>One sampled playback of a clip on one model.</summary>
        private sealed class Track
        {
            public float Dt;
            public Vector3[] Hips;
            public Quaternion[] HipsRot;
            public Vector3[] Contact;   // lowest point of the feet (sole under an ankle, or a toe)
            public float[] ContactY;    // its height above the model's ground
        }

        /// <summary>Per-model metrics of one clip.</summary>
        private struct Metrics
        {
            public float SkateMedian, SkateP90;    // horizontal speed of the planted contact point (m/s)
            public float SoleP5, SoleMedian;       // height of the lowest contact above the ground (m)
            public float Speed;                    // horizontal path of the hips per second (m/s)
            public float HipsMedian;               // hips height (m)
            public float SpeedPeak;                // p98 of the hips speed over 0.5 s windows (m/s)
        }

        private sealed class Rig
        {
            public GameObject Root;
            public Animator Animator;
            public Transform Hips, FootL, FootR, ToeL, ToeR, LegL, LegR;
            public float SoleL, SoleR;
            public Vector3 Home;
        }

        private static void Measure(Source src, List<AnimationClip> clips)
        {
            string outFolder = Path.Combine(OutRoot, src.Name);
            Directory.CreateDirectory(outFolder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Rig actor = CreateRig(src.ActorPath, new Vector3(-50f, 0f, 0f));
            Rig ch45 = CreateRig(Ch45Path, new Vector3(50f, 0f, 0f));
            if (actor == null || ch45 == null) return;
            var sheets = new SheetRenderer();

            var ci = CultureInfo.InvariantCulture;
            var csv = new StringBuilder("clip,length,frameRate,model,footIK,speed,speedPeak,hipsHeight,skateMedian,skateP90,soleP5,soleMedian\n");
            var summary = new StringBuilder($"[MocapProbe] {src.Name}: resumen — patinaje = velocidad del punto de apoyo (mediana / p90, m/s); suela = altura del apoyo más bajo (p5 / mediana, m):\n");

            try
            {
                foreach (AnimationClip clip in clips)
                {
                    // The source first, to find the moment worth looking at
                    Track source = Simulate(actor, clip, false, null, null);
                    int[] frames = PickSheetFrames(source);
                    sheets.Begin(frames.Length);

                    source     = Simulate(actor, clip, false, frames, (i, rig) => sheets.Capture(0, i, rig));
                    Track raw  = Simulate(ch45, clip, false, null, null);
                    Track ik   = Simulate(ch45, clip, true, frames, (i, rig) => sheets.Capture(1, i, rig));
                    CarryAlong(ik, raw);
                    sheets.Save(Path.Combine(outFolder, clip.name + ".png"));

                    Metrics mu = Evaluate(source, clip), mr = Evaluate(raw, clip), mi = Evaluate(ik, clip);
                    foreach ((string model, bool footIK, Metrics m) in new[] { (src.Name, false, mu), ("Ch45", false, mr), ("Ch45", true, mi) })
                    {
                        csv.Append(clip.name).Append(',').Append(clip.length.ToString("F2", ci)).Append(',')
                           .Append(clip.frameRate.ToString("F0", ci)).Append(',').Append(model).Append(',').Append(footIK ? 1 : 0);
                        foreach (float v in new[] { m.Speed, m.SpeedPeak, m.HipsMedian, m.SkateMedian, m.SkateP90, m.SoleP5, m.SoleMedian })
                            csv.Append(',').Append(v.ToString("F3", ci));
                        csv.Append('\n');
                    }
                    summary.AppendLine(string.Format(ci,
                        "  {0,-24} {1,5:F1} s | vel media {2:F2} / punta {3:F2} m/s | patinaje actor {4:F3}/{5:F3} · Ch45 {6:F3}/{7:F3} · Ch45+FootIK {8:F3}/{9:F3} | suela actor {10:F3}/{11:F3} · Ch45 {12:F3}/{13:F3} · +FootIK {14:F3}/{15:F3}",
                        clip.name, clip.length, mu.Speed, mu.SpeedPeak,
                        mu.SkateMedian, mu.SkateP90, mr.SkateMedian, mr.SkateP90, mi.SkateMedian, mi.SkateP90,
                        mu.SoleP5, mu.SoleMedian, mr.SoleP5, mr.SoleMedian, mi.SoleP5, mi.SoleMedian));
                }
            }
            finally
            {
                sheets.Dispose();
            }

            File.WriteAllText(Path.Combine(outFolder, "metrics.csv"), csv.ToString());
            Debug.Log(summary.ToString());
            Debug.Log($"[MocapProbe] {src.Name}: métricas en {outFolder}/metrics.csv y hojas de poses en {outFolder}/*.png (arriba el actor, abajo Ch45 con Foot IK)");
        }

        private static Rig CreateRig(string path, Vector3 position)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { Debug.LogError($"[MocapProbe] No se encontró {path}."); return null; }
            GameObject go = Object.Instantiate(asset, position, Quaternion.identity);
            Animator animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            if (animator.avatar == null)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = null;
            if (animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"[MocapProbe] {path} no tiene Avatar Humanoid.");
                return null;
            }
            return new Rig
            {
                Root = go, Animator = animator, Home = position,
                Hips  = animator.GetBoneTransform(HumanBodyBones.Hips),
                FootL = animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                FootR = animator.GetBoneTransform(HumanBodyBones.RightFoot),
                ToeL  = animator.GetBoneTransform(HumanBodyBones.LeftToes),
                ToeR  = animator.GetBoneTransform(HumanBodyBones.RightToes),
                LegL  = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                LegR  = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                SoleL = animator.leftFeetBottomHeight,
                SoleR = animator.rightFeetBottomHeight,
            };
        }

        /// <summary>
        /// Samples <paramref name="clip"/> on <paramref name="rig"/> at SampleRate: without Foot IK through
        /// clip sampling (root travel included), with Foot IK through a PlayableGraph (body in place, see
        /// CarryAlong). Calls <paramref name="capture"/> on the listed sample indices.
        /// </summary>
        private static Track Simulate(Rig rig, AnimationClip clip, bool footIK, int[] captureAt, System.Action<int, Rig> capture)
        {
            int n = Mathf.Max(2, Mathf.FloorToInt(clip.length * SampleRate));
            var track = new Track
            {
                Dt = clip.length / n, Hips = new Vector3[n + 1], HipsRot = new Quaternion[n + 1],
                Contact = new Vector3[n + 1], ContactY = new float[n + 1],
            };
            Transform root = rig.Root.transform;

            PlayableGraph graph = PlayableGraph.Create("MocapProbe");
            AnimationMode.StartAnimationMode();
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Animation", rig.Animator);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(true);
                playable.SetApplyPlayableIK(false);
                output.SetSourcePlayable(playable);

                int next = 0;
                for (int i = 0; i <= n; i++)
                {
                    root.SetPositionAndRotation(rig.Home, Quaternion.identity);
                    AnimationMode.BeginSampling();
                    if (footIK) AnimationMode.SamplePlayableGraph(graph, 0, i * track.Dt);
                    else        AnimationMode.SampleAnimationClip(rig.Root, clip, i * track.Dt);
                    AnimationMode.EndSampling();
                    Vector3 contact = LowestFootPoint(rig) - rig.Home;
                    track.Hips[i] = rig.Hips.position - rig.Home;
                    track.HipsRot[i] = rig.Hips.rotation;
                    track.Contact[i] = contact;
                    track.ContactY[i] = contact.y;
                    if (captureAt != null && next < captureAt.Length && captureAt[next] == i)
                    {
                        capture(next, rig);
                        next++;
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                graph.Destroy();
            }
            return track;
        }

        /// <summary>
        /// Moves the in-place Foot IK pose of <paramref name="ik"/> onto the travelling hips of
        /// <paramref name="withRoot"/> (same clip, same model), sample by sample: the rigid transform that
        /// takes the in-place hips to the travelling hips (horizontal and vertical: the graph also removes
        /// the root's height, e.g. the flight of a jog or a climb) carries the feet with them.
        /// </summary>
        private static void CarryAlong(Track ik, Track withRoot)
        {
            for (int i = 0; i < ik.Hips.Length; i++)
            {
                Quaternion turn = withRoot.HipsRot[i] * Quaternion.Inverse(ik.HipsRot[i]);
                Vector3 offset = Quaternion.Euler(0f, turn.eulerAngles.y, 0f) * (ik.Contact[i] - ik.Hips[i]);
                ik.Contact[i] = withRoot.Hips[i] + offset;
                ik.ContactY[i] = ik.Contact[i].y;
                ik.Hips[i] = withRoot.Hips[i];
                ik.HipsRot[i] = withRoot.HipsRot[i];
            }
        }

        /// <summary>Lowest point of the feet: a sole under an ankle, or a toe.</summary>
        private static Vector3 LowestFootPoint(Rig rig)
        {
            Vector3 best = rig.FootL.position + Vector3.down * rig.SoleL;
            Vector3 heelR = rig.FootR.position + Vector3.down * rig.SoleR;
            if (heelR.y < best.y) best = heelR;
            if (rig.ToeL != null && rig.ToeL.position.y < best.y) best = rig.ToeL.position;
            if (rig.ToeR != null && rig.ToeR.position.y < best.y) best = rig.ToeR.position;
            return best;
        }

        /// <summary>
        /// Planted contact = the lowest point of the feet within 2 cm of the clip's floor (p5) in two
        /// consecutive samples; its horizontal speed is the skating. The mocap on its own actor is the
        /// reference: what Ch45 adds on top of it comes from the retarget.
        /// </summary>
        private static Metrics Evaluate(Track t, AnimationClip clip)
        {
            int n = t.Hips.Length - 1;
            float floor = Percentile(t.ContactY, 0.05f);
            var skate = new List<float>();
            for (int i = 1; i <= n; i++)
            {
                if (t.ContactY[i] > floor + 0.02f || t.ContactY[i - 1] > floor + 0.02f) continue;
                Vector3 d = t.Contact[i] - t.Contact[i - 1];
                d.y = 0f;
                float v = d.magnitude / t.Dt;
                if (v < 6f) skate.Add(v); // the contact switching to the other foot is not skating
            }
            float path = 0f;
            for (int i = 1; i <= n; i++)
            {
                Vector3 d = t.Hips[i] - t.Hips[i - 1];
                d.y = 0f;
                path += d.magnitude;
            }
            int window = Mathf.Max(1, Mathf.RoundToInt(0.5f / t.Dt));
            var windowed = new List<float>();
            for (int i = window; i <= n; i++)
            {
                Vector3 d = t.Hips[i] - t.Hips[i - window];
                d.y = 0f;
                windowed.Add(d.magnitude / (window * t.Dt));
            }
            return new Metrics
            {
                SkateMedian = skate.Count > 0 ? Percentile(skate, 0.5f) : 0f,
                SkateP90    = skate.Count > 0 ? Percentile(skate, 0.9f) : 0f,
                SoleP5      = floor,
                SoleMedian  = Percentile(t.ContactY, 0.5f),
                Speed       = path / clip.length,
                SpeedPeak   = windowed.Count > 0 ? Percentile(windowed, 0.98f) : 0f,
                HipsMedian  = Percentile(t.Hips.Select(h => h.y), 0.5f),
            };
        }

        private static float Percentile(IEnumerable<float> values, float q)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0f;
            return sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
        }

        // ─── Contact sheets ──────────────────────────────────────────────────────────

        /// <summary>
        /// Sample indices worth looking at: around the highest lift of the feet (a vault, a climb) or,
        /// in ground locomotion, around the sharpest turn of the hips' path (a pivot, a plant turn).
        /// </summary>
        private static int[] PickSheetFrames(Track t)
        {
            int n = t.Hips.Length - 1;
            int span = Mathf.Max(1, Mathf.RoundToInt(0.25f / t.Dt));
            bool airborne = t.ContactY.Max() > 0.3f;
            int center = n / 2;
            float best = float.MinValue;
            for (int i = span; i <= n - span; i++)
            {
                float score;
                if (airborne) score = t.ContactY[i];
                else
                {
                    Vector3 a = t.Hips[i] - t.Hips[i - span], b = t.Hips[i + span] - t.Hips[i];
                    a.y = 0f; b.y = 0f;
                    score = a.magnitude > 0.05f && b.magnitude > 0.05f ? Vector3.Angle(a, b) : 0f;
                }
                if (score > best) { best = score; center = i; }
            }
            int step = Mathf.Max(1, Mathf.RoundToInt(0.24f / t.Dt));
            var frames = new int[SheetFrames];
            for (int f = 0; f < SheetFrames; f++)
                frames[f] = Mathf.Clamp(center + (f - SheetFrames / 2) * step, 0, n);
            for (int f = 1; f < SheetFrames; f++)
                frames[f] = Mathf.Max(frames[f], frames[f - 1] + 1);
            return frames;
        }

        private sealed class SheetRenderer : System.IDisposable
        {
            private readonly GameObject _camGo, _lightGo, _ground;
            private readonly Camera _cam;
            private readonly RenderTexture _rt;
            private readonly Texture2D _cell;
            private Texture2D _sheet;

            public SheetRenderer()
            {
                _camGo = new GameObject("ProbeCamera");
                _cam = _camGo.AddComponent<Camera>();
                _cam.orthographic = true;
                _cam.orthographicSize = 1.25f;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.82f, 0.84f, 0.88f);
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 30f;
                _lightGo = new GameObject("ProbeLight");
                var light = _lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                _lightGo.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
                _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                Object.DestroyImmediate(_ground.GetComponent<Collider>());
                _rt = new RenderTexture(CellWidth, CellHeight, 24, RenderTextureFormat.ARGB32);
                _cam.targetTexture = _rt;
                _cell = new Texture2D(CellWidth, CellHeight, TextureFormat.RGB24, false);
            }

            public void Begin(int frames)
            {
                if (_sheet != null) Object.DestroyImmediate(_sheet);
                _sheet = new Texture2D(CellWidth * frames, CellHeight * 2, TextureFormat.RGB24, false);
            }

            /// <summary>Side view of the rig (from its left), following the hips; ground at the model's floor.</summary>
            public void Capture(int row, int column, Rig rig)
            {
                Vector3 right = rig.LegR.position - rig.LegL.position;
                right.y = 0f;
                Vector3 forward = Vector3.Cross(right.normalized, Vector3.up);
                float floor = rig.Root.transform.position.y;
                Vector3 focus = new Vector3(rig.Hips.position.x, Mathf.Max(floor + 1.0f, rig.Hips.position.y + 0.05f), rig.Hips.position.z);
                Vector3 view = Quaternion.AngleAxis(-70f, Vector3.up) * forward;
                _camGo.transform.SetPositionAndRotation(focus - view * 8f, Quaternion.LookRotation(view, Vector3.up));
                _ground.transform.position = new Vector3(focus.x, floor, focus.z);

                _cam.Render();
                RenderTexture.active = _rt;
                _cell.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), 0, 0);
                _cell.Apply();
                RenderTexture.active = null;
                _sheet.SetPixels(column * CellWidth, (1 - row) * CellHeight, CellWidth, CellHeight, _cell.GetPixels());
            }

            public void Save(string path)
            {
                _sheet.Apply();
                File.WriteAllBytes(path, _sheet.EncodeToPNG());
            }

            public void Dispose()
            {
                _cam.targetTexture = null;
                Object.DestroyImmediate(_rt);
                Object.DestroyImmediate(_cell);
                if (_sheet != null) Object.DestroyImmediate(_sheet);
                Object.DestroyImmediate(_ground);
                Object.DestroyImmediate(_lightGo);
                Object.DestroyImmediate(_camGo);
            }
        }
    }
}
