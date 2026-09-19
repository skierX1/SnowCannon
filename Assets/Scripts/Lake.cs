using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// The cannon's water reservoir, drawn as a real little 3D pond sitting on the snow in the
    /// bottom-left of the field: a snowy bank, a flared basin wall, a dark bed, and a body of
    /// water whose rippling surface rises and falls with the current level. It doubles as the
    /// ammunition gauge: every snowman that spawns pours 2 marks in, every shot spends 1, and the
    /// cannon can only fire while the lake holds water. A floating number above the pond shows the
    /// exact level, and a yellow hose runs along the bottom of the screen from the pond to the
    /// cannon. Everything is built from procedural meshes + the always-included URP shaders, so it
    /// renders identically on desktop, Android and iOS.
    /// </summary>
    public sealed class Lake : MonoBehaviour
    {
        public int Marks { get; private set; }
        public bool CanFire => Marks > 0;

        /// <summary>World position of the pond's centre, used to route the supply hose.</summary>
        public Vector3 WorldPosition => transform.position;

        // The reservoir's capacity grows by LakeMarksPerLevel marks for every level beyond the
        // first, so a longer run can bank more water. The director drives this at each level
        // boundary via SetCapacityForLevel; until then it sits at the base capacity.
        int capacity = GameConfig.LakeMaxMarks;
        int Max { get { return capacity; } }
        public void SetCapacityForLevel(int level)
        {
            capacity = GameConfig.LakeMaxMarks + Mathf.Max(0, level - 1) * GameConfig.LakeMarksPerLevel;
        }
        const int SpawnGain = GameConfig.LakeSpawnGain;
        const int FireCost = GameConfig.LakeFireCost;

        // Pond geometry (world units). The basin is deliberately shallow and the water is kept
        // high near the rim, so the pond always reads as a filled lake (not a dark empty bowl)
        // while the surface still visibly rises and falls with the level.
        const float BankR = 1.78f;
        const float BasinTopR = 1.40f;
        const float BasinBotR = 1.05f;
        const float BasinH = 0.40f;
        const float WaterR = 1.30f;
        const float WaterMinY = 0.20f;
        const float WaterMaxY = 0.42f;
        const float FloorY = 0.06f;

        // One shared organic seed + amplitude so every pond piece (bank, rim, basin, water) shares
        // the same blobby outline and the shoreline nests perfectly into an irregular, natural edge.
        const float Seed = 2.37f;
        const float Amp = 0.16f;

        Camera cam;
        Transform waterBody;
        Mesh waterSurfaceMesh;
        Vector3[] waterBase, waterScratch;
        Renderer waterSurfaceRend, floorRend;
        Material waterSurfaceMat, waterBodyMat;
        Texture2D waterTex;
        List<Transform> lilies;
        float flash;
        Vector3 basePos;
        // The corner-anchored centre the pond is pinned to each frame. The dry-flash shake offsets
        // from this so the corner lock and the shake never fight over transform.position.
        Vector3 anchorPos;
        float displayLevel;   // eased toward the true level so the water glides up/down

        // The supply hose that feeds the cannon, with a scrolling flow texture and a travelling
        // peristaltic squish wave that visibly pushes water along the tube toward the cannon.
        Material hoseMat;
        float hoseScroll;
        Mesh hoseMesh;
        Vector3[] hoseBaseVerts, hoseNormals, hoseScratch;
        float[] hoseRingT;
        float hoseWavePhase;
        // The hose centreline in world space, kept so the director can test a snowman against the tube.
        List<Vector3> hosePts;
        // Eased 0..1 pump strength. Ramps up while the lake holds water and down to 0 when it runs
        // dry, so the flow-texture scroll and the peristaltic squish both fade out (the tube settles
        // back to a still, round shape) exactly when the lake is empty, and resume when it refills.
        float hoseFlow = 1f;

        // The whole pond's authored footprint. Reduced 1.5x from the previous 3.375 (which was
        // itself a 1.5x step-up on 2.25) so the lake reads as a comfortable corner feature without
        // dominating the field. The ACTUAL on-screen size is then fixed per screen resolution by
        // SizeAgainstCannon, which only ever scales this DOWN to clear the cannon's leftmost reach.
        const float Scale = 2.25f;

        // The pond's full footprint radius (the grass sward is the widest element at BankR*1.18),
        // used to test the lake against the cannon and shrink it out of any overlap.
        const float FootR = BankR * 1.18f;

        // The cannon the pond must never touch. The pond is sized against the cannon's FIXED
        // leftmost reach (Cannon.LeftmostX), never its live position, so the lake never pulses in
        // size as the turret turns. See SizeAgainstCannon.
        Transform cannon;
        Cannon cannonComp;

        // The screen pixel size the pond was last sized for. Its scale is FIXED for the whole run
        // and only recomputed when this changes (a phone rotation or a desktop window resize),
        // never as the cannon aims. Initialised to an impossible value so the first Update sizes it.
        int sizedForWidth = -1;
        int sizedForHeight = -1;

        // The cannon leftmost-reach value the pond was last sized against. The cannon's leftmost X
        // is FIXED while aiming (it only depends on basePos/halfTravel), so this never changes
        // mid-run and the lake never pulses; it exists only so the lake re-sizes once if the
        // cannon's Start() computes halfTravel AFTER the lake's first frame (ordering safety).
        float sizedForLeftmost = float.NaN;

        // The world depth the pond is parked at (near the front of the field = bottom of screen).
        const float LakeZ = -7.0f;

        // The normalised screen position the pond's centre is pinned to each frame: the bottom-left
        // corner (a touch inside the edges so the whole disc stays visible). Viewport space is
        // resolution-independent, so this holds on every phone model and desktop window size.
        const float CornerVX = 0.20f;
        const float CornerVY = 0.17f;

        static readonly Color BankColor = new Color(0.94f, 0.965f, 1f, 1f);
        static readonly Color BasinColor = new Color(0.16f, 0.24f, 0.27f, 1f);
        static readonly Color BasinDry = new Color(0.62f, 0.14f, 0.14f, 1f);
        static readonly Color WaterLow = new Color(0.16f, 0.44f, 0.82f, 1f);
        static readonly Color WaterFull = new Color(0.22f, 0.74f, 0.92f, 1f);
        static readonly Color MudColor = new Color(0.55f, 0.45f, 0.32f, 1f);
        static readonly Color ReedGreen = new Color(0.20f, 0.52f, 0.22f, 1f);
        static readonly Color CattailBrown = new Color(0.45f, 0.28f, 0.14f, 1f);
        static readonly Color RockGray = new Color(0.55f, 0.57f, 0.60f, 1f);
        static readonly Color LilyGreen = new Color(0.16f, 0.48f, 0.22f, 1f);
        static readonly Color LilyBud = new Color(0.96f, 0.72f, 0.86f, 1f);

        /// <summary>Builds the 3D pond in the world, placed bottom-left of the visible field.</summary>
        public static Lake Create(Transform worldParent, Camera cam)
        {
            var go = new GameObject("Lake");
            var lake = go.AddComponent<Lake>();
            lake.cam = cam;

            // Park the pond at the front of the field (bottom of the screen) and tuck it exactly
            // into the bottom-left corner of the visible field, sized so its bank meets the screen
            // edge without overhanging it. PlaceInCorner is re-run every frame so the corner lock
            // survives any screen-ratio change (different phone models, desktop resolutions).
            go.transform.position = new Vector3(0f, 0f, LakeZ);
            go.transform.localScale = Vector3.one * Scale;
            lake.cam = cam;
            lake.PlaceInCorner();

            lake.Build();
            return lake;
        }

        /// <summary>Hands the lake the cannon transform so it can shrink itself clear of the
        /// muzzle on narrow screens where the corner pond and the aiming cannon would otherwise
        /// collide. Safe to call after the cannon exists.</summary>
        public void SetCannon(Transform c)
        {
            cannon = c;
            cannonComp = c != null ? c.GetComponent<Cannon>() : null;
        }

        /// <summary>A bright white-gravel material for the pond's inner basin and shoreline:
        /// the pebbled Gravel texture under a near-white tint, tiled to the caller's density.
        /// Replaces the old dark-grey basin so the lake floor and shore read as clean pale stones.</summary>
        Material GravelMaterial(float tile = 3f)
        {
            var tex = TextureFactory.Gravel();
            tex.wrapMode = TextureWrapMode.Repeat;
            var m = Mat.Textured(tex, new Color(0.93f, 0.94f, 0.96f, 1f));
            m.SetFloat("_Smoothness", 0.12f);
            m.SetTextureScale("_BaseMap", new Vector2(tile, tile));
            m.name = "mat_gravel";
            return m;
        }

        void Build()
        {
            basePos = transform.position;
            waterTex = TextureFactory.WaterSwirl();

            // Snowy bank: an organic, blobby disc the whole pond sits in.
            var bank = new GameObject("bank");
            bank.transform.SetParent(transform, false);
            bank.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            bank.AddComponent<MeshFilter>().sharedMesh = MeshFactory.WaveDiscOrganic(BankR, 2, 52, Seed, Amp);
            bank.AddComponent<MeshRenderer>().material = Mat.Opaque(BankColor);

            // Muddy shoreline ring just inside the snow, echoing the reference's tan rim.
            var rim = new GameObject("mud_rim");
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            rim.AddComponent<MeshFilter>().sharedMesh = MeshFactory.WaveDiscOrganic(BasinTopR * 1.05f, 2, 48, Seed, Amp);
            rim.AddComponent<MeshRenderer>().material = GravelMaterial(4f);

            // Basin wall: a flared organic bowl, double-sided so we see the inside.
            var wall = new GameObject("basin_wall");
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = new Vector3(0f, FloorY, 0f);
            wall.AddComponent<MeshFilter>().sharedMesh =
                MeshFactory.TruncatedConeOrganic(BasinBotR, BasinTopR, BasinH, 48, Seed, Amp, false, false);
            var wallMat = GravelMaterial(3f);
            wallMat.SetFloat("_Cull", 0f);
            wall.AddComponent<MeshRenderer>().material = wallMat;

            // Dark bed at the bottom of the bowl.
            var floor = new GameObject("basin_floor");
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, FloorY + 0.01f, 0f);
            floor.AddComponent<MeshFilter>().sharedMesh = MeshFactory.WaveDiscOrganic(BasinBotR * 0.98f, 2, 32, Seed, Amp);
            floorRend = floor.AddComponent<MeshRenderer>();
            floorRend.material = GravelMaterial(3f);

            // Water body: an organic capped cylinder from the bed up to the surface, scaled by
            // the level. Textured with the swirl so it reads as a mass of water.
            waterBody = new GameObject("water_body").transform;
            waterBody.SetParent(transform, false);
            waterBody.localPosition = new Vector3(0f, FloorY, 0f);
            waterBody.gameObject.AddComponent<MeshFilter>().sharedMesh =
                MeshFactory.TruncatedConeOrganic(WaterR, WaterR, 1f, 44, Seed, Amp, true, true);
            waterBodyMat = Mat.Textured(waterTex, new Color(0.85f, 0.92f, 1f, 1f));
            waterBody.gameObject.AddComponent<MeshRenderer>().material = waterBodyMat;

            // Water surface: a rippling, lightly transparent organic disc that rides on top of the
            // body, textured with the same swirl (slowly scrolling) so the level reads as water.
            var surf = new GameObject("water_surface");
            surf.transform.SetParent(transform, false);
            surf.transform.localPosition = new Vector3(0f, WaterMinY, 0f);
            waterSurfaceMesh = MeshFactory.WaveDiscOrganic(WaterR, 6, 44, Seed, Amp);
            waterBase = waterSurfaceMesh.vertices;
            waterScratch = new Vector3[waterBase.Length];
            surf.AddComponent<MeshFilter>().sharedMesh = waterSurfaceMesh;
            waterSurfaceRend = surf.AddComponent<MeshRenderer>();
            waterSurfaceMat = Mat.Textured(waterTex, new Color(0.82f, 0.92f, 1f, 1f));
            Mat.MakeTransparent(waterSurfaceMat);
            waterSurfaceMat.SetFloat("_Cull", 0f);
            waterSurfaceMat.renderQueue = 3000;
            waterSurfaceRend.material = waterSurfaceMat;

            BuildReeds();
            BuildRocks();
            BuildLilies();
            BuildGrass();

            displayLevel = Mathf.Clamp01(GameConfig.LakeStartMarks / (float)Max);
            Marks = Mathf.Clamp(GameConfig.LakeStartMarks, 0, Max);
            ApplyWater();
        }

        /// <summary>A scatter of reed stalks (some topped with a brown cattail) ringing the shore.</summary>
        void BuildReeds()
        {
            var parent = new GameObject("reeds");
            parent.transform.SetParent(transform, false);
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                float rad = Mathf.Lerp(BasinTopR * 0.98f, BankR * 0.9f, Random.value);
                BuildReed(parent.transform, new Vector3(Mathf.Cos(ang) * rad, 0.02f, Mathf.Sin(ang) * rad),
                          Random.Range(0.5f, 0.95f), Random.Range(-0.12f, 0.12f), Random.value < 0.5f);
            }
        }

        void BuildReed(Transform parent, Vector3 b0, float height, float lean, bool cattail)
        {
            var pts = new List<Vector3>();
            const int steps = 4;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float bend = lean * t * t;
                pts.Add(new Vector3(b0.x + bend, b0.y + height * t, b0.z + bend * 0.5f));
            }
            var stalk = new GameObject("reed");
            stalk.transform.SetParent(parent, false);
            stalk.AddComponent<MeshFilter>().sharedMesh = MeshFactory.TubeAlongPath(pts, 0.018f, 5);
            stalk.AddComponent<MeshRenderer>().material = Mat.Opaque(ReedGreen);

            if (cattail)
            {
                Vector3 top = pts[steps];
                var head = new GameObject("cattail");
                head.transform.SetParent(parent, false);
                head.transform.localPosition = new Vector3(top.x, top.y + 0.02f, top.z);
                head.AddComponent<MeshFilter>().sharedMesh = MeshFactory.TruncatedCone(0.045f, 0.03f, 0.16f, 8);
                head.AddComponent<MeshRenderer>().material = Mat.Opaque(CattailBrown);
            }
        }

        /// <summary>A few grey boulders half-sunk into the shoreline.</summary>
        void BuildRocks()
        {
            var parent = new GameObject("rocks");
            parent.transform.SetParent(transform, false);
            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                float rad = Mathf.Lerp(BasinTopR * 1.02f, BankR * 0.95f, Random.value);
                float size = Random.Range(0.16f, 0.34f);
                var rock = new GameObject("rock");
                rock.transform.SetParent(parent.transform, false);
                rock.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, -size * 0.28f, Mathf.Sin(ang) * rad);
                rock.transform.localScale = new Vector3(size, size * Random.Range(0.7f, 1f), size);
                rock.AddComponent<MeshFilter>().sharedMesh = MeshFactory.LumpySphere(MeshFactory.UVSphere(8, 12), 0.12f, Random.Range(0f, 9f));
                rock.AddComponent<MeshRenderer>().material = Mat.Opaque(RockGray);
            }
        }

        /// <summary>Blobby lily pads (some with a bud) that float on the water and ride its level.</summary>
        void BuildLilies()
        {
            lilies = new List<Transform>();
            var parent = new GameObject("lilies");
            parent.transform.SetParent(transform, false);
            const int count = 4;
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = Mathf.Lerp(WaterR * 0.35f, WaterR * 0.82f, Random.value);
                float size = Random.Range(0.16f, 0.26f);
                var pad = new GameObject("lily");
                pad.transform.SetParent(parent.transform, false);
                pad.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, WaterMinY + 0.01f, Mathf.Sin(ang) * rad);
                pad.transform.localScale = new Vector3(size, size, size);
                pad.AddComponent<MeshFilter>().sharedMesh = MeshFactory.WaveDiscOrganic(1f, 1, 12, Random.Range(0f, 6.28f), 0.28f);
                pad.AddComponent<MeshRenderer>().material = Mat.Opaque(LilyGreen);
                lilies.Add(pad.transform);

                if (Random.value < 0.5f)
                {
                    var bud = new GameObject("bud");
                    bud.transform.SetParent(pad.transform, false);
                    bud.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                    bud.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
                    bud.AddComponent<MeshFilter>().sharedMesh = MeshFactory.UVSphere(6, 8);
                    bud.AddComponent<MeshRenderer>().material = Mat.Opaque(LilyBud);
                }
            }
        }

        /// <summary>Lays a yellow supply hose from the pond to the given world target (the cannon),
        /// hugging the bottom of the screen. Safe to call once after the cannon exists.</summary>
        public void ConnectHose(Vector3 targetWorld)
        {
            // Leave the pond from its cannon-facing rim and enter the cannon low on its back-left
            // flank, so the hose visibly feeds the machine from behind rather than the muzzle end.
            var from = transform.position + new Vector3(BasinTopR * 0.7f * Scale, 0.14f, 0.1f);
            var to = new Vector3(targetWorld.x - 0.95f, 0.16f, targetWorld.z - 0.85f);

            // A gentle S-curve: bow the run sideways with an opposing sine so it is not a straight rod.
            var axis = to - from; axis.y = 0f;
            float len = Mathf.Max(0.001f, axis.magnitude);
            Vector3 fwd = axis / len;
            Vector3 side = new Vector3(-fwd.z, 0f, fwd.x);   // horizontal perpendicular

            var pts = new List<Vector3>();
            const int steps = 26;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = Vector3.Lerp(from, to, t);
                // Two opposing lateral bows (an S) plus a ground-hugging sag with a mid rise.
                float bow = Mathf.Sin(t * Mathf.PI * 2f) * len * 0.05f;
                float sway = Mathf.Sin(t * Mathf.PI) * len * 0.02f;
                p += side * (bow + sway);
                p.y = 0.12f + Mathf.Sin(t * Mathf.PI) * 0.18f;
                pts.Add(p);
            }
            hosePts = pts;
            var go = new GameObject("supply_hose");
            var hoseFilter = go.AddComponent<MeshFilter>();
            hoseMesh = MeshFactory.TubeAlongPath(pts, 0.14f, 10);
            hoseFilter.sharedMesh = hoseMesh;
            var r = go.AddComponent<MeshRenderer>();
            var flowTex = TextureFactory.HoseFlow();
            hoseMat = Mat.Textured(flowTex, new Color(0.96f, 0.74f, 0.14f, 1f));
            hoseMat.SetTextureScale("_BaseMap", new Vector2(1f, 7f));   // tile the bands along the length
            r.material = hoseMat;

            // Capture the tube geometry so Update can run a peristaltic squish along it. Vertices
            // are laid out ring by ring (radialSegs + 1 per ring), so the ring index gives each
            // vertex its position t along the lake->cannon run.
            hoseBaseVerts = hoseMesh.vertices;
            hoseNormals = hoseMesh.normals;
            hoseScratch = new Vector3[hoseBaseVerts.Length];
            hoseRingT = new float[hoseBaseVerts.Length];
            int stride = 10 + 1;   // radialSegs + 1
            for (int i = 0; i < hoseBaseVerts.Length; i++)
                hoseRingT[i] = (i / stride) / (float)(pts.Count - 1);
        }

        /// <summary>True if a snowman of world radius <paramref name="r"/> centred at
        /// <paramref name="p"/> overlaps the pond (its bank/grass footprint) or the supply hose.
        /// The director uses this to end the run the instant a snowman reaches the lake.</summary>
        public bool TouchesSnowman(Vector3 p, float r)
        {
            var c = transform.position;
            float dx = p.x - c.x, dz = p.z - c.z;
            float lakeR = BankR * Scale + r;
            if (dx * dx + dz * dz <= lakeR * lakeR) return true;

            if (hosePts != null)
            {
                float hr = 0.14f + r;
                for (int i = 0; i + 1 < hosePts.Count; i++)
                    if (DistToSegmentXZ(p, hosePts[i], hosePts[i + 1]) <= hr * hr) return true;
            }
            return false;
        }

        static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float apx = p.x - a.x, apz = p.z - a.z;
            float len2 = abx * abx + abz * abz;
            float t = len2 > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / len2) : 0f;
            float cx = a.x + abx * t, cz = a.z + abz * t;
            float ddx = p.x - cx, ddz = p.z - cz;
            return ddx * ddx + ddz * ddz;
        }

        /// <summary>A green sward and a ring of blades hugging the pond, so it reads as a
        /// grass-fed spring rather than a hole punched in the snow.</summary>
        void BuildGrass()
        {
            var parent = new GameObject("grass");
            parent.transform.SetParent(transform, false);

            // A green sward disc poking out from under the snowy bank.
            var sward = new GameObject("sward");
            sward.transform.SetParent(parent.transform, false);
            sward.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            sward.AddComponent<MeshFilter>().sharedMesh =
                MeshFactory.WaveDiscOrganic(BankR * 1.18f, 3, 40, Seed + 1.1f, Amp * 1.3f);
            sward.AddComponent<MeshRenderer>().material = Mat.Opaque(new Color(0.24f, 0.5f, 0.22f, 1f));

            // A scatter of tapered green blades ringing the rim.
            var bladeMat = Mat.Opaque(new Color(0.28f, 0.56f, 0.24f, 1f));
            const int count = 26;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                float rad = Mathf.Lerp(BankR * 0.92f, BankR * 1.16f, Random.value);
                float h = Random.Range(0.18f, 0.42f);
                var blade = new GameObject("blade" + i);
                blade.transform.SetParent(parent.transform, false);
                blade.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, 0.02f, Mathf.Sin(ang) * rad);
                blade.transform.localEulerAngles = new Vector3(Random.Range(-16f, 16f), 0f, Random.Range(-16f, 16f));
                blade.AddComponent<MeshFilter>().sharedMesh = MeshFactory.TruncatedCone(0.03f, 0f, h, 5);
                blade.AddComponent<MeshRenderer>().material = bladeMat;
            }
        }

        public void OnSnowmanSpawned()
        {
            Marks = Mathf.Min(Max, Marks + SpawnGain);
        }

        /// <summary>Adds a one-off top-up of marks (clamped to the lake's capacity). Used at run start
        /// to fold in the RESERVE WATER upgrade / meta head-start.</summary>
        public void GrantMarks(int amount)
        {
            if (amount <= 0) return;
            Marks = Mathf.Min(Max, Marks + amount);
        }

        /// <summary>Spends a specific number of marks (premium shots cost more than one). Returns false
        /// and flashes when the lake cannot cover the cost.</summary>
        public bool OnFireAttemptMarks(int cost)
        {
            cost = Mathf.Max(1, cost);
            if (Marks < cost) { TriggerDryFlash(); return false; }
            Marks = Mathf.Max(0, Marks - cost);
            return true;
        }

        /// <summary>Spends one mark if there is water; returns false (and flashes) when dry.</summary>
        public bool OnFireAttempt()
        {
            if (Marks <= 0) { TriggerDryFlash(); return false; }
            Marks = Mathf.Max(0, Marks - FireCost);
            return true;
        }

        public void TriggerDryFlash() { flash = 1f; }

        void ApplyWater()
        {
            float f = displayLevel;
            float waterY = Mathf.Lerp(WaterMinY, WaterMaxY, f);

            if (waterBody != null)
            {
                float h = Mathf.Max(0.02f, waterY - FloorY);
                waterBody.localScale = new Vector3(1f, h, 1f);
                waterBody.localPosition = new Vector3(0f, FloorY, 0f);
                waterBody.gameObject.SetActive(Marks > 0);
            }
            if (waterBodyMat != null) waterBodyMat.SetColor("_BaseColor", Color.Lerp(new Color(0.82f, 0.9f, 1f, 1f), new Color(0.55f, 0.82f, 1f, 1f), f));
            if (waterSurfaceRend != null)
            {
                waterSurfaceRend.transform.localPosition = new Vector3(0f, waterY, 0f);
                waterSurfaceRend.enabled = Marks > 0;
            }

            // Lily pads float on the surface, so they rise and fall with the water level.
            if (lilies != null)
            {
                for (int i = 0; i < lilies.Count; i++)
                {
                    if (lilies[i] == null) continue;
                    var p = lilies[i].localPosition;
                    lilies[i].localPosition = new Vector3(p.x, waterY + 0.012f, p.z);
                    lilies[i].gameObject.SetActive(Marks > 0);
                }
            }

        }

        /// <summary>Pins the pond to the bottom-left corner of the visible field at its fixed
        /// depth, so the bank edge meets the left screen edge exactly (never over the corner).
        /// Recomputed from the live camera every frame, so a change of screen ratio (a different
        /// phone model, or a desktop resolution change) keeps the lake snug in the corner.</summary>
        void PlaceInCorner()
        {
            if (cam == null) return;
            // The world point on the ground plane (y=0) that projects to the bottom-left of the
            // screen. Pinning the pond's centre there seats it snugly in the bottom-left corner on
            // any aspect ratio or resolution, because viewport coordinates are normalised (0..1).
            Ray r = cam.ViewportPointToRay(new Vector3(CornerVX, CornerVY, 0f));
            if (Mathf.Abs(r.direction.y) < 1e-4f) return;
            float t = -r.origin.y / r.direction.y;
            if (t <= 0f || float.IsNaN(t) || float.IsInfinity(t)) return;
            Vector3 p = r.origin + r.direction * t;
            float lakeX = Mathf.Clamp(p.x, -45f, -1f);
            float lakeZ = Mathf.Clamp(p.z, -11f, -3f);
            // The player asked to slide the pond left by half its own footprint so half of it hangs
            // off the left edge of the screen. This is a PURE TRANSLATION -- the pond's scale is
            // untouched (sizing lives only in SizeAgainstCannon), so only the centre moves left by
            // FootR * Scale, which is exactly half the footprint width.
            lakeX -= FootR * Scale;
            anchorPos = new Vector3(lakeX, 0f, lakeZ);
            basePos = anchorPos;
            transform.position = anchorPos;
            // NOTE: the pond's SIZE is deliberately NOT recomputed here. PlaceInCorner runs every
            // frame; sizing here (against the cannon's live position) was the bug that made the
            // lake grow/shrink as the cannon aimed. Sizing happens only in SizeAgainstCannon, which
            // Update calls just when the screen resolution changes.
        }

        /// <summary>Scales the pond down (never up past its authored size) so its right edge stays
        /// clear of the cannon's LEFTMOST position over the cannon's whole aim sweep. Sizing against
        /// the fixed leftmost reach (Cannon.LeftmostX) — not the live position, which slides as the
        /// turret turns — is what keeps the lake a constant size during play while still guaranteeing
        /// it never overlaps the cannon however the player is aiming. Called once at the start of the
        /// run and again only when the screen resolution changes.</summary>
        void SizeAgainstCannon()
        {
            if (cannon == null)
            {
                transform.localScale = Vector3.one * Scale;
                return;
            }
            // The cannon's fixed leftmost world X (fully left-aimed). Fall back to the live X only
            // if the Cannon component is somehow unavailable.
            float leftmostX = cannonComp != null ? cannonComp.LeftmostX : cannon.position.x;
            const float CannonHalf = 1.5f;                 // generous half-width of the cannon body
            float naturalR = FootR * Scale;                // full-size footprint radius
            float allowed = (leftmostX - CannonHalf) - anchorPos.x;   // gap to the cannon's leftmost left edge
            // Keep at least 34 % of the pond so it never vanishes on an absurdly narrow screen.
            float r = Mathf.Min(naturalR, Mathf.Max(naturalR * 0.34f, allowed));
            float s = r / FootR;
            transform.localScale = new Vector3(s, s, s);
        }

        void Update()
        {
            // Keep the pond locked to the bottom-left corner even if the screen ratio changes.
            PlaceInCorner();

            // The pond's SIZE is fixed for the whole run and only recomputed when the screen
            // resolution changes (a phone rotation, a desktop window resize). It deliberately does
            // NOT follow the cannon's live position — that used to make the lake pulse in size as
            // the player aimed. Sized against the cannon's fixed leftmost reach instead.
            int scrW = Screen.width;
            int scrH = Screen.height;
            bool resize = scrW != sizedForWidth || scrH != sizedForHeight;
            if (cannonComp != null && cannonComp.LeftmostX != sizedForLeftmost) resize = true;
            if (resize)
            {
                sizedForWidth = scrW;
                sizedForHeight = scrH;
                sizedForLeftmost = cannonComp != null ? cannonComp.LeftmostX : float.NaN;
                SizeAgainstCannon();
            }

            // Ease the visible water toward the true level so it glides up and down.
            float target = Mathf.Clamp01(Marks / (float)Max);
            displayLevel = Mathf.MoveTowards(displayLevel, target, Time.deltaTime * 1.6f);
            ApplyWater();

            // Scroll the hose's flow texture so the water reads as travelling lake -> cannon.
            // (The V axis runs along the tube from the lake end to the cannon end, so DECREASING
            // the offset drags the bands toward the cannon; the old positive drift ran them back
            // toward the lake, which read as the wrong direction.)
            // The pump only runs while the lake holds water: the eased `hoseFlow` factor scales both
            // the flow-texture scroll and the squish amplitude, so the whole "water moving" read stops
            // (and the tube settles to a still, round shape) the moment the lake is empty, and eases
            // back in as soon as it holds water again.
            hoseFlow = Mathf.MoveTowards(hoseFlow, Marks > 0 ? 1f : 0f, Time.deltaTime * 2.5f);

            if (hoseMat != null && hoseFlow > 0f)
            {
                hoseScroll -= Time.deltaTime * 0.7f * hoseFlow;
                if (hoseScroll < -1000f) hoseScroll += 1000f;
                Vector4 st = hoseMat.GetVector("_BaseMap_ST");
                hoseMat.SetVector("_BaseMap_ST", new Vector4(st.x, st.y, st.z, hoseScroll));
            }

            // A peristaltic squish: a travelling outward bulge rides from the lake end to the
            // cannon end, squeezing the tube like water being pumped through it (the same squash
            // idea the cannon uses when firing, but continuous). Only outward crests, so the tube
            // pinches between them.
            if (hoseMesh != null && hoseBaseVerts != null)
            {
                // Phase and amplitude both ride on `hoseFlow`: when the lake is dry the wave stops
                // advancing and the bulge scales to zero, leaving the tube perfectly round and still.
                hoseWavePhase += Time.deltaTime * 1.4f * hoseFlow;
                const float amp = 0.055f;
                for (int i = 0; i < hoseBaseVerts.Length; i++)
                {
                    float t = hoseRingT[i];
                    // Fade the squish to zero at both ends (over the first/last ~16% of the run).
                    // The tube is open-ended, so a crest arriving at the cannon joint flared the end
                    // ring outward and exposed the bright interior as a repeating white spot; keeping
                    // the end rings perfectly round removes that flash while the mid-tube still pulses.
                    float env = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.16f);
                    float w = Mathf.Sin((t * 3f - hoseWavePhase) * Mathf.PI * 2f);
                    float bulge = Mathf.Max(0f, w) * amp * hoseFlow * env;
                    hoseScratch[i] = hoseBaseVerts[i] + hoseNormals[i] * bulge;
                }
                hoseMesh.vertices = hoseScratch;
                hoseMesh.RecalculateBounds();
            }

            // Rippling water surface: push each vertex up/down by a travelling sine.
            if (waterSurfaceMesh != null && Marks > 0)
            {
                float t = Time.time;
                for (int i = 0; i < waterBase.Length; i++)
                {
                    Vector3 v = waterBase[i];
                    float w = Mathf.Sin(v.x * 3.4f + t * 2.3f) * 0.03f
                            + Mathf.Sin(v.z * 3.9f - t * 1.9f) * 0.03f
                            + Mathf.Sin((v.x + v.z) * 2.1f + t * 3.1f) * 0.018f;
                    waterScratch[i] = new Vector3(v.x, v.y + w, v.z);
                }
                waterSurfaceMesh.vertices = waterScratch;
                waterSurfaceMesh.RecalculateNormals();
            }

            // Slowly scroll the swirl texture so the water reads as gently flowing (URP Lit tiling).
            // Gated on the lake holding water, so a dry pond is completely still (no drifting swirl).
            if (waterSurfaceMat != null && Marks > 0)
            {
                Vector4 off = waterSurfaceMat.GetVector("_BaseMap_ST");
                waterSurfaceMat.SetVector("_BaseMap_ST",
                    new Vector4(off.x, off.y, off.z + Time.deltaTime * 0.012f, off.w + Time.deltaTime * 0.009f));
            }

            // Dry-flash: tint the bed red and give the pond a small shake on a blocked shot.
            if (flash > 0f)
            {
                flash = Mathf.Max(0f, flash - Time.deltaTime * 3.2f);
                if (floorRend != null)
                    floorRend.material.color = Color.Lerp(BasinColor, BasinDry, flash);
                transform.position = anchorPos + new Vector3(Mathf.Sin(Time.time * 55f) * flash * 0.06f, 0f, 0f);
            }
            else if (floorRend != null && floorRend.material.color != BasinColor)
            {
                floorRend.material.color = BasinColor;
                transform.position = anchorPos;
            }
        }
    }
}
