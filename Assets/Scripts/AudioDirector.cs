using System.Collections.Generic;
using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Owns all sound. Clips are synthesised once at start-up so the game is audible
    /// without shipping any audio file; real clips dropped into Assets/Audio win if found.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [SerializeField] AudioClip throwClip;
        [SerializeField] AudioClip[] screamClips;
        [SerializeField] AudioClip musicClip;

        // The scooter engine idle is synthesised lazily and shared by every live scooter, which
        // each mount it on their own 3D AudioSource so the putter tracks the vehicle across screen.
        AudioClip scooterClip;

        /// <summary>The shared looping scooter engine clip, built once on first use.</summary>
        public AudioClip ScooterClip
        {
            get { if (scooterClip == null) scooterClip = AudioFactory.ScooterEngine(); return scooterClip; }
        }

        readonly List<AudioSource> throwPool = new List<AudioSource>();
        readonly List<AudioSource> screamPool = new List<AudioSource>();
        AudioSource musicSource;

        // Lazily resolved reflection handle to the mobile-only Handheld.Vibrate(), kept off the
        // compile path so the desktop player builds (see Vibrate()).
        bool vibrateResolved;
        System.Reflection.MethodInfo vibrate;

        int nextThrow;
        int nextScream;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
        }

        void Build()
        {
            if (throwClip == null) throwClip = AudioFactory.Throw();
            if (screamClips == null || screamClips.Length == 0) screamClips = AudioFactory.Screams();
            if (musicClip == null) musicClip = AudioFactory.Music();

            for (int i = 0; i < 6; i++) throwPool.Add(MakeSource("throw" + i, throwClip, false));
            for (int i = 0; i < 6; i++) screamPool.Add(MakeSource("scream" + i, null, true));

            var musicGo = new GameObject("MusicSource");
            Object.DontDestroyOnLoad(musicGo);
            musicGo.transform.SetParent(transform, false);
            musicSource = musicGo.AddComponent<AudioSource>();
            musicSource.clip = musicClip;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.volume = 0.35f;
            musicSource.priority = 8;

            ApplySettings();
        }

        static AudioSource MakeSource(string name, AudioClip clip, bool threeD)
        {
            var go = new GameObject(name);
            // The singleton root is DontDestroyOnLoad, so its pooled sources must be too,
            // otherwise they are destroyed when the Play scene unloads and the next
            // ApplySettings() touches destroyed objects.
            Object.DontDestroyOnLoad(go);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.playOnAwake = false;
            src.spatialBlend = threeD ? 1f : 0f;
            src.priority = 0;
            return src;
        }

        /// <summary>Re-reads the persisted sound, music, volume and per-effect switches.</summary>
        public void ApplySettings()
        {
            float vol = Settings.Volume;
            bool sound = Settings.Sound;
            foreach (var s in throwPool)
            {
                if (s == null) continue;
                s.enabled = sound && Settings.CannonSound;
                s.volume = vol;
            }
            foreach (var s in screamPool)
            {
                if (s == null) continue;
                s.enabled = sound && Settings.SnowmanSound;
                s.volume = vol;
            }
            if (musicSource != null)
            {
                musicSource.enabled = Settings.Music;
                musicSource.volume = 0.35f * vol;
                if (Settings.Music && !musicSource.isPlaying) musicSource.Play();
                else if (!Settings.Music && musicSource.isPlaying) musicSource.Stop();
            }
        }

        public void PlayThrow()
        {
            if (!Settings.Sound || !Settings.CannonSound || throwPool.Count == 0) return;
            var src = throwPool[nextThrow % throwPool.Count];
            nextThrow++;
            if (src == null) return;
            src.transform.position = transform.position;
            src.pitch = Random.Range(0.90f, 1.12f);
            src.volume = Settings.Volume * Random.Range(0.75f, 1.0f);
            src.Play();
        }

        /// <summary>Picks one of the scream variations at random, then jitters its pitch.</summary>
        public void PlayScream(Vector3 worldPosition)
        {
            if (!Settings.Sound || !Settings.SnowmanSound || screamClips == null || screamClips.Length == 0 || screamPool.Count == 0) return;
            var src = screamPool[nextScream % screamPool.Count];
            nextScream++;
            if (src == null) return;
            src.transform.position = worldPosition;
            src.clip = screamClips[Random.Range(0, screamClips.Length)];
            src.pitch = Random.Range(0.85f, 1.25f);
            src.volume = Settings.Volume * Random.Range(0.8f, 1.0f);
            src.Play();
        }

        public void PlayPop(Vector3 worldPosition)
        {
            if (!Settings.Sound || !Settings.CannonSound || throwPool.Count == 0) return;
            var src = throwPool[nextThrow % throwPool.Count];
            nextThrow++;
            if (src == null) return;
            src.transform.position = worldPosition;
            src.clip = throwClip;
            src.pitch = Random.Range(1.3f, 1.7f);
            src.volume = Settings.Volume * 0.5f;
            src.Play();
        }

        /// <summary>Short haptic tick on a hit, ignored on desktop.</summary>
        /// <remarks>
        /// <c>UnityEngine.Handheld</c> only exists in the mobile player modules, so a direct
        /// <c>Handheld.Vibrate()</c> call compiles in the editor (where every platform assembly is
        /// loaded) yet fails to build for the desktop player with CS0103. We reach it through
        /// reflection instead: no compile-time reference to the stripped type, so the desktop build
        /// succeeds and the call simply no-ops where haptics aren't available.
        /// </remarks>
        public void Vibrate()
        {
            if (!Settings.Vibration) return;
            if (!vibrateResolved)
            {
                vibrateResolved = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("UnityEngine.Handheld", false);
                    if (t == null) continue;
                    vibrate = t.GetMethod("Vibrate", System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static, null, new System.Type[0], null);
                    break;
                }
            }
            try { vibrate?.Invoke(null, null); } catch { }
        }
    }
}
