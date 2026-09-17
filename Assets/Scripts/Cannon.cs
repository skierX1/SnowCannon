using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// The player's snow cannon. It stays planted at the centre of the field and never slides
    /// across it; instead it turns in place to aim. W/A/S/D, the mouse, or the virtual stick
    /// rotate the whole cannon (yaw, left/right) and elevate the barrel (pitch), and the
    /// snowball is launched along the barrel with gravity, so the chosen angle decides whether
    /// it reaches a snowman, sails over it, or drops short.
    /// </summary>
    public sealed class Cannon : MonoBehaviour
    {
        public Vector3 MuzzleWorldPosition { get; private set; }
        public Vector3 AimDirection { get; private set; }

        /// <summary>World-space radius of the cannon's footprint, used to end the run when a
        /// snowman reaches the machine.</summary>
        public float FootprintRadius { get { return 1.5f; } }

        /// <summary>The fixed world X of the cannon's LEFTMOST position over its whole aim sweep
        /// (fully left-aimed). The lake sizes itself against this so it never overlaps the cannon
        /// no matter how the player is currently aiming — unlike reading the live position, which
        /// slides as the turret turns and would make the lake pulse in size.</summary>
        public float LeftmostX { get { return basePos.x - halfTravel; } }

        Transform muzzle;
        Transform barrelPivot;
        Transform fanBlades;
        Renderer beaconGlow;
        Light beaconLight;
        float beaconPhase;
        Vector3 basePos;
        float halfTravel = 1.8f;
        // The main camera and the FOV/aspect it was last measured against, so a screen rotation
        // (which changes the aspect, and the FOV once FitCamera re-fits) re-derives halfTravel
        // instead of keeping the value frozen at the orientation the run started in.
        Camera camRef;
        float sizedFov = -1f;
        float sizedAspect = -1f;
        float cooldown;
        // While > 0 the cannon is jammed (a Bomber's shot splattered the mechanism) and cannot fire.
        float jamTimer;
        float fanSpinDelay = 1.2f;
        float fanSpinSpeed;
        // Fan coast-down after the run ends: -1 means "still running", otherwise it is the
        // remaining seconds of the spin-down and the speed is interpolated to zero from the
        // speed captured at the moment the game-over card went up.
        const float FanSpinDownTime = 10f;
        float fanSpinDownTimer = -1f;
        float fanSpinDownFrom;
        // Once the run has ended the fan must never spin back up: the coast-down branch resets
        // fanSpinDownTimer to -1 when it finishes, which would otherwise fall the code back into
        // the spin-UP branch and re-start the fan a few seconds behind the game-over card. This
        // latch is set by StopFan and gates the spin-up branch so the fan stays stopped until the
        // next run (which builds a brand-new Cannon instance, so no reset is needed).
        bool fanStopped;

        // Fire "squash": the oversized snowball forcing its way down the narrow bore. The tube
        // walls balloon outward in a bulge that travels from the back of the barrel to the
        // muzzle (a fat ball squeezing through a tight pipe), and the whole tube dips down and
        // settles. Driven by `squash` (1 -> 0 over SquashTime).
        const float SquashTime = 0.22f;
        const float BulgeAmp = 0.34f;
        const float BulgeWidth = 0.55f;
        const float BulgeStartZ = -1.3f;
        const float BulgeEndZ = 1.25f;
        const float RecoilMax = 7f;
        float squash;
        float recoilKick;
        readonly List<BulgeElem> bulgeElems = new List<BulgeElem>();
        bool bulgeActive;

        // Smooth tube geometry: one welded, smooth-normal cylinder per wall so the barrel reads
        // as round instead of a faceted ring of boxes. The fire-squash bulge deforms it by
        // displacing its vertices radially each frame (MeshFactory.Tube + DisplaceTube).
        const float TubeLen = 2.4f;
        const float TubeCenterZ = -0.1f;
        const float TubeR = 1.18f;
        const float BoreR = 1.02f;
        const int TubeSegments = 48;
        const int TubeRings = 16;
        Mesh boreMesh;
        Vector3[] boreBase, boreScratch;
        float[] boreAngle;
        // Every tube band (grey tail, yellow body, grey muzzle) plus the dark bore liner. They are
        // all displaced together by the fire-squash bulge, each around its own centre z (zc0).
        struct TubeSeg { public Mesh mesh; public Vector3[] bas, scratch; public float[] ang; public float baseR, zc0; }
        readonly List<TubeSeg> segs = new List<TubeSeg>();

        SnowCannonGame game;

        // Barrel aim, in degrees. yaw sweeps left/right, pitch is the elevation above the
        // horizon. Both are driven by the same move vector the old driving code used.
        float yaw;
        float pitch = 16f;
        const float AimYawSpeed = 95f;
        const float AimPitchSpeed = 70f;
        // Total sweep is 160 degrees (±80). Widened from ±40 so the far-left and far-right
        // snowmen that spawn across the whole visible width are all reachable on a phone screen.
        const float MaxYaw = 80f;
        const float MinPitch = 4f;
        const float MaxPitch = 46f;

        public static Cannon Create(SnowCannonGame owner)
        {
            var go = new GameObject("Cannon");
            // The drum/pivot is authored with the bagged pedestal hanging below it (the base cube's
            // bottom sits at local y ~ -1.03), so at y=0 the whole pedestal sank under the snow.
            // Raise the pivot so the pedestal rests ON the ground plane (y=0) instead of half-buried.
            go.transform.position = new Vector3(0f, 1.0f, -5f);
            var c = go.AddComponent<Cannon>();
            c.game = owner;
            c.Build();
            return c;
        }

        void Build()
        {
            var yellow = Mat.Opaque(GameConfig.CannonYellow);
            var dark = Mat.Opaque(GameConfig.CannonYellowDark);
            // The whole unit is painted yellow (strict requirement): the structural parts that
            // used to be steel-grey now carry the same yellow so the cannon reads yellow end to end.
            var steel = Mat.Opaque(GameConfig.CannonYellow);
            var fan = Mat.Opaque(new Color(0.10f, 0.11f, 0.13f, 1f));
            var bladeMat = Mat.Opaque(new Color(0.5f, 0.52f, 0.56f, 1f));

            // A low stand the drum sits on (the real unit rides on a bagged pedestal).
            AddPart("base", PrimitiveType.Cube, dark, new Vector3(0f, -0.78f, 0f),
                    new Vector3(1.7f, 0.5f, 1.7f));
            AddPart("post", PrimitiveType.Cylinder, steel, new Vector3(0f, -0.4f, 0f),
                    new Vector3(0.7f, 0.55f, 0.7f));

            // The whole drum tilts with the aim (pitch); the root yaws it left/right.
            barrelPivot = new GameObject("turret").transform;
            barrelPivot.SetParent(transform, false);
            barrelPivot.localPosition = new Vector3(0f, 0.15f, 0f);
            barrelPivot.localEulerAngles = new Vector3(-pitch, 0f, 0f);

            // The cannon is a TUBE, open at both ends: a ring of wall segments with no cap on
            // either side, so looking in through the back you see straight down the dark bore
            // to the fan sitting at the very back of the tube.
            // The tube is one smooth, welded cylinder mesh (shared vertices, smooth normals) so it
            // reads as a round barrel instead of a faceted ring of boxes. The fire-squash bulge is
            // applied by displacing its vertices radially each frame (see DisplaceTube in Update).
            // The barrel is one smooth tube split into three colour bands so it reads like the
            // real Latemar-style unit: a grey muzzle end, a yellow body, and a grey tail. All three
            // share the fire-squash bulge (displaced together in Update through the `segs` list).
            const float RearEndZ = -1.30f;
            const float FrontEndZ = 1.10f;
            const float RearSplitZ = -0.72f;   // grey tail -> yellow body
            const float FrontSplitZ = 0.55f;   // yellow body -> grey muzzle
            AddTubeBand("wall_tail", steel, TubeR, RearEndZ, RearSplitZ);
            AddTubeBand("wall_body", yellow, TubeR, RearSplitZ, FrontSplitZ);
            AddTubeBand("wall_muzzle", steel, TubeR, FrontSplitZ, FrontEndZ);

            // Dark inner liner just inside the wall so the bore reads as hollow. Double-sided so
            // the camera looking in through the open back sees the inner surface.
            var boreMat = Mat.Opaque(new Color(0.10f, 0.11f, 0.13f, 1f));
            boreMat.SetFloat("_Cull", 0f);
            boreMesh = MeshFactory.Tube(BoreR, TubeLen * 0.98f, TubeSegments, TubeRings);
            boreBase = boreMesh.vertices;
            boreAngle = DeriveAngles(boreBase);
            boreScratch = new Vector3[boreBase.Length];
            var boreGo = new GameObject("bore");
            boreGo.AddComponent<MeshFilter>().sharedMesh = boreMesh;
            boreGo.AddComponent<MeshRenderer>().material = boreMat;
            boreGo.transform.SetParent(barrelPivot, false);
            boreGo.transform.localPosition = new Vector3(0f, 0f, TubeCenterZ);
            segs.Add(new TubeSeg { mesh = boreMesh, bas = boreBase, scratch = boreScratch, ang = boreAngle, baseR = BoreR, zc0 = TubeCenterZ });

            // Steel rims framing the two open ends of the tube.
            // The front rim is registered as a bulge element so the very mouth stretches with the
            // travelling bulge; the back rim stays put.
            AddRing("rim_front", barrelPivot, steel, TubeSegments, 1.26f, 0.2f, 0.3f, 1.12f, true);
            AddRing("rim_back", barrelPivot, steel, TubeSegments, 1.26f, 0.2f, 0.3f, -1.3f);

            // The fan lives INSIDE the tube, at the very back end (the side the player sees
            // through the open back). It sits still for a short wind-up, then spins up.
            // A dark housing plate sits BEHIND the fan (deeper into the tube) so the blades
            // stay visible through the open back while the plate gives them a backdrop and
            // carries the rear branding. The camera looks from -Z, so nearer = more negative z.
            var backPlate = AddPart("back_plate", PrimitiveType.Cylinder, steel, Vector3.zero,
                                   new Vector3(2.28f, 0.08f, 2.28f), new Vector3(90f, 0f, 0f));
            backPlate.SetParent(barrelPivot, false);
            backPlate.localPosition = new Vector3(0f, 0f, -0.55f);

            fanBlades = new GameObject("fan").transform;
            fanBlades.SetParent(barrelPivot, false);
            fanBlades.localPosition = new Vector3(0f, 0f, -1.05f);

            var hubMat = Mat.Opaque(new Color(0.22f, 0.24f, 0.28f, 1f));
            var hub = AddPart("fan_hub", PrimitiveType.Cylinder, hubMat, Vector3.zero,
                             new Vector3(0.5f, 0.35f, 0.5f), new Vector3(90f, 0f, 0f));
            hub.SetParent(fanBlades, false);
            hub.localPosition = Vector3.zero;

            const int blades = 7;
            for (int i = 0; i < blades; i++)
            {
                float adeg = (i / (float)blades) * 360f;
                var arm = new GameObject("fan_arm" + i).transform;
                arm.SetParent(fanBlades, false);
                arm.localPosition = Vector3.zero;
                arm.localEulerAngles = new Vector3(0f, 0f, adeg);

                // Blade tips stay inside the bore (reaches ~0.78 < boreR 1.02).
                var b = AddPart("fan_blade" + i, PrimitiveType.Cube, bladeMat, Vector3.zero,
                               new Vector3(0.72f, 0.28f, 0.06f));
                b.SetParent(arm, false);
                b.localPosition = new Vector3(0.42f, 0f, 0f);
                b.localEulerAngles = new Vector3(38f, 0f, 0f);
            }

            // A ring of water nozzles around the mouth of the tube, like the real unit.
            const int nozzles = 14;
            for (int i = 0; i < nozzles; i++)
            {
                float a = (i / (float)nozzles) * Mathf.PI * 2f;
                var nScale = new Vector3(0.22f, 0.22f, 0.22f);
                var n = AddPart("nozzle" + i, PrimitiveType.Sphere, steel, Vector3.zero, nScale);
                n.SetParent(barrelPivot, false);
                n.localPosition = new Vector3(Mathf.Cos(a) * 1.32f, Mathf.Sin(a) * 1.32f, 1.16f);
                // Register the nozzle ring too, so the outermost lip of the muzzle bulges as the
                // snowball forces its way out.
                bulgeElems.Add(new BulgeElem { t = n, angle = a, z = 1.16f, baseR = 1.32f, baseScale = nScale });
            }

            // The grey, curvy front shroud: a flared truncated cone like the real unit's nose,
            // with a dark mesh face recessed behind the nozzle ring. Painted yellow so the whole
            // cannon reads yellow (the dark fan face still gives the muzzle end definition).
            var shroudMat = Mat.Opaque(GameConfig.CannonYellow);
            var shroud = AddMesh("shroud", MeshFactory.TruncatedCone(1.06f, 1.44f, 0.55f, 48),
                                shroudMat, new Vector3(0f, 0f, 0.55f), new Vector3(90f, 0f, 0f));
            shroud.SetParent(barrelPivot, false);

            var face = AddPart("fan_face", PrimitiveType.Cylinder, fan, Vector3.zero,
                               new Vector3(2.55f, 0.06f, 2.55f), new Vector3(90f, 0f, 0f));
            face.SetParent(barrelPivot, false);
            face.localPosition = new Vector3(0f, 0f, 1.06f);

            // A red beacon on the crown of the tube that blinks while the game runs.
            AddPart("beacon_base", PrimitiveType.Cylinder, steel,
                    new Vector3(0f, 1.3f, 0.1f), new Vector3(0.36f, 0.16f, 0.36f))
               .SetParent(barrelPivot, false);
            var beaconMat = Mat.Opaque(new Color(0.95f, 0.12f, 0.08f, 1f), true);
            beaconGlow = AddPart("beacon_light", PrimitiveType.Sphere, beaconMat,
                                new Vector3(0f, 1.5f, 0.1f), new Vector3(0.34f, 0.3f, 0.34f))
                        .GetComponent<MeshRenderer>();
            beaconGlow.transform.SetParent(barrelPivot, false);
            var bl = new GameObject("beacon_point").AddComponent<Light>();
            bl.type = LightType.Point;
            bl.color = new Color(1f, 0.15f, 0.1f, 1f);
            bl.range = 5f;
            bl.intensity = 0f;
            bl.shadows = LightShadows.None;
            bl.transform.SetParent(barrelPivot, false);
            bl.transform.localPosition = new Vector3(0f, 1.55f, 0.1f);
            beaconLight = bl;

            // The rear branding decal has been removed: its text was authored left-aligned, so the
            // trailing glyphs poked out beyond the tube silhouette on the right flank and read as a
            // stray black mark. The cannon now carries no floating text decal.

            // Snowballs leave from the centre of the tube's mouth.
            muzzle = new GameObject("muzzle_point").transform;
            muzzle.SetParent(barrelPivot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.3f);

            // A single capsule collider keeps the whole cannon one solid body on the ground.
            // CapsuleCollider.direction is an int axis index (0=X,1=Y,2=Z); the enum type is gone.
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 1.3f;
            col.height = 2.9f;
            col.center = new Vector3(0f, 0.2f, 0f);
            col.direction = 1; // upright capsule along Y

            // Keep it planted: no physics response needed, we move it by hand.
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.isKinematic = true;

            UpdateAim();
        }

        /// <summary>Adds one colour band of the barrel tube between two z positions (barrel
        /// space). The band is a smooth open cylinder centred on its own mid-z so the fire-squash
        /// bulge can displace it around that centre.</summary>
        void AddTubeBand(string name, Material mat, float radius, float zFrom, float zTo)
        {
            float len = zTo - zFrom;
            float zc = (zFrom + zTo) * 0.5f;
            int rings = Mathf.Max(4, Mathf.RoundToInt(TubeRings * len / TubeLen));
            var mesh = MeshFactory.Tube(radius, len, TubeSegments, rings);
            var bas = mesh.vertices;
            var ang = DeriveAngles(bas);
            var scratch = new Vector3[bas.Length];
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().material = mat;
            go.transform.SetParent(barrelPivot, false);
            go.transform.localPosition = new Vector3(0f, 0f, zc);
            segs.Add(new TubeSeg { mesh = mesh, bas = bas, scratch = scratch, ang = ang, baseR = radius, zc0 = zc });
        }

        /// <summary>Pushes a smooth tube mesh's vertices radially outward along the travelling
        /// gaussian bulge, then recomputes its (still smooth) normals. `zc0` is the mesh's own
        /// centre z in barrel space (its local verts are centred on zero).</summary>
        void DisplaceTube(Mesh m, Vector3[] baseVerts, float[] ang, Vector3[] scratch,
                          float baseR, float zc, float amp, float inv2w2, float zc0)
        {
            if (m == null || baseVerts == null) return;
            for (int i = 0; i < baseVerts.Length; i++)
            {
                float dd = (baseVerts[i].z + zc0) - zc;
                float g = Mathf.Exp(-dd * dd * inv2w2);
                float r = baseR * (1f + amp * g);
                scratch[i] = new Vector3(Mathf.Cos(ang[i]) * r, Mathf.Sin(ang[i]) * r, baseVerts[i].z);
            }
            m.vertices = scratch;
            m.RecalculateNormals();
        }

        void ResetTube(Mesh m, Vector3[] baseVerts)
        {
            if (m == null || baseVerts == null) return;
            m.vertices = baseVerts;
            m.RecalculateNormals();
        }

        static float[] DeriveAngles(Vector3[] verts)
        {
            var a = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++) a[i] = Mathf.Atan2(verts[i].y, verts[i].x);
            return a;
        }

        /// <summary>A ring of thin boxes around the Z axis, used as the steel rim that frames
        /// each open end of the tube.</summary>
        void AddRing(string name, Transform parent, Material mat, int segments,
                     float radius, float thickness, float length, float z, bool bulge = false)
        {
            float w = 2f * radius * Mathf.Sin(Mathf.PI / segments) * 1.08f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var segScale = new Vector3(thickness, w, length);
                var seg = AddPart(name + i, PrimitiveType.Cube, mat, Vector3.zero, segScale);
                seg.SetParent(parent, false);
                seg.localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z);
                seg.localEulerAngles = new Vector3(0f, 0f, a * Mathf.Rad2Deg);
                if (bulge)
                    bulgeElems.Add(new BulgeElem { t = seg, angle = a, z = z, baseR = radius, baseScale = segScale });
            }
        }

        void AddLabel(string name, string text, Vector3 pos, Vector3 euler, Vector3 target, Color color)
        {
            var font = Ui.Font;
            if (font == null) return;
            var mesh = MeshFactory.Text(text, font, 64);
            if (mesh == null) return;
            var atlas = font.material != null ? font.material.mainTexture as Texture2D : null;
            if (atlas == null) return;

            var go = new GameObject(name);
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = Mat.LabelDecal(atlas, color);

            // The mesh is authored in font-pixel units; normalise it to the requested world size.
            var b = mesh.bounds;
            float w = Mathf.Max(0.0001f, b.size.x);
            float h = Mathf.Max(0.0001f, b.size.y);
            go.transform.SetParent(barrelPivot, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = new Vector3(target.x / w, target.y / h, 1f);
        }

        Transform AddMesh(string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler)
        {
            var go = new GameObject(name);
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.material = mat;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            return go.transform;
        }

        Transform AddPart(string name, PrimitiveType type, Material mat, Vector3 pos,
                          Vector3 scale, Vector3? euler = null)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = name;
            p.transform.SetParent(transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            if (euler != null) p.transform.localEulerAngles = euler.Value;
            var r = p.GetComponent<MeshRenderer>();
            r.material = mat;
            Destroy(p.GetComponent<Collider>());
            return p.transform;
        }

        void Start()
        {
            basePos = transform.position;
            camRef = Camera.main;
            RecomputeHalfTravel(true);
        }

        /// <summary>The strafe reach is a sixth of the visible screen width at the cannon's depth.
        /// Recomputed whenever the camera's FOV or aspect changes (a phone rotation, a desktop
        /// window resize) so the aim sweep and the lake's sizing track the live projection.</summary>
        void RecomputeHalfTravel(bool force)
        {
            if (camRef == null) camRef = Camera.main;
            if (camRef == null) return;
            if (!force && Mathf.Approximately(camRef.fieldOfView, sizedFov)
                && Mathf.Approximately(camRef.aspect, sizedAspect)) return;
            sizedFov = camRef.fieldOfView;
            sizedAspect = camRef.aspect;
            float dist = Mathf.Abs(camRef.transform.position.z - basePos.z);
            float halfH = Mathf.Tan(sizedFov * 0.5f * Mathf.PI / 180f) * dist;
            halfTravel = Mathf.Max(1.2f, halfH * sizedAspect / 6f);
        }

        void Update()
        {
            // Keep the strafe reach (and therefore LeftmostX, which the lake sizes against) in sync
            // with the live camera so a mid-run screen rotation re-fits the layout instead of leaving
            // the lake mis-placed/mis-sized and the cannon appearing to change bulk.
            RecomputeHalfTravel(false);

            var controls = game != null ? game.Controls : null;
            Vector2 move = controls != null ? controls.ReadMove() : Vector2.zero;

            // Advance the fire-squash wave: a bulge travels down the bore as the oversized
            // snowball forces its way through, and the whole tube dips (recoils) and settles.
            if (squash > 0f)
            {
                squash = Mathf.Max(0f, squash - Time.deltaTime / SquashTime);
                float prog = 1f - squash;                       // 0 -> 1 across the shot
                float zc = Mathf.Lerp(BulgeStartZ, BulgeEndZ, prog);
                // Hold the bulge near full strength through the whole travel so it reaches the
                // muzzle: a plain sine faded it to nothing before the mouth, leaving the front of
                // the tube static. Ramp in quickly, hold, then release over the last fifth.
                float env;
                if (prog < 0.12f) env = prog / 0.12f;
                else if (prog > 0.80f) env = (1f - prog) / 0.20f;
                else env = 1f;
                float amp = BulgeAmp * env;
                recoilKick = RecoilMax * Mathf.Sin(Mathf.PI * prog);
                float inv2w2 = 1f / (2f * BulgeWidth * BulgeWidth);
                // Push every tube band (and the bore liner) out along the travelling bulge.
                for (int i = 0; i < segs.Count; i++)
                {
                    var t = segs[i];
                    DisplaceTube(t.mesh, t.bas, t.ang, t.scratch, t.baseR, zc, amp, inv2w2, t.zc0);
                }
                for (int i = 0; i < bulgeElems.Count; i++)
                {
                    var e = bulgeElems[i];
                    float dd = e.z - zc;
                    float g = Mathf.Exp(-dd * dd * inv2w2);
                    float r = e.baseR * (1f + amp * g);
                    float sw = 1f + 0.9f * amp * g;
                    e.t.localPosition = new Vector3(Mathf.Cos(e.angle) * r, Mathf.Sin(e.angle) * r, e.z);
                    e.t.localScale = new Vector3(e.baseScale.x * sw, e.baseScale.y * sw, e.baseScale.z);
                }
                bulgeActive = true;
            }
            else if (bulgeActive)
            {
                recoilKick = 0f;
                for (int i = 0; i < segs.Count; i++) ResetTube(segs[i].mesh, segs[i].bas);
                for (int i = 0; i < bulgeElems.Count; i++)
                {
                    var e = bulgeElems[i];
                    e.t.localPosition = new Vector3(Mathf.Cos(e.angle) * e.baseR, Mathf.Sin(e.angle) * e.baseR, e.z);
                    e.t.localScale = e.baseScale;
                }
                bulgeActive = false;
            }

            // The cannon never leaves its spot: it only turns in place. The move vector drives
            // the turret's yaw (the whole cannon rotates) and the barrel's elevation (pitch).
            yaw = Mathf.Clamp(yaw + move.x * AimYawSpeed * Time.deltaTime, -MaxYaw, MaxYaw);
            pitch = Mathf.Clamp(pitch + move.y * AimPitchSpeed * Time.deltaTime, MinPitch, MaxPitch);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (barrelPivot != null)
                barrelPivot.localEulerAngles = new Vector3(-(pitch + recoilKick), 0f, 0f);

            // While it turns, the whole unit also slides sideways: fully left-aimed it stands
            // a sixth of the screen width left of centre, fully right-aimed a sixth right.
            float lateral = (yaw / MaxYaw) * halfTravel;
            transform.position = basePos + Vector3.right * lateral;

            // The beacon on the crown blinks the whole time the game runs.
            beaconPhase += Time.deltaTime * 6f;
            float pulse = Mathf.Max(0f, Mathf.Sin(beaconPhase));
            if (beaconLight != null) beaconLight.intensity = pulse * 2.4f;
            if (beaconGlow != null)
                beaconGlow.transform.localScale = Vector3.one * (0.3f * (0.75f + 0.45f * pulse));

            // The back fan is steady during the wind-up, then spins up to full speed. Once the
            // run is over it coasts down to a standstill over FanSpinDownTime instead of
            // whirling behind the game-over card.
            if (fanBlades != null)
            {
                if (fanSpinDownTimer >= 0f)
                {
                    fanSpinDownTimer -= Time.deltaTime;
                    float remaining = Mathf.Clamp01(fanSpinDownTimer / FanSpinDownTime);
                    fanSpinSpeed = fanSpinDownFrom * remaining;
                    if (fanSpinDownTimer <= 0f)
                    {
                        fanSpinSpeed = 0f;
                        fanSpinDownTimer = -1f;
                    }
                    fanBlades.Rotate(Vector3.forward, fanSpinSpeed * Time.deltaTime, Space.Self);
                }
                else if (fanSpinDelay > 0f)
                {
                    fanSpinDelay -= Time.deltaTime;
                }
                else if (!fanStopped)
                {
                    // Spins three times faster than before during play.
                    if (fanSpinSpeed < 900f) fanSpinSpeed += 600f * Time.deltaTime;
                    fanBlades.Rotate(Vector3.forward, fanSpinSpeed * Time.deltaTime, Space.Self);
                }
            }

            UpdateAim();

            cooldown -= Time.deltaTime;
            if (jamTimer > 0f) jamTimer -= Time.deltaTime;
            if (game == null || controls == null) return;

            if (controls.IsFireHeld() && cooldown <= 0f && jamTimer <= 0f)
            {
                // The RAPID FIRE upgrade shortens the interval between shots.
                cooldown = GameConfig.FireCooldown * RunUpgrades.FireRateMult;
                Fire();
            }
        }

        /// <summary>Briefly jams the cannon so it cannot fire for the given duration. Called when a
        /// Bomber's lobbed shot lands near the mechanism.</summary>
        public void Jam(float duration)
        {
            if (duration > jamTimer) jamTimer = duration;
        }

        /// <summary>Called when the game-over card is shown: the fan stops driving and
        /// decelerates smoothly to a standstill over FanSpinDownTime, like real machinery
        /// losing power. Idempotent, so a repeated call cannot restart the countdown.</summary>
        public void StopFan()
        {
            fanStopped = true;
            if (fanSpinDownTimer >= 0f) return;
            fanSpinDownFrom = fanSpinSpeed;
            fanSpinDownTimer = FanSpinDownTime;
        }

        /// <summary>One tile of the tube wall (or bore liner) that the fire-squash bulge can
        /// push radially outward as the snowball travels through it.</summary>
        sealed class BulgeElem
        {
            public Transform t;
            public float angle;
            public float z;
            public float baseR;
            public Vector3 baseScale;
        }

        void UpdateAim()
        {
            // The barrel pivot is a child of the (yawed) body, so its world forward already
            // combines the turret's yaw and the barrel's pitch; the muzzle follows the same chain.
            AimDirection = barrelPivot != null ? barrelPivot.forward : transform.forward;
            MuzzleWorldPosition = muzzle != null ? muzzle.position : transform.position + Vector3.up;
        }

        void Fire()
        {
            if (game == null) return;

            // The director owns which shot is armed (basic / spray / lance / blizzard). Premium shots
            // cost extra lake water but clear clusters, punch a line, or chill the field.
            ShotType shot = game.ArmedShot;
            int cost = Mathf.Max(1, Mathf.CeilToInt(ShotDefs.WaterCost(shot) * RunUpgrades.WaterCostMult));
            if (!game.TryConsumeLakeMarks(cost)) return;

            // Launch along the barrel's actual 3D facing (yaw + elevation). Gravity in Snowball
            // then curves it into a ballistic arc, so the angle the player set decides the range.
            Vector3 dir = AimDirection;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir = dir.normalized;

            int count = Mathf.Max(1, ShotDefs.Count(shot));
            float spread = ShotDefs.SpreadDeg(shot);
            int pierce = ShotDefs.Pierce(shot);
            bool chills = ShotDefs.Chills(shot);
            Color tint = ShotDefs.Tint(shot);

            Vector3 origin = MuzzleWorldPosition + dir * 0.4f;
            for (int i = 0; i < count; i++)
            {
                // Fan the volley symmetrically about the barrel by rotating around world up.
                float off = count <= 1 ? 0f : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (float)(count - 1));
                Vector3 d = off == 0f ? dir : Quaternion.Euler(0f, off, 0f) * dir;
                game.SpawnSnowballPremium(origin, d, pierce, chills, tint);
            }
            game.PlayThrowAt(transform.position);

            // Kick the drum and puff snow out of the mouth.
            squash = 1f;
            SpawnMuzzleDust(dir);
        }

        /// <summary>A short burst of soft snow puffs drifting out of the muzzle; each expands
        /// and fades over ~1 s then destroys itself.</summary>
        void SpawnMuzzleDust(Vector3 dir)
        {
            Vector3 m = MuzzleWorldPosition;
            Vector3 f = dir.normalized;
            // Two axes perpendicular to the barrel so the puff can splay out in a wide cone around
            // the muzzle, not just straight down the bore.
            Vector3 r = Vector3.Cross(f, Vector3.up);
            if (r.sqrMagnitude < 0.0001f) r = Vector3.Cross(f, Vector3.forward);
            r.Normalize();
            Vector3 u = Vector3.Cross(r, f).normalized;

            int n = Random.Range(18, 26);
            for (int i = 0; i < n; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                Vector3 radial = r * Mathf.Cos(ang) + u * Mathf.Sin(ang);
                float rad = Mathf.Sqrt(Random.Range(0f, 1f));
                Vector3 off = radial * (rad * Random.Range(0.2f, 0.7f));
                Vector3 side = radial * Random.Range(1.4f, 3.4f);
                Vector3 vel = f * Random.Range(0.8f, 2.2f) + side + Vector3.up * Random.Range(0.2f, 1.1f);
                DustPuff.Spawn(m + f * Random.Range(0f, 0.4f) + off, vel);
            }
        }
    }

    /// <summary>A single muzzle snow puff: a camera-facing soft sprite that grows, drifts
    /// outward and fades to nothing over its lifetime, then removes itself. Uses a plain
    /// SpriteRenderer (built-in sprite shader) so it survives player builds, matching the
    /// proven cloud/flame approach in Weather.</summary>
    public sealed class DustPuff : MonoBehaviour
    {
        SpriteRenderer sr;
        Vector3 drift;
        float life;
        float maxLife;
        float startScale;
        float endScale;
        float baseAlpha;

        public static void Spawn(Vector3 pos, Vector3 velocity)
        {
            var go = new GameObject("muzzle_dust");
            go.transform.position = pos;
            var puff = go.AddComponent<DustPuff>();
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TextureFactory.CircleSprite(1.2f);
            sr.sortingOrder = 60;
            puff.sr = sr;

            float tint = Random.Range(0.86f, 1f);
            puff.baseAlpha = Random.Range(0.55f, 0.9f);
            sr.color = new Color(tint, tint, Random.Range(0.92f, 1f), puff.baseAlpha);

            puff.startScale = Random.Range(0.4f, 0.75f);
            puff.endScale = puff.startScale * Random.Range(3.0f, 4.6f);
            puff.maxLife = Random.Range(0.9f, 1.35f);
            puff.life = puff.maxLife;
            go.transform.localScale = Vector3.one * puff.startScale;

            // Caller supplies the wide-angle splay velocity.
            puff.drift = velocity;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            float t = 1f - life / maxLife;                 // 0 -> 1
            float e = 1f - (1f - t) * (1f - t);            // ease-out growth
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, e);

            drift *= Mathf.Max(0f, 1f - 1.6f * dt);        // drag
            transform.position += drift * dt;

            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);

            var c = sr.color;
            c.a = baseAlpha * (1f - t);
            sr.color = c;
        }
    }
}
