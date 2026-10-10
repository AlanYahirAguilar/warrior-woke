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
    /// Retarget probe for the mocap (P29, P34, P37): the Kinematica Demo takes (Unity Companion License),
    /// the 100STYLE takes (CC BY 4.0, backward and strafe) and the CMU kick (free, attribution
    /// requested). All are Generic rigs that must retarget cleanly onto Ch45 through Humanoid. For each source:
    ///  1. Imports its actor's skeleton as Humanoid with its own Avatar, and every clip of its Animations
    ///     folder as Humanoid copying that Avatar, root motion kept.
    ///  2. Plays each clip (PlayableGraph, root motion, Foot IK on/off) on the actor and on Ch45 at 30 Hz
    ///     and measures: planted-foot skating, how far the lowest sole is from the ground, travel speed
    ///     and hips height. A retarget that is clean keeps Ch45's numbers close to the actor's.
    ///  3. Renders contact sheets around the action (actor on top, Ch45 with Foot IK below) to
    ///     Logs/MocapProbe/{source}/.
    /// Menu: Tools → Warrior Woke → Probar Retarget del Mocap. Batch: MocapRetargetProbe.RunBatch
    /// (add "-wwSources CMU" to probe only some sources).
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
            /// <summary>Only the takes whose file name starts with this (several sources share a folder); null = all.</summary>
            public string ClipPrefix;
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
            new Source
            {
                // CMU mocap, Daz-friendly BVH (heavy attack kick, docs/arquitectura.md P37). Daz names:
                // Collar is the shoulder, Shldr the upper arm, Thigh the upper leg (the Buttock in between
                // is folded into it by bvh2fbx.py); no toes, so the feet end at the ankle. Every CMU subject
                // has its own skeleton (same names, other lengths), so each one is a source with its actor
                Name       = "CMU",
                ActorPath  = "Assets/ThirdParty/CMU/Character/CMU_135_04_FrontKick_Skeleton.fbx",
                ClipFolder = "Assets/ThirdParty/CMU/Animations",
                ClipPrefix = "CMU_135_",
                Mapping    = CmuMapping,
            },
            new Source
            {
                // CMU subject 14 (boxing): the hook of the light chain (P37)
                Name       = "CMU14",
                ActorPath  = "Assets/ThirdParty/CMU/Character/CMU_14_01_Hook_Skeleton.fbx",
                ClipFolder = "Assets/ThirdParty/CMU/Animations",
                ClipPrefix = "CMU_14_",
                Mapping    = CmuMapping,
            },
        };

        /// <summary>Humanoid mapping of the CMU Daz-friendly skeleton.</summary>
        private static Dictionary<string, string> CmuMapping => new Dictionary<string, string>
        {
            ["Hips"] = "hip", ["Spine"] = "abdomen", ["Chest"] = "chest", ["Neck"] = "neck", ["Head"] = "head",
            ["LeftShoulder"]  = "lCollar",  ["LeftUpperArm"]  = "lShldr",
            ["LeftLowerArm"]  = "lForeArm", ["LeftHand"]      = "lHand",
            ["RightShoulder"] = "rCollar",  ["RightUpperArm"] = "rShldr",
            ["RightLowerArm"] = "rForeArm", ["RightHand"]     = "rHand",
            ["LeftUpperLeg"]  = "lThigh",   ["LeftLowerLeg"]  = "lShin",  ["LeftFoot"]  = "lFoot",
            ["RightUpperLeg"] = "rThigh",   ["RightLowerLeg"] = "rShin",  ["RightFoot"] = "rFoot",
        };

        /// <summary>The take files of a source (its folder, filtered by its prefix).</summary>
        private static IEnumerable<string> TakePaths(Source source) =>
            AssetDatabase.FindAssets("t:Model", new[] { source.ClipFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => source.ClipPrefix == null || Path.GetFileName(p).StartsWith(source.ClipPrefix));

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
                if (!Selected(source.Name)) continue;
                Avatar avatar = ImportActor(source);
                if (avatar == null) { ok = false; continue; }
                List<AnimationClip> clips = ImportClips(source, avatar);
                if (clips.Count == 0) { Debug.LogError($"[MocapProbe] {source.Name}: no se importó ningún clip."); ok = false; continue; }
                Measure(source, clips);
            }
            return ok;
        }

        /// <summary>All sources, or only those after -wwSources on the command line (comma-separated).</summary>
        private static bool Selected(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-wwSources") return args[i + 1].Split(',').Contains(name);
            return true;
        }

        // ─── Import ──────────────────────────────────────────────────────────────────

        /// <summary>Imports the actor of the source named <paramref name="name"/> as Humanoid and returns its Avatar (PlayerAnimationSetup uses the CMU one).</summary>
        internal static Avatar ImportActor(string name)
        {
            Source source = Sources.FirstOrDefault(s => s.Name == name);
            return source != null ? ImportActor(source) : null;
        }

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
            string path = TakePaths(source).FirstOrDefault();
            if (path == null) return names;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return names;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                names.Add(t.name);
            return names;
        }

        private static List<AnimationClip> ImportClips(Source source, Avatar avatar)
        {
            var clips = new List<AnimationClip>();
            foreach (string path in TakePaths(source))
            {
                AnimationClip clip = ImportTake(path, avatar);
                if (clip != null) clips.Add(clip);
            }
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        /// <summary>
        /// Imports one mocap take as Humanoid copying <paramref name="avatar"/> (its actor's), with the
        /// whole take as one clip and its root motion kept. A take that also carries the player's vault
        /// sub-clips (VaultCatalogBuilder configures it, with this same whole-take clip first) is left as
        /// it is. Returns the whole-take clip, or null if it did not import.
        /// </summary>
        internal static AnimationClip ImportTake(string path, Avatar avatar)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return null;
            string name = Path.GetFileNameWithoutExtension(path);
            SerializedProperty existing = new SerializedObject(importer).FindProperty("m_ClipAnimations");
            if (existing != null && existing.arraySize > 1 && importer.animationType == ModelImporterAnimationType.Human)
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

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
            // the clip setter is used (T22); these takes have no curves to lose.
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes.Length == 0) { Debug.LogWarning($"[MocapProbe] {path} no tiene tomas."); return null; }
            ModelImporterClipAnimation take = takes[0];
            take.name                    = name;
            take.loopTime                = false;
            take.lockRootRotation        = false;
            // Ground locomotion of the motion matching database: height baked into the pose
            // (MxMLocomotionBuilder.IsGroundLocomotion); parkour takes keep their vertical root motion
            take.lockRootHeightY         = MxMLocomotionBuilder.IsGroundLocomotion(path);
            take.lockRootPositionXZ      = false;
            take.keepOriginalOrientation = false;
            take.keepOriginalPositionY   = false;
            take.keepOriginalPositionXZ  = false;
            take.heightFromFeet          = true;
            importer.clipAnimations = new[] { take };
            importer.SaveAndReimport();

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null && clip.humanMotion) return clip;
            Debug.LogError($"[MocapProbe] {name}: el clip no quedó como Humanoid.");
            return null;
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
            var sheets = new PoseSheetRenderer(CellWidth, CellHeight, 1.25f);

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
                    sheets.Begin(frames.Length, 2);

                    source     = Simulate(actor, clip, false, frames, (i, rig) => CaptureSide(sheets, 0, i, rig));
                    Track raw  = Simulate(ch45, clip, false, null, null);
                    Track ik   = Simulate(ch45, clip, true, frames, (i, rig) => CaptureSide(sheets, 1, i, rig));
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

        /// <summary>Side view of the rig (from its left), following the hips; ground at the model's floor.</summary>
        private static void CaptureSide(PoseSheetRenderer sheets, int row, int column, Rig rig)
        {
            Vector3 right = rig.LegR.position - rig.LegL.position;
            right.y = 0f;
            Vector3 forward = Vector3.Cross(right.normalized, Vector3.up);
            float floor = rig.Root.transform.position.y;
            Vector3 focus = new Vector3(rig.Hips.position.x, Mathf.Max(floor + 1.0f, rig.Hips.position.y + 0.05f), rig.Hips.position.z);
            sheets.Capture(row, column, focus, Quaternion.AngleAxis(-70f, Vector3.up) * forward, floor);
        }
    }
}
