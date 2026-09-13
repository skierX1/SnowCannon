using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Runs the two build-critical fixes automatically at the start of *every* player build,
    /// whether it is launched from the Build Settings window or from the scripted BuildRunner:
    ///
    /// 1. Disables Burst AOT compilation. The game has no Burst jobs of its own; Burst is only
    ///    pulled in transitively and its AOT postprocessor demands a Visual Studio C++ toolchain
    ///    that isn't installed here, which otherwise aborts the build.
    /// 2. Registers the runtime-resolved URP shaders in Graphics Settings' Always Included Shaders
    ///    list so build-time stripping can't remove them (they are only ever referenced through
    ///    Shader.Find at runtime, so nothing serialised pins them).
    ///
    /// Both are idempotent and also persist to committed assets, so this is a safety net that
    /// keeps re-applying them even if the settings are ever reset.
    /// </summary>
    public sealed class BuildPreprocessor : IPreprocessBuild
    {
        // Run before the other preprocessors so the Burst/shader fixes are in place first.
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildTarget target, string path)
        {
            // Only relevant for the desktop player we ship; harmless for any other target.
            BurstDisabler.Disable();
            ShaderRegistrar.EnsureRegistered();
        }
    }
}
