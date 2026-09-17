using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>The tactical snowman archetypes. The director's level-weighted spawn mix chooses
    /// these so the field escalates from plain Runners into a blend the player must read and
    /// prioritise: Runners are quick and fragile, Tanks soak many hits, Splitters burst into a
    /// wave, Banners speed up their neighbours, and Bombers lob shots that jam the cannon.</summary>
    public enum SnowmanKind { Runner, Tank, Splitter, Banner, Bomber }

    /// <summary>
    /// A snowman: three stacked snow balls, coal eyes, a carrot nose, a pot hat and two
    /// branch arms with hands. The bottom ball rolls while the stack advances. One hit
    /// destroys the whole thing: the parts tumble to the ground and fade out over two seconds.
    /// </summary>
    public sealed class Snowman : MonoBehaviour, IHitTarget
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

        /// <summary>The tactical archetype this snowman was spawned as.</summary>
        public SnowmanKind Kind { get; private set; }
        // Remaining hits before the stack bursts (1 for the fragile kinds, more for the tough ones).
        int hp = 1;
        // A temporary speed multiplier applied by a nearby Banner; decays back toward 0 each frame.
        float speedBoost;
        // The owning director, handed in after Spawn so a Splitter can spawn children and a Bomber
        // can lob shots at the cannon.
        SnowCannonGame owner;
        float bannerTimer;
        float bomberTimer = 1.5f;
        Transform auraRing;

        /// <summary>IHitTarget: a snowman still counts as a live target until it is dying or gone.</summary>
        public bool Alive => !IsDying && !IsDone;

        /// <summary>World Y of the head centre, used to decide head vs body hits.</summary>
        public float HeadWorldY { get; private set; }

        /// <summary>World-space radius of the snowman's ground footprint, used for proximity
        /// tests against the cannon, lake and hose.</summary>
        public float FootprintRadius { get { return Rb * size; } }

        /// <summary>Every live snowman, so two of them can detect and bounce off each other
        /// without the game having to hand the whole list to each Update.</summary>
        static readonly List<Snowman> all = new List<Snowman>();

        Transform bottomBall;
        readonly List<Chunk> chunks = new List<Chunk>();
        float fadeTimer;
        float size = 1f;
        float swayPhase;

        // Spawn-in "pop up from the snow": instead of blinking into existence, the snowman scales
        // up from nothing with a slight elastic overshoot and rises from just under the snow
        // surface to resting on it, so it visibly grows into the field like a real snowman being
        // rolled up out of the drift. Runs once, over SpawnInTime, at the start of its life.
        const float SpawnInTime = 0.42f;
        float spawnInTimer;
        bool spawningIn = true;

        // The hinged lower jaw that opens and closes at random while the snowman marches.
        Transform jaw;
        float jawPhase, jawTimer, jawOpen;
        // 0 = pot, 1 = icicles, 2 = punk mohawk, 3 = shaggy snow afro. Chosen per snowman.
        int hairStyle;
        // 0 = outward burst, 1 = confetti pop, 2 = collapse down, 3 = tornado spin.
        int deathStyle;

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
        public static Snowman Spawn(float size, float speed, int textureVariant = -1,
                                     float maxAngleDeg = 30f, SnowmanKind kind = SnowmanKind.Runner)
        {
            var go = new GameObject("Snowman");
            var sm = go.AddComponent<Snowman>();
            sm.Kind = kind;
            sm.size = Mathf.Max(0.4f, size);
            sm.Speed = speed;
            // Each snowman picks its own snow look so the field never looks cloned.
            sm.textureVariant = textureVariant >= 0 ? textureVariant : Random.Range(0, 6);
            // A random diagonal heading, up to maxAngleDeg off straight-ahead.
            sm.maxAngle = Mathf.Clamp(maxAngleDeg, 0f, 30f);
            sm.headingDeg = Random.Range(-sm.maxAngle, sm.maxAngle);
            sm.deathStyle = Random.Range(0, 4);

            // Per-archetype tuning off the caller's base size/speed: the tough kinds are bigger,
            // slower and need more hits; Runners are the quick, fragile baseline.
            switch (kind)
            {
                case SnowmanKind.Runner:
                    sm.size *= 0.82f; sm.Speed *= 1.35f; sm.hp = 1; break;
                case SnowmanKind.Tank:
                    sm.size *= 1.5f; sm.Speed *= 0.55f; sm.hp = GameConfig.TankHp; break;
                case SnowmanKind.Splitter:
                    sm.size *= 1.05f; sm.hp = 1; break;
                case SnowmanKind.Banner:
                    sm.size *= 1.1f; sm.Speed *= 0.85f; sm.hp = GameConfig.BannerHp; break;
                case SnowmanKind.Bomber:
                    sm.size *= 1.1f; sm.Speed *= 0.8f; sm.hp = GameConfig.BomberHp; break;
                default:
                    sm.hp = 1; break;
            }

            sm.Build();
            all.Add(sm);
            return sm;
        }

        /// <summary>Lets the director hand a freshly spawned snowman its owner reference (used by
        /// the Bomber to lob shots at the cannon and by a Splitter to spawn its children).</summary>
        public void SetOwner(SnowCannonGame o) { owner = o; }

        int textureVariant;

        void Build()
        {
            var snow = Mat.SnowMaterial(textureVariant);
            Register(snow);
            // The rolling bottom ball gets its own dirtier skin: the scattered dirt clumps
            // travel with the spin, so the ball's rotation is visible as it marches.
            var snowDirty = Mat.SnowMaterialDirt(textureVariant);
            Register(snowDirty);

            // The three stacked balls. The lowest one is the rolling wheel. They are built from
            // a small shared pool of noise-displaced spheres so they read as hand-rolled lumps,
            // not perfect billiard balls. Each snowman picks its own variant so the field varies.
            int ballVariant = Random.Range(0, 100000);
            bottomBall = AddLumpyBall("ball_bottom", snowDirty, new Vector3(0, Yb, 0), Rb * 2f, ballVariant);
            AddLumpyBall("ball_middle", snow, new Vector3(0, Ym, 0), Rm * 2f, ballVariant + 1);
            AddLumpyBall("ball_head", snow, new Vector3(0, Yt, 0), Rt * 2f, ballVariant + 2);

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

            // An animated mouth: a dark cavity, a gappy row of upper teeth, and a hinged lower
            // jaw that opens and closes at random while the snowman marches.
            BuildMouth(coal);

            // A per-snowman hair style: the classic pot, or icicles, a punk mohawk, or a shaggy
            // snow afro, so the field never reads as a row of identical snowmen.
            BuildHair(pot);

            // Branch arms with hands, rooted in the middle ball and angled up and outward.
            BuildArm("arm_l", branch, new Vector3(-Rm * 0.5f, Ym + Rm * 0.1f, 0), -1f);
            BuildArm("arm_r", branch, new Vector3(Rm * 0.5f, Ym + Rm * 0.1f, 0), 1f);

            // Archetype-specific silhouette so each kind is readable at a glance.
            BuildKindExtras();

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
            Mat.SetShadows(gameObject, true, false);

            // Start collapsed to (almost) nothing so the first Update's spawn-in animation grows
            // it up out of the snow instead of showing a full-size snowman for one frame.
            transform.localScale = Vector3.one * (size * 0.001f);
        }

        void BuildMouth(Material coal)
        {
            var tooth = Mat.Opaque(new Color(0.96f, 0.97f, 1f, 1f));
            Register(tooth);
            float my = Yt - Rt * 0.30f;      // mouth centre height
            float mz = -Rt * 0.86f;           // front of the face
            float mw = Rt * 0.62f;             // half width of the mouth

            // Dark open cavity behind the teeth.
            AddMeshPart("mouth_cavity", MeshFactory.TruncatedCone(Rt * 0.02f, mw * 0.9f, Rt * 0.18f, 12), coal,
                        new Vector3(0, my, mz + Rt * 0.05f), Vector3.one, new Vector3(90f, 0f, 0f));

            // Upper teeth: a row of small boxes, a couple randomly missing for a gappy grin.
            int teeth = 5;
            var miss = new bool[teeth];
            int missing = Random.Range(0, 3);
            for (int k = 0; k < missing; k++) miss[Random.Range(0, teeth)] = true;
            for (int i = 0; i < teeth; i++)
            {
                if (miss[i]) continue;
                float tx = Mathf.Lerp(-mw, mw, (i + 0.5f) / teeth);
                AddMeshPart("tooth_u" + i, MeshFactory.TruncatedCone(0.03f, 0.02f, Rt * 0.14f, 6), tooth,
                            new Vector3(tx, my + Rt * 0.10f, mz), Vector3.one, Vector3.zero);
            }

            // Lower jaw: a pivot at the back of the mouth that swings the lower lip + teeth open.
            var pivot = new GameObject("jaw").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0, my - Rt * 0.02f, mz + Rt * 0.2f);
            jaw = pivot;
            AddMeshPartTo(pivot, "lower_lip", MeshFactory.TruncatedCone(0.03f, mw * 0.8f, Rt * 0.12f, 12), coal,
                          new Vector3(0, -Rt * 0.06f, -Rt * 0.16f), Vector3.one, new Vector3(-90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float tx = Mathf.Lerp(-mw * 0.7f, mw * 0.7f, (i + 0.5f) / 3);
                AddMeshPartTo(pivot, "tooth_l" + i, MeshFactory.TruncatedCone(0.028f, 0.018f, Rt * 0.1f, 6), tooth,
                              new Vector3(tx, Rt * 0.02f, -Rt * 0.14f), Vector3.one, Vector3.zero);
            }
        }

        void BuildHair(Material pot)
        {
            hairStyle = Random.Range(0, 4);
            float crown = Yt + Rt * 0.86f;
            switch (hairStyle)
            {
                case 1: // icicles hanging off the crown
                {
                    var ice = Mat.Opaque(new Color(0.72f, 0.88f, 1f, 0.9f));
                    Register(ice);
                    int n = Random.Range(5, 9);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i / (float)n) * Mathf.PI * 2f;
                        float rr = Rt * Random.Range(0.5f, 0.85f);
                        float len = Rt * Random.Range(0.5f, 1.1f);
                        AddMeshPart("icicle" + i, MeshFactory.TruncatedCone(Rt * 0.12f, 0f, len, 7), ice,
                                    new Vector3(Mathf.Cos(a) * rr, crown, Mathf.Sin(a) * rr), Vector3.one,
                                    new Vector3(180f, 0f, 0f));
                    }
                    break;
                }
                case 2: // punk mohawk: a line of upward spikes
                {
                    var spikeMat = Mat.Opaque(GameConfig.PotColors[Random.Range(0, GameConfig.PotColors.Length)]);
                    Register(spikeMat);
                    int n = Random.Range(5, 8);
                    for (int i = 0; i < n; i++)
                    {
                        float z = Mathf.Lerp(-Rt * 0.7f, Rt * 0.7f, (i + 0.5f) / n);
                        float h = Rt * Random.Range(0.7f, 1.3f);
                        AddMeshPart("spike" + i, MeshFactory.TruncatedCone(Rt * 0.16f, 0f, h, 7), spikeMat,
                                    new Vector3(0, crown, z), Vector3.one, Vector3.zero);
                    }
                    break;
                }
                case 3: // a shaggy snow afro
                {
                    var afro = Mat.SnowMaterial(Random.Range(0, 6));
                    Register(afro);
                    AddMeshPart("afro", MeshFactory.LumpySphere(MeshFactory.UVSphere(10, 14), 0.06f, 3.3f), afro,
                                new Vector3(0, crown + Rt * 0.1f, 0),
                                new Vector3(Rt * 2.1f, Rt * 1.5f, Rt * 2.1f), Vector3.zero);
                    break;
                }
                default: // the classic pot bucket
                {
                    AddMeshPart("pot", MeshFactory.TruncatedCone(Rt * 0.55f, Rt * 0.86f, Rt * 0.9f, 14), pot,
                                new Vector3(0, crown, 0), Vector3.one, Vector3.zero);
                    AddMeshPart("pot_rim", MeshFactory.TruncatedCone(Rt * 0.92f, Rt * 0.86f, Rt * 0.16f, 14), pot,
                                new Vector3(0, crown + Rt * 0.86f, 0), Vector3.one, Vector3.zero);
                    break;
                }
            }
        }

        Transform AddMeshPartTo(Transform parent, string name, Mesh mesh, Material mat,
                                Vector3 localPos, Vector3 scale, Vector3 localEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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

        /// <summary>Adds the per-archetype colour/accessory so the field reads at a glance: a red
        /// scarf for Runners, a heavy dark vest for Tanks, a green bandana for Splitters, a flagpole
        /// and ground aura for Banners, and a dark belly shell for Bombers.</summary>
        void BuildKindExtras()
        {
            switch (Kind)
            {
                case SnowmanKind.Runner:
                {
                    var scarf = Mat.Opaque(new Color(0.85f, 0.16f, 0.18f, 1f));
                    Register(scarf);
                    AddMeshPart("scarf", MeshFactory.TruncatedCone(Rm * 0.5f, Rm * 0.62f, Rm * 0.28f, 12), scarf,
                                new Vector3(0, Ym + Rm * 0.9f, 0), Vector3.one, Vector3.zero);
                    break;
                }
                case SnowmanKind.Tank:
                {
                    var vest = Mat.Opaque(new Color(0.2f, 0.22f, 0.26f, 1f));
                    Register(vest);
                    AddMeshPart("vest", MeshFactory.TruncatedCone(Rm * 0.7f, Rm * 0.95f, Rm * 1.1f, 14), vest,
                                new Vector3(0, Ym, 0), Vector3.one, Vector3.zero);
                    if (bottomBall != null) bottomBall.localScale = Vector3.one * (Rb * 2.5f);
                    break;
                }
                case SnowmanKind.Splitter:
                {
                    var band = Mat.Opaque(new Color(0.2f, 0.7f, 0.3f, 1f));
                    Register(band);
                    AddMeshPart("band", MeshFactory.TruncatedCone(Rm * 0.55f, Rm * 0.66f, Rm * 0.24f, 12), band,
                                new Vector3(0, Ym + Rm * 0.95f, 0), Vector3.one, Vector3.zero);
                    break;
                }
                case SnowmanKind.Banner:
                {
                    var pole = Mat.Opaque(new Color(0.45f, 0.3f, 0.15f, 1f));
                    Register(pole);
                    AddMeshPart("pole", MeshFactory.TruncatedCone(0.05f, 0.05f, Rt * 2.4f, 6), pole,
                               new Vector3(Rt * 0.7f, Yt + Rt * 0.4f, 0), Vector3.one, Vector3.zero);
                    var flag = Mat.Opaque(new Color(0.9f, 0.7f, 0.1f, 1f));
                    Register(flag);
                    var flagGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    flagGo.name = "flag";
                    flagGo.transform.SetParent(transform, false);
                    flagGo.transform.localPosition = new Vector3(Rt * 1.15f, Yt + Rt * 1.4f, 0f);
                    flagGo.transform.localScale = new Vector3(Rt * 1.1f, Rt * 0.7f, 0.02f);
                    flagGo.GetComponent<MeshRenderer>().material = flag;
                    Destroy(flagGo.GetComponent<Collider>());
                    Track(flagGo, flag);

                    // A faint ground ring marking the buff radius.
                    var aura = new GameObject("aura");
                    aura.transform.SetParent(transform, false);
                    aura.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                    var mf = aura.AddComponent<MeshFilter>();
                    mf.sharedMesh = MeshFactory.FlatRing(GameConfig.BannerAuraRadius * 0.9f, GameConfig.BannerAuraRadius, 40);
                    var rr = aura.AddComponent<MeshRenderer>();
                    var am = Mat.Transparent(new Color(0.95f, 0.8f, 0.2f, 0.5f));
                    am.SetFloat("_Cull", 0f);
                    rr.material = am;
                    Register(am);
                    auraRing = aura.transform;
                    break;
                }
                case SnowmanKind.Bomber:
                {
                    var shell = Mat.Opaque(new Color(0.15f, 0.16f, 0.2f, 1f));
                    Register(shell);
                    AddSphere("shell", shell, new Vector3(0, Ym, -Rm * 0.9f), Rm * 0.5f);
                    break;
                }
            }
        }

        /// <summary>Buffs every live snowman within the banner's radius up to the boost speed. The
        /// boost decays on each snowman, so killing the banner lets the buffed ones slow back down.</summary>
        void ApplyBannerAura()
        {
            float r2 = GameConfig.BannerAuraRadius * GameConfig.BannerAuraRadius;
            var me = transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || o == this || o.IsDying || o.IsDone) continue;
                var d = o.transform.position - me;
                if (d.x * d.x + d.z * d.z <= r2)
                    o.speedBoost = Mathf.Max(o.speedBoost, GameConfig.BannerSpeedBoost);
            }
        }

        /// <summary>Hurls a snow shell at the player's cannon.</summary>
        void LobBomberShot()
        {
            if (owner == null) return;
            BomberShot.Launch(transform.position + Vector3.up * Ym, owner.CannonWorldPos, owner);
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

        // A small shared pool of hand-rolled lumpy spheres, built once and reused by every
        // snowman. Keyed by variant so the field varies but we never rebuild geometry per spawn.
        static readonly Mesh[] lumpyPool = new Mesh[8];

        static Mesh LumpyMesh(int variant)
        {
            int idx = Mathf.Abs(variant) % lumpyPool.Length;
            var cached = lumpyPool[idx];
            if (cached != null) return cached;
            // Subtle amplitude so the balls read as slightly irregular, not deformed.
            float seed = idx * 12.9898f + 1.7f;
            var mesh = MeshFactory.LumpySphere(MeshFactory.UVSphere(16, 24), 0.045f, seed);
            lumpyPool[idx] = mesh;
            return mesh;
        }

        Transform AddLumpyBall(string name, Material mat, Vector3 localPos, float diameter, int variant)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = LumpyMesh(variant);
            var r = go.AddComponent<MeshRenderer>();
            r.material = mat;
            Track(go, mat);
            return go.transform;
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
            // The march is scaled by the shared weather/upgrade globals: a blizzard (or the CHILL
            // FIELD upgrade) slows the whole field, an ice patch slickens it so they slide faster.
            float fieldMod = (1f - Mathf.Clamp01(GameRuntime.FieldSlow)) *
                             (1f + Mathf.Clamp01(GameRuntime.IceFactor) * 0.5f);
            p += dir * (Speed * (1f + speedBoost) * fieldMod * Time.deltaTime);

            // Bounce off the visible screen edges so a snowman always stays on screen. The limit
            // is the field width that is actually visible at THIS depth (the camera is a pinhole,
            // so the far rows are narrower on screen than the near rows), which makes them reflect
            // right at the edge of the display instead of at an invisible logical boundary.
            float limit = GameConfig.VisibleHalfWidthAtZ(Camera.main, p.z) - 0.6f * size;
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

            // The mouth opens and closes at random while the snowman marches.
            if (jaw != null)
            {
                jawTimer -= Time.deltaTime;
                if (jawTimer <= 0f)
                {
                    jawTimer = Random.Range(0.5f, 2.2f);
                    jawOpen = Random.value < 0.55f ? Random.Range(0.15f, 1f) : 0f;
                }
                jawPhase = Mathf.MoveTowards(jawPhase, jawOpen, Time.deltaTime * 6f);
                jaw.localEulerAngles = new Vector3(-jawPhase * 34f, 0f, 0f);
            }

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

            // Archetype behaviours, active only once fully spawned in: a Banner periodically buffs
            // nearby snowmen (the boost decays, so killing the banner lets them slow back down); a
            // Bomber lobs a shell at the cannon on its own cadence.
            if (!spawningIn)
            {
                if (speedBoost > 0f) speedBoost = Mathf.Max(0f, speedBoost - Time.deltaTime * 0.5f);
                if (Kind == SnowmanKind.Banner)
                {
                    bannerTimer -= Time.deltaTime;
                    if (bannerTimer <= 0f) { bannerTimer = 0.6f; ApplyBannerAura(); }
                }
                else if (Kind == SnowmanKind.Bomber)
                {
                    bomberTimer -= Time.deltaTime;
                    if (bomberTimer <= 0f) { bomberTimer = Random.Range(2.2f, 3.6f); LobBomberShot(); }
                }
            }

            // Spawn-in "pop up from the snow": grow the stack up from nothing with a slight elastic
            // overshoot while it rises from just under the snow surface to resting on it. Done last
            // so it overrides the march's scale/height for the duration of the pop.
            if (spawningIn)
            {
                spawnInTimer += Time.deltaTime;
                float k = Mathf.Clamp01(spawnInTimer / SpawnInTime);
                // Elastic ease-out (overshoots just past 1 then settles) for a lively "pop".
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                float q = k - 1f;
                float e = 1f + c3 * q * q * q + c1 * q * q;
                float s = size * Mathf.Max(0.001f, e);
                transform.localScale = Vector3.one * s;
                var sp = transform.position;
                sp.y = Mathf.Lerp(-Rb * size * 1.6f, 0f, k);
                transform.position = sp;
                HeadWorldY = sp.y + Yt * s;
                if (k >= 1f)
                {
                    spawningIn = false;
                    transform.localScale = Vector3.one * size;
                    var fp = transform.position;
                    fp.y = 0f;
                    transform.position = fp;
                    HeadWorldY = Yt * size;
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

            // If it is still popping up out of the snow, snap it to full size first so the death
            // burst detaches the parts at their real scale instead of a shrunken one.
            if (spawningIn)
            {
                spawningIn = false;
                transform.localScale = Vector3.one * size;
                var gp = transform.position;
                gp.y = 0f;
                transform.position = gp;
                HeadWorldY = Yt * size;
            }

            // Tougher archetypes soak several hits. A non-fatal hit only chips the stack (a small
            // knock for feedback) and scores nothing, so the player must land several shots on a
            // Tank/Banner/Bomber before it bursts. The snowball despawns on any connect, so each
            // shot costs exactly one hit.
            hp--;
            if (hp > 0)
            {
                if (bottomBall != null) bottomBall.Rotate(Vector3.forward, Random.Range(-20f, 20f), Space.Self);
                return false;
            }

            points = hitPoint.y >= HeadWorldY - Rt * size * 0.45f
                       ? GameConfig.PointsHead
                       : GameConfig.PointsBody;

            // A Splitter bursts into a small wave of Runners on its killing blow, so the player is
            // rewarded for taking it out EARLY (while far away) rather than letting it reach the line.
            if (Kind == SnowmanKind.Splitter && owner != null)
            {
                var basePos = transform.position;
                int kids = Random.Range(2, 4);
                for (int k = 0; k < kids; k++)
                {
                    float ox = Random.Range(-1.2f, 1.2f);
                    float oz = Random.Range(-0.6f, 0.8f);
                    owner.SpawnAt(SnowmanKind.Runner, basePos + new Vector3(ox, 0f, oz),
                                  GameConfig.SnowmanMinScale, GameConfig.SnowmanMinSpeed * 1.15f);
                }
            }

            IsDying = true;
            fadeTimer = 0f;

            // Detach every part and fling it so the stack visibly collapses. The fling style is
            // picked per snowman so different ones die in different ways.
            foreach (var c in chunks)
            {
                c.detached = true;
                c.transform.SetParent(null, true);
                Vector3 v, av;
                switch (deathStyle)
                {
                    case 1: // confetti pop straight up
                        v = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(3.5f, 6f), Random.Range(-0.5f, 1.5f));
                        av = new Vector3(Random.Range(-420f, 420f), Random.Range(-420f, 420f), Random.Range(-420f, 420f));
                        break;
                    case 2: // collapse down with a low bounce
                        v = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.4f, 1.4f), Random.Range(-0.4f, 1.2f));
                        av = new Vector3(Random.Range(-120f, 120f), Random.Range(-120f, 120f), Random.Range(-120f, 120f));
                        break;
                    case 3: // tornado spin
                        v = new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(2f, 4f), Random.Range(-1f, 2f));
                        av = new Vector3(Random.Range(-600f, 600f), Random.Range(-600f, 600f), Random.Range(-600f, 600f));
                        break;
                    default: // classic outward burst
                        v = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(1.4f, 3.4f), Random.Range(-0.6f, 2.2f));
                        av = new Vector3(Random.Range(-260f, 260f), Random.Range(-260f, 260f), Random.Range(-260f, 260f));
                        break;
                }
                c.velocity = v;
                c.angularVelocity = av;
            }
            return true;
        }

        /// <summary>Called when the snowman wanders into the lake, its grass bank, or the supply
        /// hose. It is NOT a breach -- the figure simply collapses DOWN into the water and fades
        /// out, so reaching the pond removes the threat without ending the run. Mirrors the shot
        /// death's chunk detach but with a downward, inward velocity so the stack sinks rather than
        /// bursting outward.</summary>
        public void Melt()
        {
            if (IsDying || IsDone) return;
            IsDying = true;
            fadeTimer = 0f;

            foreach (var c in chunks)
            {
                c.detached = true;
                c.transform.SetParent(null, true);
                // A gentle sink: a little inward drift, a downward push, a lazy tumble. UpdateDeath
                // adds gravity and fades the alpha, so the stack dissolves as it drops into the pond.
                c.velocity = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-1.4f, -0.5f), Random.Range(-0.25f, 0.25f));
                c.angularVelocity = new Vector3(Random.Range(-90f, 90f), Random.Range(-90f, 90f), Random.Range(-90f, 90f));
            }
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

    /// <summary>
    /// A snow shell hurled by a Bomber at the player's cannon. It flies a short ballistic lob and,
    /// when it lands close to the cannon, briefly jams the firing mechanism. It is a nuisance
    /// projectile only: it scores nothing and cannot be shot for points.
    /// </summary>
    public sealed class BomberShot : MonoBehaviour
    {
        Vector3 velocity;
        float life;
        bool dead;
        Vector3 target;
        SnowCannonGame owner;

        const float G = 26f;
        const float UpSpeed = 12f;

        public static void Launch(Vector3 origin, Vector3 target, SnowCannonGame owner)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BomberShot";
            go.transform.position = origin;
            var s = go.AddComponent<BomberShot>();
            s.target = target;
            s.owner = owner;

            Vector2 h = new Vector2(target.x - origin.x, target.z - origin.z);
            float dist = h.magnitude;
            if (h.sqrMagnitude < 1e-4f) h = new Vector2(0f, 1f);
            h.Normalize();
            float flight = 2f * UpSpeed / G;                       // time to rise and fall back to launch height
            float horiz = Mathf.Min(30f, dist / Mathf.Max(0.3f, flight));
            s.velocity = new Vector3(h.x * horiz, UpSpeed, h.y * horiz);
            s.life = flight + 0.6f;

            var r = go.GetComponent<MeshRenderer>();
            r.material = Mat.Opaque(new Color(0.16f, 0.17f, 0.21f, 1f));
            var col = go.GetComponent<SphereCollider>();
            if (col != null) Destroy(col);
            go.transform.localScale = Vector3.one * 0.7f;
        }

        void Update()
        {
            if (dead) return;
            life -= Time.deltaTime;
            velocity.y -= G * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;

            var p = transform.position;
            bool landed = p.y <= 0.12f || life <= 0f ||
                          p.z > GameConfig.FieldMaxZ + 6f || Mathf.Abs(p.x) > 40f;
            if (landed)
            {
                // Only jam the cannon if the shell actually fell near it; a wide miss is a dud.
                float dx = p.x - target.x, dz = p.z - target.z;
                if (owner != null && dx * dx + dz * dz <= 9f) owner.JamCannon(GameConfig.BomberJamTime);
                dead = true;
                Destroy(gameObject);
            }
        }
    }
}
