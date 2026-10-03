using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds and validates the player's animation setup (docs/arquitectura.md §5.10):
    ///  1. Extracts character.fbx's embedded textures (Unity does not use embedded textures until
    ///     they are extracted) so URP's FBX material import links the Base/Normal/Emission maps.
    ///  2. Imports the Ch45 guard transitions as Humanoid clips that reuse character.fbx's Avatar,
    ///     and sets the root motion of every clip the player uses (P22): locomotion, air, landing and
    ///     slide clips play in place (the Rigidbody moves the body); vault, ledge grab and climb keep
    ///     their root motion (PlayerAnimator applies it and warps it with MatchTarget). Clip settings
    ///     are edited through SerializedObject: ModelImporter.clipAnimations fails to marshal in this
    ///     Unity version and dropped the clips' curves (LHandCurve), which are restored here.
    ///  3. Generates Assets/Characters/Player/PlayerAnimator.controller (IK pass on).
    ///  4. Assigns it to Player.prefab's "Model" Animator (soles on the collider bottom, measured on
    ///     the idle pose), adds PlayerAnimator / PlayerAnimatorIK / PlayerContactIK.
    ///  5. "Validar Personaje" checks Avatars, material, clips, curves, missing scripts, sampled poses
    ///     and the model's foot contact.
    /// Re-running is safe: the controller keeps its GUID and is rebuilt in place.
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod WarriorWoke.EditorTools.PlayerAnimationSetup.SetupAndValidateBatch
    /// </summary>
    internal static class PlayerAnimationSetup
    {
        private const string PrefabPath     = "Assets/Prefabs/Player.prefab";
        private const string ScenePath      = "Assets/Scenes/Level-1.unity";
        private const string ModelPath      = "Assets/Characters/Player/character.fbx";
        private const string ControllerPath = "Assets/Characters/Player/PlayerAnimator.controller";
        private const string TexturesFolder = "Assets/Characters/Player/Textures";
        private const string Ch45Folder     = "Assets/Characters/Player/Animations/";
        private const string GuardEnterPath = Ch45Folder + "Ch45_nonPBR@Standing Idle To Fight Idle.fbx";
        private const string GuardExitPath  = Ch45Folder + "Ch45_nonPBR@Fight Idle To Standing Idle.fbx";
        private const string DpsFolder      = "Assets/ThirdParty/DynamicParkourSystem/Animations/";
        private const string LowPolyMove    = "Assets/LowPoly/Animations/Movement/";
        private const string LowPolyCombat  = "Assets/LowPoly/Animations/Combat/";
        private const string MixamoTake     = "mixamo.com";
        private const string ModelChildName = "Model";
        // Walking backward uses LowPoly "RunBackward" slowed down: the Ch45 "Walk Backward Arc" clip
        // was measured crouched (head ~1.0 m above the feet in every sample), not a natural walk.
        private const float  WalkBackTimeScale = 0.6f;

        // Durations the FSM gives each action (seconds). Clips are sped up to fit them.
        private const float LightAttackVisualTime = 0.5f;  // 0.25 s strike + combo window up to 0.5 s
        private const float HeavyAttackTime       = 0.8f;  // PlayerHeavyAttackState / GDD §5.7
        private const float DodgeTime             = 0.5f;  // PlayerDodgeState / GDD §5.5
        private const float ParkourClipSpeed      = 1.1f;  // ledge grab and climb: natural pace, slightly brisk
        private const float LandRollTime          = 1.1f;  // landing roll (Quaternius "Roll", 1.47 s natural)
        private const float GuardTransitionTime   = 0.3f;  // Ch45 guard enter/exit, sped up from ~1 s
        private const float LandTime              = 0.45f; // soft landing
        private const float LandRunTime           = 0.4f;  // landing into a run
        private const float LandHardTime          = 0.9f;  // heavy landing: the whole absorb, natural pace

        // Quaternius Universal Animation Library (CC0): crouch, slide, mantle and landing roll (P28)
        private const string QuaterniusFolder = "Assets/ThirdParty/Quaternius/Animations/";
        private const string Ual1 = QuaterniusFolder + "UAL1_Standard.fbx";
        private const string Ual2 = QuaterniusFolder + "UAL2_Standard.fbx";

        // Clips generated from other clips (reversed): walking backward from the DPS walk
        private const string GeneratedFolder  = "Assets/Characters/Player/Animations/Generated";
        private const string WalkBackwardPath = GeneratedFolder + "/WalkBackward.anim";
        private const string DropToHangPath   = GeneratedFolder + "/CrouchToBracedHang.anim";

        /// <summary>Measured ground speed of the fastest locomotion clip (Run), written to PlayerAnimator.</summary>
        private static float _fastestClipSpeed = 5.9f;

        // ─── Menu / batch entry points ───────────────────────────────────────────────

        [MenuItem("Tools/Warrior Woke/Configurar Animaciones del Jugador")]
        private static void SetupMenu()
        {
            if (Setup()) Validate();
        }

        [MenuItem("Tools/Warrior Woke/Validar Personaje")]
        private static void ValidateMenu() => Validate();

        public static void SetupAndValidateBatch()
        {
            bool ok = Setup() && ParkourObstaclePrefabs.Generate() && ParkourTestCircuitBuilder.Build() && Validate();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void ValidateBatch()
        {
            EditorApplication.Exit(Validate() ? 0 : 1);
        }

        // ─── Setup ───────────────────────────────────────────────────────────────────

        private static bool Setup()
        {
            Avatar avatar = LoadAvatar(ModelPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[PlayerAnimationSetup] {ModelPath} no tiene un Avatar Humanoid válido. Corre primero 'Configurar Modelo del Jugador'.");
                return false;
            }

            EnsureModelTextures();

            ConfigureCh45Clip(GuardEnterPath, PlayerAnimatorIds.BlockEnterName, false, avatar);
            ConfigureCh45Clip(GuardExitPath, PlayerAnimatorIds.BlockExitName, false, avatar);
            ConfigureClipImports();
            ConfigureQuaternius();
            GenerateReversedClip(LoadClip(DpsFolder + "Walk.fbx", "Walk"), WalkBackwardPath);
            GenerateReversedClip(LoadClip(DpsFolder + "Braced Hang Climb.fbx", "Braced Hang To Crouch"), DropToHangPath);

            var clips = new ClipSet();
            if (!clips.Load()) return false;

            AnimatorController controller = BuildController(clips);
            if (controller == null) return false;

            return AssignToPrefab(controller, avatar);
        }

        /// <summary>
        /// Extracts the 5 textures embedded in character.fbx (Ch45_1001_*) next to the model, marks the
        /// normal map as Normal Map and the gloss map as linear data, then reimports the model so URP's
        /// FBX material import assigns them. The FBX's own material is kept; nothing is replaced.
        /// </summary>
        private static void EnsureModelTextures()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) return;

            if (!AssetDatabase.IsValidFolder(TexturesFolder))
            {
                AssetDatabase.CreateFolder("Assets/Characters/Player", "Textures");
                if (!importer.ExtractTextures(TexturesFolder))
                    Debug.LogWarning("[PlayerAnimationSetup] character.fbx no tenía texturas embebidas que extraer.");
                AssetDatabase.Refresh();
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;

                bool changed = false;
                if (path.Contains("_Normal") && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
                if (path.Contains("_Glossiness") && ti.sRGBTexture)
                {
                    ti.sRGBTexture = false;
                    changed = true;
                }
                if (changed) ti.SaveAndReimport();
            }

            // Re-run the model import so its material links the textures that now exist on disk.
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Ch45 Mixamo clips share character.fbx's rig, so they reuse its Avatar.</summary>
        private static void ConfigureCh45Clip(string path, string clipName, bool loop, Avatar sourceAvatar)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[PlayerAnimationSetup] No se encontró {path}.");
                return;
            }

            importer.animationType       = ModelImporterAnimationType.Human;
            importer.avatarSetup         = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar        = sourceAvatar;
            importer.materialImportMode  = ModelImporterMaterialImportMode.None; // the model already has its material
            importer.importAnimation     = true;
            importer.importBlendShapes   = false;
            importer.importCameras       = false;
            importer.importLights        = false;

            ModelImporterClipAnimation source = null;
            foreach (ModelImporterClipAnimation clip in importer.defaultClipAnimations)
            {
                if (clip.takeName == MixamoTake) source = clip;
            }

            if (source == null)
            {
                Debug.LogError($"[PlayerAnimationSetup] {path} no tiene la toma '{MixamoTake}'.");
                return;
            }

            source.name = clipName;
            source.loopTime = loop;
            BakeRootIntoPose(source);
            importer.clipAnimations = new[] { source };
            importer.SaveAndReimport();
        }

        /// <summary>Root motion of a clip: what stays in the pose and what moves the GameObject.</summary>
        private enum RootMode
        {
            /// <summary>Everything baked into the pose (a loop that must not move at all).</summary>
            Baked,
            /// <summary>Horizontal travel removed from the pose (plays in place); vertical motion stays in the pose. The Rigidbody moves the body.</summary>
            InPlace,
            /// <summary>Horizontal and vertical travel are root motion (horizontal based on the center of mass): in place unless PlayerAnimator applies it (parkour).</summary>
            RootMotion,
        }

        private readonly struct ClipImport
        {
            public readonly string File, Clip;
            public readonly RootMode Mode;
            public readonly bool Loop, FeetBased;
            public ClipImport(string file, string clip, RootMode mode, bool loop, bool feetBased)
            {
                File = file; Clip = clip; Mode = mode; Loop = loop; FeetBased = feetBased;
            }
        }

        // Every non-Ch45 clip whose import the player depends on, with its root motion (decision P22).
        private static readonly ClipImport[] ClipImports =
        {
            new ClipImport(DpsFolder + "Walk.fbx", "Walk", RootMode.InPlace, true, true),
            new ClipImport(DpsFolder + "Jog Forward.fbx", "Jog Forward", RootMode.InPlace, true, true),
            new ClipImport(DpsFolder + "Run.fbx", "Run", RootMode.InPlace, true, true),
            new ClipImport(DpsFolder + "Fall Idle.fbx", "Fall A Loop", RootMode.InPlace, true, true),
            new ClipImport(DpsFolder + "Falling To Landing.fbx", "Falling To Landing", RootMode.InPlace, false, true),
            new ClipImport(DpsFolder + "Land To Run Forward.fbx", "Fall A Land To Run Forward", RootMode.InPlace, false, true),
            new ClipImport(DpsFolder + "VaultFence.fbx", "Vault1", RootMode.RootMotion, false, false),
            new ClipImport(DpsFolder + "Idle To Braced Hang.fbx", "Idle To Braced Hang", RootMode.RootMotion, false, false),
            new ClipImport(DpsFolder + "Braced Hanging Idle.fbx", "Hanging Idle", RootMode.Baked, true, false),
            new ClipImport(DpsFolder + "Braced Hang Climb.fbx", "Braced Hang To Crouch", RootMode.RootMotion, false, true),
            // LowPoly jump: the body rises by physics, so the clip's own rise must not stay in the pose
            new ClipImport(LowPolyMove + "Jumps.fbx", "Jump_Up", RootMode.RootMotion, false, true),
        };

        // LHandCurve of VaultFence (Dynamic Parkour System's original .meta): weight of the planted left hand.
        private static readonly Keyframe[] VaultHandCurveKeys =
        {
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.21843004f, 1f, 0f, 0f),
            new Keyframe(0.52901024f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f),
        };

        /// <summary>
        /// Applies ClipImports. The Dynamic Parkour System FBX get a Humanoid Avatar from their own
        /// Mixamo rig (the original copies Erika's Avatar, which is not imported). Clip ranges come
        /// from the original .meta files and are kept; only root motion, loop and curves are written.
        /// </summary>
        private static void ConfigureClipImports()
        {
            var files = new HashSet<string>();
            foreach (ClipImport c in ClipImports) files.Add(c.File);

            foreach (string path in files)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    Debug.LogError($"[PlayerAnimationSetup] No se encontró {path}.");
                    continue;
                }

                if (path.StartsWith(DpsFolder))
                {
                    importer.animationType      = ModelImporterAnimationType.Human;
                    importer.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.importAnimation    = true;
                }

                var so = new SerializedObject(importer);
                SerializedProperty clips = so.FindProperty("m_ClipAnimations");
                if (clips == null || clips.arraySize == 0)
                {
                    Debug.LogError($"[PlayerAnimationSetup] {path} no tiene clips definidos en su .meta.");
                    continue;
                }

                for (int i = 0; i < clips.arraySize; i++)
                {
                    SerializedProperty clip = clips.GetArrayElementAtIndex(i);
                    string clipName = clip.FindPropertyRelative("name").stringValue;
                    foreach (ClipImport c in ClipImports)
                    {
                        if (c.File != path || c.Clip != clipName) continue;
                        clip.FindPropertyRelative("loopTime").boolValue             = c.Loop;
                        clip.FindPropertyRelative("loopBlendOrientation").boolValue = true; // the code turns the body
                        clip.FindPropertyRelative("loopBlendPositionXZ").boolValue  = c.Mode == RootMode.Baked;
                        clip.FindPropertyRelative("loopBlendPositionY").boolValue   = c.Mode != RootMode.RootMotion;
                        clip.FindPropertyRelative("heightFromFeet").boolValue       = c.FeetBased;
                        // Root-motion clips follow the body's center of mass, so the pose stays over the
                        // root (Vault1 starts mid-run: based on "Original" its pose began 0.9 m ahead of it)
                        if (c.Mode == RootMode.RootMotion)
                            clip.FindPropertyRelative("keepOriginalPositionXZ").boolValue = false;
                        if (c.Clip == PlayerAnimatorIds.VaultClip)
                            SetClipCurve(clip, PlayerAnimatorIds.LHandCurve, new AnimationCurve(VaultHandCurveKeys));
                    }
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
        }

        /// <summary>One take of a Quaternius library and how it is imported.</summary>
        private readonly struct QuaterniusClip
        {
            public readonly string File, Take, Name;
            public readonly RootMode Mode;
            public readonly bool Loop;
            public QuaterniusClip(string file, string take, string name, RootMode mode, bool loop)
            {
                File = file; Take = take; Name = name; Mode = mode; Loop = loop;
            }
        }

        private static readonly QuaterniusClip[] QuaterniusClips =
        {
            new QuaterniusClip(Ual1, "Rig|Crouch_Idle_Loop", "Crouch_Idle", RootMode.Baked, true),
            new QuaterniusClip(Ual1, "Rig|Crouch_Fwd_Loop", "Crouch_Fwd", RootMode.InPlace, true),
            new QuaterniusClip(Ual1, "Rig|Roll", "Roll", RootMode.InPlace, false),
            new QuaterniusClip(Ual2, "Armature|Slide_Start", "Slide_Start", RootMode.InPlace, false),
            new QuaterniusClip(Ual2, "Armature|Slide_Loop", "Slide_Loop", RootMode.InPlace, true),
            new QuaterniusClip(Ual2, "Armature|Slide_Exit", "Slide_Exit", RootMode.InPlace, false),
            new QuaterniusClip(Ual2, "Armature|ClimbUp_1m_RM", PlayerAnimatorIds.MantleClip, RootMode.RootMotion, false),
        };

        /// <summary>
        /// Imports the Quaternius libraries as Humanoid (their own Avatar) with only the takes the player
        /// uses. Their rig faces −Z in the raw file, so the root keeps its original orientation (like
        /// the LowPoly and DPS clips) turned 180°: the clip faces and moves forward. (With the root based
        /// on the body orientation, the result depended on the pose and came out facing backward.) Only
        /// the clip setter is used (T22).
        /// </summary>
        private static void ConfigureQuaternius()
        {
            foreach (string path in new[] { Ual1, Ual2 })
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    Debug.LogError($"[PlayerAnimationSetup] No se encontró {path}.");
                    continue;
                }
                importer.animationType      = ModelImporterAnimationType.Human;
                importer.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation    = true;
                importer.importBlendShapes  = false;
                importer.importCameras      = false;
                importer.importLights       = false;

                var clips = new List<ModelImporterClipAnimation>();
                foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
                {
                    foreach (QuaterniusClip q in QuaterniusClips)
                    {
                        if (q.File != path || q.Take != take.takeName) continue;
                        take.name                   = q.Name;
                        take.loopTime               = q.Loop;
                        take.rotationOffset         = 180f;
                        take.lockRootRotation       = true;                          // the code turns the body
                        take.lockRootHeightY        = q.Mode != RootMode.RootMotion;
                        take.lockRootPositionXZ     = q.Mode == RootMode.Baked;
                        take.keepOriginalOrientation = true;
                        take.keepOriginalPositionY  = false;
                        take.keepOriginalPositionXZ = false;
                        take.heightFromFeet         = true;
                        clips.Add(take);
                    }
                }
                importer.clipAnimations = clips.ToArray();
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Writes <paramref name="path"/> as <paramref name="source"/> played backward (every curve,
        /// muscles and root, mirrored in time), keeping the asset's GUID when it already exists. A
        /// forward walk reversed is a convincing backward walk with the same pace and footfall.
        /// </summary>
        private static void GenerateReversedClip(AnimationClip source, string path)
        {
            if (source == null) return;
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/Characters/Player/Animations", "Generated");

            var reversed = new AnimationClip { frameRate = source.frameRate };
            float length = source.length;
            int curves = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                var keys = new Keyframe[curve.length];
                for (int i = 0; i < curve.length; i++)
                {
                    Keyframe k = curve.keys[curve.length - 1 - i];
                    keys[i] = new Keyframe(length - k.time, k.value, -k.outTangent, -k.inTangent, k.outWeight, k.inWeight);
                }
                AnimationUtility.SetEditorCurve(reversed, binding, new AnimationCurve(keys));
                curves++;
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            AnimationUtility.SetAnimationClipSettings(reversed, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(reversed, existing);
                existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(reversed, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PlayerAnimationSetup] {path}: '{source.name}' invertido ({curves} curvas).");
        }

        private static void SetClipCurve(SerializedProperty clip, string curveName, AnimationCurve curve)
        {
            SerializedProperty curves = clip.FindPropertyRelative("curves");
            SerializedProperty entry = null;
            for (int i = 0; i < curves.arraySize; i++)
            {
                if (curves.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == curveName)
                    entry = curves.GetArrayElementAtIndex(i);
            }
            if (entry == null)
            {
                curves.InsertArrayElementAtIndex(curves.arraySize);
                entry = curves.GetArrayElementAtIndex(curves.arraySize - 1);
                entry.FindPropertyRelative("name").stringValue = curveName;
            }
            entry.FindPropertyRelative("curve").animationCurveValue = curve;
        }

        /// <summary>Ch45 guard transitions are in place: everything stays in the pose.</summary>
        private static void BakeRootIntoPose(ModelImporterClipAnimation clip)
        {
            clip.lockRootRotation        = true;
            clip.lockRootHeightY         = true;
            clip.lockRootPositionXZ      = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY   = true;
            clip.keepOriginalPositionXZ  = true;
        }

        private static AnimatorController BuildController(ClipSet c)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Rebuild in place so the asset keeps its GUID (Player.prefab references it).
            foreach (AnimatorControllerParameter p in controller.parameters)
                controller.RemoveParameter(p);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState s in sm.states)
                sm.RemoveState(s.state);
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (sub is BlendTree) Object.DestroyImmediate(sub, true);
            }

            // IK pass: OnAnimatorIK drives the vault hand (PlayerAnimatorIK → PlayerAnimator.ApplyIK)
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            sm = controller.layers[0].stateMachine;

            controller.AddParameter(PlayerAnimatorIds.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.MoveZ, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimatorIds.LocomotionRate,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(PlayerAnimatorIds.DodgeX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.DodgeY, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.LHandCurve, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimatorIds.ParkourSpeed,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(new AnimatorControllerParameter { name = PlayerAnimatorIds.SlideEnterRate, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            // Locomotion: 2D directional blend on the velocity under the body (MoveX right, MoveZ
            // forward, m/s). Every clip sits at its measured ground velocity (ClipMeasurement), so the
            // blend picks the clips whose pace and direction match the real movement: walk, jog, run,
            // walking and running backward, strafes and diagonals. Beyond the run, LocomotionRate
            // plays it faster (sprint) instead of sliding the feet.
            Dictionary<AnimationClip, Vector2> pace = ClipMeasurement.GroundVelocities(c.Locomotion);
            AnimatorState locomotion = controller.CreateBlendTreeInController(PlayerAnimatorIds.LocomotionName, out BlendTree locoTree, 0);
            locoTree.blendType = BlendTreeType.FreeformDirectional2D;
            locoTree.blendParameter = PlayerAnimatorIds.MoveX;
            locoTree.blendParameterY = PlayerAnimatorIds.MoveZ;
            locoTree.AddChild(c.Idle, Vector2.zero);
            foreach (AnimationClip clip in c.Locomotion)
            {
                if (clip == c.Idle) continue;
                locoTree.AddChild(clip, pace[clip]);
                Debug.Log($"[PlayerAnimationSetup] Locomoción: '{clip.name}' en ({pace[clip].x:F2}, {pace[clip].y:F2}) m/s");
            }
            _fastestClipSpeed = pace[c.Run].y;
            locomotion.speedParameter = PlayerAnimatorIds.LocomotionRate;
            locomotion.speedParameterActive = true;
            sm.defaultState = locomotion;

            // Crouch: idle and crouched walk at its measured pace (the body turns toward where it moves)
            Dictionary<AnimationClip, Vector2> crouchPace = ClipMeasurement.GroundVelocities(new[] { c.CrouchFwd });
            AnimatorState crouch = controller.CreateBlendTreeInController(PlayerAnimatorIds.CrouchName, out BlendTree crouchTree, 0);
            crouchTree.blendType = BlendTreeType.FreeformDirectional2D;
            crouchTree.blendParameter = PlayerAnimatorIds.MoveX;
            crouchTree.blendParameterY = PlayerAnimatorIds.MoveZ;
            crouchTree.AddChild(c.CrouchIdle, Vector2.zero);
            crouchTree.AddChild(c.CrouchFwd, new Vector2(0f, Mathf.Max(0.3f, crouchPace[c.CrouchFwd].y)));

            // Air
            AddState(sm, PlayerAnimatorIds.JumpName, c.JumpUp, 1f, new Vector2(500, 0));
            AddState(sm, PlayerAnimatorIds.FallName, c.FallLoop, 1f, new Vector2(500, 60));
            AnimatorState land     = AddState(sm, PlayerAnimatorIds.LandName, c.Land, Fit(c.Land, LandTime), new Vector2(500, 120));
            AnimatorState landRun  = AddState(sm, PlayerAnimatorIds.LandRunName, c.LandRun, Fit(c.LandRun, LandRunTime), new Vector2(500, 180));
            AnimatorState landHard = AddState(sm, PlayerAnimatorIds.LandHardName, c.Land, Fit(c.Land, LandHardTime), new Vector2(500, 240));
            AddExitTimeTransition(land, locomotion);
            AddExitTimeTransition(landRun, locomotion);
            AddExitTimeTransition(landHard, locomotion, 0.8f, 0.3f); // the recovery blends into locomotion
            AnimatorState landRoll = AddState(sm, PlayerAnimatorIds.LandRollName, c.Roll, Fit(c.Roll, LandRollTime), new Vector2(500, 300));
            AddExitTimeTransition(landRoll, locomotion, 0.8f, 0.25f); // the roll comes up running

            // Parkour (Dynamic Parkour System clips)
            // Vault: playback speed = approach speed / clip speed (ParkourSpeed, set by PlayerAnimator)
            AnimatorState vault = AddState(sm, PlayerAnimatorIds.VaultName, c.Vault, 1f, new Vector2(800, 0));
            vault.speedParameter = PlayerAnimatorIds.ParkourSpeed;
            vault.speedParameterActive = true;

            // Slide (Quaternius, measured): the drop reaches the ground at 0.42 of "Slide_Start" (0.35 s,
            // faster for a faster entry), "Slide_Loop" is a true loop that lasts as long as the FSM's
            // contextual slide, and "Slide_Exit" gets up into the run
            AnimatorState slide     = AddState(sm, PlayerAnimatorIds.SlideName, c.SlideEnter, 1f, new Vector2(800, 60));
            slide.speedParameter = PlayerAnimatorIds.SlideEnterRate;
            slide.speedParameterActive = true;
            AnimatorState slideLoop = AddState(sm, PlayerAnimatorIds.SlideLoopName, c.SlideLoop, 1f, new Vector2(1050, 60));
            AnimatorState slideExit = AddState(sm, PlayerAnimatorIds.SlideExitName, c.SlideExit, 1f, new Vector2(1050, 120));
            // The last frame of "Slide_Start" is the first of "Slide_Loop" (measured): no blend needed,
            // and a blend there let a hand sink into the floor (the IK did not hold it mid-transition)
            AddExitTimeTransition(slide, slideLoop, 1f, 0f);
            AddExitTimeTransition(slideExit, locomotion, 0.85f, 0.15f);

            // Mantle: Quaternius "ClimbUp_1m" with root motion, warped onto the measured top
            AddState(sm, PlayerAnimatorIds.MantleName, c.Mantle, 1f, new Vector2(800, 120));

            AnimatorState ledgeGrab = AddState(sm, PlayerAnimatorIds.LedgeGrabName, c.LedgeEnter, ParkourClipSpeed, new Vector2(800, 180));
            AnimatorState ledgeHang = AddState(sm, PlayerAnimatorIds.LedgeHangName, c.LedgeHang, 1f, new Vector2(1050, 180));
            AddState(sm, PlayerAnimatorIds.LedgeClimbName, c.LedgeClimb, ParkourClipSpeed, new Vector2(800, 240));
            AddState(sm, PlayerAnimatorIds.LedgeDropName, c.LedgeDrop, 1f, new Vector2(800, 300));
            AddExitTimeTransition(ledgeGrab, ledgeHang, 0.9f, 0.15f);

            // Combat
            AddState(sm, PlayerAnimatorIds.LightAttackRightName, c.PunchRight, Fit(c.PunchRight, LightAttackVisualTime), new Vector2(-300, 0));
            AddState(sm, PlayerAnimatorIds.LightAttackLeftName, c.PunchLeft, Fit(c.PunchLeft, LightAttackVisualTime), new Vector2(-300, 60));
            AddState(sm, PlayerAnimatorIds.HeavyAttackName, c.HeavyAttack, Fit(c.HeavyAttack, HeavyAttackTime), new Vector2(-300, 120));

            // Guard: Ch45 enter transition → LowPoly blocking loop; exit transition → Locomotion
            AnimatorState blockEnter = AddState(sm, PlayerAnimatorIds.BlockEnterName, c.GuardEnter, Fit(c.GuardEnter, GuardTransitionTime), new Vector2(-300, 200));
            AnimatorState blockLoop  = AddState(sm, PlayerAnimatorIds.BlockLoopName, c.Blocking, 1f, new Vector2(-300, 260));
            AnimatorState blockExit  = AddState(sm, PlayerAnimatorIds.BlockExitName, c.GuardExit, Fit(c.GuardExit, GuardTransitionTime), new Vector2(-300, 320));
            AddExitTimeTransition(blockEnter, blockLoop);
            AddExitTimeTransition(blockExit, locomotion);

            // Dodge: directional roll chosen by DodgeX/DodgeY (local dodge direction)
            AnimatorState dodge = controller.CreateBlendTreeInController(PlayerAnimatorIds.DodgeName, out BlendTree dodgeTree, 0);
            dodgeTree.blendType = BlendTreeType.SimpleDirectional2D;
            dodgeTree.blendParameter = PlayerAnimatorIds.DodgeX;
            dodgeTree.blendParameterY = PlayerAnimatorIds.DodgeY;
            dodgeTree.AddChild(c.RollForward, new Vector2(0f, 1f));
            dodgeTree.AddChild(c.RollBackward, new Vector2(0f, -1f));
            dodgeTree.AddChild(c.RollLeft, new Vector2(-1f, 0f));
            dodgeTree.AddChild(c.RollRight, new Vector2(1f, 0f));
            dodge.speed = Fit(c.RollForward, DodgeTime);

            PlaceState(sm, locomotion, new Vector2(100, 0));
            PlaceState(sm, dodge, new Vector2(100, 200));
            PlaceState(sm, crouch, new Vector2(100, 100));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PlayerAnimationSetup] Controller generado: {ControllerPath} ({sm.states.Length} estados, {controller.parameters.Length} parámetros).");
            return controller;
        }

        private static bool AssignToPrefab(AnimatorController controller, Avatar avatar)
        {
            float soleBelowRoot = MeasureIdleSole(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[PlayerAnimationSetup] No se pudo abrir {PrefabPath}.");
                return false;
            }

            try
            {
                Transform model = root.transform.Find(ModelChildName);
                Animator animator = model != null ? model.GetComponent<Animator>() : null;
                if (animator == null)
                {
                    Debug.LogError("[PlayerAnimationSetup] Player.prefab no tiene el hijo 'Model' con Animator. Corre 'Configurar Modelo del Jugador'.");
                    return false;
                }

                animator.runtimeAnimatorController = controller;
                animator.avatar = avatar;
                animator.applyRootMotion = false; // PlayerAnimator turns it on only during parkour (P22)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // Soles on the collider bottom: the model was centered on its renderer bounds, which
                // include padding, and stood ~11 cm above the ground (measured on the idle pose).
                var capsule = root.GetComponent<CapsuleCollider>();
                model.localPosition = new Vector3(0f, -capsule.height * 0.5f - soleBelowRoot, 0f);

                // Interpolated body: the camera and the model follow the 50 Hz physics without jitter (T7)
                root.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;

                if (model.GetComponent<PlayerAnimatorIK>() == null)
                    model.gameObject.AddComponent<PlayerAnimatorIK>();
                if (root.GetComponent<PlayerContactIK>() == null)
                    root.AddComponent<PlayerContactIK>();

                var playerAnimator = root.GetComponent<PlayerAnimator>();
                if (playerAnimator == null)
                    playerAnimator = root.AddComponent<PlayerAnimator>();
                var so = new SerializedObject(playerAnimator);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("fastestClipSpeed").floatValue = _fastestClipSpeed;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Movement values of the human-scale tuning (P23) and the auto step layers
                var movement = new SerializedObject(root.GetComponent<PlayerMovement>());
                movement.FindProperty("SlideSpeed").floatValue = 7.5f;
                movement.FindProperty("JumpSpeed").floatValue = 4.5f;
                movement.FindProperty("Acceleration").floatValue = 10f;
                movement.FindProperty("Deceleration").floatValue = 13f;
                movement.FindProperty("stepLayer").intValue = LayerMask.GetMask("Ground", "Obstacle");
                movement.FindProperty("ceilingLayer").intValue = LayerMask.GetMask("Ground", "Obstacle");
                movement.ApplyModifiedPropertiesWithoutUndo();

                var env = new SerializedObject(root.GetComponent<EnvironmentChecker>());
                env.FindProperty("landingLayer").intValue = LayerMask.GetMask("Ground", "Obstacle");
                env.ApplyModifiedPropertiesWithoutUndo();

                var contact = new SerializedObject(root.GetComponent<PlayerContactIK>());
                contact.FindProperty("groundLayer").intValue = LayerMask.GetMask("Ground", "Obstacle");
                contact.FindProperty("wallLayer").intValue = LayerMask.GetMask("Obstacle");
                contact.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[PlayerAnimationSetup] Player.prefab: controller asignado, Model a y = {model.localPosition.y:F3} (suela {soleBelowRoot:F3} m bajo su raíz), Rigidbody interpolado, PlayerAnimator, PlayerAnimatorIK y PlayerContactIK presentes.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ─── Validation ──────────────────────────────────────────────────────────────

        private static bool Validate()
        {
            bool ok = true;

            Avatar avatar = LoadAvatar(ModelPath);
            ok &= Check(avatar != null && avatar.isValid && avatar.isHuman, $"Avatar Humanoid válido en {ModelPath}");

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { DpsFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Avatar a = LoadAvatar(path);
                ok &= Check(a != null && a.isValid && a.isHuman, $"Avatar Humanoid válido en {path}");
            }

            foreach (string path in new[] { Ual1, Ual2 })
            {
                Avatar a = LoadAvatar(path);
                ok &= Check(a != null && a.isValid && a.isHuman, $"Avatar Humanoid válido en {path}");
            }
            ok &= Check(AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkBackwardPath) != null, $"Existe el clip generado {WalkBackwardPath}");

            ReportMaterials();

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            ok &= Check(controller != null, $"Existe {ControllerPath}");
            if (controller != null)
            {
                ok &= Check(controller.layers[0].iKPass, "IK Pass activo en la capa base");
                foreach (ChildAnimatorState s in controller.layers[0].stateMachine.states)
                    ok &= Check(HasAllMotions(s.state.motion), $"Estado '{s.state.name}' tiene todos sus clips");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ok &= Check(prefab != null && CountMissingScripts(prefab) == 0, "Player.prefab sin Missing Scripts");
            if (prefab != null)
            {
                Transform model = prefab.transform.Find(ModelChildName);
                Animator animator = model != null ? model.GetComponent<Animator>() : null;
                ok &= Check(animator != null && animator.runtimeAnimatorController == controller, "Animator de 'Model' usa PlayerAnimator.controller");
                ok &= Check(animator != null && !animator.applyRootMotion, "Apply Root Motion desactivado por defecto (solo se activa en parkour)");
                ok &= Check(prefab.GetComponent<PlayerAnimator>() != null, "Player.prefab tiene PlayerAnimator");
                ok &= Check(prefab.GetComponent<PlayerContactIK>() != null, "Player.prefab tiene PlayerContactIK");
                ok &= Check(model != null && model.GetComponent<PlayerAnimatorIK>() != null, "'Model' tiene PlayerAnimatorIK");
                ok &= Check(prefab.GetComponent<Rigidbody>().interpolation == RigidbodyInterpolation.Interpolate, "Rigidbody interpolado");

                if (model != null)
                {
                    float halfHeight = prefab.GetComponent<CapsuleCollider>().height * 0.5f;
                    float soleGap = model.localPosition.y + halfHeight + MeasureIdleSole(prefab);
                    ok &= Check(Mathf.Abs(soleGap) < 0.01f, $"De pie, las suelas tocan la base del collider (diferencia {soleGap * 100f:F1} cm)");
                }

                AnimationClip vaultClip = LoadClip(DpsFolder + "VaultFence.fbx", PlayerAnimatorIds.VaultClip);
                ok &= Check(vaultClip != null && CurveKeyCount(vaultClip, PlayerAnimatorIds.LHandCurve) >= 3,
                            "El clip Vault1 tiene la curva LHandCurve (IK de la mano del vault)");

                if (animator != null && controller != null)
                    ok &= ValidatePoses(prefab, controller);
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int sceneMissing = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
                sceneMissing += CountMissingScripts(go);
            ok &= Check(sceneMissing == 0, $"{ScenePath} sin Missing Scripts");
            ok &= ParkourTestCircuitBuilder.Validate(scene);

            Debug.Log(ok ? "[PlayerAnimationSetup] VALIDACIÓN OK" : "[PlayerAnimationSetup] VALIDACIÓN CON ERRORES");
            return ok;
        }

        private static void ReportMaterials()
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (!(obj is Material mat)) continue;
                Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                Texture bump    = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                string bumpPath = bump != null ? AssetDatabase.GetAssetPath(bump) : "-";
                bool bumpIsNormal = bump != null && AssetImporter.GetAtPath(bumpPath) is TextureImporter ti && ti.textureType == TextureImporterType.NormalMap;
                Debug.Log($"[PlayerAnimationSetup] Material '{mat.name}': shader={mat.shader.name}, " +
                          $"_BaseMap={(baseMap != null ? baseMap.name : "NINGUNA")}, " +
                          $"_BumpMap={(bump != null ? bump.name : "NINGUNA")} ({bumpPath}, formato {(bump is Texture2D t2 ? t2.format.ToString() : "-")}, " +
                          $"importado como NormalMap: {bumpIsNormal}), _NORMALMAP={mat.IsKeywordEnabled("_NORMALMAP")}, " +
                          $"_EMISSION={mat.IsKeywordEnabled("_EMISSION")}, _BaseColor={(mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "-")}");
            }
        }

        // Clips whose mid pose is standing; the others (rolls, falls, vault, slide, hanging, landing) are not.
        private static readonly HashSet<string> UprightClips = new HashSet<string>
        {
            "Idle", "Walk", "Jog Forward", "Run", "RunBackward", "RunBackwardLeft", "RunBackwardRight", "RunLeft", "RunRight",
            "StrafeLeft", "StrafeRight", "WalkBackward", "Jump_Up",
            "PunchRight", "PunchLeft", "MeleeAttack_OneHanded",
            PlayerAnimatorIds.BlockEnterName, "BlockingLoop", PlayerAnimatorIds.BlockExitName,
        };

        /// <summary>
        /// Samples every clip used by the controller on a temporary instance (AnimationMode, which
        /// evaluates Humanoid retargeting in edit mode) and checks the pose is sane: no NaN, the pose
        /// actually differs from the bind pose (the clip drives the rig), and standing clips keep the
        /// head well above the feet. Catches broken Avatars or clips that do not map onto Ch45.
        /// </summary>
        private static bool ValidatePoses(GameObject prefab, AnimatorController controller)
        {
            bool ok = true;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            AnimationMode.StartAnimationMode();
            try
            {
                GameObject model = instance.transform.Find(ModelChildName).gameObject;
                Animator animator = model.GetComponent<Animator>();
                Transform head  = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 bindHand = handR.position;

                var seen = new HashSet<AnimationClip>();
                foreach (ChildAnimatorState s in controller.layers[0].stateMachine.states)
                {
                    foreach (AnimationClip clip in ClipsOf(s.state.motion))
                    {
                        if (!seen.Add(clip)) continue;

                        // Sample 5 moments; AnimationMode evaluates Humanoid retargeting in edit mode
                        // (Animator.Update does not).
                        float minHead = float.MaxValue, maxHead = float.MinValue, maxHand = 0f;
                        bool finite = true;
                        for (int i = 1; i <= 5; i++)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(model, clip, clip.length * (i / 6f));
                            AnimationMode.EndSampling();

                            float feetY = Mathf.Min(footL.position.y, footR.position.y);
                            float headAboveFeet = head.position.y - feetY;
                            finite &= !float.IsNaN(headAboveFeet);
                            minHead = Mathf.Min(minHead, headAboveFeet);
                            maxHead = Mathf.Max(maxHead, headAboveFeet);
                            maxHand = Mathf.Max(maxHand, Vector3.Distance(handR.position, bindHand));
                        }

                        bool driven = maxHand > 0.05f; // the clip really moves the Ch45 rig
                        bool poseOk = finite && driven && (!UprightClips.Contains(clip.name) || minHead > 1.1f); // 1.1: sprint leans forward
                        ok &= Check(poseOk, $"Pose del clip '{clip.name}' ({s.state.name}): cabeza entre {minHead:F2} y {maxHead:F2} m sobre los pies, mano desplazada hasta {maxHand:F2} m");
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);
            }
            return ok;
        }

        /// <summary>
        /// Height of the lowest vertex of the skinned mesh relative to the model's root, in the first
        /// frame of the Idle clip (negative = below the root). The soles, not the bones, touch the ground.
        /// </summary>
        private static float MeasureIdleSole(GameObject prefab)
        {
            AnimationClip idle = LoadClip(LowPolyMove + "Idle.fbx", "Idle");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var mesh = new Mesh();
            AnimationMode.StartAnimationMode();
            try
            {
                GameObject model = instance.transform.Find(ModelChildName).gameObject;
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(model, idle, 0f);
                AnimationMode.EndSampling();

                float min = float.MaxValue;
                foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.BakeMesh(mesh, true);
                    foreach (Vector3 v in mesh.vertices)
                        min = Mathf.Min(min, smr.transform.TransformPoint(v).y);
                }
                return min - model.transform.position.y;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(instance);
            }
        }

        private static int CurveKeyCount(AnimationClip clip, string curveName)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName == curveName)
                    return AnimationUtility.GetEditorCurve(clip, binding).length;
            }
            return 0;
        }

        private static IEnumerable<AnimationClip> ClipsOf(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree tree)
            {
                foreach (ChildMotion child in tree.children)
                {
                    foreach (AnimationClip c in ClipsOf(child.motion))
                        yield return c;
                }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, float speed, Vector2 position)
        {
            AnimatorState state = sm.AddState(name, new Vector3(position.x, position.y, 0f));
            state.motion = motion;
            state.speed = speed;
            return state;
        }

        private static void AddExitTimeTransition(AnimatorState from, AnimatorState to, float exitTime = 0.85f, float duration = 0.15f)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
            t.hasFixedDuration = true;
        }

        private static void PlaceState(AnimatorStateMachine sm, AnimatorState state, Vector2 position)
        {
            ChildAnimatorState[] states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state == state) states[i].position = new Vector3(position.x, position.y, 0f);
            }
            sm.states = states;
        }

        /// <summary>Playback speed that makes the clip last 'seconds'.</summary>
        private static float Fit(AnimationClip clip, float seconds) => clip != null && seconds > 0f ? clip.length / seconds : 1f;

        private static bool HasAllMotions(Motion motion)
        {
            if (motion == null) return false;
            if (motion is BlendTree tree)
            {
                foreach (ChildMotion child in tree.children)
                {
                    if (!HasAllMotions(child.motion)) return false;
                }
            }
            return true;
        }

        private static int CountMissingScripts(GameObject go)
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            foreach (Transform child in go.transform)
                count += CountMissingScripts(child.gameObject);
            return count;
        }

        private static Avatar LoadAvatar(string path)
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Avatar avatar) return avatar;
            }
            return null;
        }

        private static AnimationClip LoadClip(string path, string clipName)
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is AnimationClip clip && clip.name == clipName && !clip.name.StartsWith("__preview__"))
                    return clip;
            }
            Debug.LogError($"[PlayerAnimationSetup] No se encontró el clip '{clipName}' en {path}.");
            return null;
        }

        internal static bool Check(bool condition, string label)
        {
            if (condition) Debug.Log($"[PlayerAnimationSetup] OK    {label}");
            else Debug.LogError($"[PlayerAnimationSetup] FALLA {label}");
            return condition;
        }

        /// <summary>Every clip the controller uses, loaded from the assets already in the project.</summary>
        private sealed class ClipSet
        {
            public AnimationClip Idle, Walk, Jog, Run, WalkBackward, RunBackward, RunBackLeft, RunBackRight, RunDiagA, RunDiagB, StrafeLeft, StrafeRight;
            public AnimationClip CrouchIdle, CrouchFwd, Roll, Mantle, LedgeDrop;
            public AnimationClip JumpUp, FallLoop, Land, LandRun;
            public AnimationClip Vault, SlideEnter, SlideLoop, SlideExit, LedgeEnter, LedgeHang, LedgeClimb;

            /// <summary>Clips of the directional locomotion blend (placed at their measured velocity).</summary>
            public AnimationClip[] Locomotion => new[] { Idle, Walk, Jog, Run, WalkBackward, RunBackward, RunBackLeft, RunBackRight, RunDiagA, RunDiagB, StrafeLeft, StrafeRight };
            public AnimationClip RollForward, RollBackward, RollLeft, RollRight;
            public AnimationClip PunchRight, PunchLeft, HeavyAttack, Blocking, GuardEnter, GuardExit;

            public bool Load()
            {
                Idle         = LoadClip(LowPolyMove + "Idle.fbx", "Idle");
                Walk         = LoadClip(DpsFolder + "Walk.fbx", "Walk");
                Jog          = LoadClip(DpsFolder + "Jog Forward.fbx", "Jog Forward");
                Run          = LoadClip(DpsFolder + "Run.fbx", "Run");
                WalkBackward = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkBackwardPath);
                RunBackward  = LoadClip(LowPolyMove + "RunBackward.fbx", "RunBackward");
                RunBackLeft  = LoadClip(LowPolyMove + "RunBackwardLeft.fbx", "RunBackwardLeft");
                RunBackRight = LoadClip(LowPolyMove + "RunBackwardRight.fbx", "RunBackwardRight");
                RunDiagA     = LoadClip(LowPolyMove + "RunLeft.fbx", "RunLeft");   // measured: a forward diagonal
                RunDiagB     = LoadClip(LowPolyMove + "RunRight.fbx", "RunRight"); // measured: the other forward diagonal
                StrafeLeft   = LoadClip(LowPolyMove + "StrafeLeft.fbx", "StrafeLeft");
                StrafeRight  = LoadClip(LowPolyMove + "StrafeRight.fbx", "StrafeRight");
                CrouchIdle   = LoadClip(Ual1, "Crouch_Idle");
                CrouchFwd    = LoadClip(Ual1, "Crouch_Fwd");
                Roll         = LoadClip(Ual1, "Roll");
                Mantle       = LoadClip(Ual2, PlayerAnimatorIds.MantleClip);
                LedgeDrop    = AssetDatabase.LoadAssetAtPath<AnimationClip>(DropToHangPath);
                JumpUp       = LoadClip(LowPolyMove + "Jumps.fbx", "Jump_Up");
                FallLoop     = LoadClip(DpsFolder + "Fall Idle.fbx", "Fall A Loop");
                Land         = LoadClip(DpsFolder + "Falling To Landing.fbx", "Falling To Landing");
                LandRun      = LoadClip(DpsFolder + "Land To Run Forward.fbx", "Fall A Land To Run Forward");
                Vault        = LoadClip(DpsFolder + "VaultFence.fbx", "Vault1");
                SlideEnter   = LoadClip(Ual2, "Slide_Start");
                SlideLoop    = LoadClip(Ual2, "Slide_Loop");
                SlideExit    = LoadClip(Ual2, "Slide_Exit");
                LedgeEnter   = LoadClip(DpsFolder + "Idle To Braced Hang.fbx", "Idle To Braced Hang");
                LedgeHang    = LoadClip(DpsFolder + "Braced Hanging Idle.fbx", "Hanging Idle");
                LedgeClimb   = LoadClip(DpsFolder + "Braced Hang Climb.fbx", "Braced Hang To Crouch");
                RollForward  = LoadClip(LowPolyMove + "RollForward.fbx", "RollForward");
                RollBackward = LoadClip(LowPolyMove + "RollBackward.fbx", "RollBackward");
                RollLeft     = LoadClip(LowPolyMove + "RollLeft.fbx", "RollLeft");
                RollRight    = LoadClip(LowPolyMove + "RollRight.fbx", "RollRight");
                PunchRight   = LoadClip(LowPolyCombat + "PunchRight.fbx", "PunchRight");
                PunchLeft    = LoadClip(LowPolyCombat + "PunchLeft.fbx", "PunchLeft");
                HeavyAttack  = LoadClip(LowPolyCombat + "MeleeAttack_OneHanded.fbx", "MeleeAttack_OneHanded");
                Blocking     = LoadClip(LowPolyCombat + "BlockingLoop.fbx", "BlockingLoop");
                GuardEnter   = LoadClip(GuardEnterPath, PlayerAnimatorIds.BlockEnterName);
                GuardExit    = LoadClip(GuardExitPath, PlayerAnimatorIds.BlockExitName);

                foreach (AnimationClip clip in new[] { Idle, Walk, Jog, Run, WalkBackward, RunBackward, RunBackLeft, RunBackRight, RunDiagA, RunDiagB,
                                                       StrafeLeft, StrafeRight, CrouchIdle, CrouchFwd, Roll, Mantle, LedgeDrop, JumpUp, FallLoop, Land,
                                                       LandRun, Vault, SlideEnter, SlideLoop, SlideExit, LedgeEnter, LedgeHang, LedgeClimb,
                                                       RollForward, RollBackward, RollLeft, RollRight, PunchRight,
                                                       PunchLeft, HeavyAttack, Blocking, GuardEnter, GuardExit })
                {
                    if (clip == null) return false;
                }
                return true;
            }
        }
    }
}
