using System.Collections.Generic;
using System.IO;
using System.Linq;
using MxM;
using MxMEditor;
using UnityEditor;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds and pre-processes the motion matching database of the locomotion (P29, P34): the
    /// Kinematica Demo takes for forward locomotion (starts, stops, plant turns, circles and snakes at
    /// walk, jog and sprint) and the 100STYLE Neutral and Rushed takes for backward, strafe and direction
    /// changes, all retargeted onto Ch45. Parkour takes (vaults, climbs, ledge) stay out: they become
    /// actions with their own clip, not searchable locomotion.
    ///  1. Recreates <see cref="PreProcessPath"/> (MxMPreProcessData) from the lists below, so the
    ///     database is always the result of this file and never of hand edits in the inspector.
    ///  2. Runs MxM's pre-processor into <see cref="AnimDataPath"/> (MxMAnimData), keeping its GUID and
    ///     its calibration sets when it already exists.
    /// Menu: Tools → Warrior Woke → Construir Datos de Motion Matching. Batch: MxMLocomotionBuilder.RunBatch.
    /// The clips must already be imported as Humanoid copying their actor's Avatar (MocapRetargetProbe).
    /// </summary>
    internal static class MxMLocomotionBuilder
    {
        public const string Folder         = "Assets/Data/MxM";
        public const string PreProcessPath = Folder + "/MxM_Locomotion_PreProcess.asset";
        public const string AnimDataPath   = Folder + "/MxM_Locomotion_AnimData.asset";
        public const string Ch45Path       = "Assets/Characters/Player/character.fbx";

        private const string Tag            = "[MxMBuilder]";
        private const string KinematicaClips = "Assets/ThirdParty/Kinematica/Animations/";
        private const string StyleClips      = "Assets/ThirdParty/100STYLE/Animations/";

        /// <summary>Pose sampling of the database (s). MxM's default is 0.1; 0.05 doubles the choices.</summary>
        private const float PoseInterval = 0.05f;

        /// <summary>Trajectory points matched against the input (s): two in the past, four ahead.</summary>
        private static readonly float[] TrajectoryTimes = { -0.5f, -0.25f, 0.2f, 0.4f, 0.7f, 1.0f };

        /// <summary>Joints whose position and velocity enter the pose cost.</summary>
        private static readonly HumanBodyBones[] PoseJoints =
            { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.Hips };

        private const string IdleClip = KinematicaClips + "Idle.fbx";

        /// <summary>
        /// Tag of the 100STYLE takes. MxM only searches poses whose tags equal the required tags, so the
        /// two sets never mix: free locomotion (no tags) searches Kinematica and strafe (this tag)
        /// searches 100STYLE. Without it MxM jumps between both styles while running forward.
        /// </summary>
        public const ETags StrafeTag = PlayerMxMLocomotion.StrafeTag;

        private static readonly (string category, ETags tags, string[] clips)[] Categories =
        {
            ("Kinematica", ETags.None, new[]
            {
                "Acceleration", "Start_Stop_1", "Start_Stop_2", "Stop_to_Face_1", "Plants_Turns_Regular_1",
                "Circles_Walk_1", "Circles_Jog_1", "Circles_Sprint_1", "Circles_Sprint_2",
                "Snakes_Walk", "Snakes_Jog", "Snakes_Sprint",
            }.Select(c => KinematicaClips + c + ".fbx").ToArray()),
            // Forward (FW/FR) and idle (ID) stay out: Kinematica already covers them in one style
            ("100STYLE", StrafeTag, new[]
            {
                "Neutral_BW", "Neutral_BR", "Neutral_SW", "Neutral_SR", "Neutral_TR1",
                "Rushed_BW",  "Rushed_BR",  "Rushed_SW",  "Rushed_SR",  "Rushed_TR1",
            }.Select(c => StyleClips + c + ".fbx").ToArray()),
        };

        /// <summary>
        /// Takes of the database. Their root height is baked into the pose (Root Transform Position Y:
        /// Bake Into Pose, based upon feet): on flat ground the vertical bob belongs to the pose, not to
        /// the root, or the motor (which holds the body on the ground) sinks the feet into the floor
        /// during the stance and the planted foot slides. Parkour takes keep their vertical root motion.
        /// </summary>
        public static bool IsGroundLocomotion(string clipPath) =>
            clipPath == IdleClip || Categories.Any(c => c.clips.Contains(clipPath));

        [MenuItem("Tools/Warrior Woke/Construir Datos de Motion Matching")]
        private static void RunMenu() => Build();

        public static void RunBatch()
        {
            int code = 1;
            try { code = Build() ? 0 : 1; }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        public static bool Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ch45Path);
            Animator animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogError($"{Tag} {Ch45Path} necesita un Animator con Avatar Humanoid en la raíz.");
                return false;
            }

            BakeRootHeight(IdleClip);
            foreach ((string _, ETags _, string[] paths) in Categories)
                foreach (string path in paths)
                    BakeRootHeight(path);

            AnimationClip idle = LoadClip(IdleClip);
            if (idle == null) return false;
            var categories = new List<(string name, ETags tags, List<AnimationClip> clips)>();
            foreach ((string category, ETags tags, string[] paths) in Categories)
            {
                var clips = new List<AnimationClip>();
                foreach (string path in paths)
                {
                    AnimationClip clip = LoadClip(path);
                    if (clip == null) return false;
                    clips.Add(clip);
                }
                categories.Add((category, tags, clips));
            }

            Directory.CreateDirectory(Folder);
            if (AssetDatabase.LoadAssetAtPath<Object>(PreProcessPath) != null)
                AssetDatabase.DeleteAsset(PreProcessPath);

            MxMPreProcessData data = ScriptableObject.CreateInstance<MxMPreProcessData>();
            AssetDatabase.CreateAsset(data, PreProcessPath);
            data.Prefab = prefab;
            data.TrajectoryPoints.Clear();
            data.TrajectoryPoints.AddRange(TrajectoryTimes);
            data.PoseJoints.Clear();
            foreach (HumanBodyBones bone in PoseJoints)
                data.PoseJoints.Add(new PoseJoint { BoneId = bone, BoneName = bone.ToString() });
            var so = new SerializedObject(data);
            so.FindProperty("m_poseInterval").floatValue = PoseInterval;
            so.FindProperty("m_getBonesByName").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            data.TagNames[0] = "Strafe";

            data.CompositeCategories.Clear();
            int poses = 0;
            for (int c = 0; c < categories.Count; c++)
            {
                var category = new CompositeCategory(categories[c].name);
                data.CompositeCategories.Add(category);
                foreach (AnimationClip clip in categories[c].clips)
                {
                    // Takes of continuous capture: their edges are a cut, not a pose to blend into
                    var composite = ScriptableObject.CreateInstance<MxMAnimationClipComposite>();
                    composite.name = clip.name + "_comp";
                    composite.PrimaryClip = clip;
                    composite.Looping = false;
                    composite.IgnoreEdges = true;
                    composite.ExtrapolateTrajectory = true;
                    composite.GlobalTags = categories[c].tags;
                    composite.GlobalFavourTags = ETags.None;
                    composite.TargetPreProcess = data;
                    composite.TargetPrefab = prefab;
                    composite.CategoryId = c;
                    composite.ValidateBaseData();
                    composite.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(composite, data);
                    category.Composites.Add(composite);
                    poses += Mathf.FloorToInt(clip.length / PoseInterval);
                }
            }

            var idleSet = ScriptableObject.CreateInstance<MxMAnimationIdleSet>();
            idleSet.name = "Idle_idleset";
            idleSet.TargetPreProcess = data;
            idleSet.SetPrimaryAnim(idle);
            idleSet.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(idleSet, data);
            data.AnimationIdleSets.Clear();
            data.AnimationIdleSets.Add(idleSet);

            data.ValidateData();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            // Same steps as the pre-process button of MxMPreProcessDataInspector
            var existing = AssetDatabase.LoadAssetAtPath<MxMAnimData>(AnimDataPath);
            var calibration = new List<CalibrationData>();
            if (existing != null && existing.CalibrationSets != null)
                calibration.AddRange(existing.CalibrationSets.Select(set => new CalibrationData(set)));
            MxMAnimData animData = existing != null ? existing : ScriptableObject.CreateInstance<MxMAnimData>();

            var preProcessor = new MxMPreProcessor();
            preProcessor.SetupSceneForProcessing(data);
            preProcessor.PreProcessData(animData);
            animData.InitializeCalibration(calibration);
            EditorUtility.SetDirty(animData);
            if (existing == null) AssetDatabase.CreateAsset(animData, AnimDataPath);
            data.LastSavedAnimData = animData;
            data.LastSaveDirectory = AnimDataPath.Replace(".asset", "");
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            int clipCount = categories.Sum(c => c.clips.Count);
            float minutes = categories.Sum(c => c.clips.Sum(k => k.length)) / 60f;
            Debug.Log($"{Tag} {AnimDataPath}: {animData.Poses.Length} poses (~{poses} esperadas) de {clipCount} tomas " +
                      $"({minutes:F1} min) + idle '{idle.name}', intervalo {PoseInterval} s, trayectoria [{string.Join(", ", TrajectoryTimes)}] s.");
            if (animData.Poses.Length == 0)
            {
                Debug.LogError($"{Tag} El pre-proceso no generó poses.");
                return false;
            }
            return true;
        }

        /// <summary>Root height into the pose. Edited through m_ClipAnimations (T22), only if it changes.</summary>
        private static void BakeRootHeight(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            var so = new SerializedObject(importer);
            SerializedProperty clips = so.FindProperty("m_ClipAnimations");
            bool changed = false;
            for (int i = 0; i < clips.arraySize; i++)
            {
                SerializedProperty clip = clips.GetArrayElementAtIndex(i);
                SerializedProperty bake = clip.FindPropertyRelative("loopBlendPositionY");
                SerializedProperty feet = clip.FindPropertyRelative("heightFromFeet");
                SerializedProperty original = clip.FindPropertyRelative("keepOriginalPositionY");
                if (bake.boolValue && feet.boolValue && !original.boolValue) continue;
                bake.boolValue = true;
                feet.boolValue = true;
                original.boolValue = false;
                changed = true;
            }
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            Debug.Log($"{Tag} {path}: altura de la raíz horneada en la pose.");
        }

        private static AnimationClip LoadClip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null || !clip.humanMotion)
            {
                Debug.LogError($"{Tag} {path}: falta el clip o no es Humanoid (corre Tools → Warrior Woke → Probar Retarget del Mocap).");
                return null;
            }
            return clip;
        }
    }
}
