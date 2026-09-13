using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// The game is fully procedural: every material is created at runtime with
    /// <c>new Material(Shader.Find("Universal Render Pipeline/Lit"))</c>. Because no serialized
    /// asset ever references those shaders, Unity's build-time shader stripping removes them from
    /// the player, so every MeshRenderer (cannon, ground, snowmen) draws nothing in a build while
    /// the SpriteRenderer clouds/flakes (built-in shader) survive. The editor never strips, which
    /// is why it looks correct there.
    ///
    /// The fix is to register the runtime-resolved shaders in Graphics Settings' "Always Included
    /// Shaders" list, which forces each shader and all of its variants into the build. We do it by
    /// assigning real Shader object references through a SerializedObject (exactly what the editor
    /// UI does), so Unity serialises the correct guid/fileID itself rather than us guessing them.
    /// </summary>
    public static class ShaderRegistrar
    {
        // The shader names the game resolves at runtime (Procedural.cs Mat.LitShader/UnlitShader).
        static readonly string[] ShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
            "Diffuse",
        };

        public static void EnsureRegistered()
        {
            try
            {
                var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>(
                    "ProjectSettings/GraphicsSettings.asset");
                if (gs == null)
                {
                    Debug.LogError("[ShaderRegistrar] could not load GraphicsSettings.asset");
                    return;
                }

                var so = new SerializedObject(gs);
                so.Update();
                var list = so.FindProperty("m_AlwaysIncludedShaders");
                if (list == null)
                {
                    Debug.LogError("[ShaderRegistrar] m_AlwaysIncludedShaders not found");
                    return;
                }

                // Paths already in the list, so we never duplicate the built-in defaults.
                var present = new List<string>();
                for (int i = 0; i < list.arraySize; i++)
                {
                    var obj = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (obj != null) present.Add(AssetDatabase.GetAssetPath(obj));
                }

                int added = 0, missing = 0;
                foreach (var name in ShaderNames)
                {
                    var shader = Shader.Find(name);
                    if (shader == null) { missing++; Debug.LogWarning("[ShaderRegistrar] not found: " + name); continue; }

                    var path = AssetDatabase.GetAssetPath(shader);
                    if (string.IsNullOrEmpty(path) || present.Contains(path)) continue;

                    int index = list.arraySize;
                    list.InsertArrayElementAtIndex(index);
                    list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                    present.Add(path);
                    added++;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(gs);
                AssetDatabase.SaveAssets();
                Debug.Log("[ShaderRegistrar] added=" + added + " total-in-list=" + list.arraySize + " not-found=" + missing);
            }
            catch (Exception e)
            {
                Debug.LogError("[ShaderRegistrar] failed: " + e);
            }
        }
    }
}
