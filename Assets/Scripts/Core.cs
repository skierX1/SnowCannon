using UnityEngine;

namespace SnowCannon
{
    /// <summary>
    /// Anything a snowball can connect with and score against: the snowmen, the background
    /// people and the snow scooter all implement this so the projectile needs only one path.
    /// </summary>
    public interface IHitTarget
    {
        /// <summary>False once the target is dying or gone, so a second ball cannot double-score.</summary>
        bool Alive { get; }

        /// <summary>Registers a hit at the world point, plays the death animation, and reports the
        /// points earned (0 when the hit did not count). The caller despawns the snowball either way.</summary>
        bool Hit(Vector3 hitPoint, out int points);
    }

    /// <summary>
    /// Central tuning values for the whole game. World convention: the camera sits at
    /// negative Z and looks toward positive Z, so snowmen spawn far away (large Z) and
    /// advance toward the camera (decreasing Z).
    /// </summary>
    public static class GameConfig
    {
        // ---- Play field -------------------------------------------------------
        public const float FieldHalfWidth = 10.5f;
        public const float FieldMinZ = -12f;
        public const float FieldMaxZ = 40f;
        public const float SpawnZ = 38f;
        public const float DefeatZ = -9.5f;
        public const float CannonMinZ = -7.5f;
        public const float CannonMaxZ = -2.5f;

        // ---- Cannon ------------------------------------------------------------
        public const float CannonSpeed = 8.5f;
        public const float FireCooldown = 0.32f;
        public const float SnowballSpeed = 68f;
        public const float SnowballRadius = 0.95f;
        public const float SnowballLifeTime = 3.0f;
        public const float SnowballGravity = 40f;

        // ---- Snowman -----------------------------------------------------------
        public const float SnowmanMinScale = 0.8f;
        public const float SnowmanMaxScale = 2.0f;
        public const float SnowmanMinSpeed = 1.7f;
        public const float SnowmanMaxSpeed = 4.2f;
        public const float DeathFadeTime = 2.0f;

        // ---- Lake (the cannon's water ammunition) ------------------------------
        // Every snowman that spawns pours LakeSpawnGain marks into the lake; every shot spends
        // LakeFireCost. The cannon can only fire while the lake holds water, so the player must
        // let snowmen keep coming to reload. The level is clamped to [0, LakeMaxMarks].
        public const int LakeMaxMarks = 50;
        public const int LakeSpawnGain = 2;
        public const int LakeFireCost = 1;
        public const int LakeStartMarks = 45;

        // ---- Scoring -----------------------------------------------------------
        public const int PointsHead = 2;
        public const int PointsBody = 1;
        // A background person on foot is worth one point; the fast snow scooter is worth three.
        public const int PointsSkier = 1;
        public const int PointsScooter = 3;

        // ---- Level progression -------------------------------------------------
        public const int FirstLevelDuration = 60;
        public const int SecondLevelDuration = 90;
        public const int LevelDurationStep = 10;
        public const float FirstSpawnInterval = 1.3333f;
        public const float SpawnIntervalFactor = 0.8f;
        public const float MinSpawnInterval = 0.6f;

        /// <summary>Level 1 = 60 s, then +1 s for every level after it (61, 62, 63, ...).</summary>
        public static int LevelDuration(int level)
        {
            return FirstLevelDuration + Mathf.Max(0, level - 1);
        }

        /// <summary>Level 1 = 1.3333 s (1.5x faster than the old 2 s), every next level is
        /// 80 % of the previous one, floored at MinSpawnInterval.</summary>
        public static float SpawnInterval(int level)
        {
            float v = FirstSpawnInterval * Mathf.Pow(SpawnIntervalFactor, Mathf.Max(0, level - 1));
            return Mathf.Max(MinSpawnInterval, v);
        }

        /// <summary>The half-width of the field that is actually visible on screen at a given
        /// world depth, read from the live camera frustum. Because the camera is a pinhole, the
        /// far spawn depth projects the logical field width into only a narrow slice of the
        /// screen, so spawning/bouncing against this value (instead of the fixed FieldHalfWidth)
        /// is what makes snowmen truly use the whole screen width and bounce at the visible
        /// edges. Clamped to a sane range so a degenerate camera never flings them off-world.</summary>
        public static float VisibleHalfWidthAtZ(Camera cam, float z)
        {
            return VisibleHalfWidthAtZ(cam, z, 22f);
        }

        /// <summary>The half-width of the field visible at a world depth, with a caller-chosen
        /// ceiling. The snowman/lake lane uses the 22-unit default (tuned for the near marching
        /// band); far-background traffic passes a generous ceiling so it is not pinned to the
        /// middle of a wide desktop screen. Only guards against a degenerate camera.</summary>
        public static float VisibleHalfWidthAtZ(Camera cam, float z, float maxClamp)
        {
            if (cam == null) return FieldHalfWidth;
            Vector3 fwd = cam.transform.forward;
            float fz = Mathf.Abs(fwd.z);
            if (fz < 0.05f) fz = 0.05f;
            float dist = (z - cam.transform.position.z) / fz;
            if (dist <= 0f) return FieldHalfWidth;
            float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.PI / 180f) * dist;
            float halfW = halfH * Mathf.Max(0.2f, cam.aspect);
            return Mathf.Clamp(halfW, 3f, maxClamp);
        }

        /// <summary>The true visible half-width at a depth for the far-background skiers and the
        /// snow scooter, which must run fully edge to edge. Unlike the snowman-facing overload it
        /// is NOT capped at 22 (that value is tuned for the near marching lane and would pin
        /// distant traffic to the middle of a wide desktop screen); it only keeps a degenerate
        /// camera from flinging them to infinity.</summary>
        public static float BackgroundHalfWidthAtZ(Camera cam, float z)
        {
            return VisibleHalfWidthAtZ(cam, z, 90f);
        }

        // ---- Palette -----------------------------------------------------------
        public static readonly Color CannonYellow = new Color(0.98f, 0.78f, 0.06f, 1f);
        public static readonly Color CannonYellowDark = new Color(0.78f, 0.57f, 0.03f, 1f);
        public static readonly Color CannonSteel = new Color(0.45f, 0.47f, 0.52f, 1f);
        public static readonly Color SnowWhite = new Color(0.95f, 0.97f, 1.00f, 1f);
        public static readonly Color CoalBlack = new Color(0.08f, 0.08f, 0.10f, 1f);
        public static readonly Color CarrotOrange = new Color(0.95f, 0.45f, 0.08f, 1f);
        public static readonly Color BranchBrown = new Color(0.35f, 0.22f, 0.12f, 1f);
        public static readonly Color PotMetal = new Color(0.30f, 0.28f, 0.30f, 1f);

        /// <summary>The bucket-hat colours a snowman may wear; one is picked at random per snowman.</summary>
        public static readonly Color[] PotColors =
        {
            new Color(0.30f, 0.28f, 0.30f, 1f), // gunmetal
            new Color(0.78f, 0.18f, 0.14f, 1f), // red
            new Color(0.16f, 0.36f, 0.72f, 1f), // blue
            new Color(0.18f, 0.55f, 0.28f, 1f), // green
            new Color(0.55f, 0.36f, 0.16f, 1f), // copper
            new Color(0.85f, 0.66f, 0.12f, 1f), // brass
            new Color(0.45f, 0.47f, 0.52f, 1f), // steel
        };
        public static readonly Color SkyBlue = new Color(0.62f, 0.79f, 0.93f, 1f);
        public static readonly Color GroundSnow = new Color(0.92f, 0.95f, 1.00f, 1f);
    }

    /// <summary>PlayerPrefs backed settings and the persisted high score.</summary>
    public static class Settings
    {
        const string KeySound = "sc_sound";
        const string KeyMusic = "sc_music";
        const string KeyQuality = "sc_quality";
        const string KeyVibration = "sc_vibration";
        const string KeySensitivity = "sc_mouse_sensitivity";
        const string KeyInvertY = "sc_invert_y";
        const string KeyHighScore = "sc_high_score";
        const string KeyVolume = "sc_volume";
        const string KeyCannonSound = "sc_cannon_sound";
        const string KeySnowmanSound = "sc_snowman_sound";

        public const int QualityLevelCount = 3;

        public static bool Sound
        {
            get { return PlayerPrefs.GetInt(KeySound, 1) != 0; }
            set { PlayerPrefs.SetInt(KeySound, value ? 1 : 0); }
        }

        public static bool Music
        {
            get { return PlayerPrefs.GetInt(KeyMusic, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyMusic, value ? 1 : 0); }
        }

        public static bool Vibration
        {
            get { return PlayerPrefs.GetInt(KeyVibration, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyVibration, value ? 1 : 0); }
        }

        public static int Quality
        {
            get
            {
                int v = PlayerPrefs.GetInt(KeyQuality, QualityLevelCount - 1);
                return Mathf.Clamp(v, 0, QualityLevelCount - 1);
            }
            set
            {
                PlayerPrefs.SetInt(KeyQuality, Mathf.Clamp(value, 0, QualityLevelCount - 1));
                ApplyQuality();
            }
        }

        public static float MouseSensitivity
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, 1f), 0.15f, 4f); }
            set { PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, 0.15f, 4f)); }
        }

        public static bool InvertY
        {
            get { return PlayerPrefs.GetInt(KeyInvertY, 0) != 0; }
            set { PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); }
        }

        /// <summary>Master volume, a continuous 0..1 value driven by the menu slider.</summary>
        public static float Volume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 1f)); }
            set { PlayerPrefs.SetFloat(KeyVolume, Mathf.Clamp01(value)); }
        }

        /// <summary>Whether the cannon's throw/pop effects are audible.</summary>
        public static bool CannonSound
        {
            get { return PlayerPrefs.GetInt(KeyCannonSound, 1) != 0; }
            set { PlayerPrefs.SetInt(KeyCannonSound, value ? 1 : 0); }
        }

        /// <summary>Whether the snowmen's scream effects are audible.</summary>
        public static bool SnowmanSound
        {
            get { return PlayerPrefs.GetInt(KeySnowmanSound, 1) != 0; }
            set { PlayerPrefs.SetInt(KeySnowmanSound, value ? 1 : 0); }
        }

        public static int HighScore
        {
            get { return PlayerPrefs.GetInt(KeyHighScore, 0); }
            set
            {
                if (value > PlayerPrefs.GetInt(KeyHighScore, 0))
                {
                    PlayerPrefs.SetInt(KeyHighScore, value);
                    PlayerPrefs.Save();
                }
            }
        }

        public static void ResetHighScore()
        {
            PlayerPrefs.DeleteKey(KeyHighScore);
            PlayerPrefs.Save();
        }

        public static void Save() { PlayerPrefs.Save(); }

        /// <summary>Applies the stored quality level, clamped to what the project really has.</summary>
        public static void ApplyQuality()
        {
            int count = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (count <= 0) return;
            QualitySettings.SetQualityLevel(Mathf.Min(Quality, count - 1));
        }

        /// <summary>Loads every persisted value once at start-up.</summary>
        public static void LoadAndApply()
        {
            ApplyQuality();
        }
    }
}
