using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Runs the build-critical fixes automatically at the start of *every* player build,
    /// whether it is launched from the Build Settings window or from the scripted BuildRunner:
    ///
    /// 1. Disables Burst AOT compilation. The game has no Burst jobs of its own; Burst is only
    ///    pulled in transitively and its AOT postprocessor demands a Visual Studio C++ toolchain
    ///    that isn't installed here, which otherwise aborts the build.
    /// 2. Registers the runtime-resolved URP shaders in Graphics Settings' Always Included Shaders
    ///    list so build-time stripping can't remove them (they are only ever referenced through
    ///    Shader.Find at runtime, so nothing serialised pins them).
    /// 3. Clears stale "Create Visual Studio Solution" artifacts (.sln / Il2CppOutputProject) from
    ///    the output folder. A leftover solution there makes WinPlayerPostProcessor throw
    ///    "Build path contains a project previously built with the Create Visual Studio Solution
    ///    option", which otherwise fails the build in ~1 second.
    ///
    /// All are idempotent and also persist to committed assets, so this is a safety net that
    /// keeps re-applying them even if the settings are ever reset.
    /// </summary>
    public sealed class BuildPreprocessor : IPreprocessBuild
    {
        // Run before the other preprocessors so the Burst/shader/solution fixes are in place first.
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildTarget target, string path)
        {
            // Only relevant for the desktop player we ship; harmless for any other target.
            BurstDisabler.Disable();
            ShaderRegistrar.EnsureRegistered();
            DisableMobileSRPBatcher();
            CleanStaleSolutionArtifacts(path);
        }

        /// <summary>
        /// Forces the URP SRP Batcher OFF on the mobile render-pipeline asset. On OpenGLES3 the
        /// batcher silently drops SRP mesh draw calls, so the cannon, ground, snowmen and lake
        /// render nothing on Android while the built-in-shader sky/snowflakes/UI survive. The
        /// asset is committed with the flag off, but we re-assert it here so a reimport or a
        /// template reset can never silently re-break the phone build. Best-effort.
        /// </summary>
        static void DisableMobileSRPBatcher()
        {
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    "Assets/Settings/Mobile_RPAsset.asset");
                if (asset == null) return;
                var t = asset.GetType();
                var prop = t.GetProperty("useSRPBatcher");
                if (prop == null || !prop.CanWrite) return;
                if ((bool)prop.GetValue(asset))
                {
                    prop.SetValue(asset, false);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[BuildPreprocessor] forced useSRPBatcher=false on Mobile_RPAsset");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BuildPreprocessor] SRP-batcher fix skipped: " + e.Message);
            }
        }

        /// <summary>
        /// Removes the solution files a previous "Create Visual Studio Solution" build left in the
        /// output folder. Their presence trips WinPlayerPostProcessor.PrepareForBuild before the
        /// build even starts, so they must be gone. Best-effort: never let a cleanup error abort a
        /// build that would otherwise succeed.
        /// </summary>
        static void CleanStaleSolutionArtifacts(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;

                // The output path may be the exe file or its folder; normalise to a directory.
                string dir;
                if (File.Exists(path)) dir = Path.GetDirectoryName(path);
                else if (Directory.Exists(path)) dir = path;
                else dir = Path.GetDirectoryName(path);

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                foreach (var sln in Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(sln);
                    Debug.Log("[BuildPreprocessor] removed stale solution: " + sln);
                }
                foreach (var proj in Directory.GetDirectories(dir, "Il2CppOutputProject", SearchOption.TopDirectoryOnly))
                {
                    Directory.Delete(proj, true);
                    Debug.Log("[BuildPreprocessor] removed stale Il2CppOutputProject: " + proj);
                }
                var props = Path.Combine(dir, "UnityCommon.props");
                if (File.Exists(props)) File.Delete(props);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BuildPreprocessor] stale-solution cleanup skipped: " + e.Message);
            }
        }
    }
}
