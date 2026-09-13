using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace SnowCannon
{
    /// <summary>
    /// Runs the Windows Standalone (x86_64) player build through the classic BuildPipeline API,
    /// reached entirely via reflection so this file never hard-codes the build-API surface and can
    /// never itself break compilation. Launched with:
    ///   Unity.exe -projectPath &lt;proj&gt; -executeMethod SnowCannon.BuildRunner.BuildWindows
    /// Writes a one-line verdict to c:/tmp/sc_build_result.txt so the launcher can read it.
    /// </summary>
    public static class BuildRunner
    {
        const string ResultPath = "c:/tmp/sc_build_result.txt";
        const string ExePath = "c:/tmp/SnowCannonBuild/SnowCannon/SnowCannon.exe";

        public static void BuildWindows()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ExePath));

                var optsType = Find("UnityEditor.BuildPlayerOptions");
                var bpType = Find("UnityEditor.BuildPipeline");
                if (optsType == null || bpType == null)
                {
                    Write("FAIL|api-not-found opts=" + (optsType != null) + " bp=" + (bpType != null));
                    return;
                }

                object opts = Activator.CreateInstance(optsType);
                DumpMembers(optsType);
                SetAny(optsType, opts, new[] { "scenes" }, new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Play.unity" });
                SetAny(optsType, opts, new[] { "locationPathName", "outputPath" }, ExePath);
                SetEnumAny(optsType, opts, new[] { "target" }, "UnityEditor.BuildTarget", "StandaloneWindows64");
                SetEnumAny(optsType, opts, new[] { "targetGroup" }, "UnityEditor.BuildTargetGroup", "Standalone");

                MethodInfo build = null;
                foreach (var m in bpType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "BuildPlayer") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == optsType) { build = m; break; }
                }
                if (build == null)
                {
                    Write("FAIL|no-BuildPlayer-overload-for-options");
                    return;
                }

                ShaderRegistrar.EnsureRegistered();
                BurstDisabler.Disable();

                build.Invoke(null, new[] { opts });

                bool ok = File.Exists(ExePath) && new FileInfo(ExePath).Length > 1000;
                Write((ok ? "OK|" : "FAIL|no-exe") + "|exe=" + File.Exists(ExePath) +
                      "|bytes=" + (File.Exists(ExePath) ? new FileInfo(ExePath).Length : 0));
                Debug.Log("[BuildRunner] done, exe present=" + ok);
            }
            catch (Exception e)
            {
                var inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                Write("FAIL|exception:" + inner.Message.Replace("\r", " ").Replace("\n", " "));
                Debug.LogError("[BuildRunner] exception: " + inner);
            }
        }

        static Type Find(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return Type.GetType(fullName + ", UnityEditor", false);
        }

        static void DumpMembers(Type t)
        {
            try
            {
                var names = new System.Text.StringBuilder();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    names.Append(f.Name).Append(',');
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.CanWrite) names.Append(p.Name).Append(',');
                Debug.Log("[BuildRunner] members of " + t.Name + ": " + names);
            }
            catch { }
        }

        static void SetAny(Type t, object inst, string[] names, object value)
        {
            foreach (var name in names)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType.IsAssignableFrom(value.GetType())) { f.SetValue(inst, value); return; }
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(value.GetType())) { p.SetValue(inst, value, null); return; }
            }
        }

        static void SetEnumAny(Type t, object inst, string[] names, string enumFull, string member)
        {
            var et = Find(enumFull);
            if (et == null || !et.IsEnum) return;
            object val = Enum.Parse(et, member, true);
            SetAny(t, inst, names, val);
        }

        static void Write(string s)
        {
            try { File.WriteAllText(ResultPath, s); } catch { }
            Debug.Log("[BuildRunner] " + s);
        }
    }
}
