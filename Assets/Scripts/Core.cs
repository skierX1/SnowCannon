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

    /// <summary>The two run modes. Classic plays timed, escalating levels and ends on a level-up
    /// pick; Endless is a single ever-harder run scored against the local leaderboard.</summary>
    public enum GameMode { Classic, Endless }

    /// <summary>The aiming aid shown while the barrel tracks. Exactly one is active at a time:
    /// the flat ground landing reticle (a point on the snow), or the shimmering trajectory ribbon
    /// tracing the last third of the shot's arc. Chosen from the options menu.</summary>
    public enum AimGuide { GroundReticle, Trajectory, Off }

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
        public const float SnowballSpeed = 76f;
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

        // ---- Combo / streak multiplier ------------------------------------------
        // Two independent counters live on the director: `streak` (raw consecutive hits) and
        // `tier` (the active multiplier rung). The streak is only ever zeroed by the idle timeout;
        // a missed shot demotes the tier one rung (with a one-kill re-earn lock) but never the
        // streak. TierFromStreak maps a streak to the highest rung it has earned; TierMultiplier
        // maps a rung index to its score multiplier. Both tables are trivially re-tunable.
        public const float ComboIdleReset = 1.5f;
        static readonly int[] ComboThresholds = { 3, 6, 10 };    // streak needed to reach tier 1,2,3
        static readonly int[] ComboMultipliers = { 1, 2, 3, 5 }; // score multiplier for tier 0..3

        /// <summary>The highest multiplier rung a given consecutive-hit streak has earned.</summary>
        public static int TierFromStreak(int streak)
        {
            int t = 0;
            for (int i = 0; i < ComboThresholds.Length; i++)
                if (streak >= ComboThresholds[i]) t = i + 1;
            return t;
        }

        /// <summary>The score multiplier a given rung index yields (clamped to the table).</summary>
        public static int TierMultiplier(int tier)
        {
            if (tier < 0) tier = 0;
            if (tier >= ComboMultipliers.Length) tier = ComboMultipliers.Length - 1;
            return ComboMultipliers[tier];
        }

        // ---- Snowman archetypes -------------------------------------------------
        // Per-kind hit points and the extra hits tougher kinds need. Runner/Splitter die in one
        // hit; Tank/Banner/Bomber take more. The kind is chosen by the director's level-weighted
        // spawn mix, so the field escalates from plain Runners to a full tactical blend.
        public const int TankHp = 3;
        public const int BannerHp = 2;
        public const int BomberHp = 2;
        public const float BannerAuraRadius = 6.5f;
        public const float BannerSpeedBoost = 0.6f;
        public const float BomberJamTime = 1.1f;

        // ---- Boss (appears every BossEveryLevels levels) --------------------------
        // A big multi-phase snowman: it summons minions, lobs jamming volleys, and must be hit on
        // its glowing head weak-point for bonus damage. Body hits still count but do less.
        public const int BossEveryLevels = 5;
        public const int BossBaseHp = 16;
        public const float BossHpPerLevel = 2f;
        public const float BossScale = 3.4f;
        public const float BossSpeed = 1.05f;
        public const int BossPoints = 40;
        public const int BossBodyDamage = 1;
        public const int BossHeadDamage = 3;
        public const float BossHeadHeight = 0.82f;      // fraction of height where the head sits
        public const float BossHeadRadius = 0.9f;       // weak-point catch radius (world units)
        public const float BossSummonInterval = 4.5f;
        public const float BossVolleyInterval = 6.5f;

        // ---- Premium shots --------------------------------------------------------
        // Per-shot water costs / counts / spread / pierce live on ShotDefs. These are the shared
        // tuning the blizzard's field-chill uses on impact.
        public const float BlizzardChillTime = 3.5f;
        public const float BlizzardChillAmount = 0.4f;

        // ---- Level-up cards -------------------------------------------------------
        public const int LevelUpCardCount = 3;

        // ---- Friendly fire --------------------------------------------------------
        // Hitting a kid or penguin costs points and spills water, so wild spraying is punished.
        public const int FriendlyPenaltyPoints = 5;
        public const int FriendlyPenaltyWater = 4;

        // ---- Juice ----------------------------------------------------------------
        // A brief global time-scale dip on a kill, plus a camera punch, for impact feel.
        public const float HitStopTime = 0.05f;
        public const float HitStopScale = 0.1f;

        // ---- Level progression -------------------------------------------------
        public const int FirstLevelDuration = 45;
        public const int SecondLevelDuration = 90;
        public const int LevelDurationStep = 10;
        public const float FirstSpawnInterval = 7.0f;
        public const float SpawnIntervalFactor = 1f / 1.5f;
        public const float MinSpawnInterval = 0.6f;

        /// <summary>Level 1 = 45 s, then +1 s for every level after it (46, 47, 48, ...).</summary>
        public static int LevelDuration(int level)
        {
            return FirstLevelDuration + Mathf.Max(0, level - 1);
        }

        /// <summary>Level 1 opens at FirstSpawnInterval (7 s) so the field breathes, then every
        /// level after it spawns 1.5x faster (the interval divides by 1.5 per level), floored at
        /// MinSpawnInterval so it never becomes an unbroken wall of snowmen.</summary>
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
        const string KeyMaxLevel = "sc_max_level";
        const string KeyStartLevel = "sc_start_level";
        const string KeyVolume = "sc_volume";
        const string KeyCannonSound = "sc_cannon_sound";
        const string KeySnowmanSound = "sc_snowman_sound";
        const string KeyAimGuide = "sc_aim_guide";

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

        /// <summary>Which aiming aid is shown while the barrel tracks: the flat ground landing
        /// reticle, or the shimmering trajectory ribbon tracing the last third of the arc. Exactly
        /// one is active at a time; cycled from the options menu. The ground reticle is the default.</summary>
        public static AimGuide AimGuideMode
        {
            get
            {
                int v = PlayerPrefs.GetInt(KeyAimGuide, (int)AimGuide.GroundReticle);
                if (v == (int)AimGuide.Trajectory) return AimGuide.Trajectory;
                if (v == (int)AimGuide.Off) return AimGuide.Off;
                return AimGuide.GroundReticle;
            }
            set { PlayerPrefs.SetInt(KeyAimGuide, (int)value); }
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

        /// <summary>The highest level the player has ever reached across all runs. Only ever grows,
        /// so it is a true lifetime best. Drives the title-screen start-level picker and is shown
        /// on the high-score board. Reset back to 1 together with the high score.</summary>
        public static int MaxLevelReached
        {
            get { return Mathf.Max(1, PlayerPrefs.GetInt(KeyMaxLevel, 1)); }
            set
            {
                if (value > PlayerPrefs.GetInt(KeyMaxLevel, 1))
                {
                    PlayerPrefs.SetInt(KeyMaxLevel, value);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>The level the next run starts on, chosen on the title screen and clamped to
        /// 1..MaxLevelReached so a player can never jump ahead of the level they have actually
        /// reached. Reset back to 1 together with the high score.</summary>
        public static int StartLevel
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(KeyStartLevel, 1), 1, MaxLevelReached); }
            set { PlayerPrefs.SetInt(KeyStartLevel, Mathf.Clamp(value, 1, MaxLevelReached)); PlayerPrefs.Save(); }
        }

        public static void ResetHighScore()
        {
            PlayerPrefs.DeleteKey(KeyHighScore);
            PlayerPrefs.DeleteKey(KeyMaxLevel);
            PlayerPrefs.DeleteKey(KeyStartLevel);
            PlayerPrefs.Save();
        }

        // ---- Meta progression (lifetime-score bank + permanent unlocks) ----------
        const string KeyCoins = "sc_coins";
        const string KeyPermCombo = "sc_perm_combo";
        const string KeyPermWater = "sc_perm_water";
        const string KeyMode = "sc_mode";

        /// <summary>The spendable bank. Every run deposits its final score here; the shop spends it
        /// on permanent unlocks that fold into the next run's start.</summary>
        public static int Coins
        {
            get { return PlayerPrefs.GetInt(KeyCoins, 0); }
            set { PlayerPrefs.SetInt(KeyCoins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static void DepositCoins(int amount)
        {
            if (amount <= 0) return;
            Coins = Coins + amount;
        }

        public const int MaxPermCombo = 3;
        public const int MaxPermWater = 3;

        public static int PermComboBoost
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(KeyPermCombo, 0), 0, MaxPermCombo); }
        }

        public static int PermExtraWater
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(KeyPermWater, 0), 0, MaxPermWater); }
        }

        /// <summary>The escalating price of the next level of a permanent track.</summary>
        public static int PermCost(int currentLevel) { return 60 + currentLevel * 90; }

        /// <summary>Buys one level of the combo-starter track if the bank covers it.</summary>
        public static bool BuyPermCombo()
        {
            int lvl = PermComboBoost;
            if (lvl >= MaxPermCombo) return false;
            int cost = PermCost(lvl);
            if (Coins < cost) return false;
            Coins = Coins - cost;
            PlayerPrefs.SetInt(KeyPermCombo, lvl + 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Buys one level of the reserve-water track if the bank covers it.</summary>
        public static bool BuyPermWater()
        {
            int lvl = PermExtraWater;
            if (lvl >= MaxPermWater) return false;
            int cost = PermCost(lvl);
            if (Coins < cost) return false;
            Coins = Coins - cost;
            PlayerPrefs.SetInt(KeyPermWater, lvl + 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>The selected run mode (Classic timed levels, or Endless).</summary>
        public static GameMode Mode
        {
            get { return (GameMode)Mathf.Clamp(PlayerPrefs.GetInt(KeyMode, 0), 0, 1); }
            set { PlayerPrefs.SetInt(KeyMode, (int)value); PlayerPrefs.Save(); }
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
