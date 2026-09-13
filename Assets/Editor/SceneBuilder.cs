using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SnowCannon
{
    /// <summary>
    /// Builds the two game scenes from code. Every object is generated at run time by the
    /// runtime scripts, so a scene only needs a single bootstrap GameObject carrying the
    /// right component. Run from the menu (SnowCannon &gt; Build Scenes) or from the command
    /// line with -executeMethod SnowCannon.EditorTools.BuildScenes.
    /// </summary>
    public static class EditorTools
    {
        const string SceneFolder = "Assets/Scenes";
        const string MenuPath = SceneFolder + "/Menu.unity";
        const string PlayPath = SceneFolder + "/Play.unity";

        static bool autoRan;

        /// <summary>
        /// Ensures the scenes exist and are registered in the build, right after the editor
        /// loads, so a fresh checkout is playable without any manual step. Idempotent.
        /// </summary>
        [InitializeOnLoadMethod]
        public static void AutoBuildOnLoad()
        {
            if (autoRan) return;
            autoRan = true;
            BuildScenes();
        }

        [MenuItem("SnowCannon/Build Scenes")]
        public static void BuildScenes()
        {
            Directory.CreateDirectory(SceneFolder);

            CreateScene(MenuPath, "MainMenu", () => new GameObject("MainMenu").AddComponent<MainMenu>());
            CreateScene(PlayPath, "Play", () => new GameObject("Game").AddComponent<SnowCannonGame>());

            RegisterScenesInBuild();
            AssetDatabase.Refresh();

            Debug.Log("[SnowCannon] scenes built: " + MenuPath + ", " + PlayPath);
        }

        static void CreateScene(string path, string title, System.Action bootstrap)
        {
            if (File.Exists(path))
            {
                Debug.Log("[SnowCannon] scene already present, keeping " + path);
                return;
            }

            // NewScene(..., Single) makes the fresh scene the active one, so anything we
            // create next lands straight in it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bootstrap();
            EditorSceneManager.SaveScene(scene, path);
        }

        /// <summary>
        /// Writes the two scenes straight into the serialized build-settings asset using the
        /// GUIDs from their .meta files. This is done by text because during the load-time hook
        /// the asset database is still cold, so the normal scenes setter would silently drop the
        /// not-yet-imported paths.
        /// </summary>
        static void RegisterScenesInBuild()
        {
            string menuGuid = ReadGuid(MenuPath + ".meta");
            string playGuid = ReadGuid(PlayPath + ".meta");
            if (menuGuid == null || playGuid == null) return;

            string asset = "ProjectSettings/EditorBuildSettings.asset";
            if (!File.Exists(asset)) return;

            string text = File.ReadAllText(asset);
            string block =
                "  m_Scenes:\n" +
                "  - enabled: 1\n" +
                "    path: " + MenuPath + "\n" +
                "    guid: " + menuGuid + "\n" +
                "  - enabled: 1\n" +
                "    path: " + PlayPath + "\n" +
                "    guid: " + playGuid + "\n";

            int start = text.IndexOf("  m_Scenes:");
            int end = text.IndexOf("  m_configObjects:");
            if (start >= 0 && end > start)
            {
                string updated = text.Substring(0, start) + block + text.Substring(end);
                File.WriteAllText(asset, updated);
            }
        }

        static string ReadGuid(string metaPath)
        {
            if (!File.Exists(metaPath)) return null;
            foreach (string line in File.ReadAllLines(metaPath))
            {
                string t = line.Trim();
                if (t.StartsWith("guid:")) return t.Substring(5).Trim();
            }
            return null;
        }
    }
}
