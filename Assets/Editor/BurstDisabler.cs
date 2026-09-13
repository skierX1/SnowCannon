using System;
using System.Reflection;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Turns off Burst Ahead-Of-Time compilation for player builds. The game has no Burst jobs of
    /// its own; Burst is only pulled in transitively (URP / com.unity.collections), and its AOT
    /// postprocessor insists on a Visual Studio C++ toolchain that isn't installed on this machine,
    /// which is what aborts the Windows build. Disabling the master "Enable Burst Compilation"
    /// switch is the supported, install-free fix. Done entirely by reflection so this file can
    /// never break compilation if the Burst package layout shifts.
    /// </summary>
    public static class BurstDisabler
    {
        public static void Disable()
        {
            try
            {
                var aot = Find("Unity.Burst.Editor.BurstPlatformAotSettings");
                if (aot == null) { Debug.LogWarning("[BurstDisabler] AOT settings type not found"); return; }

                var enableField = aot.GetField("EnableBurstCompilation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var getOrCreate = aot.GetMethod("GetOrCreateSettings",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var save = aot.GetMethod("Save",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (enableField == null || getOrCreate == null)
                {
                    Debug.LogWarning("[BurstDisabler] expected members missing");
                    return;
                }

                // The single parameterless-ish overload: GetOrCreateSettings(Nullable<BuildTarget>, bool).
                object settings = null;
                foreach (var m in aot.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != "GetOrCreateSettings") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    // First arg = Nullable<BuildTarget> (null => shared/common settings), second = createIfMissing.
                    settings = m.Invoke(null, new object[] { null, true });
                    break;
                }
                if (settings == null) { Debug.LogWarning("[BurstDisabler] GetOrCreateSettings returned null"); return; }

                enableField.SetValue(settings, false);

                if (save != null)
                {
                    try { save.Invoke(settings, new object[] { null }); }
                    catch (Exception se) { Debug.LogWarning("[BurstDisabler] Save(null) failed: " + se.Message); }
                }

                // Also flip the per-target copy for the Windows target if the API exposes it.
                TryDisableForTarget(aot, enableField, getOrCreate, save, "StandaloneWindows64");

                Debug.Log("[BurstDisabler] Burst AOT compilation disabled (EnableBurstCompilation=false).");
            }
            catch (Exception e)
            {
                Debug.LogError("[BurstDisabler] failed: " + e);
            }
        }

        static void TryDisableForTarget(Type aot, FieldInfo enableField, MethodInfo getOrCreate, MethodInfo save, string targetName)
        {
            try
            {
                var bt = Find("UnityEditor.BuildTarget");
                if (bt == null || !bt.IsEnum) return;
                object target = Enum.Parse(bt, targetName, true);

                foreach (var m in aot.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != "GetOrCreateSettings") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    var settings = m.Invoke(null, new[] { target, true });
                    if (settings == null) continue;
                    enableField.SetValue(settings, false);
                    if (save != null) save.Invoke(settings, new[] { target });
                    break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BurstDisabler] per-target disable skipped: " + e.Message);
            }
        }

        static Type Find(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
