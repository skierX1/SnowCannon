using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Very light atmosphere: a handful of drifting snowflakes plus a few soft clouds high
    /// up. Everything is deliberately sparse and faint so it never obscures the snowmen or
    /// the aim. Uses plain SpriteRenderers (no particle system) to stay cheap and predictable.
    /// </summary>
    public sealed class Weather : MonoBehaviour
    {
        sealed class Flake
        {
            public SpriteRenderer sr;
            public Vector3 velocity;
            public float spin;
            // Kept internal (NOT public) on purpose: `Flake` is a serialized type, and a NEW public
            // field here would change the serialised class layout and trip the editor-vs-player
            // "script class layout is incompatible" build check. Unity only serialises public /
            // [SerializeField] fields, so internal is layout-stable, yet still readable from the
            // enclosing Weather class (private would not be).
            internal float baseAlpha;
        }

        Flake[] flakes;
        SpriteRenderer[] clouds;
        float[] cloudSpeed;

        const int FlakeCount = 90;
        const float FieldX = 26f;
        const float FieldZMin = -14f;
        const float FieldZMax = 30f;
        const float TopY = 16f;

        public static Weather Attach(Transform parent)
        {
            var go = new GameObject("Weather");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Weather>();
        }

        void Start()
        {
            BuildFlakes();
            BuildClouds();
        }

        void BuildFlakes()
        {
            var sprite = TextureFactory.CircleSprite(1.4f);
            flakes = new Flake[FlakeCount];
            for (int i = 0; i < FlakeCount; i++)
            {
                var go = new GameObject("flake" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                float fa = Random.Range(0.18f, 0.42f);
                sr.color = new Color(1f, 1f, 1f, fa);
                float s = Random.Range(0.12f, 0.34f);
                go.transform.localScale = Vector3.one * s;

                flakes[i] = new Flake
                {
                    sr = sr,
                    baseAlpha = fa,
                    velocity = new Vector3(Random.Range(-0.35f, 0.35f),
                                           -Random.Range(0.7f, 1.7f),
                                           Random.Range(-0.2f, 0.2f)),
                    spin = Random.Range(-40f, 40f)
                };
                Respawn(i, true);
            }
        }

        void BuildClouds()
        {
            int n = 6;
            clouds = new SpriteRenderer[n];
            cloudSpeed = new float[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("cloud" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                // A wide, flat, soft disc reads as a drifting cloud puff. Alpha is high enough
                // to actually show against the light-blue sky (the old 0.1-0.2 was invisible).
                sr.sprite = TextureFactory.CloudSprite();
                sr.color = new Color(1f, 1f, 1f, Random.Range(0.55f, 0.85f));
                float w = Random.Range(10f, 20f);
                go.transform.localScale = new Vector3(w, w * 0.5f, 1f);
                go.transform.position = new Vector3(Random.Range(-FieldX, FieldX),
                                                   Random.Range(9f, 15f),
                                                   Random.Range(16f, 30f));
                clouds[i] = sr;
                cloudSpeed[i] = Random.Range(0.25f, 0.6f) * (Random.Range(0, 2) == 0 ? -1f : 1f);
            }
        }

        void Respawn(int i, bool anywhere)
        {
            var f = flakes[i];
            float x = Random.Range(-FieldX, FieldX);
            float z = Random.Range(FieldZMin, FieldZMax);
            float y = anywhere ? Random.Range(0f, TopY) : TopY + Random.Range(0f, 3f);
            f.sr.transform.position = new Vector3(x, y, z);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Weather is now a gameplay layer: the wind the WeatherDirector sets drifts the flakes
            // sideways, and a blizzard's low visibility thickens the snow and dims the far field so
            // snowmen emerge later. Both read the shared GameRuntime globals so this stays decoupled.
            Vector3 wind = GameRuntime.Wind;
            float vis = Mathf.Clamp01(GameRuntime.Visibility);
            float blizzard = 1f - vis;   // 0 clear .. 1 full blizzard

            for (int i = 0; i < flakes.Length; i++)
            {
                var f = flakes[i];
                var t = f.sr.transform;
                t.position += (f.velocity + new Vector3(wind.x * 0.35f, 0f, 0f)) * dt;
                t.Rotate(0f, 0f, f.spin * dt);

                // A blizzard dumps more snow and makes each flake brighter and larger.
                if (f.sr != null)
                {
                    var c = f.sr.color;
                    c.a = f.baseAlpha * (1f + blizzard * 1.6f);
                    f.sr.color = c;
                }

                if (t.position.y < -0.5f || Mathf.Abs(t.position.x) > FieldX + 2f)
                    Respawn(i, false);
            }

            for (int i = 0; i < clouds.Length; i++)
            {
                var t = clouds[i].transform;
                var p = t.position;
                p.x += cloudSpeed[i] * dt;
                if (p.x > FieldX + 12f) p.x = -FieldX - 12f;
                else if (p.x < -FieldX - 12f) p.x = FieldX + 12f;
                t.position = p;

                // Billboard the puff to the camera so it is always seen face-on.
                var cam = Camera.main;
                if (cam != null)
                    t.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
            }
        }
    }
}
