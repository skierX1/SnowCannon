using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Spawns the two kinds of living traffic that cruise across the far background, just behind
    /// the line the snowmen march from: cross-country skiers on foot and the fast snow scooter.
    /// Both are fully hittable by a snowball (a person is worth one point, the scooter three) and
    /// both are only ever removed once they have left the screen, so the horizon always feels alive.
    /// Everything is built from primitives + the always-included URP shaders, so it renders on
    /// desktop, Android and iOS alike.
    /// </summary>
    public sealed class SkierManager : MonoBehaviour
    {
        Camera cam;
        readonly List<Skier> live = new List<Skier>();
        readonly List<Scooter> scooters = new List<Scooter>();
        float spawnTimer;
        float scooterTimer;

        const float SkierZ = 47f;          // well behind the snowman spawn line (SpawnZ = 38)
        const float ScooterZ = 44f;         // the people's lane, a touch nearer than the skiers
        const float SpawnEvery = 3.4f;
        const float ScooterEvery = 9f;      // occasional, random, never a steady parade

        public static SkierManager Attach(Transform worldParent, Camera cam)
        {
            var go = new GameObject("SkierManager");
            go.transform.SetParent(worldParent, false);
            var m = go.AddComponent<SkierManager>();
            m.cam = cam;
            return m;
        }

        void Update()
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = SpawnEvery * Random.Range(0.6f, 1.5f);
                SpawnOne();
            }

            // The scooter only shows up now and then, on its own random cadence.
            scooterTimer -= Time.deltaTime;
            if (scooterTimer <= 0f)
            {
                scooterTimer = ScooterEvery * Random.Range(0.7f, 1.8f);
                SpawnScooter();
            }

            for (int i = live.Count - 1; i >= 0; i--)
            {
                var s = live[i];
                if (s == null || s.IsDone)
                {
                    if (s != null) Destroy(s.gameObject);
                    live.RemoveAt(i);
                }
            }
            for (int i = scooters.Count - 1; i >= 0; i--)
            {
                var s = scooters[i];
                if (s == null || s.IsDone)
                {
                    if (s != null) Destroy(s.gameObject);
                    scooters.RemoveAt(i);
                }
            }
        }

        void SpawnOne()
        {
            // Enter from a random edge, just outside the TRUE visible width at the skier depth
            // (uncapped), so they cross the whole screen rather than only its middle.
            float half = GameConfig.BackgroundHalfWidthAtZ(cam, SkierZ);
            bool fromLeft = Random.value < 0.5f;
            float startX = fromLeft ? -(half + 2f) : (half + 2f);
            float dir = fromLeft ? 1f : -1f;
            var s = Skier.Spawn(transform, cam, new Vector3(startX, 0f, SkierZ), dir);
            live.Add(s);
        }

        void SpawnScooter()
        {
            float half = GameConfig.BackgroundHalfWidthAtZ(cam, ScooterZ);
            bool fromLeft = Random.value < 0.5f;
            float startX = fromLeft ? -(half + 3f) : (half + 3f);
            float dir = fromLeft ? 1f : -1f;
            var s = Scooter.Spawn(transform, cam, new Vector3(startX, 0f, ScooterZ), dir);
            scooters.Add(s);
        }
    }

    /// <summary>One background person on foot: a small figure on skis with poles, gliding along X.
    /// It is a live target — a snowball that connects knocks it over with a comic cartwheel and
    /// scores a single point, after which it tumbles away and fades out.</summary>
    public sealed class Skier : MonoBehaviour, IHitTarget
    {
        public bool IsDone { get; private set; }
        public bool Alive => !IsDying && !IsDone;
        bool IsDying;

        Camera cam;
        float dir;
        float speed;
        float glidePhase;
        Transform legL, legR, poleL, poleR;
        Transform head;
        float despawnX;

        // Death animation state: the figure cartwheels away and fades out.
        float deathTimer;
        Vector3 deathSpin;
        Vector3 deathLaunch;
        // A phase that drives the limbs flailing wildly during the cartwheel (they would otherwise
        // stay frozen in whatever pose the glide left them in).
        float deathFlail;
        readonly List<Material> mats = new List<Material>();

        public static Skier Spawn(Transform parent, Camera cam, Vector3 start, float dir)
        {
            var go = new GameObject("Skier");
            go.transform.SetParent(parent, false);
            go.transform.position = start;
            var s = go.AddComponent<Skier>();
            s.cam = cam;
            s.dir = dir;
            s.speed = Random.Range(2.2f, 4.0f);
            s.glidePhase = Random.Range(0f, 6.28f);
            s.Build();
            s.despawnX = GameConfig.BackgroundHalfWidthAtZ(cam, start.z) + 3f;
            return s;
        }

        void Build()
        {
            // A bright, varied kit colour so the little figures pop against the white field.
            Color suit = new Color(Random.Range(0.2f, 0.9f), Random.Range(0.2f, 0.9f), Random.Range(0.3f, 0.95f), 1f);
            var suitMat = Mat.Opaque(suit);
            var skinMat = Mat.Opaque(new Color(0.95f, 0.80f, 0.66f, 1f));
            var skiMat = Mat.Opaque(new Color(0.15f, 0.16f, 0.2f, 1f));
            var poleMat = Mat.Opaque(new Color(0.6f, 0.62f, 0.66f, 1f));
            mats.Add(suitMat); mats.Add(skinMat); mats.Add(skiMat); mats.Add(poleMat);

            float h = Random.Range(0.85f, 1.15f);   // overall size variation

            // Torso.
            Part(PrimitiveType.Capsule, suitMat, new Vector3(0, 0.9f * h, 0), new Vector3(0.34f * h, 0.5f * h, 0.28f * h));
            // Head (kept as a handle so the death animation can spin it comically).
            head = Part(PrimitiveType.Sphere, skinMat, new Vector3(0, 1.42f * h, 0), Vector3.one * (0.3f * h));
            // A cap.
            Part(PrimitiveType.Sphere, suitMat, new Vector3(0, 1.5f * h, 0), Vector3.one * (0.26f * h));

            // Two skis: long thin boxes under the feet, pointing along travel.
            Part(PrimitiveType.Cube, skiMat, new Vector3(0, 0.05f * h, 0.1f * h), new Vector3(0.16f * h, 0.05f * h, 1.5f * h));
            Part(PrimitiveType.Cube, skiMat, new Vector3(0.18f * h, 0.05f * h, 0.1f * h), new Vector3(0.16f * h, 0.05f * h, 1.5f * h));

            // Legs (animated), pivoting at the hip.
            legL = Limb(PrimitiveType.Capsule, suitMat, new Vector3(-0.08f * h, 0.55f * h, 0), new Vector3(0.14f * h, 0.28f * h, 0.14f * h));
            legR = Limb(PrimitiveType.Capsule, suitMat, new Vector3(0.1f * h, 0.55f * h, 0), new Vector3(0.14f * h, 0.28f * h, 0.14f * h));

            // Poles (animated), pivoting at the shoulder, angled back.
            poleL = Limb(PrimitiveType.Cylinder, poleMat, new Vector3(-0.16f * h, 1.05f * h, 0), new Vector3(0.05f * h, 0.5f * h, 0.05f * h));
            poleR = Limb(PrimitiveType.Cylinder, poleMat, new Vector3(0.18f * h, 1.05f * h, 0), new Vector3(0.05f * h, 0.5f * h, 0.05f * h));

            // Face the direction of travel.
            transform.rotation = Quaternion.Euler(0f, dir > 0f ? 90f : -90f, 0f);
            Mat.SetShadows(gameObject, true, false);

            // One capsule over the whole figure so a snowball's sphere-cast can actually connect.
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.42f * h;
            col.height = 1.9f * h;
            col.center = new Vector3(0f, 0.95f * h, 0f);
            col.direction = 1; // upright along Y
        }

        Transform Part(PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var p = GameObject.CreatePrimitive(type);
            p.transform.SetParent(transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().material = mat;
            Destroy(p.GetComponent<Collider>());
            return p.transform;
        }

        // A limb parented to a pivot so it can swing about its top end.
        Transform Limb(PrimitiveType type, Material mat, Vector3 pivotPos, Vector3 scale)
        {
            var pivot = new GameObject("limb").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = pivotPos;
            var p = GameObject.CreatePrimitive(type);
            p.transform.SetParent(pivot, false);
            p.transform.localPosition = new Vector3(0f, -scale.y * 0.5f, 0f);
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().material = mat;
            Destroy(p.GetComponent<Collider>());
            return pivot;
        }

        void Update()
        {
            if (IsDying) { UpdateDeath(); return; }
            if (IsDone) return;

            var p = transform.position;
            p.x += dir * speed * Time.deltaTime;
            transform.position = p;

            // A gentle ski glide: legs and poles swing in antiphase.
            glidePhase += Time.deltaTime * 6f;
            float swing = Mathf.Sin(glidePhase) * 22f;
            if (legL != null) legL.localEulerAngles = new Vector3(swing, 0f, 0f);
            if (legR != null) legR.localEulerAngles = new Vector3(-swing, 0f, 0f);
            if (poleL != null) poleL.localEulerAngles = new Vector3(-swing * 0.8f, 0f, 0f);
            if (poleR != null) poleR.localEulerAngles = new Vector3(swing * 0.8f, 0f, 0f);

            // Only ever despawn once fully off the visible edge, never mid-screen.
            if (Mathf.Abs(p.x) > despawnX) IsDone = true;
        }

        /// <summary>A snowball connected: knock the skier into a comic cartwheel and score one point.</summary>
        public bool Hit(Vector3 hitPoint, out int points)
        {
            points = 0;
            if (IsDying || IsDone) return false;

            IsDying = true;
            deathTimer = 0f;
            points = GameConfig.PointsSkier;

            // Fling the whole figure up and spin it about a random tumble axis for the cartoon knock-back.
            deathLaunch = new Vector3(dir * Random.Range(1.5f, 3f), Random.Range(4f, 6.5f), Random.Range(-0.5f, 1f));
            deathSpin = new Vector3(Random.Range(-720f, 720f), Random.Range(-360f, 360f), Random.Range(-900f, 900f));

            // Drop the collider so the tumbling body cannot block any further snowballs.
            var col = GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Give every renderer its own transparent instance copy so we can fade the whole thing.
            var rs = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (r == null) continue;
                var inst = r.material;                 // instance copy, safe to recolour
                Mat.MakeTransparent(inst);
                r.material = inst;
                mats.Add(inst);
            }
            return true;
        }

        void UpdateDeath()
        {
            deathTimer += Time.deltaTime;
            const float life = 1.4f;
            float k = Mathf.Clamp01(deathTimer / life);

            // Ballistic tumble with a little ground bounce, then settle.
            deathLaunch.y -= 16f * Time.deltaTime;
            transform.position += deathLaunch * Time.deltaTime;
            transform.Rotate(deathSpin * Time.deltaTime, Space.Self);
            if (transform.position.y < 0.1f)
            {
                var p = transform.position; p.y = 0.1f; transform.position = p;
                deathLaunch = Vector3.zero; deathSpin *= 0.5f;
            }

            // The head keeps a goofy independent spin for the whole tumble.
            if (head != null) head.Rotate(0f, 0f, 900f * Time.deltaTime, Space.Self);

            // The limbs flail wildly as the figure cartwheels: legs and poles thrash about their
            // pivots at different rates so the knock-back reads as a real ragdoll tumble, not a
            // rigid mannequin. They were frozen in the last glide pose before this was added.
            deathFlail += Time.deltaTime;
            float fA = Mathf.Sin(deathFlail * 22f) * 70f;
            float fB = Mathf.Sin(deathFlail * 27f + 1.7f) * 80f;
            float fC = Mathf.Sin(deathFlail * 19f + 0.6f) * 60f;
            float fD = Mathf.Sin(deathFlail * 24f + 2.4f) * 75f;
            if (legL != null) legL.localEulerAngles = new Vector3(fA, 0f, fC * 0.4f);
            if (legR != null) legR.localEulerAngles = new Vector3(fB, 0f, -fD * 0.4f);
            if (poleL != null) poleL.localEulerAngles = new Vector3(-fB * 1.1f, 0f, fD * 0.5f);
            if (poleR != null) poleR.localEulerAngles = new Vector3(fA * 1.1f, 0f, -fC * 0.5f);

            // Fade everything out over the last stretch.
            float a = 1f - Mathf.SmoothStep(0.35f, 1f, k);
            for (int i = 0; i < mats.Count; i++)
            {
                if (mats[i] == null) continue;
                var c = mats[i].color; c.a = a; mats[i].color = c;
            }

            if (k >= 1f) IsDone = true;
        }

        void OnDestroy()
        {
            foreach (var m in mats) if (m != null) Destroy(m);
        }
    }

    /// <summary>
    /// The snow scooter: a fast, occasional vehicle that crosses the people's lane (just behind the
    /// snowmen) roughly seven times faster than any man. It always runs edge to edge, plays a looping
    /// engine putter while on screen, and may randomly reverse direction one or more times before it
    /// finally reaches the far side. A snowball that connects scores three points and sends it flying.
    /// </summary>
    public sealed class Scooter : MonoBehaviour, IHitTarget
    {
        public bool IsDone { get; private set; }
        public bool Alive => !IsDying && !IsDone;
        bool IsDying;

        Camera cam;
        float dir;                 // +1 = travelling to +X (right), -1 = to -X (left)
        float speed;
        float despawnX;
        float turnTimer;           // counts down to the next possible mid-run reversal
        int turnsLeft;             // how many more reversals this run may still perform
        float wheelSpin;
        Transform wheelF, wheelR;

        // Death animation.
        float deathTimer;
        Vector3 deathLaunch, deathSpin;
        readonly List<Material> mats = new List<Material>();

        // The engine putter rides on the vehicle so it pans across the screen with it.
        AudioSource engine;

        public static Scooter Spawn(Transform parent, Camera cam, Vector3 start, float dir)
        {
            var go = new GameObject("Scooter");
            go.transform.SetParent(parent, false);
            go.transform.position = start;
            var s = go.AddComponent<Scooter>();
            s.cam = cam;
            s.dir = dir;
            // ~7x a man: men run 1.7..4.2 u/s, so the scooter cruises at a brisk 15..21.
            s.speed = Random.Range(15f, 21f);
            s.Build();
            s.despawnX = GameConfig.BackgroundHalfWidthAtZ(cam, start.z) + 3f;

            // It may turn back one to three times before it commits to the far side.
            s.turnsLeft = Random.Range(0, 4);
            s.ScheduleTurn();

            // A 3D looping engine clip mounted on the vehicle itself.
            var ad = AudioDirector.Instance;
            if (ad != null && ad.ScooterClip != null)
            {
                s.engine = go.AddComponent<AudioSource>();
                s.engine.clip = ad.ScooterClip;
                s.engine.loop = true;
                s.engine.spatialBlend = 1f;
                s.engine.minDistance = 2f;
                s.engine.maxDistance = 70f;
                s.engine.volume = Settings.Sound ? 0.7f * Settings.Volume : 0f;
                s.engine.playOnAwake = false;
                s.engine.Play();
            }
            return s;
        }

        void ScheduleTurn()
        {
            // Only meaningful while turns remain; otherwise it just runs straight to the far side.
            turnTimer = turnsLeft > 0 ? Random.Range(0.7f, 2.4f) : float.MaxValue;
        }

        void Build()
        {
            var bodyMat = Mat.Opaque(new Color(0.85f, 0.16f, 0.18f, 1f));   // a red snow scooter
            var trimMat = Mat.Opaque(new Color(0.92f, 0.85f, 0.30f, 1f));   // yellow trim
            var darkMat = Mat.Opaque(new Color(0.12f, 0.13f, 0.16f, 1f));   // tyres / seat
            var chromeMat = Mat.Opaque(new Color(0.7f, 0.72f, 0.76f, 1f));  // handlebar
            var riderMat = Mat.Opaque(new Color(0.15f, 0.35f, 0.6f, 1f));   // rider coat
            var skinMat = Mat.Opaque(new Color(0.95f, 0.8f, 0.66f, 1f));
            mats.Add(bodyMat); mats.Add(trimMat); mats.Add(darkMat); mats.Add(chromeMat); mats.Add(riderMat); mats.Add(skinMat);

            var body = new GameObject("body").transform;
            body.SetParent(transform, false);

            // Deck + sloped front column + seat, forming the classic scooter silhouette.
            Box(body, darkMat, new Vector3(0f, 0.28f, 0f), new Vector3(0.5f, 0.14f, 1.1f));          // floorboard
            Box(body, bodyMat, new Vector3(0f, 0.55f, 0.5f), new Vector3(0.34f, 0.7f, 0.28f));        // front column
            Box(body, bodyMat, new Vector3(0f, 0.5f, -0.35f), new Vector3(0.42f, 0.34f, 0.7f));       // rear body
            Box(body, trimMat, new Vector3(0f, 0.72f, -0.35f), new Vector3(0.46f, 0.12f, 0.72f));      // rear cowl
            Box(body, darkMat, new Vector3(0f, 0.78f, -0.4f), new Vector3(0.4f, 0.14f, 0.5f));         // seat
            // Handlebar across the top of the column.
            Box(body, chromeMat, new Vector3(0f, 0.95f, 0.5f), new Vector3(0.7f, 0.08f, 0.08f));
            Box(body, chromeMat, new Vector3(0f, 0.86f, 0.5f), new Vector3(0.08f, 0.2f, 0.08f));

            // A little rider hunched over the bars.
            Box(body, riderMat, new Vector3(0f, 0.95f, -0.05f), new Vector3(0.34f, 0.5f, 0.4f));      // torso
            Part(body, PrimitiveType.Sphere, skinMat, new Vector3(0f, 1.32f, 0.02f), Vector3.one * 0.28f); // head
            Box(body, riderMat, new Vector3(0f, 1.42f, 0.02f), new Vector3(0.3f, 0.12f, 0.3f));        // helmet
            Box(body, riderMat, new Vector3(0f, 0.9f, 0.32f), new Vector3(0.12f, 0.12f, 0.5f));        // arm to bars

            // Two wheels (cylinders laid along X so they roll about the travel axis).
            wheelF = Wheel(body, darkMat, new Vector3(0f, 0.24f, 0.55f));
            wheelR = Wheel(body, darkMat, new Vector3(0f, 0.24f, -0.5f));

            transform.rotation = Quaternion.Euler(0f, dir > 0f ? 90f : -90f, 0f);
            Mat.SetShadows(gameObject, true, false);

            // A capsule collider over the whole vehicle so a snowball can connect.
            var col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.7f;
            col.height = 1.9f;
            col.center = new Vector3(0f, 0.7f, 0f);
            col.direction = 1;
        }

        Transform Box(Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().material = mat;
            Destroy(p.GetComponent<Collider>());
            return p.transform;
        }

        Transform Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var p = GameObject.CreatePrimitive(type);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().material = mat;
            Destroy(p.GetComponent<Collider>());
            return p.transform;
        }

        Transform Wheel(Transform parent, Material mat, Vector3 pos)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            w.transform.SetParent(parent, false);
            w.transform.localPosition = pos;
            w.transform.localScale = new Vector3(0.48f, 0.16f, 0.48f);
            w.transform.localEulerAngles = new Vector3(0f, 0f, 90f);   // lay the cylinder along X
            w.GetComponent<MeshRenderer>().material = mat;
            Destroy(w.GetComponent<Collider>());
            return w.transform;
        }

        void Update()
        {
            if (IsDying) { UpdateDeath(); return; }
            if (IsDone) return;

            var p = transform.position;
            p.x += dir * speed * Time.deltaTime;
            transform.position = p;

            // Roll the wheels fast (it is going 7x a man).
            wheelSpin += speed * 60f * Time.deltaTime;
            if (wheelF != null) wheelF.localEulerAngles = new Vector3(0f, 0f, 90f + wheelSpin);
            if (wheelR != null) wheelR.localEulerAngles = new Vector3(0f, 0f, 90f + wheelSpin);

            // A random mid-run reversal: turn back, and possibly turn back again, until it finally
            // commits to the far side. Both the trigger and the spot are left to chance.
            if (turnsLeft > 0)
            {
                turnTimer -= Time.deltaTime;
                if (turnTimer <= 0f)
                {
                    dir = -dir;
                    turnsLeft--;
                    transform.rotation = Quaternion.Euler(0f, dir > 0f ? 90f : -90f, 0f);
                    ScheduleTurn();
                }
            }

            // Gone once it clears the visible edge on whichever way it is heading.
            if (Mathf.Abs(p.x) > despawnX) IsDone = true;
        }

        /// <summary>A snowball connected: launch the scooter into the air for three points.</summary>
        public bool Hit(Vector3 hitPoint, out int points)
        {
            points = 0;
            if (IsDying || IsDone) return false;

            IsDying = true;
            deathTimer = 0f;
            points = GameConfig.PointsScooter;

            deathLaunch = new Vector3(dir * Random.Range(2f, 4f), Random.Range(5f, 8f), Random.Range(-0.5f, 1.2f));
            deathSpin = new Vector3(Random.Range(-500f, 500f), Random.Range(-900f, 900f), Random.Range(-500f, 500f));

            var col = GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Give every renderer its own transparent instance copy so we can fade the whole thing.
            var rs = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (r == null) continue;
                var inst = r.material;
                Mat.MakeTransparent(inst);
                r.material = inst;
                mats.Add(inst);
            }
            return true;
        }

        void UpdateDeath()
        {
            deathTimer += Time.deltaTime;
            const float life = 1.6f;
            float k = Mathf.Clamp01(deathTimer / life);

            deathLaunch.y -= 16f * Time.deltaTime;
            transform.position += deathLaunch * Time.deltaTime;
            transform.Rotate(deathSpin * Time.deltaTime, Space.Self);
            if (transform.position.y < 0.12f)
            {
                var p = transform.position; p.y = 0.12f; transform.position = p;
                deathLaunch = Vector3.zero; deathSpin *= 0.4f;
            }

            float a = 1f - Mathf.SmoothStep(0.4f, 1f, k);
            for (int i = 0; i < mats.Count; i++)
            {
                if (mats[i] == null) continue;
                var c = mats[i].color; c.a = a; mats[i].color = c;
            }

            // Fade the engine out as it dies.
            if (engine != null) engine.volume = Mathf.Max(0f, engine.volume - Time.deltaTime * 1.2f);

            if (k >= 1f) IsDone = true;
        }

        void OnDestroy()
        {
            foreach (var m in mats) if (m != null) Destroy(m);
        }
    }
}
