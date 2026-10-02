using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds Level-1 as the Parkour Test Area (decision P24): a flat floor and seven sections, one
    /// per movement, laid out as parallel lanes that run toward −Z from the spawn point. No signs or
    /// text: each section is color-coded and the layout is documented in docs/features.md (F32).
    ///   S01 Locomotion (x = −24) · S02 Vault (x = −14) · S03 Slide (x = −8) · S04 Ledge grab (x = −2)
    ///   S05 Climb (x = 4) · S06 Jump / Landing (x = 10) · S07 Combined (x = 17)
    /// Obstacle sizes match the detection limits in EnvironmentChecker, PlayerLedgeGrabState and
    /// PlayerMovement. Rebuilding replaces the area and removes the objects of the old blockout
    /// (houses, floating wall and enemy, capsule-shaped Ground) if they are still there.
    /// Menu: Tools → Warrior Woke → Construir Parkour Test Area.
    /// </summary>
    internal static class ParkourTestCircuitBuilder
    {
        private const string ScenePath       = "Assets/Scenes/Level-1.unity";
        public  const string RootName        = "ParkourTestArea";
        private const string LegacyRootName  = "ParkourTestCircuit";
        private const string MaterialsFolder = "Assets/Tests/ParkourCircuit/Materials";
        private const float  LaneWidth       = 4f;
        public  const float  FloorTop        = 0f;

        // Lane centers (x). Every lane starts at z = 0 and runs toward −Z.
        public const float LocomotionX = -24f, VaultX = -14f, SlideX = -8f, LedgeX = -2f, ClimbX = 4f, LandingX = 10f, ComboX = 17f;

        public static readonly Vector3 SpawnPosition = new Vector3(0f, 1.2f, 8f);

        private static int _ground;
        private static int _obstacle;

        [MenuItem("Tools/Warrior Woke/Construir Parkour Test Area")]
        private static void BuildMenu() => Build();

        public static bool Build()
        {
            _ground   = LayerMask.NameToLayer("Ground");
            _obstacle = LayerMask.NameToLayer("Obstacle");
            if (_ground < 0 || _obstacle < 0)
            {
                Debug.LogError("[ParkourTestArea] Faltan las layers Ground u Obstacle.");
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName || IsLegacyBlockout(go))
                {
                    Debug.Log($"[ParkourTestArea] Se elimina '{go.name}'.");
                    Object.DestroyImmediate(go);
                }
            }

            var root = new GameObject(RootName).transform;
            Material floor   = GetMaterial("Losa", new Color(0.45f, 0.45f, 0.42f));
            Material moveMat = GetMaterial("Step", new Color(0.3f, 0.75f, 0.3f));
            Material vault   = GetMaterial("Vault", new Color(0.95f, 0.55f, 0.15f));
            Material slide   = GetMaterial("Slide", new Color(0.2f, 0.5f, 0.95f));
            Material ledge   = GetMaterial("Ledge", new Color(0.95f, 0.85f, 0.2f));
            Material combo   = GetMaterial("Combo", new Color(0.6f, 0.6f, 0.65f));

            GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "Suelo";
            floorGo.layer = _ground;
            floorGo.isStatic = true;
            floorGo.transform.SetParent(root, false);
            floorGo.transform.localPosition = new Vector3(-3f, FloorTop - 0.25f, -25f);
            floorGo.transform.localScale = new Vector3(62f, 0.5f, 80f);
            floorGo.GetComponent<MeshRenderer>().sharedMaterial = floor;
            BuildPerimeter(root, floor);

            BuildLocomotion(Lane(root, "S01_Locomocion", LocomotionX), moveMat);
            BuildVault(Lane(root, "S02_Vault", VaultX), vault);
            BuildSlide(Lane(root, "S03_Slide", SlideX), slide);
            BuildLedge(Lane(root, "S04_LedgeGrab", LedgeX), ledge);
            BuildClimb(Lane(root, "S05_Climb", ClimbX), ledge, moveMat);
            BuildLanding(Lane(root, "S06_SaltoAterrizaje", LandingX), moveMat);
            BuildCombined(Lane(root, "S07_Combinado", ComboX), combo, vault, slide, ledge);

            PlaceSpawner(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ParkourTestArea] Área construida en {ScenePath} (suelo, perímetro y {root.childCount - 2} secciones).");
            return true;
        }

        /// <summary>Objects of the old Level-1 blockout that the test area replaces (removed by decision P24).</summary>
        private static bool IsLegacyBlockout(GameObject go)
        {
            return go.name == LegacyRootName || go.name == "Ground" || go.name == "wall" || go.name == "Enemy" ||
                   go.name.StartsWith("House_01_cyber");
        }

        public static bool Validate(Scene scene)
        {
            GameObject area = null;
            bool legacy = false;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName) area = go;
                legacy |= IsLegacyBlockout(go);
            }
            bool ok = PlayerAnimationSetup.Check(area != null && area.transform.childCount == 9, "Parkour Test Area en Level-1 (suelo, perímetro y 7 secciones)");
            ok &= PlayerAnimationSetup.Check(!legacy, "Sin objetos del blockout anterior (casas, muro, enemigo, Ground cápsula)");

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
                go.transform.SetPositionAndRotation(SpawnPosition, Quaternion.Euler(0f, 180f, 0f));
                return;
            }
            Debug.LogWarning("[ParkourTestArea] No se encontró el Spawner en la escena.");
        }

        /// <summary>
        /// 1.5 m walls around the floor so nobody runs off the area. Layer Ground: too high to vault,
        /// too low to grab, and never a ledge candidate (ledges are only searched on Obstacle).
        /// </summary>
        private static void BuildPerimeter(Transform root, Material mat)
        {
            var perimeter = new GameObject("Perimetro").transform;
            perimeter.SetParent(root, false);
            const float minX = -34f, maxX = 28f, minZ = -65f, maxZ = 15f, height = 1.5f, thick = 0.5f;
            Wall(perimeter, "Norte", new Vector3((minX + maxX) * 0.5f, height * 0.5f, maxZ + thick * 0.5f), new Vector3(maxX - minX + 2f * thick, height, thick), mat);
            Wall(perimeter, "Sur", new Vector3((minX + maxX) * 0.5f, height * 0.5f, minZ - thick * 0.5f), new Vector3(maxX - minX + 2f * thick, height, thick), mat);
            Wall(perimeter, "Oeste", new Vector3(minX - thick * 0.5f, height * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(thick, height, maxZ - minZ), mat);
            Wall(perimeter, "Este", new Vector3(maxX + thick * 0.5f, height * 0.5f, (minZ + maxZ) * 0.5f), new Vector3(thick, height, maxZ - minZ), mat);
        }

        private static void Wall(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = _ground;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ─── Sections ────────────────────────────────────────────────────────────────

        // S01: acceleration, braking, turns and backpedal in the open; curbs and stairs for the feet IK.
        private static void BuildLocomotion(Transform lane, Material mat)
        {
            const float width = 8f;
            Box(lane, "Bordillo_0.15", -8f, 0f, width, 0.15f, 0.6f, _ground, mat);
            Box(lane, "Bordillo_0.25", -11f, 0f, width, 0.25f, 0.6f, _ground, mat);
            Box(lane, "Bordillo_0.35", -14f, 0f, width, 0.35f, 0.6f, _ground, mat);
            for (int i = 0; i < 4; i++)
            {
                GameObject pillar = Box(lane, $"Pilar_{i + 1}", -20f - i * 5f, 0f, 0.6f, 2.5f, 0.6f, _obstacle, mat);
                pillar.transform.localPosition += new Vector3(i % 2 == 0 ? -1.6f : 1.6f, 0f, 0f);
            }
            Stairs(lane, "Escalera", -40f, 0f, 1.0f, 4, 0.6f, width, mat, true);
            Box(lane, "Plataforma_1m", -44.4f, 0f, width, 1.0f, 4f, _ground, mat);
            Stairs(lane, "Escalera_Bajada", -46.4f, 1.0f, 0f, 4, 0.6f, width, mat, false);
        }

        // S02 (GDD §5.4): vaultable heights 0.5–1.2 m and depths 0.3–1.4 m; 1.6 m is too high.
        private static void BuildVault(Transform lane, Material mat)
        {
            Face(lane, "Vault_0.5", -8f, 0.5f, 0.3f, _obstacle, mat);
            Face(lane, "Vault_0.75", -16f, 0.75f, 0.4f, _obstacle, mat);
            Face(lane, "Vault_1.0", -24f, 1.0f, 0.5f, _obstacle, mat);
            Face(lane, "Vault_1.2", -32f, 1.2f, 0.6f, _obstacle, mat);
            Face(lane, "Vault_0.9_Ancho", -40f, 0.9f, 1.4f, _obstacle, mat);
            Face(lane, "NoVault_1.6", -50f, 1.6f, 0.5f, _obstacle, mat);
        }

        // S03 (P2): C while sprinting under a 1.2 m bar and through a 4 m tunnel.
        private static void BuildSlide(Transform lane, Material mat)
        {
            Bar(lane, "Barra_1.2", -10f, 1.2f, 1f, mat);
            Bar(lane, "Tunel_1.2", -22f, 1.2f, 4f, mat);
        }

        // S04 (P2): tops within reach from the ground (2.1, 2.5 m), from a jump (3.0 m) and an angled wall.
        private static void BuildLedge(Transform lane, Material mat)
        {
            Face(lane, "Muro_2.1", -8f, 2.1f, 3f, _obstacle, mat);
            Face(lane, "Muro_2.5", -18f, 2.5f, 3f, _obstacle, mat);
            Face(lane, "Muro_3.0_Salto", -28f, 3.0f, 3f, _obstacle, mat);
            GameObject angled = Face(lane, "Muro_2.4_Angulo30", -40f, 2.4f, 3f, _obstacle, mat);
            angled.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
        }

        // S05 (P2): climb a 2.2 m wall, then a second wall from its top (4.4 m), drop onto a 2.2 m
        // terrace and walk down the stairs.
        private static void BuildClimb(Transform lane, Material wall, Material stairs)
        {
            Face(lane, "Muro_A_2.2", -8f, 2.2f, 4f, _obstacle, wall);
            Face(lane, "Muro_B_4.4", -12f, 4.4f, 3f, _obstacle, wall);
            Face(lane, "Terraza_2.2", -15f, 2.2f, 4f, _obstacle, wall);
            Stairs(lane, "Escalera_Bajada", -19f, 2.2f, 0f, 9, 0.45f, LaneWidth, stairs, false);
        }

        // S06: stairs up to platforms of 1, 2 and 3 m to drop from (soft, medium, heavy landing) and a
        // 2 m gap between two 1 m platforms to jump across.
        private static void BuildLanding(Transform lane, Material mat)
        {
            Stairs(lane, "Escalera_1m", -6f, 0f, 1.0f, 4, 0.5f, LaneWidth, mat, true);
            Face(lane, "Plataforma_1m_A", -8f, 1.0f, 4f, _ground, mat);
            Face(lane, "Plataforma_1m_B", -14f, 1.0f, 4f, _ground, mat);
            Stairs(lane, "Escalera_2m", -22f, 0f, 2.0f, 8, 0.5f, LaneWidth, mat, true);
            Face(lane, "Plataforma_2m", -26f, 2.0f, 3f, _ground, mat);
            Stairs(lane, "Escalera_3m", -33f, 0f, 3.0f, 12, 0.45f, LaneWidth, mat, true);
            Face(lane, "Plataforma_3m", -38.4f, 3.0f, 3f, _ground, mat);
        }

        // S07: curb → vault → slide → ledge grab and climb → drop → vault, in one run.
        private static void BuildCombined(Transform lane, Material mat, Material vault, Material slide, Material ledge)
        {
            Box(lane, "C_Bordillo", -6f, 0f, LaneWidth, 0.2f, 0.6f, _ground, mat);
            Face(lane, "C_Vault_1.0", -12f, 1.0f, 0.4f, _obstacle, vault);
            Bar(lane, "C_Barra", -21f, 1.2f, 1f, slide);
            Face(lane, "C_Muro_2.2", -30f, 2.2f, 3f, _obstacle, ledge);
            Face(lane, "C_Vault_0.75", -42f, 0.75f, 0.4f, _obstacle, vault);
        }

        // ─── Builders ────────────────────────────────────────────────────────────────

        private static Transform Lane(Transform root, string name, float x)
        {
            var lane = new GameObject(name).transform;
            lane.SetParent(root, false);
            lane.localPosition = new Vector3(x, FloorTop, 0f);
            return lane;
        }

        /// <summary>Box resting on <paramref name="bottomY"/>, centered on the lane at z.</summary>
        private static GameObject Box(Transform lane, string name, float z, float bottomY, float width, float height, float depth, int layer, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(lane, false);
            go.transform.localPosition = new Vector3(0f, bottomY + height * 0.5f, z);
            go.transform.localScale = new Vector3(width, height, depth);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        /// <summary>Block on the floor whose front face (toward the player, +Z) is at <paramref name="frontZ"/>.</summary>
        private static GameObject Face(Transform lane, string name, float frontZ, float height, float depth, int layer, Material mat)
        {
            return Box(lane, name, frontZ - depth * 0.5f, 0f, LaneWidth, height, depth, layer, mat);
        }

        /// <summary>Overhead bar (bottom at bottomY) on two posts outside the lane, for sliding under.</summary>
        private static void Bar(Transform lane, string name, float z, float bottomY, float depth, Material mat)
        {
            const float thickness = 0.3f;
            Box(lane, name, z, bottomY, LaneWidth + 0.4f, thickness, depth, _obstacle, mat);
            float postX = LaneWidth * 0.5f + 0.1f;
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject post = Box(lane, name + "_Poste", z, 0f, 0.2f, bottomY, 0.2f, _obstacle, mat);
                post.transform.localPosition += new Vector3(side * postX, 0f, 0f);
            }
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
                float z = startZ - stepDepth * (i + 0.5f);
                Box(lane, $"{name}_{i + 1}", z, 0f, width, top, stepDepth, _ground, mat);
            }
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            if (!AssetDatabase.IsValidFolder("Assets/Tests")) AssetDatabase.CreateFolder("Assets", "Tests");
            if (!AssetDatabase.IsValidFolder("Assets/Tests/ParkourCircuit")) AssetDatabase.CreateFolder("Assets/Tests", "ParkourCircuit");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/Tests/ParkourCircuit", "Materials");

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
