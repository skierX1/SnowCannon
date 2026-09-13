using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace SnowCannon
{
    /// <summary>
    /// Procedural meshes. Everything the game draws is generated at run time, so the
    /// project needs no imported models at all (phase 1 of the art plan).
    /// </summary>
    public static class MeshFactory
    {
        /// <summary>A cone with its base on y = 0 and its tip at y = height.</summary>
        public static Mesh Cone(float radius, float height, int segments)
        {
            return TruncatedCone(radius, 0f, height, segments);
        }

        /// <summary>A cone frustum standing on y = 0. radiusTop may be 0 for a sharp tip.</summary>
        public static Mesh TruncatedCone(float radiusBottom, float radiusTop, float height, int segments)
        {
            segments = Mathf.Max(6, segments);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Outward normal of the lateral surface, used for correct lighting on the cone.
            Vector3 lateral = new Vector3(height, radiusBottom - radiusTop, 0f).normalized;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);

                int b = verts.Count;
                verts.Add(new Vector3(c0 * radiusBottom, 0f, s0 * radiusBottom));
                verts.Add(new Vector3(c1 * radiusBottom, 0f, s1 * radiusBottom));
                verts.Add(new Vector3(c0 * radiusTop, height, s0 * radiusTop));
                verts.Add(new Vector3(c1 * radiusTop, height, s1 * radiusTop));

                normals.Add(new Vector3(c0 * lateral.x, lateral.y, s0 * lateral.x));
                normals.Add(new Vector3(c1 * lateral.x, lateral.y, s1 * lateral.x));
                normals.Add(new Vector3(c0 * lateral.x, lateral.y, s0 * lateral.x));
                normals.Add(new Vector3(c1 * lateral.x, lateral.y, s1 * lateral.x));

                uvs.Add(new Vector2(i / (float)segments, 0f));
                uvs.Add(new Vector2((i + 1) / (float)segments, 0f));
                uvs.Add(new Vector2(i / (float)segments, 1f));
                uvs.Add(new Vector2((i + 1) / (float)segments, 1f));

                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);

                // Bottom cap (only when the shape is a frustum).
                if (radiusBottom > 0.0001f)
                {
                    int c = verts.Count;
                    verts.Add(new Vector3(0f, 0f, 0f));
                    normals.Add(Vector3.down);
                    uvs.Add(new Vector2(0.5f, 0.5f));
                    verts.Add(new Vector3(c0 * radiusBottom, 0f, s0 * radiusBottom));
                    normals.Add(Vector3.down);
                    uvs.Add(new Vector2(c0 * 0.5f + 0.5f, s0 * 0.5f + 0.5f));
                    verts.Add(new Vector3(c1 * radiusBottom, 0f, s1 * radiusBottom));
                    normals.Add(Vector3.down);
                    uvs.Add(new Vector2(c1 * 0.5f + 0.5f, s1 * 0.5f + 0.5f));
                    tris.Add(c); tris.Add(c + 2); tris.Add(c + 1);
                }

                // Top cap.
                if (radiusTop > 0.0001f)
                {
                    int c = verts.Count;
                    verts.Add(new Vector3(0f, height, 0f));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(0.5f, 0.5f));
                    verts.Add(new Vector3(c0 * radiusTop, height, s0 * radiusTop));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(c0 * 0.5f + 0.5f, s0 * 0.5f + 0.5f));
                    verts.Add(new Vector3(c1 * radiusTop, height, s1 * radiusTop));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(c1 * 0.5f + 0.5f, s1 * 0.5f + 0.5f));
                    tris.Add(c); tris.Add(c + 1); tris.Add(c + 2);
                }
            }

            var mesh = new Mesh { name = "generated_cone" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Builds a mesh of the given string laid out left-to-right from the font's
        /// glyph metrics. Vertices are in font-pixel units (y up, baseline at 0); the caller
        /// scales it to a world size using the returned mesh's bounds. Returns null if the font
        /// cannot render the text.</summary>
        public static Mesh Text(string text, Font font, int sizePx)
        {
            if (font == null || string.IsNullOrEmpty(text)) return null;
            try
            {
                font.RequestCharactersInTexture(text, sizePx, FontStyle.Normal);
            }
            catch { }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float pen = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == ' ') { pen += sizePx * 0.32f; continue; }
                CharacterInfo ci;
                if (!font.GetCharacterInfo(ch, out ci, sizePx, FontStyle.Normal)) continue;
                if (ci.glyphWidth <= 0 || ci.glyphHeight <= 0) continue;

                Rect g = ci.vert;
                Rect u = ci.uv;
                float x0 = pen + g.xMin, x1 = pen + g.xMax;
                float y0 = g.yMin, y1 = g.yMax;
                int v = verts.Count;
                verts.Add(new Vector3(x0, y0, 0f));
                verts.Add(new Vector3(x1, y0, 0f));
                verts.Add(new Vector3(x0, y1, 0f));
                verts.Add(new Vector3(x1, y1, 0f));
                uvs.Add(new Vector2(u.xMin, u.yMin));
                uvs.Add(new Vector2(u.xMax, u.yMin));
                uvs.Add(new Vector2(u.xMin, u.yMax));
                uvs.Add(new Vector2(u.xMax, u.yMax));
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
                pen += ci.advance;
            }

            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "generated_text" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A unit quad in the XY plane, centred on the origin.</summary>
        public static Mesh Quad()
        {
            var mesh = new Mesh { name = "generated_quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A hand-thrown snowball: a sphere whose surface is pushed in and out by
        /// seeded value noise, so it reads as a lumpy, hunched ball of packed snow rather than
        /// a perfect sphere. Unit diameter, centred on the origin.</summary>
        public static Mesh LumpySnowball(int seed)
        {
            const int rings = 14, segments = 20;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const float f = 2.3f;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    var d = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta));
                    float n = Mathf.PerlinNoise(d.x * f + d.y * 0.37f + seed * 7.13f,
                                               d.z * f + d.y * 0.61f + seed * 3.71f);
                    verts.Add(d * (0.5f * (1f + (n - 0.5f) * 0.34f)));
                    uvs.Add(new Vector2((float)s / segments, (float)r / rings));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, e = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }
            var mesh = new Mesh { name = "lumpy_snowball_" + seed };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }

    /// <summary>Run-time generated textures. Keeps the project free of binary assets.</summary>
    public static class TextureFactory
    {
        /// <summary>Speckled snow: off-white base with blue-ish shading and bright grains.</summary>
        public static Texture2D Snow()
        {
            return Snow(0);
        }

        /// <summary>
        /// A speckled snow texture seeded by <paramref name="variant"/>: the noise offset, the
        /// overall brightness bias, the grain density and the blue-shade tint all shift with the
        /// variant, so snowmen built from different variants never read as clones.
        /// </summary>
        public static Texture2D Snow(int variant)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "snow_tex" + variant };
            float ox = variant * 37.31f, oy = variant * 11.77f;
            float bias = (variant % 4) switch { 0 => 0f, 1 => -9f, 2 => 7f, _ => -4f };
            float blue = (variant % 3) switch { 0 => 24f, 1 => 12f, _ => 34f };
            int grains = 2200 + (variant % 5) * 260;
            int blues = 650 + (variant % 4) * 180;

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.11f + ox, y * 0.11f + oy);
                    byte baseV = (byte)Mathf.Clamp(232 + n * 14f + bias, 196f, 255f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(baseV - 6f, 0f, 255f),
                        (byte)Mathf.Clamp(baseV - 1f, 0f, 255f),
                        baseV,
                        (byte)255);
                }
            }

            // Bright grains give the rolling ball something visible to rotate with.
            for (int i = 0; i < grains; i++)
            {
                int x = Random.Range(0, size);
                int y = Random.Range(0, size);
                byte v = (byte)Random.Range(248, 256);
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            for (int i = 0; i < blues; i++)
            {
                int x = Random.Range(0, size);
                int y = Random.Range(0, size);
                px[y * size + x] = new Color32((byte)(214 - blue / 3f), (byte)(228 - blue / 4f),
                                              (byte)Mathf.Min(255, 244 + blue / 4f), 255);
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>The rolling bottom ball's snow: the same speckled base as Snow(), plus a
        /// scatter of brown dirt clumps. The ball spins as it marches, so the dirt travels with
        /// it and the rotation becomes obvious to the eye.</summary>
        public static Texture2D SnowWithDirt(int variant)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "snow_dirt" + variant };
            float ox = variant * 37.31f, oy = variant * 11.77f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.11f + ox, y * 0.11f + oy);
                    byte baseV = (byte)Mathf.Clamp(232 + n * 14f, 196f, 255f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(baseV - 6f, 0f, 255f),
                        (byte)Mathf.Clamp(baseV - 1f, 0f, 255f),
                        baseV, 255);
                }
            }
            for (int i = 0; i < 2400; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                byte v = (byte)Random.Range(248, 256);
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            // A little random dirt: small brown clumps so the rolling ball's spin is visible.
            int clumps = 26 + (variant % 4) * 6;
            for (int c = 0; c < clumps; c++)
            {
                int cx = Random.Range(0, size), cy = Random.Range(0, size);
                int r = Random.Range(2, 5);
                float shade = Random.Range(70f, 130f);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int x = (cx + dx + size) % size, y = (cy + dy + size) % size;
                        float jitter = Random.Range(0.7f, 1.15f);
                        px[y * size + x] = new Color32(
                            (byte)Mathf.Clamp(shade * 1.25f * jitter, 0f, 255f),
                            (byte)Mathf.Clamp(shade * 0.85f * jitter, 0f, 255f),
                            (byte)Mathf.Clamp(shade * 0.55f * jitter, 0f, 255f), 255);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>A chunkier, sparklier snow for the thrown snowball: a bright base with
        /// dense grain, visible clumps and blue-ish crevice shading, so the bullet reads as
        /// packed snow rather than a flat white ball.</summary>
        public static Texture2D SnowballSnow()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "snowball_tex" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                    float clump = Mathf.PerlinNoise(x * 0.22f + 40f, y * 0.22f + 40f);
                    byte b = (byte)Mathf.Clamp(226f + n * 20f + clump * 14f, 190f, 255f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(b - 4f, 0f, 255f),
                        (byte)Mathf.Clamp(b - 1f, 0f, 255f),
                        b, 255);
                }
            }
            for (int i = 0; i < 5200; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                byte v = (byte)Random.Range(250, 256);
                px[y * size + x] = new Color32(v, v, v, 255);
            }
            for (int i = 0; i < 1600; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                px[y * size + x] = new Color32(206, 220, 240, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>Soft radial dot, used for snow flakes and cloud puffs.</summary>
        public static Texture2D SoftCircle(int size, float hardness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "soft_dot" };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, Mathf.Max(0.2f, hardness));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>A fluffy cloud built from overlapping soft puffs.</summary>
        public static Texture2D Cloud()
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "cloud_tex" };
            var acc = new float[w * h];
            for (int i = 0; i < 26; i++)
            {
                float cx = Random.Range(0.18f, 0.82f) * w;
                float cy = Random.Range(0.30f, 0.70f) * h;
                float rad = Random.Range(14f, 34f);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float dx = x - cx, dy = (y - cy) * 1.35f;
                        float d2 = dx * dx + dy * dy;
                        if (d2 > rad * rad) continue;
                        float t = 1f - Mathf.Sqrt(d2) / rad;
                        acc[y * w + x] += t * t;
                    }
                }
            }
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                float a = Mathf.Clamp01(acc[i] * 1.15f);
                px[i] = new Color32(255, 255, 255, (byte)(a * 210f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>Opaque white square, the workhorse sprite for UI shapes.</summary>
        public static Texture2D SolidWhite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "white" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        public static Sprite WhiteSprite()
        {
            var tex = SolidWhite();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, 16f);
        }

        public static Sprite CircleSprite(float hardness)
        {
            var tex = SoftCircle(64, hardness);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, 64f);
        }

        /// <summary>A fluffy cloud sprite built from the overlapping-puff texture.</summary>
        public static Sprite CloudSprite()
        {
            var tex = Cloud();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, 128f);
        }
    }

    /// <summary>
    /// Creates the handful of materials the game needs. All of them are instances of the
    /// URP Lit shader; transparency is switched on through the shader's property block.
    /// </summary>
    public static class Mat
    {
        static Shader s_Lit;
        static Shader s_Unlit;

        public static Shader LitShader
        {
            get
            {
                if (s_Lit == null)
                    s_Lit = Find("Universal Render Pipeline/Lit",
                                "Universal Render Pipeline/Simple Lit",
                                "Diffuse");
                return s_Lit;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (s_Unlit == null)
                    s_Unlit = Find("Universal Render Pipeline/Unlit",
                                  "Universal Render Pipeline/Particles/Unlit",
                                  "Universal Render Pipeline/Lit",
                                  "Diffuse");
                return s_Unlit;
            }
        }

        static Shader Find(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Diffuse");
        }

        public static Material Opaque(Color color, bool unlit = false)
        {
            var m = new Material(unlit ? UnlitShader : LitShader) { name = "mat_opaque" };
            m.color = color;
            m.SetColor("_BaseColor", color);
            return m;
        }

        public static Material Textured(Texture2D baseMap, Color tint, bool unlit = false)
        {
            var m = Opaque(tint, unlit);
            m.mainTexture = baseMap;
            m.SetTexture("_BaseMap", baseMap);
            m.name = "mat_textured";
            return m;
        }

        /// <summary>
        /// A transparent copy of a material. The URP Lit shader exposes its blending through
        /// the hidden properties _Surface / _SrcBlend / _DstBlend / _ZWrite.
        /// </summary>
        public static Material MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 2f); // cull back faces so snow balls read as solid, not see-through
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            return m;
        }

        public static Material Transparent(Color color, bool unlit = false)
        {
            var m = Opaque(color, unlit);
            m.name = "mat_transparent";
            return MakeTransparent(m);
        }

        public static Material SnowMaterial(int variant = 0)
        {
            var tex = TextureFactory.Snow(variant);
            tex.wrapMode = TextureWrapMode.Repeat;
            // A whisper of per-variant tint so the variants separate even at a glance.
            Color tint = (variant % 4) switch
            {
                0 => GameConfig.SnowWhite,
                1 => new Color(0.93f, 0.96f, 1.00f, 1f),
                2 => new Color(1.00f, 0.99f, 0.96f, 1f),
                _ => new Color(0.96f, 0.98f, 1.00f, 1f)
            };
            var m = Textured(tex, tint);
            m.SetFloat("_Smoothness", 0.15f);
            m.name = "mat_snow" + variant;
            return m;
        }

        /// <summary>The rolling bottom ball's material: speckled snow with scattered dirt so
        /// its rotation reads to the eye while the snowman marches.</summary>
        public static Material SnowMaterialDirt(int variant = 0)
        {
            var tex = TextureFactory.SnowWithDirt(variant);
            tex.wrapMode = TextureWrapMode.Repeat;
            var m = Textured(tex, GameConfig.SnowWhite);
            m.SetFloat("_Smoothness", 0.15f);
            m.name = "mat_snow_dirt" + variant;
            return m;
        }

        /// <summary>An unlit, alpha-blended decal material for text labels on the cannon.</summary>
        public static Material LabelDecal(Texture2D tex, Color color)
        {
            var m = new Material(UnlitShader) { name = "mat_label" };
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", color);
            m.color = color;
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3001;
            return m;
        }
    }

    /// <summary>
    /// All sound is synthesised at start-up, so the game is audible without shipping a
    /// single audio file. Drop real clips into Assets/Audio later and they take over.
    /// </summary>
    public static class AudioFactory
    {
        const int Rate = 44100;

        public static AudioClip Throw()
        {
            const float dur = 0.20f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            float lastNoise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = i / (float)n;
                float env = Mathf.Pow(1f - k, 2.2f);
                float white = Random.Range(-1f, 1f);
                lastNoise = lastNoise * 0.72f + white * 0.28f;          // muffled whoosh
                float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1150f, 240f, k) * t);
                data[i] = env * (lastNoise * 0.62f + sweep * 0.30f);
            }
            return Clip("throw", data);
        }

        /// <summary>A pool of comic screams; one is picked at random every hit.</summary>
        public static AudioClip[] Screams()
        {
            var list = new List<AudioClip>();
            float[] bases = { 320f, 430f, 250f, 560f };
            float[] peaks = { 1.9f, 1.45f, 2.3f, 1.7f };
            for (int v = 0; v < bases.Length; v++)
            {
                float dur = Random.Range(0.34f, 0.55f);
                int n = (int)(Rate * dur);
                var data = new float[n];
                float baseFreq = bases[v];
                float peak = peaks[v];
                float vib = Random.Range(6f, 14f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float k = i / (float)n;
                    float pitch = baseFreq * Mathf.Lerp(1f, peak, Mathf.Sin(k * Mathf.PI)) *
                                (1f + 0.10f * Mathf.Sin(2f * Mathf.PI * vib * t));
                    float phase = 2f * Mathf.PI * pitch * t;
                    float s = Mathf.Sin(phase) * 0.60f
                            + Mathf.Sin(phase * 2f) * 0.22f
                            + Mathf.Sin(phase * 3.1f) * 0.12f;
                    float env = Mathf.Pow(Mathf.Sin(k * Mathf.PI), 0.65f);
                    data[i] = s * env * 0.55f;
                }
                list.Add(Clip("scream_" + v, data));
            }
            return list.ToArray();
        }

        /// <summary>A short, cheerful, seamlessly loopable tune.</summary>
        public static AudioClip Music()
        {
            float[] notes = { 262f, 330f, 392f, 330f, 440f, 392f, 330f, 294f };
            const float noteDur = 0.34f;
            int per = (int)(Rate * noteDur);
            int n = per * notes.Length;
            var data = new float[n];
            for (int s = 0; s < notes.Length; s++)
            {
                float f = notes[s];
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)Rate;
                    float k = i / (float)per;
                    float env = Mathf.Min(1f, k * 12f) * Mathf.Pow(1f - k, 1.4f);
                    float tone = Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f
                               + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.16f;
                    data[s * per + i] = tone * env * 0.22f;
                }
            }
            return Clip("music", data);
        }

        static AudioClip Clip(string name, float[] data)
        {
            // 6.6 signature: Create(name, lengthSamples, channels, frequency, stream).
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
