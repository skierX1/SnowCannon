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

        Transform muzzle;
        Transform barrelPivot;
        Transform fanBlades;
        Renderer beaconGlow;
        Light beaconLight;
        float beaconPhase;
        Vector3 basePos;
        float halfTravel = 1.8f;
        float cooldown;
        float fanSpinDelay = 1.2f;
        float fanSpinSpeed;
        // Fan coast-down after the run ends: -1 means "still running", otherwise it is the
        // remaining seconds of the spin-down and the speed is interpolated to zero from the
        // speed captured at the moment the game-over card went up.
        const float FanSpinDownTime = 10f;
        float fanSpinDownTimer = -1f;
        float fanSpinDownFrom;

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
        SnowCannonGame game;

        // Barrel aim, in degrees. yaw sweeps left/right, pitch is the elevation above the
        // horizon. Both are driven by the same move vector the old driving code used.
        float yaw;
        float pitch = 16f;
        const float AimYawSpeed = 95f;
        const float AimPitchSpeed = 70f;
        const float MaxYaw = 40f;
        const float MinPitch = 4f;
        const float MaxPitch = 46f;

        public static Cannon Create(SnowCannonGame owner)
        {
            var go = new GameObject("Cannon");
            go.transform.position = new Vector3(0f, 0f, -5f);
            var c = go.AddComponent<Cannon>();
            c.game = owner;
            c.Build();
            return c;
        }

        void Build()
        {
            var yellow = Mat.Opaque(GameConfig.CannonYellow);
            var dark = Mat.Opaque(GameConfig.CannonYellowDark);
            var steel = Mat.Opaque(GameConfig.CannonSteel);
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
            const int segments = 36;    // angular resolution: high enough that the tube reads as round, not faceted
            const int slices = 12;      // tiles along the length so a bulge can travel down it
            const float tubeLen = 2.4f;
            const float tubeCenterZ = -0.1f;
            const float tubeR = 1.18f;   // outer wall radius
            const float boreR = 1.02f;    // inner (dark) wall radius
            float segW = 2f * tubeR * Mathf.Sin(Mathf.PI / segments) * 1.08f;
            float dz = tubeLen / slices;

            // The tube is built from a grid of small tiles (angular segments x length slices)
            // that tile into the same solid tube as before, but each one can be pushed radially
            // outward on its own -- that is what lets the fire-squash bulge travel down the bore.
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float adeg = a * Mathf.Rad2Deg;

                for (int j = 0; j < slices; j++)
                {
                    float z = tubeCenterZ - tubeLen * 0.5f + (j + 0.5f) * dz;

                    // Outer yellow wall tile. Its local X is the radial direction, so rotating
                    // the box about Z by the segment angle aims its thin axis straight outwards.
                    var wallScale = new Vector3(0.15f, segW, dz);
                    var w = AddPart("wall" + i + "_" + j, PrimitiveType.Cube, yellow, Vector3.zero, wallScale);
                    w.SetParent(barrelPivot, false);
                    w.localPosition = new Vector3(Mathf.Cos(a) * tubeR, Mathf.Sin(a) * tubeR, z);
                    w.localEulerAngles = new Vector3(0f, 0f, adeg);
                    bulgeElems.Add(new BulgeElem { t = w, angle = a, z = z, baseR = tubeR, baseScale = wallScale });

                    // Dark inner liner tile just inside the wall, so the bore reads as hollow.
                    var boreScale = new Vector3(0.1f, segW, dz * 0.98f);
                    var inn = AddPart("bore" + i + "_" + j, PrimitiveType.Cube, fan, Vector3.zero, boreScale);
                    inn.SetParent(barrelPivot, false);
                    inn.localPosition = new Vector3(Mathf.Cos(a) * boreR, Mathf.Sin(a) * boreR, z);
                    inn.localEulerAngles = new Vector3(0f, 0f, adeg);
                    bulgeElems.Add(new BulgeElem { t = inn, angle = a, z = z, baseR = boreR, baseScale = boreScale });
                }
            }

            // Steel rims framing the two open ends of the tube.
            // The front rim is registered as a bulge element so the very mouth stretches with the
            // travelling bulge; the back rim stays put.
            AddRing("rim_front", barrelPivot, steel, segments, 1.26f, 0.2f, 0.3f, 1.12f, true);
            AddRing("rim_back", barrelPivot, steel, segments, 1.26f, 0.2f, 0.3f, -1.3f);

            // The fan lives INSIDE the tube, at the very back end (the side the player sees
            // through the open back). It sits still for a short wind-up, then spins up.
            // A dark housing plate sits BEHIND the fan (deeper into the tube) so the blades
            // stay visible through the open back while the plate gives them a backdrop and
            // carries the rear branding. The camera looks from -Z, so nearer = more negative z.
            var backPlate = AddPart("back_plate", PrimitiveType.Cylinder, fan, Vector3.zero,
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

            // The gray, curvy front shroud: a flared truncated cone like the real unit's nose,
            // with a dark mesh face recessed behind the nozzle ring.
            var gray = Mat.Opaque(new Color(0.55f, 0.57f, 0.60f, 1f));
            var shroud = AddMesh("shroud", MeshFactory.TruncatedCone(1.06f, 1.44f, 0.55f, 36),
                                gray, new Vector3(0f, 0f, 0.55f), new Vector3(90f, 0f, 0f));
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

            // Branding decals: light text on the dark rear housing, dark text on the yellow flanks.
            AddLabel("label_back", "SNOW#WIPER", new Vector3(0f, -0.55f, -0.7f), Vector3.zero,
                     new Vector3(1.7f, 0.4f, 1f), new Color(0.92f, 0.95f, 1f, 1f));
            AddLabel("label_left", "SNOW#WIPER", new Vector3(-1.31f, 0.15f, 0.1f),
                     new Vector3(0f, 90f, 0f), new Vector3(1.6f, 0.36f, 1f), new Color(0.08f, 0.09f, 0.11f, 1f));
            AddLabel("label_right", "SNOW#WIPER", new Vector3(1.31f, 0.15f, 0.1f),
                     new Vector3(0f, -90f, 0f), new Vector3(1.6f, 0.36f, 1f), new Color(0.08f, 0.09f, 0.11f, 1f));

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
            // The strafe reach is a sixth of the visible screen width at the cannon's depth.
            var cam = Camera.main;
            if (cam != null)
            {
                float dist = Mathf.Abs(cam.transform.position.z - basePos.z);
                float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.PI / 180f) * dist;
                halfTravel = Mathf.Max(1.2f, halfH * cam.aspect / 6f);
            }
        }

        void Update()
        {
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
                else
                {
                    // Spins three times faster than before during play.
                    if (fanSpinSpeed < 900f) fanSpinSpeed += 600f * Time.deltaTime;
                    fanBlades.Rotate(Vector3.forward, fanSpinSpeed * Time.deltaTime, Space.Self);
                }
            }

            UpdateAim();

            cooldown -= Time.deltaTime;
            if (game == null || controls == null) return;

            if (controls.IsFireHeld() && cooldown <= 0f)
            {
                cooldown = GameConfig.FireCooldown;
                Fire();
            }
        }

        /// <summary>Called when the game-over card is shown: the fan stops driving and
        /// decelerates smoothly to a standstill over FanSpinDownTime, like real machinery
        /// losing power. Idempotent, so a repeated call cannot restart the countdown.</summary>
        public void StopFan()
        {
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
            // Launch along the barrel's actual 3D facing (yaw + elevation). Gravity in Snowball
            // then curves it into a ballistic arc, so the angle the player set decides the range.
            Vector3 dir = AimDirection;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir = dir.normalized;
            game.SpawnSnowball(MuzzleWorldPosition + dir * 0.4f, dir);
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
            int n = Random.Range(5, 8);
            for (int i = 0; i < n; i++)
            {
                Vector3 jitter = new Vector3(Random.Range(-0.3f, 0.3f),
                                             Random.Range(-0.2f, 0.35f),
                                             Random.Range(-0.3f, 0.3f));
                DustPuff.Spawn(m + dir * Random.Range(0f, 0.5f) + jitter, dir);
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

        public static void Spawn(Vector3 pos, Vector3 dir)
        {
            var go = new GameObject("muzzle_dust");
            go.transform.position = pos;
            var puff = go.AddComponent<DustPuff>();
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = TextureFactory.CircleSprite(1.4f);
            puff.sr = sr;

            float tint = Random.Range(0.82f, 1f);
            puff.baseAlpha = Random.Range(0.35f, 0.6f);
            sr.color = new Color(tint, tint, Random.Range(0.9f, 1f), puff.baseAlpha);

            puff.startScale = Random.Range(0.25f, 0.5f);
            puff.endScale = puff.startScale * Random.Range(2.4f, 3.6f);
            puff.maxLife = Random.Range(0.8f, 1.15f);
            puff.life = puff.maxLife;
            go.transform.localScale = Vector3.one * puff.startScale;

            // Billow out along the barrel, biased up and splayed sideways.
            puff.drift = dir * Random.Range(1.2f, 2.6f)
                       + new Vector3(Random.Range(-1.1f, 1.1f),
                                      Random.Range(0.3f, 1.4f),
                                      Random.Range(-0.6f, 0.6f));
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
