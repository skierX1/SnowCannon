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
            const int segments = 20;
            const float tubeLen = 2.4f;
            const float tubeR = 1.18f;   // outer wall radius
            const float boreR = 1.02f;    // inner (dark) wall radius
            float segW = 2f * tubeR * Mathf.Sin(Mathf.PI / segments) * 1.08f;

            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float adeg = a * Mathf.Rad2Deg;

                // Outer yellow wall panel. Its local X is the radial direction, so rotating
                // the box about Z by the segment angle aims its thin axis straight outwards.
                var w = AddPart("wall" + i, PrimitiveType.Cube, yellow, Vector3.zero,
                                new Vector3(0.15f, segW, tubeLen));
                w.SetParent(barrelPivot, false);
                w.localPosition = new Vector3(Mathf.Cos(a) * tubeR, Mathf.Sin(a) * tubeR, -0.1f);
                w.localEulerAngles = new Vector3(0f, 0f, adeg);

                // Dark inner liner just inside the wall, so the bore reads as hollow.
                var inn = AddPart("bore" + i, PrimitiveType.Cube, fan, Vector3.zero,
                                  new Vector3(0.1f, segW, tubeLen * 0.98f));
                inn.SetParent(barrelPivot, false);
                inn.localPosition = new Vector3(Mathf.Cos(a) * boreR, Mathf.Sin(a) * boreR, -0.1f);
                inn.localEulerAngles = new Vector3(0f, 0f, adeg);
            }

            // Steel rims framing the two open ends of the tube.
            AddRing("rim_front", barrelPivot, steel, segments, 1.26f, 0.2f, 0.3f, 1.12f);
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
                var n = AddPart("nozzle" + i, PrimitiveType.Sphere, steel, Vector3.zero,
                               new Vector3(0.22f, 0.22f, 0.22f));
                n.SetParent(barrelPivot, false);
                n.localPosition = new Vector3(Mathf.Cos(a) * 1.32f, Mathf.Sin(a) * 1.32f, 1.16f);
            }

            // The gray, curvy front shroud: a flared truncated cone like the real unit's nose,
            // with a dark mesh face recessed behind the nozzle ring.
            var gray = Mat.Opaque(new Color(0.55f, 0.57f, 0.60f, 1f));
            var shroud = AddMesh("shroud", MeshFactory.TruncatedCone(1.06f, 1.44f, 0.55f, 22),
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
                     float radius, float thickness, float length, float z)
        {
            float w = 2f * radius * Mathf.Sin(Mathf.PI / segments) * 1.08f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var seg = AddPart(name + i, PrimitiveType.Cube, mat, Vector3.zero,
                                 new Vector3(thickness, w, length));
                seg.SetParent(parent, false);
                seg.localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z);
                seg.localEulerAngles = new Vector3(0f, 0f, a * Mathf.Rad2Deg);
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

            // The cannon never leaves its spot: it only turns in place. The move vector drives
            // the turret's yaw (the whole cannon rotates) and the barrel's elevation (pitch).
            yaw = Mathf.Clamp(yaw + move.x * AimYawSpeed * Time.deltaTime, -MaxYaw, MaxYaw);
            pitch = Mathf.Clamp(pitch + move.y * AimPitchSpeed * Time.deltaTime, MinPitch, MaxPitch);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (barrelPivot != null)
                barrelPivot.localEulerAngles = new Vector3(-pitch, 0f, 0f);

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

            // The back fan is steady during the wind-up, then spins up to full speed.
            if (fanBlades != null)
            {
                if (fanSpinDelay > 0f) fanSpinDelay -= Time.deltaTime;
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
        }
    }
}
