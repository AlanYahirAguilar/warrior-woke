using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds the enemies of the GDD (§12, §13; decision P40) until the team's models arrive (P4): Ch45
    /// tinted by type, with a primitive weapon in its hand, the EnemyAnimator controller and the EnemyData of
    /// each archetype with its attacks measured on the model:
    ///  1. Imports the Quaternius takes the enemies use (the list is PlayerAnimationSetup's: both setups
    ///     share the UAL importers).
    ///  2. Builds Assets/Characters/Enemy/EnemyAnimator.controller: the player's measured locomotion blend
    ///     (its idle swapped for the combat idle) and dodge, guard, hit reactions, death, the bow shot and one
    ///     state per attack clip.
    ///  3. Builds the five prefabs (Assets/Prefabs/Enemies): light and heavy warrior, archer, the rival clan's
    ///     leader and the Commander. Root at the feet: NavMeshAgent, kinematic Rigidbody, the hit capsule on
    ///     layer Enemy (what the player's blows sweep), a body capsule on Default (what stops the player's
    ///     CharacterController: Player and Enemy do not collide), HealthSystem, EnemyPerception,
    ///     EnemyAnimator, EnemyWeapon and Enemy; the model under "Model" with EnemyRootMotion.
    ///  4. Measures every attack on the prefab's own model and weapon (AnimationMode sampling): the strike
    ///     window is where the weapon's tip moves at half its peak speed or more around the peak (relative
    ///     to the hips, so the body's travel does not count), the reach is how far in front of the start the
    ///     tip gets during the window (with the clip's travel), the bow's release is the start of the drawing
    ///     hand's fastest motion. Writes the EnemyData assets (Assets/Data/Enemies) with GDD values.
    ///  5. Contact sheets of every attack with its window (Logs/EnemyClips/&lt;state&gt;.png) and a lineup of
    ///     the five prefabs (Logs/EnemyClips/lineup.png) for visual review.
    /// Menu: Tools → Warrior Woke → Configurar Enemigos. Batch: -executeMethod
    /// WarriorWoke.EditorTools.EnemySetup.SetupBatch.
    /// </summary>
    internal static class EnemySetup
    {
        public const string Folder         = "Assets/Characters/Enemy";
        public const string ControllerPath = Folder + "/EnemyAnimator.controller";
        public const string MaterialsFolder = Folder + "/Materials";
        public const string DataFolder     = "Assets/Data/Enemies";
        public const string PrefabFolder   = "Assets/Prefabs/Enemies";
        private const string ReviewFolder  = "Logs/EnemyClips";
        private const string Tag           = "[EnemySetup]";

        private const string ModelPath        = "Assets/Characters/Player/character.fbx";
        private const string PlayerController = "Assets/Characters/Player/PlayerAnimator.controller";
        private const string Ual1             = "Assets/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx";
        private const string Ual2             = "Assets/ThirdParty/Quaternius/Animations/UAL2_Standard.fbx";
        private const string LowPolyCombat    = "Assets/LowPoly/Animations/Combat/";

        /// <summary>Horizontal radius (m) of a human target: the reach counts the tip reaching its front.</summary>
        private const float TargetRadius = 0.35f;

        /// <summary>Metres the blade must go into the target's body for the reach to count it.</summary>
        private const float ReachMargin = 0.1f;

        /// <summary>
        /// Farthest centre (m ahead) of a body of radius TargetRadius straight ahead that the point
        /// <paramref name="q"/> (relative to the attacker) touches, or 0 if it is too far aside.
        /// </summary>
        private static float Touch(Vector3 q, Vector3 forward, Vector3 right)
        {
            float f = Vector3.Dot(q, forward), l = Vector3.Dot(q, right);
            if (Mathf.Abs(l) >= TargetRadius) return 0f;
            return f + Mathf.Sqrt(TargetRadius * TargetRadius - l * l);
        }

        /// <summary>Heights (m above the floor) of a standing body the reach is measured in.</summary>
        private const float BodyLow = 0.4f, BodyHigh = 1.8f;

        /// <summary>Travel (m) of an attack clip from which its step moves the body (root motion).</summary>
        private const float StepTravel = 0.15f;

        /// <summary>Seconds of the enemy's dodge (EnemyDodgeState).</summary>
        private const float DodgeTime = 0.5f;

        // ─── Prefab paths (the area builder and the tests place them) ───────────────

        public const string LightPrefab     = PrefabFolder + "/Enemigo_Ligero.prefab";
        public const string HeavyPrefab     = PrefabFolder + "/Enemigo_Pesado.prefab";
        public const string ArcherPrefab    = PrefabFolder + "/Enemigo_Arquero.prefab";
        public const string LeaderPrefab    = PrefabFolder + "/Jefe_LiderClan.prefab";
        public const string CommanderPrefab = PrefabFolder + "/Jefe_Comandante.prefab";

        // ─── Attack clips (one Animator state each) ─────────────────────────────────

        private readonly struct AttackClip
        {
            public readonly string State, File, Clip;
            public readonly bool Bow;
            public AttackClip(string state, string file, string clip, bool bow = false) { State = state; File = file; Clip = clip; Bow = bow; }
        }

        private static readonly AttackClip[] AttackClips =
        {
            new AttackClip("Corte_A", Ual2, "Sword_Regular_A"),
            new AttackClip("Corte_B", Ual2, "Sword_Regular_B"),
            new AttackClip("Corte_C", Ual2, "Sword_Regular_C"),
            new AttackClip("Estocada", Ual2, "Sword_Dash"),
            new AttackClip("Tajo", Ual1, "Sword_Attack"),
            new AttackClip("Golpe_Alto", LowPolyCombat + "MeleeAttack_TwoHanded.fbx", "MeleeAttack_TwoHanded"),
            new AttackClip("Embestida", Ual2, "Shield_Dash"),
            new AttackClip(EnemyAnimator.ShootName, LowPolyCombat + "BowShot.fbx", "BowShot", true),
        };

        // ─── Archetypes ─────────────────────────────────────────────────────────────

        /// <summary>One attack of an archetype: its clip state and the GDD/design values; the phases are measured.</summary>
        private readonly struct AttackSpec
        {
            public readonly string Name, State;
            public readonly float Rate, WindupHold, VulnerableAfter;
            public readonly int Damage, Next;
            public readonly bool Heavy, RootMotion, BodyHit;
            public AttackSpec(string name, string state, int damage, float rate, int next = -1, bool heavy = false,
                              float hold = 0f, float vulnerable = 0f, bool rootMotion = false, bool bodyHit = false)
            {
                Name = name; State = state; Damage = damage; Rate = rate; Next = next; Heavy = heavy;
                WindupHold = hold; VulnerableAfter = vulnerable; RootMotion = rootMotion; BodyHit = bodyHit;
            }
        }

        private sealed class Archetype
        {
            public string Name, Prefab, Asset;
            public EnemyKind Kind;
            public BossStyle Boss;
            public EnemyWeaponKind Weapon;
            public Color Tint;
            public float Scale = 1f;
            public System.Action<EnemyData> Tune;
            public AttackSpec[] Attacks;
        }

        private static readonly Archetype[] Archetypes =
        {
            // GDD §12: 80 HP, 12 per cut. Comes in fast, chains cuts, dodges; weak to heavy blows and to
            // the answer after a block or a dodge. Every blow flinches it (poise below the player's jab)
            new Archetype
            {
                Name = "Guerrero ligero", Prefab = LightPrefab, Asset = "Ligero", Kind = EnemyKind.Light, Weapon = EnemyWeaponKind.Katana,
                Tint = new Color(0.80f, 0.32f, 0.26f),
                Tune = d =>
                {
                    d.maxHealth = 80; d.poise = 10; d.iFrames = 0.1f;
                    d.walkSpeed = 1.6f; d.runSpeed = 4.6f; d.strafeSpeed = 2.0f; d.turnRate = 600f;
                    // Close: it waits inside its first cut's reach (2.3 m), so it opens with the combo
                    d.preferredMin = 1.5f; d.preferredMax = 2.3f; d.attackIntervalMin = 0.5f; d.attackIntervalMax = 1.1f;
                    d.blockChance = 0.05f; d.dodgeChance = 0.35f; d.punishChance = 0.6f; d.guardDamageKept = 0.3f;
                },
                Attacks = new[]
                {
                    new AttackSpec("Corte 1", "Corte_A", 12, 1.15f, next: 1, vulnerable: 0.15f),
                    new AttackSpec("Corte 2", "Corte_B", 12, 1.15f, next: 2, vulnerable: 0.15f),
                    new AttackSpec("Corte 3", "Corte_C", 12, 1.1f, vulnerable: 0.35f),
                    new AttackSpec("Estocada", "Estocada", 12, 1.0f, vulnerable: 0.4f, rootMotion: true),
                },
            },
            // GDD §12: 150 HP, 25 per blow. Advances slowly, blocks, charges; every blow is telegraphed (the
            // wind-up is held) and leaves an opening. Light blows hurt it but do not interrupt it
            new Archetype
            {
                Name = "Guerrero pesado", Prefab = HeavyPrefab, Asset = "Pesado", Kind = EnemyKind.Heavy, Weapon = EnemyWeaponKind.Kanabo,
                Tint = new Color(0.32f, 0.38f, 0.55f), Scale = 1.12f,
                Tune = d =>
                {
                    d.maxHealth = 150; d.poise = 45; d.iFrames = 0.1f;
                    d.walkSpeed = 1.3f; d.runSpeed = 3.0f; d.strafeSpeed = 1.2f; d.turnRate = 300f;
                    d.preferredMin = 2.4f; d.preferredMax = 3.3f; d.attackIntervalMin = 1.3f; d.attackIntervalMax = 2.1f;
                    d.blockChance = 0.35f; d.dodgeChance = 0f; d.punishChance = 0.3f; d.guardDamageKept = 0.2f;
                },
                Attacks = new[]
                {
                    new AttackSpec("Golpe alto", "Golpe_Alto", 25, 0.85f, heavy: true, hold: 0.45f, vulnerable: 0.8f),
                    new AttackSpec("Tajo pesado", "Tajo", 25, 0.8f, heavy: true, hold: 0.3f, vulnerable: 0.6f),
                    new AttackSpec("Embestida", "Embestida", 25, 0.95f, heavy: true, hold: 0.35f, vulnerable: 1.0f, rootMotion: true, bodyHit: true),
                },
            },
            // GDD §12: 50 HP, 10 per arrow. Keeps its distance and looks for high ground; weak up close
            new Archetype
            {
                Name = "Arquero", Prefab = ArcherPrefab, Asset = "Arquero", Kind = EnemyKind.Archer, Weapon = EnemyWeaponKind.Bow,
                Tint = new Color(0.36f, 0.56f, 0.30f),
                Tune = d =>
                {
                    d.maxHealth = 50; d.poise = 10; d.iFrames = 0.1f;
                    d.walkSpeed = 1.6f; d.runSpeed = 4.2f; d.strafeSpeed = 1.8f; d.turnRate = 480f;
                    d.sightRange = 26f; d.sightAngle = 75f; d.hearingRange = 9f;
                    d.preferredMin = 7f; d.preferredMax = 15f; d.attackIntervalMin = 1.4f; d.attackIntervalMax = 2.2f;
                    d.blockChance = 0f; d.dodgeChance = 0.3f; d.punishChance = 0f;
                    d.arrowDamage = 10; d.arrowSpeed = 28f; d.aimTime = 0.7f; d.retreatDistance = 5f;
                },
                Attacks = new[] { new AttackSpec("Disparo", EnemyAnimator.ShootName, 10, 1.0f) },
            },
            // GDD §13: the rival clan's leader (world 1 boss), 300 HP, 15–25. Presses and punishes mistakes:
            // combos, heavy blows, guards, dodges and lunges; open after its heavy blows
            new Archetype
            {
                Name = "Líder del clan rival", Prefab = LeaderPrefab, Asset = "LiderClan", Kind = EnemyKind.Boss, Boss = BossStyle.ClanLeader,
                Weapon = EnemyWeaponKind.Katana, Tint = new Color(0.58f, 0.12f, 0.12f), Scale = 1.05f,
                Tune = d =>
                {
                    d.maxHealth = 300; d.poise = 35; d.iFrames = 0.1f;
                    d.walkSpeed = 1.7f; d.runSpeed = 5.0f; d.strafeSpeed = 2.2f; d.turnRate = 600f;
                    d.sightRange = 30f; d.sightAngle = 90f; d.hearingRange = 12f; d.loseTime = 6f;
                    // It presses: it waits inside its first cut's reach
                    d.preferredMin = 1.7f; d.preferredMax = 2.6f; d.attackIntervalMin = 0.6f; d.attackIntervalMax = 1.2f;
                    d.blockChance = 0.25f; d.dodgeChance = 0.25f; d.punishChance = 0.75f; d.guardDamageKept = 0.2f;
                },
                Attacks = new[]
                {
                    new AttackSpec("Corte 1", "Corte_A", 15, 1.2f, next: 1, vulnerable: 0.1f),
                    new AttackSpec("Corte 2", "Corte_B", 15, 1.2f, next: 2, vulnerable: 0.1f),
                    new AttackSpec("Corte 3", "Corte_C", 18, 1.15f, vulnerable: 0.3f),
                    new AttackSpec("Estocada", "Estocada", 18, 1.1f, vulnerable: 0.4f, rootMotion: true),
                    new AttackSpec("Golpe alto", "Golpe_Alto", 25, 0.95f, heavy: true, hold: 0.3f, vulnerable: 1.1f),
                },
            },
            // GDD §13: the Commander (final boss), 450 HP, 20–30. Adapts its attacks to the player's style
            // (Enemy.HeavyBias, WindupHold, BlockChance); only small openings after its attacks
            new Archetype
            {
                Name = "El Comandante", Prefab = CommanderPrefab, Asset = "Comandante", Kind = EnemyKind.Boss, Boss = BossStyle.Commander,
                Weapon = EnemyWeaponKind.Katana, Tint = new Color(0.80f, 0.64f, 0.22f), Scale = 1.08f,
                Tune = d =>
                {
                    d.maxHealth = 450; d.poise = 40; d.iFrames = 0.1f;
                    d.walkSpeed = 1.7f; d.runSpeed = 5.2f; d.strafeSpeed = 2.4f; d.turnRate = 720f;
                    d.sightRange = 30f; d.sightAngle = 90f; d.hearingRange = 12f; d.loseTime = 6f;
                    d.preferredMin = 1.8f; d.preferredMax = 2.8f; d.attackIntervalMin = 0.5f; d.attackIntervalMax = 1.0f;
                    d.blockChance = 0.3f; d.dodgeChance = 0.3f; d.punishChance = 0.8f; d.guardDamageKept = 0.15f;
                },
                Attacks = new[]
                {
                    new AttackSpec("Corte 1", "Corte_A", 20, 1.25f, next: 1),
                    new AttackSpec("Corte 2", "Corte_B", 22, 1.25f, next: 2),
                    new AttackSpec("Corte 3", "Corte_C", 24, 1.2f, vulnerable: 0.2f),
                    new AttackSpec("Estocada", "Estocada", 24, 1.15f, vulnerable: 0.25f, rootMotion: true),
                    new AttackSpec("Golpe alto", "Golpe_Alto", 30, 1.0f, heavy: true, hold: 0.25f, vulnerable: 0.45f),
                    new AttackSpec("Tajo", "Tajo", 26, 1.1f, vulnerable: 0.3f),
                },
            },
        };

        // ─── Entry points ───────────────────────────────────────────────────────────

        [MenuItem("Tools/Warrior Woke/Configurar Enemigos")]
        private static void SetupMenu() => Setup();

        public static void SetupBatch()
        {
            bool ok = false;
            try { ok = Setup(); }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Setup()
        {
            PlayerAnimationSetup.ConfigureQuaternius();
            EnsureFolder(Folder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);
            Directory.CreateDirectory(ReviewFolder);

            Avatar avatar = LoadAvatar(ModelPath);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (avatar == null || !avatar.isHuman || modelAsset == null)
            {
                Debug.LogError($"{Tag} Falta el Avatar Humanoid de {ModelPath}.");
                return false;
            }

            AnimatorController controller = BuildController(out float fastest, out Dictionary<string, AnimationClip> clips);
            if (controller == null) return false;

            var materials = new SharedMaterials();
            bool ok = true;
            var ci = CultureInfo.InvariantCulture;
            var csv = new StringBuilder("enemigo,ataque,estado,duracion,ritmo,inicio,fin,ventana_s,alcance,avance,altura_punta,danio\n");
            foreach (Archetype a in Archetypes)
            {
                EnemyData data = LoadOrCreateData(a);
                if (!BuildPrefab(a, controller, avatar, modelAsset, materials, fastest, data)) { ok = false; continue; }
                ok &= MeasureAttacks(a, data, clips, csv, ci);
            }
            File.WriteAllText(Path.Combine(ReviewFolder, "ataques.csv"), csv.ToString());
            RenderLineup();
            AssetDatabase.SaveAssets();
            ok &= Validate();
            Debug.Log(ok ? $"{Tag} Enemigos listos: {Archetypes.Length} prefabs en {PrefabFolder}, datos en {DataFolder}, hojas en {ReviewFolder}."
                         : $"{Tag} Hubo errores (ver arriba).");
            return ok;
        }

        // ─── Controller ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The enemies' controller, rebuilt in place (the prefabs keep its GUID). The locomotion and the dodge
        /// are copies of the player's measured blends; the actions are plain states the AI cross-fades to.
        /// </summary>
        private static AnimatorController BuildController(out float fastest, out Dictionary<string, AnimationClip> clips)
        {
            fastest = 4f;
            clips = new Dictionary<string, AnimationClip>();
            var player = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
            BlendTree playerLoco = FindTree(player, PlayerAnimatorIds.LocomotionName);
            BlendTree playerDodge = FindTree(player, PlayerAnimatorIds.DodgeName);
            if (playerLoco == null || playerDodge == null)
            {
                Debug.LogError($"{Tag} {PlayerController} no tiene los blends Locomotion y Dodge: corre 'Configurar Animaciones del Jugador'.");
                return null;
            }

            foreach (AttackClip c in AttackClips)
            {
                AnimationClip clip = LoadClip(c.File, c.Clip);
                if (clip == null) return null;
                clips[c.State] = clip;
            }
            AnimationClip combatIdle = LoadClip(LowPolyCombat + "IdleCombat.fbx", "IdleCombat");
            AnimationClip block = LoadClip(Ual2, "Sword_Block");
            AnimationClip hit = LoadClip(Ual1, CombatTimings.HitChestClip);
            AnimationClip hitHeavy = LoadClip(Ual1, CombatTimings.HitHeadClip);
            AnimationClip knockback = LoadClip(Ual2, "Hit_Knockback");
            AnimationClip death = LoadClip(Ual1, PlayerAnimatorIds.DeathClip);
            if (combatIdle == null || block == null || hit == null || hitHeavy == null || knockback == null || death == null) return null;

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (AnimatorControllerParameter p in controller.parameters) controller.RemoveParameter(p);
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState s in sm.states) sm.RemoveState(s.state);
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub is BlendTree) Object.DestroyImmediate(sub, true);

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "LocomotionRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("DodgeX", AnimatorControllerParameterType.Float);
            controller.AddParameter("DodgeY", AnimatorControllerParameterType.Float);

            // Locomotion: the player's blend (clips at their measured ground velocity), the idle a combat stance
            BlendTree loco = CopyTree(playerLoco, controller, EnemyAnimator.LocomotionName);
            var children = loco.children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].position == Vector2.zero) children[i].motion = combatIdle;
                fastest = Mathf.Max(fastest, children[i].position.magnitude);
            }
            loco.children = children;
            AnimatorState locomotion = sm.AddState(EnemyAnimator.LocomotionName, new Vector3(250f, 0f, 0f));
            locomotion.motion = loco;
            locomotion.speedParameter = "LocomotionRate";
            locomotion.speedParameterActive = true;
            sm.defaultState = locomotion;

            BlendTree dodgeTree = CopyTree(playerDodge, controller, EnemyAnimator.DodgeName);
            AnimatorState dodge = sm.AddState(EnemyAnimator.DodgeName, new Vector3(250f, 80f, 0f));
            dodge.motion = dodgeTree;
            AnimationClip roll = dodgeTree.children.Length > 0 ? dodgeTree.children[0].motion as AnimationClip : null;
            dodge.speed = roll != null ? roll.length / DodgeTime : 1f;

            AddState(sm, EnemyAnimator.BlockName, block, 1f, new Vector2(500, 0));
            AddState(sm, EnemyAnimator.HitName, hit, 1.3f, new Vector2(500, 80));
            AddState(sm, EnemyAnimator.HitHeavyName, hitHeavy, 1.1f, new Vector2(500, 160));
            AddState(sm, EnemyAnimator.KnockbackName, knockback, 1f, new Vector2(500, 240));
            AddState(sm, EnemyAnimator.DeathName, death, 1f, new Vector2(500, 320));
            AddState(sm, EnemyAnimator.DrawName, clips[EnemyAnimator.ShootName], 1f, new Vector2(750, 320));
            for (int i = 0; i < AttackClips.Length; i++)
                AddState(sm, AttackClips[i].State, clips[AttackClips[i].State], 1f, new Vector2(750, i * 70));

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} Controller: {ControllerPath} ({sm.states.Length} estados), clip de locomoción más rápido a {fastest:F2} m/s.");
            return controller;
        }

        private static BlendTree FindTree(AnimatorController controller, string state)
        {
            if (controller == null) return null;
            foreach (ChildAnimatorState s in controller.layers[0].stateMachine.states)
                if (s.state.name == state) return s.state.motion as BlendTree;
            return null;
        }

        private static BlendTree CopyTree(BlendTree source, AnimatorController controller, string name)
        {
            BlendTree copy = Object.Instantiate(source);
            copy.name = name;
            copy.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(copy, controller);
            return copy;
        }

        private static void AddState(AnimatorStateMachine sm, string name, Motion motion, float speed, Vector2 position)
        {
            AnimatorState state = sm.AddState(name, new Vector3(position.x, position.y, 0f));
            state.motion = motion;
            state.speed = speed;
        }

        // ─── Data ───────────────────────────────────────────────────────────────────

        private static EnemyData LoadOrCreateData(Archetype a)
        {
            string path = $"{DataFolder}/{a.Asset}.asset";
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EnemyData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.enemyName = a.Name;
            data.kind = a.Kind;
            data.bossStyle = a.Boss;
            data.weapon = a.Weapon;
            data.tint = a.Tint;
            a.Tune(data);
            EditorUtility.SetDirty(data);
            return data;
        }

        // ─── Prefabs ────────────────────────────────────────────────────────────────

        /// <summary>Materials shared by the weapons.</summary>
        private sealed class SharedMaterials
        {
            public readonly Material Steel, Wood, Dark, Iron, String;
            public SharedMaterials()
            {
                Steel = Mat("Acero", new Color(0.72f, 0.74f, 0.78f), 0.85f, 0.75f);
                Wood = Mat("Madera", new Color(0.36f, 0.22f, 0.12f), 0f, 0.3f);
                Dark = Mat("Empuñadura", new Color(0.08f, 0.07f, 0.07f), 0f, 0.4f);
                Iron = Mat("Hierro", new Color(0.22f, 0.22f, 0.24f), 0.7f, 0.45f);
                String = Mat("Cuerda", new Color(0.85f, 0.82f, 0.72f), 0f, 0.2f);
            }
        }

        private static Material Mat(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static bool BuildPrefab(Archetype a, AnimatorController controller, Avatar avatar, GameObject modelAsset,
                                        SharedMaterials m, float fastest, EnemyData data)
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            var root = new GameObject(Path.GetFileNameWithoutExtension(a.Prefab)) { layer = enemyLayer };
            try
            {
                float s = a.Scale;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                model.name = "Model";
                model.transform.localScale = Vector3.one * s;
                Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = avatar;
                animator.applyRootMotion = true; // handled by EnemyRootMotion
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the blade's sweep needs the pose off-screen too
                model.AddComponent<EnemyRootMotion>();
                Tint(model, a);
                // Soles on the floor: the root is at the feet
                model.transform.localPosition = new Vector3(0f, -IdleSole(model, animator) , 0f);

                // Weapon in the hand (a primitive until the team's models arrive)
                Transform grip = null, tip = null, muzzle = null;
                float bladeRadius = 0.07f;
                if (a.Weapon == EnemyWeaponKind.Bow) muzzle = BuildBow(animator, m);
                else BuildBlade(animator, a.Weapon, m, out grip, out tip, out bladeRadius);
                Arrow[] arrows = a.Weapon == EnemyWeaponKind.Bow ? BuildQuiver(root.transform, animator, m) : new Arrow[0];

                // Body
                float height = 1.8f * s;
                var agent = root.AddComponent<NavMeshAgent>();
                agent.baseOffset = 0f;
                agent.height = height;
                agent.radius = 0.4f * s;
                agent.speed = data.runSpeed;
                agent.angularSpeed = 0f;      // Enemy turns the body itself
                agent.acceleration = 16f;
                agent.stoppingDistance = 0.05f;
                agent.autoBraking = true;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                agent.avoidancePriority = a.Kind == EnemyKind.Boss ? 30 : a.Kind == EnemyKind.Heavy ? 40 : 50;
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var hitCapsule = root.AddComponent<CapsuleCollider>(); // the player's blows sweep it (layer Enemy)
                hitCapsule.center = new Vector3(0f, height * 0.5f, 0f);
                hitCapsule.height = height;
                hitCapsule.radius = 0.35f * s;
                var blocker = new GameObject("Cuerpo") { layer = 0 }; // stops the player's CharacterController
                blocker.transform.SetParent(root.transform, false);
                var blockCapsule = blocker.AddComponent<CapsuleCollider>();
                blockCapsule.center = hitCapsule.center;
                blockCapsule.height = height;
                blockCapsule.radius = 0.3f * s;

                var health = root.AddComponent<HealthSystem>();
                var sh = new SerializedObject(health);
                sh.FindProperty("maxHealth").intValue = data.maxHealth;
                sh.FindProperty("iFramesDuration").floatValue = data.iFrames;
                sh.ApplyModifiedPropertiesWithoutUndo();

                var perception = root.AddComponent<EnemyPerception>();
                var sp = new SerializedObject(perception);
                sp.FindProperty("eyeHeight").floatValue = 1.6f * s;
                sp.FindProperty("blockingLayers").intValue = LayerMask.GetMask("Ground", "Obstacle");
                sp.ApplyModifiedPropertiesWithoutUndo();

                var enemyAnimator = root.AddComponent<EnemyAnimator>();
                var sa = new SerializedObject(enemyAnimator);
                sa.FindProperty("animator").objectReferenceValue = animator;
                sa.FindProperty("fastestClipSpeed").floatValue = fastest;
                sa.ApplyModifiedPropertiesWithoutUndo();

                if (grip != null)
                {
                    var weapon = root.AddComponent<EnemyWeapon>();
                    weapon.Configure(grip, tip, bladeRadius);
                    var sw = new SerializedObject(weapon);
                    sw.FindProperty("targetLayers").intValue = LayerMask.GetMask("Player");
                    sw.FindProperty("blockingLayers").intValue = LayerMask.GetMask("Ground", "Obstacle");
                    sw.ApplyModifiedPropertiesWithoutUndo();
                }

                var enemy = root.AddComponent<Enemy>();
                enemy.Configure(data, null, null, arrows, muzzle);

                PrefabUtility.SaveAsPrefabAsset(root, a.Prefab);
                Debug.Log($"{Tag} {a.Prefab}: {a.Name}, escala {s:F2}, {data.maxHealth} HP, arma {a.Weapon}.");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Tinted copies of the model's own materials (its textures stay; the base colour multiplies them).</summary>
        private static void Tint(GameObject model, Archetype a)
        {
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string path = $"{MaterialsFolder}/{a.Asset}_{i}.mat";
                    var tinted = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (tinted == null)
                    {
                        tinted = new Material(mats[i]);
                        AssetDatabase.CreateAsset(tinted, path);
                    }
                    else tinted.CopyPropertiesFromMaterial(mats[i]);
                    // The Ch45 suit is nearly black: a tint multiplying its texture did not show. The colour
                    // replaces the base map (the normal map keeps the suit's detail) and the green glow goes
                    if (tinted.HasProperty("_BaseMap")) tinted.SetTexture("_BaseMap", null);
                    if (tinted.HasProperty("_MainTex")) tinted.SetTexture("_MainTex", null);
                    if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", a.Tint);
                    if (tinted.HasProperty("_Color")) tinted.SetColor("_Color", a.Tint);
                    if (tinted.HasProperty("_EmissionColor")) tinted.SetColor("_EmissionColor", Color.black);
                    tinted.DisableKeyword("_EMISSION");
                    EditorUtility.SetDirty(tinted);
                    mats[i] = tinted;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Height (m, model scale included) of the lowest vertex under the model's root in the combat idle.</summary>
        private static float IdleSole(GameObject model, Animator animator)
        {
            AnimationClip idle = LoadClip(LowPolyCombat + "IdleCombat.fbx", "IdleCombat");
            var mesh = new Mesh();
            Vector3 position = model.transform.position;
            Quaternion rotation = model.transform.rotation;
            AnimationMode.StartAnimationMode();
            try
            {
                Sample(model, idle, 0f);
                float min = float.MaxValue;
                foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.BakeMesh(mesh, true);
                    foreach (Vector3 v in mesh.vertices) min = Mathf.Min(min, smr.transform.TransformPoint(v).y);
                }
                return min - model.transform.position.y;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                model.transform.SetPositionAndRotation(position, rotation);
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// The hand's grip frame: the point inside the closed fist and the axis a held shaft runs along (from
        /// the little finger's knuckle toward the index's, out of the thumb side), both in the hand bone's space.
        /// </summary>
        private static void GripFrame(Animator animator, bool right, out Transform hand, out Vector3 gripLocal, out Quaternion axisLocal)
        {
            hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform little = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            Vector3 h = hand.position;
            Vector3 shaft = (index.position - little.position).normalized;
            Vector3 fingers = (middle.position - h).normalized;
            Vector3 palm = Vector3.Cross(index.position - h, little.position - h).normalized * (right ? -1f : 1f);
            Vector3 grip = h + (middle.position - h) * 0.85f + palm * 0.035f;
            gripLocal = hand.InverseTransformPoint(grip);
            // The shaft's local +Y; its +Z along the fingers (the blade's edge faces where the fingers point)
            Vector3 up = hand.InverseTransformDirection(shaft);
            Vector3 fwd = hand.InverseTransformDirection(Vector3.ProjectOnPlane(fingers, shaft).normalized);
            axisLocal = Quaternion.LookRotation(fwd, up);
        }

        private static void BuildBlade(Animator animator, EnemyWeaponKind kind, SharedMaterials m, out Transform grip, out Transform tip, out float radius)
        {
            GripFrame(animator, true, out Transform hand, out Vector3 gripLocal, out Quaternion axis);
            var weapon = new GameObject(kind == EnemyWeaponKind.Katana ? "Katana" : "Kanabo").transform;
            weapon.SetParent(hand, false);
            weapon.localPosition = gripLocal;
            weapon.localRotation = axis;
            // The hand bone carries the model's scale: the weapon keeps world size
            weapon.localScale = Vector3.one / Mathf.Max(0.01f, hand.lossyScale.x);
            if (kind == EnemyWeaponKind.Katana)
            {
                Part(weapon, "Tsuka", PrimitiveType.Cylinder, new Vector3(0f, 0.06f, 0f), new Vector3(0.032f, 0.13f, 0.032f), m.Dark);
                Part(weapon, "Tsuba", PrimitiveType.Cylinder, new Vector3(0f, 0.195f, 0f), new Vector3(0.085f, 0.006f, 0.085f), m.Iron);
                Part(weapon, "Hoja", PrimitiveType.Cube, new Vector3(0f, 0.56f, 0f), new Vector3(0.008f, 0.72f, 0.032f), m.Steel);
                grip = Point(weapon, "Base", 0.2f);
                tip = Point(weapon, "Punta", 0.92f);
                radius = 0.06f;
            }
            else
            {
                Part(weapon, "Mango", PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.04f, 0.16f, 0.04f), m.Dark);
                Part(weapon, "Maza", PrimitiveType.Cylinder, new Vector3(0f, 0.66f, 0f), new Vector3(0.12f, 0.45f, 0.12f), m.Iron);
                Part(weapon, "Remate", PrimitiveType.Cylinder, new Vector3(0f, 1.12f, 0f), new Vector3(0.13f, 0.02f, 0.13f), m.Iron);
                grip = Point(weapon, "Base", 0.22f);
                tip = Point(weapon, "Punta", 1.12f);
                radius = 0.1f;
            }
        }

        /// <summary>The bow in the left fist (limbs along the shaft axis); returns where the arrow leaves.</summary>
        private static Transform BuildBow(Animator animator, SharedMaterials m)
        {
            GripFrame(animator, false, out Transform hand, out Vector3 gripLocal, out Quaternion axis);
            var bow = new GameObject("Arco").transform;
            bow.SetParent(hand, false);
            bow.localPosition = gripLocal;
            bow.localRotation = axis;
            bow.localScale = Vector3.one / Mathf.Max(0.01f, hand.lossyScale.x);
            Part(bow, "Empuñadura", PrimitiveType.Cube, Vector3.zero, new Vector3(0.035f, 0.16f, 0.04f), m.Dark);
            Transform upper = Part(bow, "Pala_Sup", PrimitiveType.Cube, new Vector3(0f, 0.42f, -0.04f), new Vector3(0.02f, 0.7f, 0.03f), m.Wood);
            upper.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            Transform lower = Part(bow, "Pala_Inf", PrimitiveType.Cube, new Vector3(0f, -0.42f, -0.04f), new Vector3(0.02f, 0.7f, 0.03f), m.Wood);
            lower.localRotation = Quaternion.Euler(10f, 0f, 0f);
            Part(bow, "Cuerda", PrimitiveType.Cube, new Vector3(0f, 0f, -0.12f), new Vector3(0.004f, 1.5f, 0.004f), m.String);
            return Point(bow, "Salida", 0.04f);
        }

        /// <summary>Four pooled arrows (they leave the hierarchy at Awake) and a quiver on the back.</summary>
        private static Arrow[] BuildQuiver(Transform root, Animator animator, SharedMaterials m)
        {
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            var quiver = new GameObject("Aljaba").transform;
            quiver.SetParent(chest, false);
            quiver.position = chest.position - root.forward * 0.17f + root.right * 0.08f;
            quiver.rotation = Quaternion.LookRotation(root.forward, Vector3.up) * Quaternion.Euler(-15f, 0f, 20f);
            quiver.localScale = Vector3.one / Mathf.Max(0.01f, chest.lossyScale.x);
            Part(quiver, "Tubo", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.28f, 0.09f), m.Wood);

            var pool = new GameObject("Flechas").transform;
            pool.SetParent(root, false);
            var arrows = new Arrow[4];
            for (int i = 0; i < arrows.Length; i++)
            {
                var arrow = new GameObject($"Flecha_{i}");
                arrow.transform.SetParent(pool, false);
                Transform shaft = Part(arrow.transform, "Asta", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.37f), new Vector3(0.012f, 0.37f, 0.012f), m.Wood);
                shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Transform head = Part(arrow.transform, "Punta", PrimitiveType.Cube, Vector3.zero, new Vector3(0.018f, 0.018f, 0.05f), m.Iron);
                head.localRotation = Quaternion.Euler(0f, 0f, 45f);
                arrows[i] = arrow.AddComponent<Arrow>();
                arrow.SetActive(false);
            }
            return arrows;
        }

        private static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static Transform Point(Transform parent, string name, float y)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(0f, y, 0f);
            return t;
        }

        // ─── Measurement ────────────────────────────────────────────────────────────

        private const int Samples = 80;

        /// <summary>Measured phases of one attack clip on one archetype's model and weapon.</summary>
        private struct Phases
        {
            public float Length, Start, End, Reach, ReachInPlace, Travel, TipHeight, PeakSpeed;
        }

        private static bool MeasureAttacks(Archetype a, EnemyData data, Dictionary<string, AnimationClip> clips, StringBuilder csv, CultureInfo ci)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(a.Prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bool ok = true;
            var measured = new Dictionary<string, Phases>();
            var sheets = new PoseSheetRenderer(200, 280, 1.4f);
            try
            {
                GameObject model = instance.transform.Find("Model").gameObject;
                var animator = model.GetComponent<Animator>();
                var weapon = instance.GetComponent<EnemyWeapon>();
                Transform tipPoint = weapon != null ? weapon.Tip : null, gripPoint = weapon != null ? weapon.Grip : null;
                var attacks = new EnemyAttack[a.Attacks.Length];
                for (int i = 0; i < a.Attacks.Length; i++)
                {
                    AttackSpec spec = a.Attacks[i];
                    AttackClip source = AttackClips.First(c => c.State == spec.State);
                    string key = spec.State + (spec.BodyHit ? "_body" : "");
                    if (!measured.TryGetValue(key, out Phases ph))
                    {
                        AnimationMode.StartAnimationMode();
                        try
                        {
                            ph = Measure(model, animator, clips[spec.State], gripPoint, tipPoint, source.Bow, spec.BodyHit, a.Scale);
                        }
                        finally
                        {
                            AnimationMode.StopAnimationMode();
                            model.transform.localPosition = prefab.transform.Find("Model").localPosition;
                            model.transform.localRotation = Quaternion.identity;
                        }
                        instance.SetActive(false); // only the sheet's own instance in the picture
                        Sheet(prefab, clips[spec.State], ph, sheets, $"{a.Asset}_{spec.State}", source.Bow || spec.BodyHit);
                        instance.SetActive(true);
                        measured[key] = ph;
                    }
                    float window = (ph.End - ph.Start) * ph.Length / spec.Rate;
                    // Every attack whose clip steps moves with it (the measured reach counts the step): without
                    // it the first cut stayed 0.8 m short and the combos never chained. One that does not step
                    // reaches only as far as the blade gets from where the body stands (the overhead club's clip
                    // leans 25 cm in and back: in place it struck 7 cm short of its "reach")
                    bool stepping = spec.RootMotion || (!source.Bow && ph.Travel > StepTravel);
                    if (!stepping) ph.Reach = ph.ReachInPlace;
                    attacks[i] = new EnemyAttack
                    {
                        name = spec.Name,
                        state = spec.State,
                        length = ph.Length,
                        rate = spec.Rate,
                        activeStart = ph.Start,
                        activeEnd = ph.End,
                        damage = spec.Damage,
                        heavy = spec.Heavy,
                        reach = ph.Reach,
                        rootMotion = stepping,
                        bodyHit = spec.BodyHit,
                        lunge = 0f,
                        windupHold = spec.WindupHold,
                        vulnerableAfter = spec.VulnerableAfter,
                        next = spec.Next,
                    };
                    csv.Append(a.Asset).Append(',').Append(spec.Name).Append(',').Append(spec.State).Append(',')
                       .Append(ph.Length.ToString("F3", ci)).Append(',').Append(spec.Rate.ToString("F2", ci)).Append(',')
                       .Append(ph.Start.ToString("F3", ci)).Append(',').Append(ph.End.ToString("F3", ci)).Append(',')
                       .Append(window.ToString("F3", ci)).Append(',').Append(ph.Reach.ToString("F2", ci)).Append(',')
                       .Append(ph.Travel.ToString("F2", ci)).Append(',').Append(ph.TipHeight.ToString("F2", ci)).Append(',')
                       .Append(spec.Damage).Append('\n');
                    Debug.Log(string.Format(ci, "{0} {1} · {2} ({3}): {4:F2} s a ×{5:F2}, golpea de n = {6:F2} a {7:F2} ({8:F2} s), alcance {9:F2} m (avance {10:F2} m), punta a {11:F2} m de altura, {12} de daño",
                        Tag, a.Asset, spec.Name, spec.State, ph.Length, spec.Rate, ph.Start, ph.End, window, ph.Reach, ph.Travel, ph.TipHeight, spec.Damage));
                    if (!source.Bow)
                    {
                        ok &= PlayerAnimationSetup.Check(window >= 0.05f && window <= 0.6f, $"{a.Asset} {spec.Name}: ventana de golpe de {window:F2} s");
                        ok &= PlayerAnimationSetup.Check(ph.Reach >= 1.2f && ph.Reach <= 5f, $"{a.Asset} {spec.Name}: alcance de {ph.Reach:F2} m");
                        ok &= PlayerAnimationSetup.Check(ph.TipHeight >= 0.3f && ph.TipHeight <= 2.2f, $"{a.Asset} {spec.Name}: la punta golpea a {ph.TipHeight:F2} m (a la altura de un cuerpo)");
                    }
                    else ok &= PlayerAnimationSetup.Check(ph.Start > 0.2f && ph.Start < 0.95f, $"{a.Asset} {spec.Name}: suelta la flecha en n = {ph.Start:F2}");
                }
                data.attacks = attacks;
                EditorUtility.SetDirty(data);
            }
            finally
            {
                sheets.Dispose();
                Object.DestroyImmediate(instance);
            }
            return ok;
        }

        /// <summary>
        /// The strike window of <paramref name="clip"/>: the weapon tip's speed relative to the hips, sampled
        /// over the clip; the window is the run of samples at half the peak or more around the peak. For the
        /// bow, the drawing (right) hand: the release is the start of its fastest motion.
        /// </summary>
        private static Phases Measure(GameObject model, Animator animator, AnimationClip clip, Transform grip, Transform tip, bool bow, bool body, float scale)
        {
            Transform root = model.transform;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform point = bow || tip == null ? hand : tip;
            var rel = new Vector3[Samples + 1];
            var world = new Vector3[Samples + 1];
            var gripWorld = new Vector3[Samples + 1];
            var rootPos = new Vector3[Samples + 1];
            Sample(model, clip, 0f);
            Vector3 root0 = root.position, forward0 = root.forward, right0 = root.right;
            for (int i = 0; i <= Samples; i++)
            {
                Sample(model, clip, clip.length * i / Samples);
                rel[i] = Quaternion.Inverse(root.rotation) * (point.position - hips.position);
                world[i] = point.position;
                gripWorld[i] = grip != null && !bow ? grip.position : point.position;
                rootPos[i] = root.position;
            }
            float dt = clip.length / Samples;
            var speed = new float[Samples + 1];
            int peak = 1;
            for (int i = 1; i <= Samples; i++)
            {
                // A charge strikes with the body: its window is the travel's fastest part
                speed[i] = body ? Vector3.Dot(rootPos[i] - rootPos[i - 1], forward0) / dt : (rel[i] - rel[i - 1]).magnitude / dt;
                if (bow && i < Samples / 4) continue; // the draw's first lift is not the release
                if (speed[i] > speed[peak]) peak = i;
            }
            // The blade cuts through its fast arc: from a third of its peak speed (half left the start of
            // the arc out, 0.05–0.09 s)
            float threshold = speed[peak] * (body ? 0.5f : 0.35f);
            int a = peak, b = peak;
            while (a > 1 && speed[a - 1] >= threshold) a--;
            while (b < Samples && speed[b + 1] >= threshold) b++;
            var ph = new Phases
            {
                Length = clip.length,
                Start = (a - 1) / (float)Samples, // the speed of sample i is the motion from i − 1 to i
                End = b / (float)Samples,
                PeakSpeed = speed[peak],
            };
            float reach = 0f, inPlace = 0f, height = 0f;
            for (int i = a - 1; i <= b; i++)
            {
                // A charge reaches with the front of its body (EnemyWeapon: 0.35 m ahead of its axis)
                if (body)
                {
                    reach = Mathf.Max(reach, Vector3.Dot(rootPos[i] + forward0 * (0.35f * scale) - root0, forward0));
                }
                else
                {
                    // The farthest a body standing straight ahead can be and still be touched by the blade at
                    // its height: a point of the blade f ahead and l aside touches a body of radius R centred up
                    // to f + √(R² − l²) ahead. (The farthest point alone overstated it: the overhead club comes
                    // down across the body and its farthest point was 0.4 m aside, or near the ground)
                    for (int k = 0; k <= 10; k++)
                    {
                        Vector3 q = Vector3.Lerp(gripWorld[i], world[i], k / 10f);
                        float y = q.y - root0.y;
                        if (y < BodyLow || y > BodyHigh) continue;
                        reach = Mathf.Max(reach, Touch(q - root0, forward0, right0));
                        // Without its root motion the body stays where it is: the clip's travel so far does not count
                        inPlace = Mathf.Max(inPlace, Touch(q - rootPos[i], forward0, right0));
                    }
                }
                if (i == peak) height = body ? 1.2f * scale : world[i].y - root0.y;
            }
            ph.Travel = Vector3.Dot(rootPos[Samples] - root0, forward0);
            // A margin so the blade goes into the body, not just grazes it
            ph.Reach = body ? reach + TargetRadius * 0.5f : reach - ReachMargin;
            ph.ReachInPlace = body ? ph.Reach : inPlace - ReachMargin;
            ph.TipHeight = height;
            return ph;
        }

        /// <summary>
        /// Contact sheet of one attack on a fresh instance of the prefab (a reused one rendered its skinned
        /// mesh a pose behind its bones): the start, the wind-up, the strike window (start, middle, end) and
        /// three frames of the recovery. The
        /// red marker is the weapon's tip (the hand for the bow, the body's front for a charge) inside the window.
        /// </summary>
        private static void Sheet(GameObject prefab, AnimationClip clip, Phases ph, PoseSheetRenderer sheets, string name, bool noTip)
        {
            const int columns = 8;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            GameObject model = instance.transform.Find("Model").gameObject;
            var animator = model.GetComponent<Animator>();
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var weapon = instance.GetComponent<EnemyWeapon>();
            Transform tip = !noTip && weapon != null ? weapon.Tip : hand;
            AnimationMode.StartAnimationMode();
            var baked = new BakedBody(model);
            try
            {
                sheets.Begin(columns, 2);
                float[] frames = { 0f, ph.Start * 0.5f, ph.Start, (ph.Start + ph.End) * 0.5f, ph.End,
                                   Mathf.Lerp(ph.End, 1f, 1f / 3f), Mathf.Lerp(ph.End, 1f, 2f / 3f), 1f };
                for (int c = 0; c < columns; c++)
                {
                    float n = frames[c];
                    Sample(model, clip, clip.length * n);
                    baked.Bake();
                    bool active = n >= ph.Start - 0.001f && n <= ph.End + 0.001f;
                    sheets.SetMarker(active ? tip.position : (Vector3?)null);
                    Vector3 focus = new Vector3(hips.position.x, 0.95f, hips.position.z);
                    sheets.Capture(0, c, focus, model.transform.right, 0f);
                    sheets.Capture(1, c, focus, (model.transform.right - model.transform.forward).normalized, 0f);
                }
                sheets.SetMarker(null);
                sheets.Save(Path.Combine(ReviewFolder, name + ".png"));
            }
            finally
            {
                baked.Dispose();
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// A skinned model drawn as baked meshes. In batch, a probe camera rendered the skinned mesh in a
        /// stale pose after AnimationMode sampling (the bones and the weapon moved, the body did not), so
        /// every frame bakes the current bones (SkinnedMeshRenderer.BakeMesh) into plain meshes in its place.
        /// </summary>
        private sealed class BakedBody : System.IDisposable
        {
            private readonly SkinnedMeshRenderer[] _skins;
            private readonly Mesh[] _meshes;
            private readonly GameObject[] _proxies;

            public BakedBody(GameObject model)
            {
                _skins = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                _meshes = new Mesh[_skins.Length];
                _proxies = new GameObject[_skins.Length];
                for (int i = 0; i < _skins.Length; i++)
                {
                    _meshes[i] = new Mesh();
                    _proxies[i] = new GameObject("Horneado");
                    _proxies[i].AddComponent<MeshFilter>().sharedMesh = _meshes[i];
                    _proxies[i].AddComponent<MeshRenderer>().sharedMaterials = _skins[i].sharedMaterials;
                    _skins[i].forceRenderingOff = true;
                }
            }

            public void Bake()
            {
                for (int i = 0; i < _skins.Length; i++)
                {
                    _skins[i].BakeMesh(_meshes[i], true);
                    _proxies[i].transform.SetPositionAndRotation(_skins[i].transform.position, _skins[i].transform.rotation);
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < _skins.Length; i++)
                {
                    if (_skins[i] != null) _skins[i].forceRenderingOff = false;
                    Object.DestroyImmediate(_proxies[i]);
                    Object.DestroyImmediate(_meshes[i]);
                }
            }
        }

        /// <summary>The five prefabs side by side in their combat idle (front and side), for review.</summary>
        private static void RenderLineup()
        {
            var sheets = new PoseSheetRenderer(260, 340, 1.25f);
            var instances = new List<GameObject>();
            AnimationClip idle = LoadClip(LowPolyCombat + "IdleCombat.fbx", "IdleCombat");
            try
            {
                sheets.Begin(Archetypes.Length, 2);
                for (int i = 0; i < Archetypes.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Archetypes[i].Prefab);
                    if (prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instances.Add(go);
                    go.transform.position = new Vector3(i * 6f, 0f, 0f);
                    GameObject model = go.transform.Find("Model").gameObject;
                    AnimationMode.StartAnimationMode();
                    var baked = new BakedBody(model);
                    try
                    {
                        Sample(model, idle, 0.3f);
                        baked.Bake();
                        Vector3 focus = go.transform.position + Vector3.up * 1.0f;
                        sheets.Capture(0, i, focus, -go.transform.forward, 0f);
                        sheets.Capture(1, i, focus, go.transform.right, 0f);
                    }
                    finally
                    {
                        baked.Dispose();
                        AnimationMode.StopAnimationMode();
                    }
                    go.SetActive(false); // one at a time in the picture
                }
                sheets.Save(Path.Combine(ReviewFolder, "lineup.png"));
            }
            finally
            {
                sheets.Dispose();
                foreach (GameObject go in instances) Object.DestroyImmediate(go);
            }
        }

        // ─── Validation ─────────────────────────────────────────────────────────────

        public static bool Validate()
        {
            bool ok = true;
            foreach (Archetype a in Archetypes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(a.Prefab);
                ok &= PlayerAnimationSetup.Check(prefab != null, $"Prefab {a.Prefab}");
                if (prefab == null) continue;
                var enemy = prefab.GetComponent<Enemy>();
                ok &= PlayerAnimationSetup.Check(enemy != null && enemy.Data != null && enemy.Data.attacks.Length == a.Attacks.Length,
                                                 $"{a.Asset}: Enemy con su EnemyData y {a.Attacks.Length} ataques medidos");
                Animator animator = prefab.GetComponentInChildren<Animator>();
                ok &= PlayerAnimationSetup.Check(animator != null && animator.runtimeAnimatorController != null && animator.avatar != null && animator.avatar.isHuman,
                                                 $"{a.Asset}: Animator con EnemyAnimator.controller y Avatar Humanoid");
                ok &= PlayerAnimationSetup.Check(prefab.layer == LayerMask.NameToLayer("Enemy") && prefab.GetComponent<CapsuleCollider>() != null,
                                                 $"{a.Asset}: cápsula de golpe en la layer Enemy");
                bool melee = a.Weapon != EnemyWeaponKind.Bow;
                var weapon = prefab.GetComponent<EnemyWeapon>();
                ok &= PlayerAnimationSetup.Check(!melee || (weapon != null && weapon.Grip != null && weapon.Tip != null), $"{a.Asset}: arma con base y punta");
                ok &= PlayerAnimationSetup.Check(melee || (enemy != null && enemy.BowMuzzle != null), $"{a.Asset}: arco con punto de salida y carcaj");
                if (enemy != null && enemy.Data != null)
                    foreach (EnemyAttack attack in enemy.Data.attacks)
                        ok &= PlayerAnimationSetup.Check(attack.activeEnd > attack.activeStart && attack.length > 0f, $"{a.Asset} {attack.name}: fases medidas ({attack.activeStart:F2}–{attack.activeEnd:F2})");
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null)
            {
                var names = new HashSet<string>(controller.layers[0].stateMachine.states.Select(s => s.state.name));
                foreach (string state in new[] { EnemyAnimator.LocomotionName, EnemyAnimator.BlockName, EnemyAnimator.DodgeName, EnemyAnimator.HitName,
                                                 EnemyAnimator.HitHeavyName, EnemyAnimator.KnockbackName, EnemyAnimator.DeathName, EnemyAnimator.ShootName })
                    ok &= PlayerAnimationSetup.Check(names.Contains(state), $"EnemyAnimator.controller tiene el estado {state}");
                foreach (AttackClip c in AttackClips) ok &= PlayerAnimationSetup.Check(names.Contains(c.State), $"EnemyAnimator.controller tiene el estado {c.State}");
            }
            else ok &= PlayerAnimationSetup.Check(false, $"Existe {ControllerPath}");
            return ok;
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────

        private static void Sample(GameObject model, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(model, clip, time);
            AnimationMode.EndSampling();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Avatar LoadAvatar(string path)
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is Avatar avatar) return avatar;
            return null;
        }

        private static AnimationClip LoadClip(string path, string clipName)
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
                if (obj is AnimationClip clip && clip.name == clipName && !clip.name.StartsWith("__preview__"))
                    return clip;
            Debug.LogError($"{Tag} No se encontró el clip '{clipName}' en {path}.");
            return null;
        }
    }
}
