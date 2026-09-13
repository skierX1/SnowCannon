using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SnowCannon
{
    /// <summary>
    /// An automated Play-mode smoke test, launched with:
    ///   Unity.exe -projectPath &lt;proj&gt; -executeMethod SnowCannon.SmokeHarness.Run
    /// (GUI mode, no -batchmode: batch mode is blocked by a licensing-client handshake bug on
    /// this machine, while the GUI editor licenses fine.) The method runs exactly once at startup,
    /// so it never re-fires on the play-mode domain reload. It opens the Play scene and enters
    /// play mode; the runtime heartbeat inside SnowCannonGame (gated on SNOWCANNON_SMOKE=1) then
    /// drives a scripted sequence and writes a verdict file that the launcher polls and kills on.
    /// </summary>
    public static class SmokeHarness
    {
        const string PlayPath = "Assets/Scenes/Play.unity";
        const string MenuPath = "Assets/Scenes/Menu.unity";
        const string Started = "c:/tmp/sc_smoke_started";
        const string Verdict = "c:/tmp/sc_smoke_verdict.txt";
        const string LogPath = "c:/tmp/sc_smoke_report.txt";
        const string Enabled = "c:/tmp/sc_smoke_enabled";

        static bool ran;

        public static void Run()
        {
            if (ran) return;
            ran = true;

            if (!File.Exists(Enabled)) return;
            TryDelete(Started); TryDelete(Verdict); TryDelete(LogPath);

            Log("harness: opening " + MenuPath);
            var scene = EditorSceneManager.OpenScene(MenuPath);
            Log("harness: opened, valid=" + scene.IsValid() + " name=" + scene.name);

            // Keep play running even when the editor window is not focused, so the scripted
            // sequence completes without depending on foreground focus.
            Application.runInBackground = true;

            Log("harness: entering play mode");
            EditorApplication.EnterPlaymode();

            // The gate the runtime heartbeat waits on before it starts its scripted sequence.
            File.WriteAllText(Started, "entered");
            Log("harness: started marker written");
        }

        static void Log(string msg)
        {
            Debug.Log(msg);
            try { File.AppendAllText(LogPath, msg + "\n"); } catch { }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
