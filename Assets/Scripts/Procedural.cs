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

        /// <summary>A smooth, welded open cylinder running along the Z axis, centred on z = 0,
        /// of the given radius and length. Vertices are shared between quads and the normals are
        /// the true radial directions, so the surface shades as a round tube rather than a ring
        /// of flat facets. No end caps (the barrel is open at both ends).</summary>
        public static Mesh Tube(float radius, float length, int segments, int rings)
        {
            segments = Mathf.Max(8, segments);
            rings = Mathf.Max(2, rings);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float half = length * 0.5f;

            for (int r = 0; r <= rings; r++)
            {
                float z = -half + length * (r / (float)rings);
                for (int s = 0; s <= segments; s++)
                {
                    float a = (s / (float)segments) * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    verts.Add(new Vector3(c * radius, sn * radius, z));
                    normals.Add(new Vector3(c, sn, 0f));
                    uvs.Add(new Vector2(s / (float)segments, r / (float)rings));
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

            var mesh = new Mesh { name = "tube" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>A flat annulus (ring) lying in the XZ plane at y=0, with the given inner and
        /// outer radii. Used for the ground landing reticle and the banner's aura ring. Normals face
        /// up; callers that need it visible from both sides should disable culling on its material.</summary>
        public static Mesh FlatRing(float innerR, float outerR, int segments)
        {
            segments = Mathf.Max(8, segments);
            if (outerR < innerR) { float t = innerR; innerR = outerR; outerR = t; }
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = (s / (float)segments) * Mathf.PI * 2f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                verts.Add(new Vector3(c * innerR, 0f, sn * innerR));
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(s / (float)segments, 0f));
                verts.Add(new Vector3(c * outerR, 0f, sn * outerR));
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(s / (float)segments, 1f));
            }
            for (int s = 0; s < segments; s++)
            {
                int a0 = s * 2, b0 = s * 2 + 1, c0 = (s + 1) * 2, e0 = (s + 1) * 2 + 1;
                tris.Add(a0); tris.Add(b0); tris.Add(c0);
                tris.Add(b0); tris.Add(e0); tris.Add(c0);
            }
            var m = new Mesh { name = "flatring" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A copy of a (unit-ish) sphere mesh whose surface is pushed in and out along
        /// its normals by a smooth, seed-stable noise, so the snow balls read as hand-rolled
        /// lumps rather than perfect spheres. The source mesh is never modified.</summary>
        public static Mesh LumpySphere(Mesh src, float amp, float seed)
        {
            var verts = (Vector3[])src.vertices.Clone();
            var norms = (Vector3[])src.normals.Clone();
            var uvs = src.uv;
            var tris = src.triangles;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                Vector3 n = norms[i];
                float d = amp * (
                    Mathf.Sin(v.x * 4.1f + seed) * Mathf.Sin(v.y * 3.7f + seed * 1.7f) * Mathf.Sin(v.z * 4.3f + seed * 0.6f)
                  + 0.5f * Mathf.Sin(v.x * 7.3f + seed * 2.3f) * Mathf.Sin(v.z * 6.7f + seed * 1.1f));
                verts[i] = v + n * d;
            }

            var m = new Mesh { name = "lumpy_sphere" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, false);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>A smooth UV-sphere of radius 0.5 (same footprint as the built-in primitive
        /// sphere, so a localScale of `diameter` still yields radius diameter/2). Built here so
        /// the lumpy snow balls never have to read the built-in mesh's vertices, which are not
        /// always readable.</summary>
        public static Mesh UVSphere(int rings = 16, int segments = 24)
        {
            rings = Mathf.Max(4, rings);
            segments = Mathf.Max(6, segments);
            const float rad = 0.5f;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * (r / (float)rings);          // 0..PI, pole to pole
                float sy = Mathf.Cos(phi);
                float rr = Mathf.Sin(phi);
                for (int s = 0; s <= segments; s++)
                {
                    float th = Mathf.PI * 2f * (s / (float)segments);
                    float nx = rr * Mathf.Cos(th);
                    float nz = rr * Mathf.Sin(th);
                    // (nx, sy, nz) is already a unit direction, so scale by the radius directly.
                    verts.Add(new Vector3(nx * rad, sy * rad, nz * rad));
                    normals.Add(new Vector3(nx, sy, nz));
                    uvs.Add(new Vector2(s / (float)segments, r / (float)rings));
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

            var m = new Mesh { name = "uv_sphere" };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>A flat disc in the XZ plane (y = 0) built from a centre vertex plus concentric
        /// rings, so its vertices can be pushed up and down each frame to make a rippling water
        /// surface. Double-sided materials are recommended (the winding is not guaranteed).</summary>
        public static Mesh WaveDisc(float radius, int rings, int segments)
        {
            rings = Mathf.Max(1, rings);
            segments = Mathf.Max(6, segments);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            verts.Add(Vector3.zero);
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * (r / (float)rings);
                for (int s = 0; s < segments; s++)
                {
                    float th = Mathf.PI * 2f * (s / (float)segments);
                    float cx = Mathf.Cos(th), sz = Mathf.Sin(th);
                    verts.Add(new Vector3(cx * rr, 0f, sz * rr));
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(0.5f + cx * 0.5f, 0.5f + sz * 0.5f));
                }
            }

            for (int s = 0; s < segments; s++)
            {
                int a = 1 + s;
                int b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int r = 1; r < rings; r++)
            {
                int cur = 1 + (r - 1) * segments;
                int nxt = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    int a = cur + s, b = cur + s2, c = nxt + s, e = nxt + s2;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }

            var m = new Mesh { name = "wave_disc" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A smooth tube swept along a polyline path, used for the water hose. Each path
        /// point gets a radial ring of vertices oriented by a stable frame, and consecutive rings
        /// are stitched with quads.</summary>
        public static Mesh TubeAlongPath(List<Vector3> pts, float radius, int radialSegs)
        {
            radialSegs = Mathf.Max(4, radialSegs);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int n = pts.Count;
            if (n < 2) return new Mesh { name = "tube_path_empty" };

            for (int i = 0; i < n; i++)
            {
                Vector3 p = pts[i];
                Vector3 tangent;
                if (i == 0) tangent = pts[1] - pts[0];
                else if (i == n - 1) tangent = pts[n - 1] - pts[n - 2];
                else tangent = pts[i + 1] - pts[i - 1];
                if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
                tangent.Normalize();

                Vector3 refUp = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                Vector3 normal = Vector3.Cross(refUp, tangent).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

                for (int s = 0; s <= radialSegs; s++)
                {
                    float th = Mathf.PI * 2f * (s / (float)radialSegs);
                    Vector3 dir = normal * Mathf.Cos(th) + binormal * Mathf.Sin(th);
                    verts.Add(p + dir * radius);
                    norms.Add(dir);
                    uvs.Add(new Vector2(s / (float)radialSegs, i / (float)(n - 1)));
                }
            }

            int ring = radialSegs + 1;
            for (int i = 0; i < n - 1; i++)
            {
                for (int s = 0; s < radialSegs; s++)
                {
                    int a = i * ring + s, b = a + 1, c = a + ring, e = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }

            var mesh = new Mesh { name = "tube_path" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
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

        /// <summary>A smooth, seed-stable radial multiplier (periodic in the angle) that turns the
        /// pond's circular parts into one organic, blobby outline. Every pond piece shares the same
        /// seed so their edges nest perfectly, giving an irregular, natural shoreline.</summary>
        public static float OrganicRadius(float angle, float seed, float amp)
        {
            return 1f
                 + amp * Mathf.Sin(2f * angle + seed)
                 + amp * 0.55f * Mathf.Sin(3f * angle + seed * 1.7f + 1.3f)
                 + amp * 0.35f * Mathf.Sin(5f * angle + seed * 0.6f + 2.1f);
        }

        /// <summary>A flat, blobby disc in the XZ plane (y = 0) whose radius follows the organic
        /// profile. Built from a centre vertex plus concentric rings so its vertices can be pushed
        /// up and down each frame to ripple the water.</summary>
        public static Mesh WaveDiscOrganic(float radius, int rings, int segments, float seed, float amp)
        {
            rings = Mathf.Max(1, rings);
            segments = Mathf.Max(8, segments);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            verts.Add(Vector3.zero); norms.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * (r / (float)rings);
                for (int s = 0; s < segments; s++)
                {
                    float th = Mathf.PI * 2f * (s / (float)segments);
                    float k = OrganicRadius(th, seed, amp);
                    float cx = Mathf.Cos(th), sz = Mathf.Sin(th);
                    verts.Add(new Vector3(cx * rr * k, 0f, sz * rr * k));
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(0.5f + cx * 0.5f, 0.5f + sz * 0.5f));
                }
            }
            for (int s = 0; s < segments; s++)
            {
                int a = 1 + s;
                int b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int r = 1; r < rings; r++)
            {
                int cur = 1 + (r - 1) * segments;
                int nxt = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    int a = cur + s, b = cur + s2, c = nxt + s, e = nxt + s2;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }
            }
            var m = new Mesh { name = "wave_disc_organic" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A welded, blobby bowl wall (open tube) from a bottom radius to a top radius,
        /// its outline following the organic profile, optionally capped at either end. Normals are
        /// recomputed so the curved wall shades smoothly.</summary>
        public static Mesh TruncatedConeOrganic(float radiusBottom, float radiusTop, float height,
            int segments, float seed, float amp, bool capBottom, bool capTop)
        {
            segments = Mathf.Max(12, segments);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int rings = 3;

            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float y = height * t;
                float rad = Mathf.Lerp(radiusBottom, radiusTop, t);
                for (int s = 0; s <= segments; s++)
                {
                    float th = Mathf.PI * 2f * (s / (float)segments);
                    float k = OrganicRadius(th, seed, amp);
                    verts.Add(new Vector3(Mathf.Cos(th) * rad * k, y, Mathf.Sin(th) * rad * k));
                    norms.Add(Vector3.up);
                    uvs.Add(new Vector2(s / (float)segments, t));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, e = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(e);
                }

            if (capBottom) AddOrganicCap(verts, norms, uvs, tris, radiusBottom, 0f, seed, amp, segments, false);
            if (capTop) AddOrganicCap(verts, norms, uvs, tris, radiusTop, height, seed, amp, segments, true);

            var m = new Mesh { name = "cone_organic" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0, false);
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        static void AddOrganicCap(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            float radius, float y, float seed, float amp, int segments, bool up)
        {
            int c = verts.Count;
            Vector3 n = up ? Vector3.up : Vector3.down;
            verts.Add(new Vector3(0f, y, 0f)); norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s < segments; s++)
            {
                float th0 = Mathf.PI * 2f * (s / (float)segments);
                float th1 = Mathf.PI * 2f * ((s + 1) / (float)segments);
                float k0 = OrganicRadius(th0, seed, amp), k1 = OrganicRadius(th1, seed, amp);
                verts.Add(new Vector3(Mathf.Cos(th0) * radius * k0, y, Mathf.Sin(th0) * radius * k0));
                norms.Add(n); uvs.Add(new Vector2(0.5f + Mathf.Cos(th0) * 0.5f, 0.5f + Mathf.Sin(th0) * 0.5f));
                verts.Add(new Vector3(Mathf.Cos(th1) * radius * k1, y, Mathf.Sin(th1) * radius * k1));
                norms.Add(n); uvs.Add(new Vector2(0.5f + Mathf.Cos(th1) * 0.5f, 0.5f + Mathf.Sin(th1) * 0.5f));
                if (up) { tris.Add(c); tris.Add(c + 1 + s * 2); tris.Add(c + 2 + s * 2); }
                else { tris.Add(c); tris.Add(c + 2 + s * 2); tris.Add(c + 1 + s * 2); }
            }
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

        /// <summary>The snow field's ground texture: a speckled off-white snow base broken up by
        /// a few soft, low-frequency dirt patches and a light scatter of tiny brown grains, so the
        /// field is not a flat uniform white but still clearly reads as snow.</summary>
        public static Texture2D GroundSnow()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ground_snow_tex" };
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                    byte baseV = (byte)Mathf.Clamp(230 + n * 16f, 200f, 255f);
                    Color c = new Color32(
                        (byte)Mathf.Clamp(baseV - 6f, 0f, 255f),
                        (byte)Mathf.Clamp(baseV - 1f, 0f, 255f),
                        baseV, 255);

                    // A very sparse dirt mask: only the deepest low-frequency dips show a faint
                    // brown hint, so the field reads as clean snow with just a whisper of grit.
                    float dirt = Mathf.PerlinNoise(x * 0.017f + 40f, y * 0.017f + 12f);
                    if (dirt < 0.26f)
                    {
                        float k = Mathf.Clamp01((0.26f - dirt) / 0.26f) * 0.16f;
                        Color brown = new Color(0.55f, 0.45f, 0.34f, 1f);
                        c = Color.Lerp(c, brown, k);
                    }

                    px[y * size + x] = c;
                }
            }

            // A faint scatter of tiny cool grains so up close it is not perfectly flat, kept
            // light and bright so the overall field stays snow-white.
            for (int i = 0; i < 320; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                float shade = Random.Range(196f, 226f);
                float jitter = Random.Range(0.94f, 1.04f);
                px[y * size + x] = new Color32(
                    (byte)Mathf.Clamp(shade * 1.02f * jitter, 0f, 255f),
                    (byte)Mathf.Clamp(shade * 1.01f * jitter, 0f, 255f),
                    (byte)Mathf.Clamp(shade * 1.0f * jitter, 0f, 255f), 255);
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>The lake's inner basin + shoreline dressing: a bright, warm-white gravel.
        /// A near-white base with cool shadow crevices and a dense scatter of small rounded
        /// pebbles (each with a lit top and a shaded bottom) so the pond floor and shore read as
        /// clean pale stones rather than the old dark grey basin.</summary>
        public static Texture2D Gravel()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "gravel_tex" };
            var px = new Color32[size * size];

            // A bright, slightly warm off-white base broken by low-frequency grey crevice shading.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                    float crevice = Mathf.PerlinNoise(x * 0.14f + 30f, y * 0.14f + 8f);
                    // baseV: high (bright) where crevice is high, darker in the deep crevices.
                    float baseV = Mathf.Lerp(196f, 246f, crevice) + n * 10f;
                    baseV = Mathf.Clamp(baseV, 150f, 252f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(baseV * 1.005f, 0f, 255f),
                        (byte)Mathf.Clamp(baseV * 1.005f, 0f, 255f),
                        (byte)Mathf.Clamp(baseV, 0f, 255f), 255);
                }
            }

            // A dense scatter of small rounded pebbles: a lit crown and a soft shaded base, in
            // cool greys with a whisper of warm, so the surface reads as packed pale stones.
            for (int i = 0; i < 520; i++)
            {
                int cx = Random.Range(2, size - 2);
                int cy = Random.Range(2, size - 2);
                int r = Random.Range(2, 6);
                float tone = Random.Range(176f, 236f);
                bool warm = Random.value < 0.28f;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float d2 = dx * dx + dy * dy;
                        if (d2 > r * r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        // Lit toward the upper-left, shaded toward the lower-right.
                        float light = 1f - (dx + dy) / (float)(2 * r) * 0.5f;
                        float edge = 1f - Mathf.Sqrt(d2) / r * 0.35f;
                        float v = Mathf.Clamp(tone * light * edge, 120f, 252f);
                        byte rr = (byte)Mathf.Clamp(warm ? v * 1.03f : v * 0.99f, 0f, 255f);
                        byte gg = (byte)Mathf.Clamp(v, 0f, 255f);
                        byte bb = (byte)Mathf.Clamp(warm ? v * 0.97f : v * 1.02f, 0f, 255f);
                        px[y * size + x] = new Color32(rr, gg, bb, 255);
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

        /// <summary>
        /// A pond-water texture: a deep blue base broken by lighter cyan swirls and a few bright
        /// glints, so the lake reads as moving water rather than a flat blue disc. The swirls are
        /// built from domain-warped Perlin noise (the sample point is bent by a second noise field)
        /// which gives the streaky, current-like look of the reference photo.
        /// </summary>
        public static Texture2D WaterSwirl()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "water_swirl_tex" };
            var px = new Color32[size * size];

            Color deep = new Color(0.06f, 0.28f, 0.55f, 1f);
            Color mid = new Color(0.13f, 0.46f, 0.74f, 1f);
            Color light = new Color(0.55f, 0.82f, 0.95f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x * 0.05f, fy = y * 0.05f;
                    // Domain warp: bend the sample coordinates so the bands curl into swirls.
                    float wx = Mathf.PerlinNoise(fx + 3.1f, fy + 7.7f) - 0.5f;
                    float wy = Mathf.PerlinNoise(fx + 9.2f, fy + 1.3f) - 0.5f;
                    float band = Mathf.PerlinNoise(fx + wx * 3.4f, fy + wy * 3.4f);
                    // A second, tighter ripple layer for fine current lines.
                    float ripple = Mathf.PerlinNoise(fx * 2.3f + wy * 1.6f, fy * 2.3f + wx * 1.6f);

                    Color c = Color.Lerp(deep, mid, Mathf.Clamp01(band * 1.25f - 0.12f));
                    // Lighter crests where the ripple peaks, thin and streaky.
                    float crest = Mathf.Clamp01((ripple - 0.62f) / 0.38f);
                    c = Color.Lerp(c, light, crest * 0.72f);

                    px[y * size + x] = c;
                }
            }

            // A sparse scatter of bright glints (sun catching the surface).
            for (int i = 0; i < 260; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                px[y * size + x] = new Color32(235, 248, 255, 255);
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
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

        /// <summary>A repeating stripe pattern for the supply hose. The V axis runs along the
        /// tube length, so scrolling the texture's V offset makes the light bands travel from the
        /// lake toward the cannon, reading as water flowing through the hose.</summary>
        public static Texture2D HoseFlow()
        {
            const int w = 16, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "hose_flow" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                // A soft travelling band along the length (V), brightening the stripe.
                float band = 0.5f + 0.5f * Mathf.Sin((y / (float)h) * Mathf.PI * 2f * 4f);
                byte bright = (byte)Mathf.Clamp(150f + band * 105f, 0f, 255f);
                for (int x = 0; x < w; x++)
                {
                    // Slightly darker at the tube silhouette edges (U) for a rounded look.
                    float edge = Mathf.Abs((x / (float)(w - 1)) * 2f - 1f);
                    byte r = (byte)(bright * (1f - edge * 0.35f));
                    byte g = (byte)(bright * (1f - edge * 0.35f));
                    byte b = (byte)(bright * (1f - edge * 0.20f));
                    px[y * w + x] = new Color32(r, g, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.wrapMode = TextureWrapMode.Repeat;
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

        /// <summary>A rounded-rectangle sprite with a soft edge, authored as a 9-slice so it can
        /// be stretched to any button size without distorting the corners.</summary>
        public static Sprite RoundedRectSprite(int size = 64, int corner = 18)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "rounded_rect_tex" };
            var px = new Color32[size * size];
            float r = corner;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from the nearest corner centre; inside the corner radius we round off.
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - r), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - r), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);   // soft 1px edge
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            // The plain overload is the reliable one in 6.6; the rounded corners stretch a touch
            // when a button is very wide, which reads fine for these pill-shaped buttons.
            return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.zero, 100f);
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

        /// <summary>Turns shadow casting on/off for every renderer under a root. Used to give the
        /// solid props a soft ground shadow while keeping transparent water and decals out of the
        /// shadow map. Uses the bool castShadows API (the ShadowCastingMode enum is not in scope
        /// in this project's assembly).</summary>
        public static void SetShadows(GameObject root, bool cast, bool receive)
        {
            if (root == null) return;
            var rs = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                rs[i].castShadows = cast;
                rs[i].receiveShadows = receive;
            }
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

        /// <summary>A looping, two-stroke scooter engine idle: a low sawtooth chug with a
        /// puttering amplitude wobble and a touch of exhaust noise. Loopable (whole number of
        /// chug cycles) so the scooter can play it continuously while it is on screen.</summary>
        public static AudioClip ScooterEngine()
        {
            const float dur = 1.0f;
            const int chugs = 22;                       // whole cycles -> seamless loop
            int n = (int)(Rate * dur);
            var data = new float[n];
            float lastNoise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float cyc = (t / dur) * chugs * Mathf.PI * 2f;   // chug phase
                float f = 92f + 26f * Mathf.Sin(cyc * 0.5f);     // wobbling idle rpm
                float saw = 2f * (t * f - Mathf.Floor(0.5f + t * f));   // -1..1 sawtooth
                float pulse = 0.55f + 0.45f * Mathf.Sin(cyc);            // puttering envelope
                float white = Random.Range(-1f, 1f);
                lastNoise = lastNoise * 0.80f + white * 0.20f;           // exhaust hiss
                float s = saw * 0.5f + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.12f + lastNoise * 0.18f;
                data[i] = s * pulse * 0.5f;
            }
            return Clip("scooter", data);
        }

        /// <summary>A short, cheerful, seamlessly loopable tune (the default track).</summary>
        public static AudioClip Music()
        {
            return BuildLoop("music", new[] { 262f, 330f, 392f, 330f, 440f, 392f, 330f, 294f }, 0.34f, 0);
        }

        /// <summary>The four background tunes the game may pick from. Each is a distinct mood —
        /// a bright sine lead, a faster triangle run, a slower minor square-wave waltz, and a
        /// bouncy pentatonic climb — and each loops seamlessly, so one can be chosen at random
        /// for every run.</summary>
        public static AudioClip[] MusicVariants()
        {
            return new[]
            {
                BuildLoop("music_0", new[] { 262f, 330f, 392f, 330f, 440f, 392f, 330f, 294f }, 0.34f, 0),
                BuildLoop("music_1", new[] { 330f, 392f, 440f, 523f, 440f, 392f, 330f, 294f, 330f, 392f }, 0.26f, 1),
                BuildLoop("music_2", new[] { 294f, 349f, 440f, 349f, 294f, 262f, 294f, 349f }, 0.40f, 2),
                BuildLoop("music_3", new[] { 392f, 440f, 523f, 587f, 523f, 440f, 392f, 330f, 392f, 440f }, 0.30f, 0),
            };
        }

        /// <summary>Renders a note list into one seamlessly loopable clip. Every note cell
        /// starts and ends at (near) silence via its attack/decay envelope, so butt-joining the
        /// cells leaves no click at the loop seam. The timbre selects the harmonic recipe.</summary>
        static AudioClip BuildLoop(string name, float[] notes, float noteDur, int timbre)
        {
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
                    float ph = 2f * Mathf.PI * f * t;
                    float tone;
                    switch (timbre)
                    {
                        case 1:   // triangle-ish: fundamental + a signed third harmonic
                            tone = Mathf.Sin(ph) * 0.5f
                               + Mathf.Sin(ph * 3f) * 0.12f * (Mathf.Sin(ph) >= 0f ? 1f : -1f);
                            break;
                        case 2:   // square-ish: odd harmonics only
                            tone = Mathf.Sin(ph) * 0.42f
                               + Mathf.Sin(ph * 3f) * 0.20f
                               + Mathf.Sin(ph * 5f) * 0.12f;
                            break;
                        default:  // bright sine + octave
                            tone = Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f) * 0.16f;
                            break;
                    }
                    data[s * per + i] = tone * env * 0.22f;
                }
            }
            return Clip(name, data);
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
