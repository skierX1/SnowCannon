using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// A snowman: three stacked snow balls, coal eyes, a carrot nose, a pot hat and two
    /// branch arms with hands. The bottom ball rolls while the stack advances. One hit
    /// destroys the whole thing: the parts tumble to the ground and fade out over two seconds.
    /// </summary>
    public sealed class Snowman : MonoBehaviour
    {
        // Local geometry, before the per-instance scale is applied.
        const float Rb = 0.62f;
        const float Rm = 0.46f;
        const float Rt = 0.34f;
        const float Yb = Rb;
        const float Ym = Rb + Rm * 0.95f;
        const float Yt = Ym + Rm * 0.92f + Rt * 0.92f;

        public float Speed { get; private set; }
        public bool IsDying { get; private set; }
        public bool IsDone { get; private set; }

        /// <summary>World Y of the head centre, used to decide head vs body hits.</summary>
        public float HeadWorldY { get; private set; }

        /// <summary>Every live snowman, so two of them can detect and bounce off each other
        /// without the game having to hand the whole list to each Update.</summary>
        static readonly List<Snowman> all = new List<Snowman>();

        Transform bottomBall;
        readonly List<Chunk> chunks = new List<Chunk>();
        float fadeTimer;
        float size = 1f;
        float swayPhase;

        // Travel heading, in degrees off straight-ahead (0 = straight at the camera, + = to the
        // right). Chosen at random up to MaxAngle and reflected off the side walls and off other
        // snowmen, so the march is a random diagonal that always stays inside the field.
        float headingDeg;
        float maxAngle = 30f;

        sealed class Chunk
        {
            public Transform transform;
            public Material material;
            public Vector3 velocity;
            public Vector3 angularVelocity;
            public bool detached;
            public float baseAlpha = 1f;
        }

        /// <summary>Builds a snowman and returns its root. The caller owns it.</summary>
        public static Snowman Spawn(float size, float speed, int textureVariant = -1, float maxAngleDeg = 30f)
        {
            var go = new GameObject("Snowman");
            var sm = go.AddComponent<Snowman>();
            sm.size = Mathf.Max(0.4f, size);
            sm.Speed = speed;
            // Each snowman picks its own snow look so the field never looks cloned.
            sm.textureVariant = textureVariant >= 0 ? textureVariant : Random.Range(0, 6);
            // A random diagonal heading, up to maxAngleDeg off straight-ahead.
            sm.maxAngle = Mathf.Clamp(maxAngleDeg, 0f, 30f);
            sm.headingDeg = Random.Range(-sm.maxAngle, sm.maxAngle);
            sm.Build();
            all.Add(sm);
            return sm;
        }

        int textureVariant;

        void Build()
        {
            var snow = Mat.SnowMaterial(textureVariant);
            Register(snow);
            // The rolling bottom ball gets its own dirtier skin: the scattered dirt clumps
            // travel with the spin, so the ball's rotation is visible as it marches.
            var snowDirty = Mat.SnowMaterialDirt(textureVariant);
            Register(snowDirty);

            // The three stacked balls. The lowest one is the rolling wheel.
            bottomBall = AddSphere("ball_bottom", snowDirty, new Vector3(0, Yb, 0), Rb * 2f);
            AddSphere("ball_middle", snow, new Vector3(0, Ym, 0), Rm * 2f);
            var head = AddSphere("ball_head", snow, new Vector3(0, Yt, 0), Rt * 2f);

            var coal = Mat.Opaque(GameConfig.CoalBlack);
            Register(coal);
            var carrot = Mat.Opaque(GameConfig.CarrotOrange);
            Register(carrot);
            var branch = Mat.Opaque(GameConfig.BranchBrown);
            Register(branch);
            // Every snowman wears a randomly coloured bucket, so the field stays varied.
            var pot = Mat.Opaque(GameConfig.PotColors[Random.Range(0, GameConfig.PotColors.Length)]);
            Register(pot);

            // Face points toward the camera, i.e. toward -Z.
            float eye = Rt * 0.34f;
            AddSphere("eye_l", coal, new Vector3(-Rt * 0.42f, Yt + Rt * 0.22f, -Rt * 0.86f), eye);
            AddSphere("eye_r", coal, new Vector3(Rt * 0.42f, Yt + Rt * 0.22f, -Rt * 0.86f), eye);

            // Carrot nose: a cone sticking out of the face.
            var nose = AddMeshPart("nose", MeshFactory.Cone(Rt * 0.22f, Rt * 0.95f, 10), carrot,
                                   new Vector3(0, Yt + Rt * 0.02f, -Rt * 0.92f), Vector3.one,
                                   new Vector3(-90f, 0f, 0f));
            nose.localScale = new Vector3(1f, 1f, 1f);

            // Coal mouth dots.
            for (int i = -2; i <= 2; i++)
            {
                AddSphere("mouth" + i, coal,
                          new Vector3(i * Rt * 0.24f, Yt - Rt * 0.34f, -Rt * 0.82f),
                          Rt * 0.10f);
            }

            // Pot hat: a bucket worn upside down, so the wide mouth faces up.
            float potBase = Yt + Rt * 0.86f;
            AddMeshPart("pot", MeshFactory.TruncatedCone(Rt * 0.55f, Rt * 0.86f, Rt * 0.9f, 14), pot,
                        new Vector3(0, potBase, 0), Vector3.one, Vector3.zero);
            AddMeshPart("pot_rim", MeshFactory.TruncatedCone(Rt * 0.92f, Rt * 0.86f, Rt * 0.16f, 14), pot,
                        new Vector3(0, potBase + Rt * 0.86f, 0), Vector3.one, Vector3.zero);

            // Branch arms with hands, rooted in the middle ball and angled up and outward.
            BuildArm("arm_l", branch, new Vector3(-Rm * 0.5f, Ym + Rm * 0.1f, 0), -1f);
            BuildArm("arm_r", branch, new Vector3(Rm * 0.5f, Ym + Rm * 0.1f, 0), 1f);

            // One capsule over the whole stack so a snowball's sphere-cast can actually connect.
            // The individual balls are decorative and have their colliders removed.
            float top = Yt + Rt;
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = Rb * 0.92f;
            col.height = top;
            col.center = new Vector3(0f, top * 0.5f, 0f);
            col.direction = 1; // upright along Y

            transform.localScale = Vector3.one * size;
            HeadWorldY = transform.position.y + Yt * size;
        }

        void BuildArm(string name, Material mat, Vector3 shoulder, float side)
        {
            const float twigLen = 0.82f;
            float rad = 52f * Mathf.PI / 180f;
            Vector3 dir = new Vector3(side * Mathf.Sin(rad), Mathf.Cos(rad), 0f); // outward-up

            // Main twig. TruncatedCone stands on y = 0, so its base sits exactly on the pivot
            // (the shoulder, which is inside the middle ball) and it visibly grows out of the ball.
            AddMeshPart(name, MeshFactory.TruncatedCone(0.05f, 0.03f, twigLen, 7), mat,
                        shoulder, Vector3.one, new Vector3(0f, 0f, -side * 52f));

            // The twig's true far end, so the hand lands on the branch tip rather than beside it.
            Vector3 tip = shoulder + dir * twigLen;

            // A small hand blob on the tip, then two finger twigs sprouting from it.
            AddSphere(name + "_hand", mat, tip, 0.17f);
            for (int f = 0; f < 2; f++)
            {
                float ang = -side * (28f + f * 40f);
                AddMeshPart(name + "_f" + f, MeshFactory.TruncatedCone(0.03f, 0.018f, 0.26f, 6), mat,
                            tip, Vector3.one, new Vector3(0f, 0f, ang));
            }
        }

        static void OffsetMesh(MeshFilter filter, Vector3 offset)
        {
            var mesh = filter.sharedMesh;
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] += offset;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        Transform AddSphere(string name, Material mat, Vector3 localPos, float diameter)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = name;
            p.transform.SetParent(transform, false);
            p.transform.localPosition = localPos;
            p.transform.localScale = Vector3.one * diameter;
            var r = p.GetComponent<MeshRenderer>();
            r.material = mat;
            Destroy(p.GetComponent<Collider>());
            Track(p, mat);
            return p.transform;
        }

        Transform AddMeshPart(string name, Mesh mesh, Material mat, Vector3 localPos,
                              Vector3 scale, Vector3 localEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = localEuler;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = mat;
            Track(go, mat);
            return go.transform;
        }

        void Track(GameObject go, Material mat)
        {
            chunks.Add(new Chunk { transform = go.transform, material = mat });
        }

        void Register(Material m)
        {
            // Snowmen fade out on death, so every material must already be able to blend.
            Mat.MakeTransparent(m);
        }

        void Update()
        {
            if (IsDying)
            {
                UpdateDeath();
                return;
            }

            var p = transform.position;

            // March along the current heading: straight toward the camera, angled up to 30 deg
            // left or right, so the path is a random diagonal that uses the whole field width.
            float rad = headingDeg * (Mathf.PI / 180f);
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad));
            p += dir * (Speed * Time.deltaTime);

            // Bounce off the side walls so a snowman always stays inside the screen.
            float limit = GameConfig.FieldHalfWidth - 0.6f * size;
            if (p.x <= -limit) { p.x = -limit; headingDeg = -headingDeg; }
            else if (p.x >= limit) { p.x = limit; headingDeg = -headingDeg; }

            transform.position = p;
            HeadWorldY = p.y + Yt * size;

            // Two snowmen that meet on the way down shove apart and both bounce off, so they
            // never end up overlapping.
            ResolveCollisions();

            // A gentle waddle, plus a slight lean into the direction of travel.
            swayPhase += Time.deltaTime * 3.4f;
            float yaw = Mathf.Sin(swayPhase) * 9.1f - headingDeg * 0.6f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // The bottom ball rolls: omega = v / r, about the axis perpendicular to travel.
            if (bottomBall != null)
            {
                // For a ball rolling on the ground (up = +Y) toward `dir`, the no-slip spin axis
                // is up x dir = (dir.z, 0, -dir.x); spinning a positive angle about that axis
                // carries the top of the ball forward, i.e. the ball rolls the way it travels.
                Vector3 rollAxis = new Vector3(dir.z, 0f, -dir.x);
                if (rollAxis.sqrMagnitude > 0.0001f)
                {
                    // omega = v / r, converted to degrees per frame (Mathf has no Rad2Deg in 6.6).
                    float omegaDeg = (Speed / (Rb * size)) * Time.deltaTime * (180f / Mathf.PI);
                    bottomBall.Rotate(rollAxis.normalized, omegaDeg, Space.World);
                }
            }
        }

        /// <summary>Separates this snowman from any other it has driven into and reflects both
        /// headings, so colliding snowmen bounce out instead of overlapping.</summary>
        void ResolveCollisions()
        {
            float rA = Rb * size;
            var pa = transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o == this || o.IsDying || o.IsDone) continue;

                var pb = o.transform.position;
                float dx = pa.x - pb.x, dz = pa.z - pb.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float minDist = rA + Rb * o.size;
                if (dist >= minDist) continue;

                // Perfectly stacked: nudge apart along a random sideways axis.
                if (dist < 0.0001f)
                {
                    dx = Random.Range(-1f, 1f); dz = Random.Range(-1f, 1f);
                    dist = Mathf.Max(0.0001f, Mathf.Sqrt(dx * dx + dz * dz));
                }

                float nx = dx / dist, nz = dz / dist;
                float push = (minDist - dist) * 0.5f;
                pa.x += nx * push; pa.z += nz * push;
                var ob = o.transform.position;
                ob.x -= nx * push; ob.z -= nz * push;
                o.transform.position = ob;

                // Both turn around and head off in their own new diagonal.
                headingDeg = -headingDeg;
                o.headingDeg = -o.headingDeg;
            }
            transform.position = pa;
        }

        /// <summary>Called by the game when a snowball connects. Head hits score more.</summary>
        public bool Hit(Vector3 hitPoint, out int points)
        {
            points = 0;
            if (IsDying) return false;

            points = hitPoint.y >= HeadWorldY - Rt * size * 0.45f
                       ? GameConfig.PointsHead
                       : GameConfig.PointsBody;

            IsDying = true;
            fadeTimer = 0f;

            // Detach every part and fling it so the stack visibly collapses.
            foreach (var c in chunks)
            {
                c.detached = true;
                c.transform.SetParent(null, true);
                c.velocity = new Vector3(Random.Range(-1.6f, 1.6f),
                                         Random.Range(1.4f, 3.4f),
                                         Random.Range(-0.6f, 2.2f));
                c.angularVelocity = new Vector3(Random.Range(-260f, 260f),
                                                Random.Range(-260f, 260f),
                                                Random.Range(-260f, 260f));
            }
            return true;
        }

        void UpdateDeath()
        {
            fadeTimer += Time.deltaTime;
            float k = Mathf.Clamp01(fadeTimer / GameConfig.DeathFadeTime);
            float alpha = 1f - k;

            foreach (var c in chunks)
            {
                if (c.material == null) continue;
                var col = c.material.color;
                col.a = c.baseAlpha * alpha;
                c.material.color = col;

                if (!c.detached) continue;

                c.velocity.y -= 14f * Time.deltaTime;
                c.transform.position += c.velocity * Time.deltaTime;
                c.transform.Rotate(c.angularVelocity * Time.deltaTime, Space.Self);

                // Settle on the ground instead of sinking through it.
                if (c.transform.position.y < 0.12f)
                {
                    var p = c.transform.position;
                    p.y = 0.12f;
                    c.transform.position = p;
                    c.velocity = Vector3.zero;
                    c.angularVelocity = Vector3.zero;
                }
            }

            if (k >= 1f) IsDone = true;
        }

        void OnDestroy()
        {
            all.Remove(this);
            foreach (var c in chunks)
            {
                if (c.material != null) Destroy(c.material);
            }
        }
    }
}
