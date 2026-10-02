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
        private const float SlideTime             = 0.8f;  // PlayerMovement.SlideDuration (shortest slide)
        private const float SlideEnterTime        = 0.3f;  // "Slide Down": the body drops to the ground
        private const float SlideExitTime         = 0.5f;  // "Slide Up": back on the feet, running
        private const float GuardTransitionTime   = 0.3f;  // Ch45 guard enter/exit, sped up from ~1 s
        private const float LandTime              = 0.45f; // soft landing
        private const float LandRunTime           = 0.4f;  // landing into a run
        private const float LandHardTime          = 0.9f;  // heavy landing: the whole absorb, natural pace

        // Locomotion blend thresholds = clip speed / BaseSpeed (5 m/s). Walk and Jog speeds were
        // measured from the DPS clips' root travel; walking backward is PlayerMovement.BackpedalSpeed.
        private const float BaseSpeed          = 5f;
        private const float WalkBackThreshold  = -1.5f / BaseSpeed;
        private const float WalkThreshold      = 1.7f / BaseSpeed;
        private const float JogThreshold       = 2.6f / BaseSpeed;
        private const float RunThreshold       = 1f;
        private const float SprintThreshold    = 1.4f; // PlayerMovement.SprintMultiplier

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
            bool ok = Setup() && ParkourTestCircuitBuilder.Build() && Validate();
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
            new ClipImport(DpsFolder + "Slide.fbx", "Slide Down", RootMode.InPlace, false, true),
            new ClipImport(DpsFolder + "Slide.fbx", "Slide", RootMode.InPlace, true, true),
            new ClipImport(DpsFolder + "Slide.fbx", "Slide Up", RootMode.InPlace, false, true),
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

            controller.AddParameter(PlayerAnimatorIds.Speed, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.DodgeX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.DodgeY, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimatorIds.LHandCurve, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimatorIds.ParkourSpeed,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

            // Locomotion: signed speed (1 = BaseSpeed). Backpedal < 0 < Walk < Jog < Run < Sprint.
            AnimatorState locomotion = controller.CreateBlendTreeInController(PlayerAnimatorIds.LocomotionName, out BlendTree locoTree, 0);
            locoTree.blendType = BlendTreeType.Simple1D;
            locoTree.blendParameter = PlayerAnimatorIds.Speed;
            locoTree.useAutomaticThresholds = false;
            locoTree.AddChild(c.WalkBackward, WalkBackThreshold);
            locoTree.AddChild(c.Idle, 0f);
            locoTree.AddChild(c.Walk, WalkThreshold);
            locoTree.AddChild(c.Jog, JogThreshold);
            locoTree.AddChild(c.Run, RunThreshold);
            locoTree.AddChild(c.Sprint, SprintThreshold);
            ChildMotion[] loco = locoTree.children;
            loco[0].timeScale = WalkBackTimeScale; // backward jog clip played at walking pace
            locoTree.children = loco;
            sm.defaultState = locomotion;

            // Air
            AddState(sm, PlayerAnimatorIds.JumpName, c.JumpUp, 1f, new Vector2(500, 0));
            AddState(sm, PlayerAnimatorIds.FallName, c.FallLoop, 1f, new Vector2(500, 60));
            AnimatorState land     = AddState(sm, PlayerAnimatorIds.LandName, c.Land, Fit(c.Land, LandTime), new Vector2(500, 120));
            AnimatorState landRun  = AddState(sm, PlayerAnimatorIds.LandRunName, c.LandRun, Fit(c.LandRun, LandRunTime), new Vector2(500, 180));
            AnimatorState landHard = AddState(sm, PlayerAnimatorIds.LandHardName, c.Land, Fit(c.Land, LandHardTime), new Vector2(500, 240));
            AddExitTimeTransition(land, locomotion);
            AddExitTimeTransition(landRun, locomotion);
            AddExitTimeTransition(landHard, locomotion, 0.8f, 0.3f); // the recovery blends into locomotion

            // Parkour (Dynamic Parkour System clips)
            // Vault: playback speed = approach speed / clip speed (ParkourSpeed, set by PlayerAnimator)
            AnimatorState vault = AddState(sm, PlayerAnimatorIds.VaultName, c.Vault, 1f, new Vector2(800, 0));
            vault.speedParameter = PlayerAnimatorIds.ParkourSpeed;
            vault.speedParameterActive = true;

            // Slide in three phases: drop to the ground → slide (loop, as long as the FSM slides) → get up
            AnimatorState slide     = AddState(sm, PlayerAnimatorIds.SlideName, c.SlideEnter, Fit(c.SlideEnter, SlideEnterTime), new Vector2(800, 60));
            AnimatorState slideLoop = AddState(sm, PlayerAnimatorIds.SlideLoopName, c.SlideLoop, 1f, new Vector2(1050, 60));
            AnimatorState slideExit = AddState(sm, PlayerAnimatorIds.SlideExitName, c.SlideExit, Fit(c.SlideExit, SlideExitTime), new Vector2(1050, 120));
            AddExitTimeTransition(slide, slideLoop, 0.9f, 0.1f);
            AddExitTimeTransition(slideExit, locomotion, 0.75f, 0.2f);

            AnimatorState ledgeGrab = AddState(sm, PlayerAnimatorIds.LedgeGrabName, c.LedgeEnter, ParkourClipSpeed, new Vector2(800, 180));
            AnimatorState ledgeHang = AddState(sm, PlayerAnimatorIds.LedgeHangName, c.LedgeHang, 1f, new Vector2(1050, 180));
            AddState(sm, PlayerAnimatorIds.LedgeClimbName, c.LedgeClimb, ParkourClipSpeed, new Vector2(800, 240));
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
                so.ApplyModifiedPropertiesWithoutUndo();

                // Movement values of the human-scale tuning (P23) and the auto step layers
                var movement = new SerializedObject(root.GetComponent<PlayerMovement>());
                movement.FindProperty("SlideSpeed").floatValue = 7.5f;
                movement.FindProperty("SlideDuration").floatValue = SlideTime;
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
            "Idle", "Walk", "Jog Forward", "Run", "Sprint", "RunBackward", "Jump_Up",
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
            public AnimationClip Idle, Walk, Jog, Run, Sprint, WalkBackward;
            public AnimationClip JumpUp, FallLoop, Land, LandRun;
            public AnimationClip Vault, SlideEnter, SlideLoop, SlideExit, LedgeEnter, LedgeHang, LedgeClimb;
            public AnimationClip RollForward, RollBackward, RollLeft, RollRight;
            public AnimationClip PunchRight, PunchLeft, HeavyAttack, Blocking, GuardEnter, GuardExit;

            public bool Load()
            {
                Idle         = LoadClip(LowPolyMove + "Idle.fbx", "Idle");
                Walk         = LoadClip(DpsFolder + "Walk.fbx", "Walk");
                Jog          = LoadClip(DpsFolder + "Jog Forward.fbx", "Jog Forward");
                Run          = LoadClip(DpsFolder + "Run.fbx", "Run");
                Sprint       = LoadClip(LowPolyMove + "Sprint.fbx", "Sprint");
                WalkBackward = LoadClip(LowPolyMove + "RunBackward.fbx", "RunBackward");
                JumpUp       = LoadClip(LowPolyMove + "Jumps.fbx", "Jump_Up");
                FallLoop     = LoadClip(DpsFolder + "Fall Idle.fbx", "Fall A Loop");
                Land         = LoadClip(DpsFolder + "Falling To Landing.fbx", "Falling To Landing");
                LandRun      = LoadClip(DpsFolder + "Land To Run Forward.fbx", "Fall A Land To Run Forward");
                Vault        = LoadClip(DpsFolder + "VaultFence.fbx", "Vault1");
                SlideEnter   = LoadClip(DpsFolder + "Slide.fbx", "Slide Down");
                SlideLoop    = LoadClip(DpsFolder + "Slide.fbx", "Slide");
                SlideExit    = LoadClip(DpsFolder + "Slide.fbx", "Slide Up");
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

                foreach (AnimationClip clip in new[] { Idle, Walk, Jog, Run, Sprint, WalkBackward, JumpUp, FallLoop, Land,
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
