using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine.Events;

namespace SnowCannon
{
    /// <summary>
    /// Repairs the Build Settings so the "Windows" build produces a runnable desktop player.
    /// The project shipped with a Universal Windows Platform (UWP / WSAPlayer) profile whose
    /// build needs the Visual Studio C++ "Universal Windows Platform tools" workload, which is
    /// not installed here, so every build failed in the UWP post-processor. This tool creates a
    /// proper Standalone Windows profile through the editor's own platform factory and makes it
    /// the active one, then removes the broken UWP profile. Launched with:
    ///   Unity.exe -projectPath &lt;proj&gt; -executeMethod SnowCannon.DesktopProfileFixer.Fix
    /// Writes a one-line verdict to c:/tmp/sc_profile_result.txt.
    /// </summary>
    public static class DesktopProfileFixer
    {
        const string ResultPath = "c:/tmp/sc_profile_result.txt";
        const string Folder = "Assets/Settings/Build Profiles";
        const string NewName = "Windows Standalone";
        const string NewPath = Folder + "/Windows Standalone.asset";

        /// <summary>
        /// A ScriptableObject acts as the callback target: Unity's persistent-event registration
        /// requires the listener's target to derive from Object, which a plain static closure is not.
        /// </summary>
        sealed class ProfileCallbackHolder : ScriptableObject
        {
            public BuildProfile result;
            public void OnReady(BuildProfile profile) { result = profile; }
        }

        public static void Fix()
        {
            try
            {
                // 1. Enumerate installed platform modules and pick the desktop "Windows" one.
                var modules = BuildProfile.GetInstalledPlatformModules();
                if (modules == null || modules.Count == 0)
                {
                    Write("FAIL|no-installed-modules");
                    return;
                }

                GUID windowsGuid = new GUID();
                string windowsName = null;
                var seen = new System.Text.StringBuilder();
                foreach (var m in modules)
                {
                    string dn = m.displayName ?? "";
                    seen.Append("[").Append(dn).Append(" = ").Append(m.platformGuid).Append("] ");
                    bool isWindows = dn.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isUwp = dn.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0
                             || dn.IndexOf("Store", StringComparison.OrdinalIgnoreCase) >= 0
                             || dn.IndexOf("Metro", StringComparison.OrdinalIgnoreCase) >= 0;
                    // The desktop player module is named exactly "Windows"; the UWP one is
                    // "Universal Windows Platform", so excluding UWP leaves the desktop target.
                    if (isWindows && !isUwp && windowsName == null)
                    {
                        windowsGuid = m.platformGuid;
                        windowsName = dn;
                    }
                }
                Debug.Log("[ProfileFixer] installed modules: " + seen);

                if (windowsName == null)
                {
                    Write("FAIL|no-windows-module|seen=" + seen);
                    return;
                }
                Debug.Log("[ProfileFixer] chosen desktop module: " + windowsName + " guid=" + windowsGuid);

                // 2. Remove any stale copy of the target asset so CreateAsset cannot collide.
                if (File.Exists(NewPath))
                    AssetDatabase.DeleteAsset(NewPath);
                Directory.CreateDirectory(Folder);

                // 3. Create the profile through the editor's supported factory, capturing the
                //    created instance via a ScriptableObject-hosted callback.
                var holder = ScriptableObject.CreateInstance<ProfileCallbackHolder>();
                BuildProfile created = null;
                try
                {
                    // Binding to an instance method on an Object-derived target makes the delegate's
                    // target be that ScriptableObject, satisfying Unity's persistent-listener validation.
                    var onReady = new UnityAction<BuildProfile>(holder.OnReady);
                    BuildProfile.CreateBuildProfile(windowsGuid, NewName, onReady);
                    created = holder.result;
                }
                finally
                {
                    if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
                }

                if (created == null)
                    created = BuildProfile.GetBuildProfileAtPath(NewPath);
                if (created == null)
                {
                    Write("FAIL|CreateBuildProfile-returned-null");
                    return;
                }

                // 4. Persist it as an asset.
                if (!File.Exists(NewPath))
                    AssetDatabase.CreateAsset(created, NewPath);

                // 5. Make it the active profile.
                BuildProfile.SetActiveBuildProfile(created);

                // 6. Remove the broken UWP profile so Build Settings cannot select it again.
                var uwpPath = Folder + "/Universal Windows Platform.asset";
                if (File.Exists(uwpPath))
                {
                    AssetDatabase.DeleteAsset(uwpPath);
                    var meta = uwpPath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }

                // 7. Flush to disk (best effort; CreateAsset already persisted it).
                try { AssetDatabase.SaveAssets(); }
                catch (Exception flushErr)
                {
                    Debug.LogWarning("[ProfileFixer] flush best-effort failed: " + flushErr.Message);
                }

                bool ok = File.Exists(NewPath) && new FileInfo(NewPath).Length > 100;
                Write((ok ? "OK|" : "FAIL|asset-not-written") + "|path=" + NewPath +
                      "|module=" + windowsName + "|guid=" + windowsGuid);
                Debug.Log("[ProfileFixer] done, profile written=" + ok);
            }
            catch (Exception e)
            {
                var inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                Write("FAIL|exception:" + inner.Message.Replace("\r", " ").Replace("\n", " ") +
                      "||" + (inner.StackTrace ?? "").Replace("\r", " ").Replace("\n", " | "));
                Debug.LogError("[ProfileFixer] exception: " + inner);
            }
        }

        static void Write(string s)
        {
            try { File.WriteAllText(ResultPath, s); } catch { }
            Debug.Log("[ProfileFixer] " + s);
        }
    }
}
