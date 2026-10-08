using UnityEditor;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// One-click setup: takes Assets/Characters/Player/character.fbx, configures it as a
    /// Humanoid model (falling back to Generic if the FBX has no valid biped rig), swaps it
    /// in as Player.prefab's visual model (replacing the placeholder box), and fits the
    /// CharacterController/HeadPoint to the model's real dimensions so it stands correctly on
    /// the ground. Re-running it is safe (it replaces its own previous "Model" child).
    /// See docs/contexto.md and docs/arquitectura.md §5.9.
    /// </summary>
    internal static class PlayerCharacterSetup
    {
        private const string PrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ModelPath = "Assets/Characters/Player/character.fbx";
        private const string ModelChildName = "Model";
        private const float TargetHeightMeters = 1.8f;

        [MenuItem("Tools/Warrior Woke/Configurar Modelo del Jugador")]
        private static void SetupPlayerModel()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                Debug.LogError($"[PlayerCharacterSetup] No se encontró {ModelPath}. Copia character.fbx ahí, espera a que aparezca en el Project window, y vuelve a correr este comando.");
                return;
            }

            ConfigureImportSettings();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[PlayerCharacterSetup] No se pudo cargar el modelo en {ModelPath} después de configurarlo.");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[PlayerCharacterSetup] No se pudo abrir {PrefabPath}.");
                return;
            }

            try
            {
                ApplyModelToPrefab(prefabRoot, modelAsset);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[PlayerCharacterSetup] Listo. Player.prefab quedó actualizado con el nuevo modelo — ábrelo o entra a Level-1 para verlo.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void ConfigureImportSettings()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[PlayerCharacterSetup] No se pudo leer el ModelImporter de {ModelPath}; se usará la configuración de importación actual.");
                return;
            }

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.SaveAndReimport();
            }

            if (!HasValidHumanAvatar())
            {
                Debug.LogWarning("[PlayerCharacterSetup] El modelo no tiene un rig Humanoid válido; se usará Generic. El personaje se verá igual, pero no podrá reusar animaciones Humanoid de Mixamo/otros modelos hasta que tenga un rig humano correcto.");
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
            }
        }

        private static bool HasValidHumanAvatar()
        {
            foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (obj is Avatar avatar && avatar.isValid && avatar.isHuman)
                    return true;
            }
            return false;
        }

        private static void ApplyModelToPrefab(GameObject prefabRoot, GameObject modelAsset)
        {
            Transform root = prefabRoot.transform;

            var oldFilter = prefabRoot.GetComponent<MeshFilter>();
            if (oldFilter != null) Object.DestroyImmediate(oldFilter);

            var oldRenderer = prefabRoot.GetComponent<MeshRenderer>();
            if (oldRenderer != null) Object.DestroyImmediate(oldRenderer);

            // Idempotent: remove a model from a previous run of this command.
            Transform existingModel = root.Find(ModelChildName);
            if (existingModel != null) Object.DestroyImmediate(existingModel.gameObject);

            root.localScale = Vector3.one;

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root);
            modelInstance.name = ModelChildName;
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[PlayerCharacterSetup] El modelo no tiene Renderers visibles; no se pudo ajustar el collider automáticamente al tamaño real.");
                return;
            }

            Bounds bounds = ComputeCombinedBounds(renderers);
            float rawHeight = bounds.size.y;

            if (rawHeight > 0.001f && (rawHeight < 0.5f || rawHeight > 4f))
            {
                float scaleFactor = TargetHeightMeters / rawHeight;
                modelInstance.transform.localScale = Vector3.one * scaleFactor;
                renderers = modelInstance.GetComponentsInChildren<Renderer>();
                bounds = ComputeCombinedBounds(renderers);
                Debug.Log($"[PlayerCharacterSetup] El modelo medía {rawHeight:F2} unidades en su escala original; se reescaló x{scaleFactor:F3} para quedar en ~{TargetHeightMeters:F1}m.");
            }

            float height = bounds.size.y;

            // Re-center the model so its vertical middle sits at the root's local origin —
            // matches the CharacterController/CenterPoint convention already used by the rest
            // of the player (root = torso center, not feet). This is what actually fixes
            // "el personaje flota o se hunde": the collider bottom now matches the model's
            // real feet, whatever the FBX's internal pivot happened to be.
            float centerOffsetY = bounds.center.y - root.position.y;
            modelInstance.transform.localPosition = new Vector3(0f, -centerOffsetY, 0f);

            var body = prefabRoot.GetComponent<CharacterController>();
            if (body != null)
            {
                body.height = height;
                body.center = Vector3.zero;
                body.radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.6f, 0.25f, 0.6f);
                Debug.Log($"[PlayerCharacterSetup] CharacterController ajustado: height={body.height:F2}, radius={body.radius:F2}.");
            }

            Transform headPoint = root.Find("HeadPoint");
            if (headPoint != null)
                headPoint.localPosition = new Vector3(0f, height / 2f, 0f);
        }

        private static Bounds ComputeCombinedBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
