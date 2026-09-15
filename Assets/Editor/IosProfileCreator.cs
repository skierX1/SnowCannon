using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine.Events;

namespace SnowCannon
{
    /// <summary>
    /// Creates a proper iOS build profile through the editor's own platform factory so the
    /// project is buildable for iOS (the profile is created on any machine; the actual iOS player
    /// build itself must run on macOS with Xcode). Mirrors DesktopProfileFixer's proven pattern:
    /// the CreateBuildProfile callback is hosted on a ScriptableObject so its delegate target
    /// derives from Object, which Unity's persistent-event registration requires.
    ///
    /// Also ensures the Android profile builds an APK (not an AAB) by clearing the
    /// "Build App Bundle" flag on the Android platform settings, so the Build Settings button
    /// produces a directly installable .apk.
    ///
    /// Launched with:  Unity.exe -projectPath &lt;proj&gt; -executeMethod SnowCannon.IosProfileCreator.Create
    /// Writes a one-line verdict to c:/tmp/sc_ios_profile_result.txt.
    /// </summary>
    public static class IosProfileCreator
    {
        const string ResultPath = "c:/tmp/sc_ios_profile_result.txt";
        const string Folder = "Assets/Settings/Build Profiles";
        const string IosName = "iOS";
        const string IosPath = Folder + "/iOS.asset";
        // The iOS platform module guid, verified via BuildProfile.GetInstalledPlatformModules().
        const string IosGuid = "ad48d16a66894befa4d8181998c3cb09";

        sealed class ProfileCallbackHolder : ScriptableObject
        {
            public BuildProfile result;
            public void OnReady(BuildProfile profile) { result = profile; }
        }

        public static void Create()
        {
            try
            {
                Directory.CreateDirectory(Folder);

                // 1. Remove any stale copy so CreateAsset cannot collide.
                if (File.Exists(IosPath)) AssetDatabase.DeleteAsset(IosPath);

                // 2. Create the profile through the editor's supported factory.
                var holder = ScriptableObject.CreateInstance<ProfileCallbackHolder>();
                BuildProfile created = null;
                try
                {
                    var onReady = new UnityAction<BuildProfile>(holder.OnReady);
                    BuildProfile.CreateBuildProfile(new GUID(IosGuid), IosName, onReady);
                    created = holder.result;
                }
                finally
                {
                    if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
                }

                if (created == null) created = BuildProfile.GetBuildProfileAtPath(IosPath);
                if (created == null) { Write("FAIL|CreateBuildProfile-returned-null"); return; }

                // 3. Persist it as an asset.
                if (!File.Exists(IosPath)) AssetDatabase.CreateAsset(created, IosPath);

                // 4. Force the Android profile to emit an APK, not an AAB. The Build Settings
                //    button builds the active profile; if the user's active profile is Android the
                //    "Build App Bundle" toggle must be off to get a .apk. We clear it on the
                //    AndroidPlatformBuildSettings of the Android profile if present.
                ClearAndroidAppBundleFlag();

                try { AssetDatabase.SaveAssets(); } catch { }

                bool ok = File.Exists(IosPath) && new FileInfo(IosPath).Length > 100;
                Write((ok ? "OK|" : "FAIL|asset-not-written") + "|path=" + IosPath);
                Debug.Log("[IosProfileCreator] done, written=" + ok);
            }
            catch (Exception e)
            {
                var inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                Write("FAIL|exception:" + inner.Message.Replace("\r", " ").Replace("\n", " "));
                Debug.LogError("[IosProfileCreator] exception: " + inner);
            }
        }

        /// <summary>Best-effort: flip the Android profile's "Build App Bundle" flag off so the
        /// Build Settings button produces an installable .apk. Never fatal if the API shape
        /// differs — the profile YAML is also edited directly by hand as the primary fix.</summary>
        static void ClearAndroidAppBundleFlag()
        {
            try
            {
                var androidPath = Folder + "/Android™.asset";
                if (!File.Exists(androidPath)) return;
                var text = File.ReadAllText(androidPath);
                if (text.Contains("m_BuildAppBundle: 1"))
                {
                    text = text.Replace("m_BuildAppBundle: 1", "m_BuildAppBundle: 0");
                    File.WriteAllText(androidPath, text);
                    Debug.Log("[IosProfileCreator] cleared Android BuildAppBundle flag");
                }
            }
            catch (Exception e) { Debug.LogWarning("[IosProfileCreator] android flag clear skipped: " + e.Message); }
        }

        static void Write(string s)
        {
            try { File.WriteAllText(ResultPath, s); } catch { }
            Debug.Log("[IosProfileCreator] " + s);
        }
    }
}
