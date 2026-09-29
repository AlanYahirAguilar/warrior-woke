using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Al abrir el proyecto, si la escena activa no es la escena principal del juego
    /// (por ejemplo, Unity abrió una escena vacía o la demo del asset pack de entorno),
    /// carga automáticamente Assets/Scenes/Level-1.unity.
    /// Solo actúa una vez por sesión del Editor (ver SessionState) para no pelear
    /// contra un cambio de escena hecho a propósito durante el trabajo diario.
    /// Ver CONTEXTO.md, sección "Por qué a veces 'no se ve nada' al hacer pull".
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneAutoLoader
    {
        private const string MainScenePath = "Assets/Scenes/Level-1.unity";
        private const string SessionFlagKey = "WarriorWoke.SceneAutoLoader.CheckedThisSession";

        static SceneAutoLoader()
        {
            if (Application.isBatchMode)
                return;

            if (SessionState.GetBool(SessionFlagKey, false))
                return;

            SessionState.SetBool(SessionFlagKey, true);
            EditorApplication.delayCall += OpenMainSceneIfNeeded;
        }

        private static void OpenMainSceneIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!File.Exists(MainScenePath))
                return;

            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path == MainScenePath)
                return;

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Debug.Log($"[SceneAutoLoader] Se abrió automáticamente la escena principal: {MainScenePath}");
        }
    }
}
