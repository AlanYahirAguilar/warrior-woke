using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Generates the standard parkour obstacle prefabs (Assets/Prefabs/Parkour) from ParkourStandard
    /// and validates obstacles against it (docs/arquitectura.md §5.13). The prefabs are generated, not
    /// edited by hand: to change the standard, change ParkourStandard and regenerate. Regenerating keeps
    /// each prefab's GUID, so the scenes that use them keep their links.
    /// Each prefab: a root with ParkourObstacle (pivot on the floor at the approach face, obstacle
    /// along +Z) and plain cube children with a BoxCollider on the type's layer. Nothing else.
    /// Menus: Tools → Warrior Woke → Generar Prefabs de Obstáculos / Validar Obstáculos de Parkour.
    /// </summary>
    internal static class ParkourObstaclePrefabs
    {
        public const string Folder          = "Assets/Prefabs/Parkour";
        private const string MaterialsFolder = Folder + "/Materials";
        public const string GeometryName    = "Geometry";

        /// <summary>Order of the combined course: (type, distance of its front face from the course start).</summary>
        public static readonly (ParkourObstacleType type, float front)[] CombinedLayout =
        {
            (ParkourObstacleType.Step,        6f),
            (ParkourObstacleType.MediumVault, 12f),
            (ParkourObstacleType.SlideBar,    20.5f),
            (ParkourObstacleType.Ledge,       30f),
            (ParkourObstacleType.LowVault,    42f),
            (ParkourObstacleType.Mantle,      50f),
        };
        /// <summary>Depth of the ledge in the combined course: the runner climbs it and drops off its back.</summary>
        public const float CombinedLedgeDepth = 3f;

        public static string PathOf(ParkourObstacleType type) => $"{Folder}/ParkourObstacle_{FileName(type)}.prefab";

        private static string FileName(ParkourObstacleType type) => type == ParkourObstacleType.SlideBar ? "Slide" : type.ToString();

        public static GameObject Load(ParkourObstacleType type) => AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(type));

        [MenuItem("Tools/Warrior Woke/Generar Prefabs de Obstáculos")]
        private static void GenerateMenu() => Generate();

        public static bool Generate()
        {
            int ground = LayerMask.NameToLayer("Ground"), obstacle = LayerMask.NameToLayer("Obstacle");
            if (ground < 0 || obstacle < 0)
            {
                Debug.LogError("[ParkourObstacle] Faltan las layers Ground u Obstacle.");
                return false;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Parkour");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder(Folder, "Materials");

            foreach (ParkourObstacleType type in System.Enum.GetValues(typeof(ParkourObstacleType)))
            {
                if (type == ParkourObstacleType.Combined) continue; // nests the others: built last
                GameObject root = Build(type);
                PrefabUtility.SaveAsPrefabAsset(root, PathOf(type));
                Object.DestroyImmediate(root);
            }

            GameObject combined = BuildCombined();
            PrefabUtility.SaveAsPrefabAsset(combined, PathOf(ParkourObstacleType.Combined));
            Object.DestroyImmediate(combined);

            AssetDatabase.SaveAssets();
            Debug.Log($"[ParkourObstacle] Prefabs generados en {Folder}.");
            return ValidatePrefabs();
        }

        // ─── Builders ────────────────────────────────────────────────────────────────

        private static GameObject Build(ParkourObstacleType type)
        {
            ParkourObstacleSpec spec = ParkourStandard.Spec(type);
            var root = new GameObject($"ParkourObstacle_{FileName(type)}");
            root.isStatic = true;
            root.AddComponent<ParkourObstacle>().EditorSetType(type);
            int layer = LayerMask.NameToLayer(spec.Layer);
            root.layer = layer;

            switch (type)
            {
                case ParkourObstacleType.SlideBar:
                    BuildSlideBar(root, spec.Width, spec.Height, spec.Depth, MaterialFor(type));
                    break;
                case ParkourObstacleType.JumpGap:
                    Cube(root.transform, "Despegue", new Vector3(0f, spec.Height * 0.5f, ParkourStandard.JumpPlatformDepth * 0.5f),
                         new Vector3(spec.Width, spec.Height, ParkourStandard.JumpPlatformDepth), layer, MaterialFor(type));
                    Cube(root.transform, "Aterrizaje", new Vector3(0f, spec.Height * 0.5f, ParkourStandard.JumpPlatformDepth * 1.5f + spec.Depth),
                         new Vector3(spec.Width, spec.Height, ParkourStandard.JumpPlatformDepth), layer, MaterialFor(type));
                    break;
                default:
                    Cube(root.transform, GeometryName, Vector3.zero, Vector3.one, layer, MaterialFor(type));
                    SetBoxSize(root, spec.Width, spec.Height, spec.Depth);
                    break;
            }
            return root;
        }

        private static GameObject BuildCombined()
        {
            var root = new GameObject("ParkourObstacle_Combined");
            root.isStatic = true;
            root.AddComponent<ParkourObstacle>().EditorSetType(ParkourObstacleType.Combined);
            foreach ((ParkourObstacleType type, float front) in CombinedLayout)
            {
                var child = (GameObject)PrefabUtility.InstantiatePrefab(Load(type), root.transform);
                child.transform.localPosition = new Vector3(0f, 0f, front);
                if (type == ParkourObstacleType.Ledge)
                    SetBoxSize(child, ParkourStandard.PrefabWidth, ParkourStandard.Spec(type).Height, CombinedLedgeDepth);
            }
            return root;
        }

        /// <summary>Resizes a box obstacle (one Geometry child) keeping the pivot on the floor at the approach face.</summary>
        public static void SetBoxSize(GameObject obstacle, float width, float height, float depth)
        {
            Transform geometry = obstacle.transform.Find(GeometryName);
            geometry.localPosition = new Vector3(0f, height * 0.5f, depth * 0.5f);
            geometry.localScale    = new Vector3(width, height, depth);
        }

        /// <summary>Bar (bottom at <paramref name="clearance"/>) on two posts outside the passage, from z = 0 to <paramref name="depth"/>.</summary>
        public static void BuildSlideBar(GameObject root, float width, float clearance, float depth, Material mat)
        {
            int layer = root.layer;
            float thickness = ParkourStandard.SlideBarThickness;
            Transform bar = root.transform.Find("Barra") ?? Cube(root.transform, "Barra", Vector3.zero, Vector3.one, layer, mat).transform;
            bar.localPosition = new Vector3(0f, clearance + thickness * 0.5f, depth * 0.5f);
            bar.localScale    = new Vector3(width + 0.4f, thickness, depth);
            for (int i = 0; i < 2; i++)
            {
                string name = i == 0 ? "Poste_Izq" : "Poste_Der";
                Transform post = root.transform.Find(name) ?? Cube(root.transform, name, Vector3.zero, Vector3.one, layer, mat).transform;
                post.localPosition = new Vector3((i == 0 ? -1f : 1f) * (width * 0.5f + 0.1f), clearance * 0.5f, depth * 0.5f);
                post.localScale    = new Vector3(0.2f, clearance, Mathf.Min(0.2f, depth));
            }
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, int layer, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ─── Materials (one color per family) ────────────────────────────────────────

        private static Material MaterialFor(ParkourObstacleType type)
        {
            switch (type)
            {
                case ParkourObstacleType.Step:
                case ParkourObstacleType.JumpGap:   return GetMaterial("Step", new Color(0.3f, 0.75f, 0.3f));
                case ParkourObstacleType.Barrier:   return GetMaterial("Barrier", new Color(0.55f, 0.3f, 0.3f));
                case ParkourObstacleType.Mantle:    return GetMaterial("Mantle", new Color(0.6f, 0.35f, 0.8f));
                case ParkourObstacleType.Ledge:
                case ParkourObstacleType.ClimbWall: return GetMaterial("Ledge", new Color(0.95f, 0.85f, 0.2f));
                case ParkourObstacleType.SlideBar:  return GetMaterial("Slide", new Color(0.2f, 0.5f, 0.95f));
                default:                            return GetMaterial("Vault", new Color(0.95f, 0.55f, 0.15f));
            }
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ─── Validation ──────────────────────────────────────────────────────────────

        /// <summary>Every standard prefab exists and complies with the standard.</summary>
        public static bool ValidatePrefabs()
        {
            bool ok = true;
            foreach (ParkourObstacleType type in System.Enum.GetValues(typeof(ParkourObstacleType)))
            {
                GameObject prefab = Load(type);
                var issues = new List<string>();
                var obstacle = prefab != null ? prefab.GetComponent<ParkourObstacle>() : null;
                bool valid = obstacle != null && obstacle.Type == type && obstacle.Validate(issues);
                foreach (string issue in issues) Debug.LogWarning($"[ParkourObstacle] {issue}");
                ok &= PlayerAnimationSetup.Check(valid, $"Prefab estándar {System.IO.Path.GetFileName(PathOf(type))} cumple el estándar");
            }
            return ok;
        }

        /// <summary>Every ParkourObstacle in the open scenes complies with the standard.</summary>
        public static bool ValidateSceneObstacles(out int count)
        {
            var issues = new List<string>();
            ParkourObstacle[] all = Object.FindObjectsByType<ParkourObstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            count = all.Length;
            foreach (ParkourObstacle o in all) o.Validate(issues);
            foreach (string issue in issues) Debug.LogWarning($"[ParkourObstacle] {issue}");
            return issues.Count == 0;
        }

        [MenuItem("Tools/Warrior Woke/Validar Obstáculos de Parkour")]
        private static void ValidateMenu()
        {
            bool prefabs = ValidatePrefabs();
            bool scene = ValidateSceneObstacles(out int count);
            PlayerAnimationSetup.Check(scene, $"{count} obstáculos de la escena cumplen el estándar");
            Debug.Log(prefabs && scene ? "[ParkourObstacle] Todo cumple el Parkour Obstacle Standard." : "[ParkourObstacle] Hay obstáculos fuera del estándar (ver avisos).");
        }
    }

    /// <summary>Shows in the Inspector whether the obstacle complies with the standard, and why not.</summary>
    [CustomEditor(typeof(ParkourObstacle))]
    internal class ParkourObstacleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var obstacle = (ParkourObstacle)target;
            ParkourObstacleType type = obstacle.Type;

            if (type != ParkourObstacleType.Combined)
            {
                ParkourObstacleSpec spec = ParkourStandard.Spec(type);
                obstacle.TryMeasure(out float height, out float depth, out float width);
                EditorGUILayout.LabelField("Estándar", $"{spec.Height:F2} m de alto · {spec.Depth:F2} m de fondo · layer {spec.Layer}");
                EditorGUILayout.LabelField("Rango válido", $"alto {spec.MinHeight:F2}–{spec.MaxHeight:F2} m · ancho ≥ {spec.MinWidth:F2} m");
                EditorGUILayout.LabelField("Medido", $"{height:F2} m de alto · {depth:F2} m de fondo · {width:F2} m de ancho");
            }

            var issues = new List<string>();
            if (obstacle.Validate(issues))
                EditorGUILayout.HelpBox("Cumple el Parkour Obstacle Standard.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Warning);
        }
    }
}
