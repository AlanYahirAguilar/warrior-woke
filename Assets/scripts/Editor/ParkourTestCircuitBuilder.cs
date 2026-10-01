using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds the parkour test zone "ParkourTestCircuit" inside Level-1 (decision: test zone in the
    /// game scene). Five signed lanes run toward −Z from z = −16, south of House_01_cyber (whose
    /// colliders reach z = −12.4) and of the spawn point (0, 2, −2.5):
    ///   VAULT (x = −8) · SLIDE (x = −3) · AUTO STEP (x = 2) · LEDGE (x = 7) · COMBINADO (x = 12).
    /// Obstacle sizes match the detection limits in EnvironmentChecker / PlayerMovement.
    /// Everything stands on its own flat slab (top at SlabTop): Level-1's "Ground" is a squashed
    /// capsule (a 0.5 m dome), so obstacles placed on it would end up half buried and uneven.
    /// x = 17 is left free (on the slab) as a straight corridor for general movement tests.
    /// Re-running replaces the previous circuit. Menu: Tools → Warrior Woke → Construir Circuito de Parkour.
    /// </summary>
    internal static class ParkourTestCircuitBuilder
    {
        private const string ScenePath       = "Assets/Scenes/Level-1.unity";
        private const string RootName        = "ParkourTestCircuit";
        private const string MaterialsFolder = "Assets/Tests/ParkourCircuit/Materials";
        private const float  LaneWidth       = 3f;
        private const float  StartZ          = -8f;   // lane-local; lanes are shifted by ZOffset
        public  const float  ZOffset         = -8f;   // keeps the circuit clear of House_01_cyber
        public  const float  SlabTop         = 0.55f; // above the highest point of the Ground dome (0.5)

        private static int _ground;
        private static int _obstacle;

        [MenuItem("Tools/Warrior Woke/Construir Circuito de Parkour")]
        private static void BuildMenu() => Build();

        public static bool Build()
        {
            _ground   = LayerMask.NameToLayer("Ground");
            _obstacle = LayerMask.NameToLayer("Obstacle");
            if (_ground < 0 || _obstacle < 0)
            {
                Debug.LogError("[ParkourTestCircuit] Faltan las layers Ground u Obstacle.");
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName) Object.DestroyImmediate(go);
            }

            var root = new GameObject(RootName).transform;
            Material slab = GetMaterial("Losa", new Color(0.45f, 0.45f, 0.42f));
            GameObject floor = Box(root, "Losa", -24f + ZOffset, 0f, 30f, SlabTop, 35f, _ground, slab);
            floor.transform.localPosition += new Vector3(5f, 0f, 0f);

            Material vault = GetMaterial("Vault", new Color(0.95f, 0.55f, 0.15f));
            Material slide = GetMaterial("Slide", new Color(0.2f, 0.5f, 0.95f));
            Material step  = GetMaterial("Step", new Color(0.3f, 0.75f, 0.3f));
            Material ledge = GetMaterial("Ledge", new Color(0.95f, 0.85f, 0.2f));
            Material combo = GetMaterial("Combo", new Color(0.6f, 0.6f, 0.65f));
            Material board = GetMaterial("Sign", new Color(0.08f, 0.08f, 0.1f));

            BuildVaultLane(Lane(root, "Vault", -8f), vault, board);
            BuildSlideLane(Lane(root, "Slide", -3f), slide, board);
            BuildStepLane(Lane(root, "AutoStep", 2f), step, board);
            BuildLedgeLane(Lane(root, "Ledge", 7f), ledge, board);
            BuildComboLane(Lane(root, "Combinado", 12f), combo, board);

            PlaceSpawner(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ParkourTestCircuit] Circuito construido en {ScenePath} (losa + {root.childCount - 1} carriles).");
            return true;
        }

        public static bool Validate(Scene scene)
        {
            GameObject circuit = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName) circuit = go;
            }
            if (!PlayerAnimationSetup.Check(circuit != null && circuit.transform.childCount == 6, "Circuito de parkour en Level-1 (losa + 5 carriles)"))
                return false;

            // No other scene geometry may intrude into the circuit (the Ground dome lies under the slab).
            Bounds area = Encapsulate(circuit);
            area.Expand(new Vector3(1f, -0.6f, 1f));
            bool clear = true;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go == circuit || go.name == "Ground") continue;
                foreach (Collider c in go.GetComponentsInChildren<Collider>())
                {
                    if (c.bounds.Intersects(area))
                    {
                        Debug.LogWarning($"[ParkourTestCircuit] '{c.name}' ({go.name}) invade el circuito: {c.bounds}");
                        clear = false;
                    }
                }
            }
            return PlayerAnimationSetup.Check(clear, "Ningún objeto de la escena invade el circuito");
        }

        /// <summary>Logs the collider bounds of every root object (to choose a free area).</summary>
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
                Debug.Log($"[ParkourTestCircuit] {go.name}: min {b.min} max {b.max}");
            }
        }

        public static void LogSceneBoundsBatch()
        {
            LogSceneBounds();
            EditorApplication.Exit(0);
        }

        private static Bounds Encapsulate(GameObject go)
        {
            Collider[] cols = go.GetComponentsInChildren<Collider>();
            Bounds b = cols[0].bounds;
            foreach (Collider c in cols) b.Encapsulate(c.bounds);
            return b;
        }

        /// <summary>
        /// The original spawn (0, 2, −2.5) is enclosed by House_01_cyber's colliders, so the player
        /// could not walk to the circuit. The Spawner is placed at the circuit entrance, facing the
        /// signs (approved for testing; move it back when the real level is laid out).
        /// </summary>
        private static void PlaceSpawner(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<Spawner>() == null) continue;
                go.transform.SetPositionAndRotation(SpawnPosition, Quaternion.Euler(0f, 180f, 0f));
                Debug.Log($"[ParkourTestCircuit] Spawner movido a {SpawnPosition}, mirando hacia el circuito.");
                return;
            }
            Debug.LogWarning("[ParkourTestCircuit] No se encontró el Spawner en la escena.");
        }

        public static readonly Vector3 SpawnPosition = new Vector3(2f, 2f, -13.5f);

        // ─── Lanes ───────────────────────────────────────────────────────────────────

        // Vault (GDD §5.4): low obstacles between 0.45 and 1.2 m high, up to 1.5 m deep.
        private static void BuildVaultLane(Transform lane, Material mat, Material board)
        {
            Sign(lane, "VAULT\nEspacio cerca del obstáculo", board);
            Box(lane, "Vault_0.6m", -13f, 0f, LaneWidth, 0.6f, 0.4f, _obstacle, mat);
            Label(lane, "0.6 m", -13f, 1.1f);
            Box(lane, "Vault_1.0m", -19f, 0f, LaneWidth, 1.0f, 0.5f, _obstacle, mat);
            Label(lane, "1.0 m", -19f, 1.5f);
            Box(lane, "Vault_1.1m_Ancho", -25f, 0f, LaneWidth, 1.1f, 1.2f, _obstacle, mat);
            Label(lane, "1.1 m (1.2 m de ancho)", -25f, 1.6f);
            Box(lane, "NoVault_1.6m", -31f, 0f, LaneWidth, 1.6f, 0.5f, _obstacle, mat);
            Label(lane, "1.6 m: demasiado alto (salta)", -31f, 2.1f);
        }

        // Slide (P2): Shift + C under bars whose bottom is 1.2 m high (standing does not fit).
        private static void BuildSlideLane(Transform lane, Material mat, Material board)
        {
            Sign(lane, "SLIDE\nShift + C bajo la barra", board);
            Bar(lane, "Slide_Barra", -14f, 1.2f, 1f, mat);
            Label(lane, "barra a 1.2 m", -14f, 2.1f);
            Bar(lane, "Slide_Tunel", -24f, 1.2f, 4f, mat);
            Label(lane, "túnel de 4 m", -24f, 2.1f);
        }

        // Auto step: edges up to 0.4 m are climbed without jumping.
        private static void BuildStepLane(Transform lane, Material mat, Material board)
        {
            Sign(lane, "AUTO STEP\nCamina hacia los escalones", board);
            const float rise = 0.25f, depth = 0.8f;
            for (int i = 0; i < 5; i++)
            {
                float z = -11f - i * depth;
                Box(lane, $"Escalon_{i + 1}", z, 0f, LaneWidth, rise * (i + 1), depth, _ground, mat);
            }
            Box(lane, "Plataforma", -11f - 5 * depth - 2f, 0f, LaneWidth, rise * 5, 4f, _ground, mat);
            Label(lane, "escalones de 0.25 m", -13f, 2f);
            Box(lane, "Bordillo_0.35m", -26f, 0f, LaneWidth, 0.35f, 0.6f, _ground, mat);
            Label(lane, "bordillo 0.35 m", -26f, 1f);
        }

        // Ledge grab / climb (P2): jump against the wall, Space to climb. The walls are taller than a
    // jump reaches (feet rise ~2.5 m), otherwise the player lands on top instead of grabbing.
        private static void BuildLedgeLane(Transform lane, Material mat, Material board)
        {
            Sign(lane, "LEDGE\nSalta contra el muro y Espacio", board);
            Box(lane, "Muro_3.0m", -15f, 0f, LaneWidth, 3.0f, 2f, _obstacle, mat);
            Label(lane, "3.0 m", -13.5f, 3.5f);
            Box(lane, "Muro_3.5m", -24f, 0f, LaneWidth, 3.5f, 2f, _obstacle, mat);
            Label(lane, "3.5 m", -22.5f, 4f);
        }

        // Everything in a row: step → vault → slide → ledge.
        private static void BuildComboLane(Transform lane, Material mat, Material board)
        {
            Sign(lane, "COMBINADO\nStep → Vault → Slide → Ledge", board);
            Box(lane, "C_Bordillo", -12f, 0f, LaneWidth, 0.3f, 0.6f, _ground, mat);
            Box(lane, "C_Vault", -17f, 0f, LaneWidth, 1.0f, 0.5f, _obstacle, mat);
            Bar(lane, "C_Slide", -24f, 1.2f, 1.5f, mat);
            Box(lane, "C_Ledge", -32f, 0f, LaneWidth, 3.0f, 3f, _obstacle, mat);
        }

        // ─── Builders ────────────────────────────────────────────────────────────────

        private static Transform Lane(Transform root, string name, float x)
        {
            var lane = new GameObject(name).transform;
            lane.SetParent(root, false);
            lane.localPosition = new Vector3(x, SlabTop, ZOffset);
            return lane;
        }

        /// <summary>Box resting on the floor (bottomY) centered on the lane at z.</summary>
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

        /// <summary>Dark board with white text at the lane entrance, readable from the spawn point.</summary>
        private static void Sign(Transform lane, string text, Material board)
        {
            GameObject panel = Box(lane, "Cartel", StartZ + 0.5f, 2.2f, LaneWidth - 0.2f, 1.1f, 0.1f, 0, board);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            TextAt(lane, text, new Vector3(0f, 2.75f, StartZ + 0.42f), 0.12f);
        }

        private static void Label(Transform lane, string text, float z, float y)
        {
            TextAt(lane, text, new Vector3(0f, y, z + 0.5f), 0.08f);
        }

        private static void TextAt(Transform lane, string text, Vector3 localPos, float size)
        {
            var go = new GameObject("Texto");
            go.transform.SetParent(lane, false);
            go.transform.localPosition = localPos;
            // The player approaches from +Z looking toward −Z: turn the text so it faces them.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;
            go.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
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
