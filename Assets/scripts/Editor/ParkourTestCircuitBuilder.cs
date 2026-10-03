using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds Level-1 as the Parkour Test Area (decisions P24, P25): a flat floor, a perimeter and nine
    /// lanes that run toward −Z from the spawn point, one per standard obstacle. Every obstacle is an
    /// instance of a standard prefab (ParkourObstaclePrefabs, Parkour Obstacle Standard); only the
    /// floor, the stairs, the platforms and the pillars are plain test fixtures. No signs or text:
    /// each family has a color and the layout is documented in docs/features.md (F32).
    ///   Locomotion (x = −25) · LowVault (−16) · MediumVault (−10) · HighVault (−4) · Slide (2)
    ///   Ledge (8) · ClimbWall (14) · Jump (20) · Combined (26) · Flow lab (32)
    /// Menu: Tools → Warrior Woke → Construir Parkour Test Area.
    /// </summary>
    internal static class ParkourTestCircuitBuilder
    {
        private const string ScenePath      = "Assets/Scenes/Level-1.unity";
        public  const string RootName       = "ParkourTestArea";
        private const string FloorMaterial  = "Assets/Tests/ParkourTestArea/Materials/Losa.mat";
        private const string FixtureMaterial = ParkourObstaclePrefabs.Folder + "/Materials/Step.mat";
        private const float  LaneWidth      = ParkourStandard.PrefabWidth;
        public  const float  FloorTop       = 0f;
        private const int    LaneCount      = 11;

        // Floor and perimeter (x and z limits of the walkable area)
        private const float MinX = -34f, MaxX = 44f, MinZ = -65f, MaxZ = 15f;

        // Lane centers (x). Every lane starts at z = 0 and runs toward −Z.
        public const float LocomotionX = -25f, LowVaultX = -16f, MediumVaultX = -10f, HighVaultX = -4f, SlideX = 2f,
                           LedgeX = 8f, ClimbX = 14f, JumpX = 20f, ComboX = 26f, FlowX = 32f, MantleX = 38f;

        // Front faces (z) of the obstacles the Play Mode test uses
        public const float VaultFront = -8f, MediumDeepFront = -20f, MediumDeepDepth = 1.4f;
        public const float SlideBarFront = -9.5f, TunnelFront = -20f, TunnelDepth = 4f;
        public const float LedgeFront = -8f, LedgeAngledFront = -20f, LedgeAngle = 30f;
        public const float ClimbWallFront = -8f, StackFront = -20f, StackDepth = 11f, StackUpperFront = -24f, StackUpperDepth = 3f;
        public const float JumpGapFront = -8f;
        public const float FlowVaultFront = -14f, FlowRunwayStart = -20f;
        public const float MantleFront = -8f, MantleLowFront = -18f, MantleLowHeight = 0.9f, MantleHighFront = -28f, MantleHighHeight = 1.5f;

        public static readonly Vector3 SpawnPosition = new Vector3(0f, 1.2f, 8f);

        /// <summary>Lanes face −Z: the prefab's +Z (approach) turns toward −Z.</summary>
        private static readonly Quaternion LaneFacing = Quaternion.Euler(0f, 180f, 0f);

        private static int _ground;

        [MenuItem("Tools/Warrior Woke/Construir Parkour Test Area")]
        private static void BuildMenu() => Build();

        public static bool Build()
        {
            _ground = LayerMask.NameToLayer("Ground");
            if (_ground < 0 || LayerMask.NameToLayer("Obstacle") < 0)
            {
                Debug.LogError("[ParkourTestArea] Faltan las layers Ground u Obstacle.");
                return false;
            }
            if (ParkourObstaclePrefabs.Load(ParkourObstacleType.Combined) == null && !ParkourObstaclePrefabs.Generate())
                return false;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) Object.DestroyImmediate(go);

            var root = new GameObject(RootName).transform;
            Material floor   = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
            Material fixture = AssetDatabase.LoadAssetAtPath<Material>(FixtureMaterial);

            Box(root, "Suelo", new Vector3((MinX + MaxX) * 0.5f, FloorTop - 0.25f, (MinZ + MaxZ) * 0.5f), new Vector3(MaxX - MinX, 0.5f, MaxZ - MinZ), floor);
            BuildPerimeter(root);

            BuildLocomotion(Lane(root, "S01_Locomocion", LocomotionX), fixture);
            BuildVault(Lane(root, "S02_LowVault", LowVaultX), ParkourObstacleType.LowVault);
            BuildVault(Lane(root, "S03_MediumVault", MediumVaultX), ParkourObstacleType.MediumVault);
            BuildVault(Lane(root, "S04_HighVault", HighVaultX), ParkourObstacleType.HighVault);
            BuildSlide(Lane(root, "S05_Slide", SlideX));
            BuildLedge(Lane(root, "S06_Ledge", LedgeX));
            BuildClimb(Lane(root, "S07_ClimbWall", ClimbX), fixture);
            BuildJump(Lane(root, "S08_Jump", JumpX), fixture);
            Place(Lane(root, "S09_Combinado", ComboX), ParkourObstacleType.Combined, 0f);
            BuildFlow(Lane(root, "S10_Fluidez", FlowX));
            BuildMantle(Lane(root, "S11_Mantle", MantleX));

            PlaceSpawner(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ParkourTestArea] Área construida en {ScenePath} (suelo, perímetro y {LaneCount} secciones).");
            return true;
        }

        public static bool Validate(Scene scene)
        {
            GameObject area = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) area = go;
            bool ok = PlayerAnimationSetup.Check(area != null && area.transform.childCount == LaneCount + 2,
                                                 $"Parkour Test Area en Level-1 (suelo, perímetro y {LaneCount} secciones)");

            // Nothing else in the scene has colliders: the area is the whole level
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go == area) continue;
                foreach (Collider c in go.GetComponentsInChildren<Collider>())
                {
                    Debug.LogWarning($"[ParkourTestArea] Collider fuera del área: '{c.name}' ({go.name})");
                    ok = false;
                }
            }

            bool standard = ParkourObstaclePrefabs.ValidateSceneObstacles(out int count);
            ok &= PlayerAnimationSetup.Check(standard && count > 0, $"Los {count} obstáculos del área cumplen el Parkour Obstacle Standard");
            ok &= ParkourObstaclePrefabs.ValidatePrefabs();
            return ok;
        }

        /// <summary>Logs the collider bounds of every root object.</summary>
        [MenuItem("Tools/Warrior Woke/Listar límites de la escena")]
        public static void LogSceneBounds()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Collider[] cols = go.GetComponentsInChildren<Collider>();
                if (cols.Length == 0) continue;
                Bounds b = cols[0].bounds;
                foreach (Collider c in cols) b.Encapsulate(c.bounds);
                Debug.Log($"[ParkourTestArea] {go.name}: min {b.min} max {b.max}");
            }
        }

        /// <summary>The player starts at the entrance of the area, facing the lanes (−Z).</summary>
        private static void PlaceSpawner(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<Spawner>() == null) continue;
                go.transform.SetPositionAndRotation(SpawnPosition, LaneFacing);
                return;
            }
            Debug.LogWarning("[ParkourTestArea] No se encontró el Spawner en la escena.");
        }

        /// <summary>Standard barriers (1.5 m, layer Ground) around the floor, facing inward.</summary>
        private static void BuildPerimeter(Transform root)
        {
            var perimeter = new GameObject("Perimetro").transform;
            perimeter.SetParent(root, false);
            float depth = ParkourStandard.Spec(ParkourObstacleType.Barrier).Depth;
            float height = ParkourStandard.Spec(ParkourObstacleType.Barrier).Height;
            float lengthX = MaxX - MinX + 2f * depth, lengthZ = MaxZ - MinZ;
            Barrier(perimeter, "Norte", new Vector3((MinX + MaxX) * 0.5f, 0f, MaxZ), 0f, lengthX, height, depth);
            Barrier(perimeter, "Sur", new Vector3((MinX + MaxX) * 0.5f, 0f, MinZ), 180f, lengthX, height, depth);
            Barrier(perimeter, "Oeste", new Vector3(MinX, 0f, (MinZ + MaxZ) * 0.5f), -90f, lengthZ, height, depth);
            Barrier(perimeter, "Este", new Vector3(MaxX, 0f, (MinZ + MaxZ) * 0.5f), 90f, lengthZ, height, depth);
        }

        private static void Barrier(Transform parent, string name, Vector3 pivot, float yaw, float width, float height, float depth)
        {
            GameObject go = Instantiate(ParkourObstacleType.Barrier, parent, pivot, Quaternion.Euler(0f, yaw, 0f));
            go.name = name;
            ParkourObstaclePrefabs.SetBoxSize(go, width, height, depth);
        }

        // ─── Sections ────────────────────────────────────────────────────────────────

        // S01: acceleration, braking, turns and backpedal in the open; steps across the standard range
        // (auto step) and stairs for the feet IK. The pillars are plain fixtures to run around.
        private static void BuildLocomotion(Transform lane, Material fixture)
        {
            const float width = 8f;
            float[] heights = { 0.15f, 0.25f, 0.35f };
            for (int i = 0; i < heights.Length; i++)
            {
                GameObject step = Place(lane, ParkourObstacleType.Step, -8f - i * 3f);
                step.name = $"Escalon_{heights[i]:0.00}";
                ParkourObstaclePrefabs.SetBoxSize(step, width, heights[i], ParkourStandard.Spec(ParkourObstacleType.Step).Depth);
            }
            for (int i = 0; i < 4; i++)
                Box(lane, $"Pilar_{i + 1}", new Vector3(i % 2 == 0 ? -1.6f : 1.6f, 1.25f, -20f - i * 5f), new Vector3(0.6f, 2.5f, 0.6f), fixture);
            Stairs(lane, "Escalera", -40f, 0f, 1.0f, 4, 0.6f, width, fixture, true);
            Box(lane, "Plataforma_1m", new Vector3(0f, 0.5f, -44.4f), new Vector3(width, 1.0f, 4f), fixture);
            Stairs(lane, "Escalera_Bajada", -46.4f, 1.0f, 0f, 4, 0.6f, width, fixture, false);
        }

        // S02–S04 (GDD §5.4): one standard vault each; the medium lane adds a deep one (still in range).
        private static void BuildVault(Transform lane, ParkourObstacleType type)
        {
            Place(lane, type, VaultFront);
            if (type != ParkourObstacleType.MediumVault) return;
            GameObject deep = Place(lane, type, MediumDeepFront);
            deep.name += "_Fondo1.4";
            ParkourObstaclePrefabs.SetBoxSize(deep, LaneWidth, ParkourStandard.Spec(type).Height, MediumDeepDepth);
        }

        // S05 (P2): C while sprinting under the standard bar and through a 4 m tunnel of the same bar.
        private static void BuildSlide(Transform lane)
        {
            Place(lane, ParkourObstacleType.SlideBar, SlideBarFront);
            GameObject tunnel = Place(lane, ParkourObstacleType.SlideBar, TunnelFront);
            tunnel.name += "_Tunel";
            ParkourObstaclePrefabs.BuildSlideBar(tunnel, LaneWidth, ParkourStandard.Spec(ParkourObstacleType.SlideBar).Height, TunnelDepth,
                                                 tunnel.GetComponentInChildren<MeshRenderer>().sharedMaterial);
        }

        // S06 (P2): the standard ledge, square and turned 30°.
        private static void BuildLedge(Transform lane)
        {
            Place(lane, ParkourObstacleType.Ledge, LedgeFront);
            GameObject angled = Place(lane, ParkourObstacleType.Ledge, LedgeAngledFront);
            angled.name += "_Angulo30";
            angled.transform.localRotation = Quaternion.Euler(0f, 180f + LedgeAngle, 0f);
        }

        // S07 (P2): the standard climb wall (jump + grab), then a chained climb: a ledge whose top is a
        // terrace and a second ledge standing on it (4.4 m), and stairs down from the terrace.
        private static void BuildClimb(Transform lane, Material fixture)
        {
            Place(lane, ParkourObstacleType.ClimbWall, ClimbWallFront);
            float h = ParkourStandard.Spec(ParkourObstacleType.Ledge).Height;
            GameObject terrace = Place(lane, ParkourObstacleType.Ledge, StackFront);
            terrace.name += "_Terraza";
            ParkourObstaclePrefabs.SetBoxSize(terrace, LaneWidth, h, StackDepth);
            GameObject upper = Place(lane, ParkourObstacleType.Ledge, StackUpperFront, h);
            upper.name += "_Superior";
            ParkourObstaclePrefabs.SetBoxSize(upper, LaneWidth, h, StackUpperDepth);
            Stairs(lane, "Escalera_Bajada", StackFront - StackDepth, h, 0f, 9, 0.45f, LaneWidth, fixture, false);
        }

        // S08: the standard gap (stairs up to it), then platforms of 2 and 3 m to drop from (medium and
        // heavy landing; the gap's platform gives the light one).
        private static void BuildJump(Transform lane, Material fixture)
        {
            float h = ParkourStandard.Spec(ParkourObstacleType.JumpGap).Height;
            Stairs(lane, "Escalera_Hueco", JumpGapFront + 2f, 0f, h, 4, 0.5f, LaneWidth, fixture, true);
            Place(lane, ParkourObstacleType.JumpGap, JumpGapFront);
            Stairs(lane, "Escalera_2m", -22f, 0f, 2.0f, 8, 0.5f, LaneWidth, fixture, true);
            Box(lane, "Plataforma_2m", new Vector3(0f, 1.0f, -27.5f), new Vector3(LaneWidth, 2.0f, 3f), fixture);
            Stairs(lane, "Escalera_3m", -33f, 0f, 3.0f, 12, 0.45f, LaneWidth, fixture, true);
            Box(lane, "Plataforma_3m", new Vector3(0f, 1.5f, -39.9f), new Vector3(LaneWidth, 3.0f, 3f), fixture);
        }

        // S10: movement-quality lab. A low vault 14 m from the start (sprint, slide toward it: the slide
        // stops short of it, or Space chains into the vault) and an open runway behind it for stops,
        // turns, reversals and slides at different speeds.
        private static void BuildFlow(Transform lane)
        {
            Place(lane, ParkourObstacleType.LowVault, FlowVaultFront);
        }

        // S11 (P28): blocks to climb onto, at the standard height and at both ends of the mantle range.
        private static void BuildMantle(Transform lane)
        {
            ParkourObstacleSpec spec = ParkourStandard.Spec(ParkourObstacleType.Mantle);
            Place(lane, ParkourObstacleType.Mantle, MantleFront);
            GameObject low = Place(lane, ParkourObstacleType.Mantle, MantleLowFront);
            low.name += "_Bajo";
            ParkourObstaclePrefabs.SetBoxSize(low, LaneWidth, MantleLowHeight, spec.Depth);
            GameObject high = Place(lane, ParkourObstacleType.Mantle, MantleHighFront);
            high.name += "_Alto";
            ParkourObstaclePrefabs.SetBoxSize(high, LaneWidth, MantleHighHeight, spec.Depth);
        }

        // ─── Builders ────────────────────────────────────────────────────────────────

        private static Transform Lane(Transform root, string name, float x)
        {
            var lane = new GameObject(name).transform;
            lane.SetParent(root, false);
            lane.localPosition = new Vector3(x, FloorTop, 0f);
            return lane;
        }

        /// <summary>Standard obstacle on the lane axis whose approach face is at <paramref name="frontZ"/>, facing the lane.</summary>
        private static GameObject Place(Transform lane, ParkourObstacleType type, float frontZ, float baseY = 0f)
        {
            GameObject go = Instantiate(type, lane, new Vector3(0f, baseY, frontZ), LaneFacing);
            go.name = $"{ParkourStandard.Label(type).Replace(' ', '_')}_{-frontZ:0.#}";
            return go;
        }

        private static GameObject Instantiate(ParkourObstacleType type, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(ParkourObstaclePrefabs.Load(type), parent);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go;
        }

        /// <summary>Plain test fixture (layer Ground): not a parkour obstacle.</summary>
        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = _ground;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Straight stairs starting at <paramref name="startZ"/> toward −Z, from <paramref name="fromY"/> to
        /// <paramref name="toY"/> in <paramref name="steps"/> steps (each a solid block down to the floor).
        /// Going up, the last step is at <paramref name="toY"/> (the platform that follows); going down,
        /// the last step is one rise above <paramref name="toY"/>.
        /// </summary>
        private static void Stairs(Transform lane, string name, float startZ, float fromY, float toY, int steps, float stepDepth, float width, Material mat, bool up)
        {
            float rise = (toY - fromY) / (up ? steps : steps + 1);
            for (int i = 0; i < steps; i++)
            {
                float top = fromY + rise * (i + 1);
                Box(lane, $"{name}_{i + 1}", new Vector3(0f, top * 0.5f, startZ - stepDepth * (i + 0.5f)), new Vector3(width, top, stepDepth), mat);
            }
        }
    }
}
