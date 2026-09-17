using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SnowCannon
{
    /// <summary>
    /// The play-scene director. It builds the whole arcade scene procedurally (camera, sun,
    /// snow field, cannon, HUD), owns the single input surface, runs the spawn loop and the
    /// level timer, watches for a snowman reaching the bottom, and shows the final score.
    /// </summary>
    public sealed class SnowCannonGame : MonoBehaviour
    {
        enum State { Playing, GameOver }

        State state = State.Playing;

        public PlayerControls Controls { get; private set; }

        Camera cam;
        // The aspect the camera was last fitted to. When the device rotates (or a desktop window
        // resizes) the aspect changes and FitCamera must re-run so the FOV, the cannon's strafe
        // reach, and the lake's corner placement all re-derive from the new projection.
        float lastFitAspect = -1f;
        readonly List<Snowman> active = new List<Snowman>();

        int level = 1;
        int score;
        int hits;
        float levelTimer;
        float spawnTimer;

        // Combo state: `comboStreak` is the raw consecutive-hit count (only the idle timeout zeroes
        // it), `comboTier` is the active multiplier rung, `comboMissLock` is the number of kills still
        // owed before the tier may climb again after a miss, and `comboTimer` is the idle countdown.
        int comboStreak;
        int comboTier;
        int comboMissLock;
        float comboTimer;

        Text levelText;
        Text timeText;
        Text scoreText;
        Text waterText;
        Text comboText;
        RectTransform popupRoot;

        GameObject gameOverPanel;
        Text finalScoreText;
        float gameOverCooldown;

        Cannon cannon;
        Lake lake;
        int totalSpawned;
        int snowballsFired;

        // ---- Tier 2 / Tier 3 run state ---------------------------------------
        GameMode mode = GameMode.Classic;
        ShotType armedShot = ShotType.Basic;

        // The every-fifth-level boss, tracked separately from the marching `active` list.
        Boss boss;

        // The current level's optional objective (Classic mode).
        Objective objective;

        // The weather director that drives the shared GameRuntime wind / visibility / field-slow.
        WeatherDirector weather;

        // Friendly targets (kids / penguins) that cross the near field; hitting them is penalised.
        readonly List<Friendly> friendlies = new List<Friendly>();
        float friendlyTimer = 6f;

        // Hit-stop juice: a brief global time-scale dip on a kill.
        float hitStopTimer;

        // The level-up overlay currently shown (null when not paused for a pick).
        LevelUpUI levelUpUI;

        // HUD extras added by the Tier 2/3 features.
        Text objectiveText;
        Text shotText;
        Image dangerVignette;
        Text bossText;
        Image bossBarImage;
        GameObject bossBarGo;
        readonly List<Button> shotButtons = new List<Button>();

        // Round-robin lane counter so spawns are dealt out evenly across the field width
        // instead of leaving the horizontal spread to chance (which clumps them centrally).
        int spawnLane;
        const int SpawnLanes = 7;

        bool smokeEnabled;
        float smokeElapsed;
        int smokeFrame;
        int smokePhase;
        float phaseClock;
        float nextAim;
        bool smokeFinished;
        string colliderProbeResult = "pending";
        string captureNote = "pending";
        bool capturedPlay;
        bool capturedOver;
        readonly System.Text.StringBuilder smokeEvents = new System.Text.StringBuilder();

        /// <summary>The live play-scene director, so self-contained entities (the Bomber's lobbed
        /// shot) can reach the cannon without being handed a reference at spawn time.</summary>
        public static SnowCannonGame Active;

        /// <summary>The player's cannon, for entities that need to aim at or harass it.</summary>
        public Cannon CannonRef => cannon;

        /// <summary>World position of the cannon, used as the Bomber's lob target.</summary>
        public Vector3 CannonWorldPos => cannon != null ? cannon.transform.position : new Vector3(0f, 1f, -5f);

        /// <summary>Called when a Bomber's shot lands near the cannon: briefly jams the firing.</summary>
        public void JamCannon(float t) { if (cannon != null) cannon.Jam(t); }

        /// <summary>The shot the player has armed (basic / spray / lance / blizzard). Read by the
        /// cannon's fire path to pick the volley size, spread, pierce, chill and water cost.</summary>
        public ShotType ArmedShot => armedShot;

        /// <summary>Arms a premium shot for the next trigger pull. Clamps to Basic if the lake cannot
        /// even cover its cheapest possible cost, so the player is never stuck on an unaffordable shot.</summary>
        public void SelectShot(ShotType t)
        {
            armedShot = t;
            RefreshShotHud();
        }

        /// <summary>Spends a specific number of lake marks (premium shots cost more than one). Returns
        /// false and flashes the basin when the lake cannot cover the cost.</summary>
        public bool TryConsumeLakeMarks(int cost)
        {
            if (lake == null) return true;
            return lake.OnFireAttemptMarks(cost);
        }

        /// <summary>Launches a premium snowball (piercing / chilling / tinted). The basic path keeps
        /// using <see cref="SpawnSnowball"/> so existing callers are unaffected.</summary>
        public void SpawnSnowballPremium(Vector3 origin, Vector3 direction, int pierce, bool chills, Color tint)
        {
            if (state != State.Playing) return;
            snowballsFired++;
            Snowball.Fire(origin, direction, this, pierce, chills, tint);
        }

        /// <summary>A blizzard ball connected: briefly chill the whole field beyond the weather baseline.</summary>
        public void ApplyBlizzardChill() { blizzardChill = GameConfig.BlizzardChillTime; }
        float blizzardChill;

        /// <summary>A snowball hit a friendly (kid / penguin): deduct points and spill lake water, so
        /// wild spraying into the near field is punished.</summary>
        public void RegisterFriendlyHit()
        {
            score = Mathf.Max(0, score - GameConfig.FriendlyPenaltyPoints);
            if (lake != null) lake.OnFireAttemptMarks(GameConfig.FriendlyPenaltyWater);
            if (popupRoot != null && cam != null)
            {
                // Float the penalty from wherever the friendly was; the caller already played a pop.
            }
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayThrow();
        }

        /// <summary>The boss lobs a volley of jamming shells at the cannon.</summary>
        public void BossLobVolley(Vector3 from)
        {
            int n = Random.Range(2, 4);
            for (int i = 0; i < n; i++)
                BomberShot.Launch(from + Vector3.up * 2f + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)),
                                  CannonWorldPos, this);
        }

        void Awake()
        {
            Settings.LoadAndApply();
            Active = this;
            smokeEnabled = File.Exists("c:/tmp/sc_smoke_enabled");
            EnsureAudio();

            // The run is always fullscreen, with the pointer captured inside the window.
            ConfigureCursorForPlay();

            Controls = new PlayerControls();
            Controls.Enable();

            BuildWorld();
            BuildHud();

            cannon = Cannon.Create(this);
            TouchControls.Attach(this);

            // The ballistic landing reticle tracks the barrel and shows where a shot would touch
            // down. It is hidden while the lake is dry or when the option is off.
            AimReticle.Create(transform, cam, cannon, this);

            // The 3D water pond lives in the world (bottom-left of the field) and feeds the cannon
            // through a yellow hose that runs along the bottom of the screen.
            lake = Lake.Create(transform, cam);
            if (lake != null && cannon != null)
            {
                lake.SetCannon(cannon.transform);   // lets the pond auto-shrink clear of the cannon
                lake.ConnectHose(cannon.transform.position);
            }

            // Give the solid props a soft ground shadow; keep the water and text decals out of it.
            if (cannon != null) Mat.SetShadows(cannon.gameObject, true, false);
            if (lake != null) Mat.SetShadows(lake.gameObject, false, false);

            GameSession.BeginRun();
            level = 1;
            score = 0;
            hits = 0;
            comboStreak = 0;
            comboTier = 0;
            comboMissLock = 0;
            comboTimer = 0f;
            levelTimer = GameConfig.LevelDuration(level);
            spawnTimer = 0.6f; // a short grace beat before the first snowman

            // ---- Tier 2 / Tier 3 run set-up -------------------------------------
            mode = Settings.Mode;
            armedShot = ShotType.Basic;
            GameRuntime.Reset();
            RunUpgrades.ResetRun(Settings.PermComboBoost, Settings.PermExtraWater);
            // Fold the RESERVE WATER upgrade / meta head-start into the lake's opening level.
            if (lake != null) lake.GrantMarks(RunUpgrades.ExtraWater);
            weather = WeatherDirector.Attach(transform);
            objective = mode == GameMode.Classic ? Objective.Roll(level) : null;
            friendlyTimer = 7f;
            hitStopTimer = 0f;
            boss = null;

            // Every run gets one of the four background tunes, chosen at random.
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayRandomMusic();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (Controls != null) Controls.Dispose();
        }

        static void EnsureAudio()
        {
            if (AudioDirector.Instance != null) return;
            var go = new GameObject("AudioDirector");
            go.AddComponent<AudioDirector>();
        }

        /// <summary>Full-screen the run and capture the mouse: the cursor is hidden and can
        /// never leave the game window while the player is aiming.</summary>
        void ConfigureCursorForPlay()
        {
            if (!Application.isEditor) Screen.fullScreen = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>Release the pointer again so the player can see and use the cursor, e.g.
        /// on the game-over card.</summary>
        void ConfigureCursorForMenu()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        // ---- world --------------------------------------------------------------

        void BuildWorld()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.SkyBlue;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            // Make sure the editor Game view actually picks this camera up: a runtime camera
            // that is not flagged as a Game camera on display 0 shows as "No cameras rendering".
            cam.cameraType = CameraType.Game;
            cam.targetDisplay = 0;
            cam.cullingMask = -1;
            cam.enabled = true;
            FitCamera();

            // A soft sun that casts a gentle ground shadow under the props.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.98f, 0.92f, 1f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.32f;
            sunGo.transform.rotation = Quaternion.Euler(new Vector3(52f, -18f, 0f));

            // The snow field: one huge textured plane under the whole play area.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "LargePlane";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0f, 0f, 12f);
            ground.transform.localScale = new Vector3(90f, 1f, 140f);
            var groundMat = Mat.Textured(TextureFactory.GroundSnow(), GameConfig.GroundSnow);
            // The plane is 900 x 1400 world units but its UVs span 0..1 once, so tile the snow
            // texture to a square world scale (~12.5 units per tile) to keep the dirt patches and
            // grain at a believable size instead of stretched across the whole field.
            var groundTex = groundMat.mainTexture;
            if (groundTex != null)
            {
                groundTex.wrapMode = TextureWrapMode.Repeat;
                groundTex.anisoLevel = 4;
            }
            groundMat.mainTextureScale = new Vector2(72f, 112f);
            groundMat.SetTextureScale("_BaseMap", new Vector2(72f, 112f));
            ground.GetComponent<MeshRenderer>().material = groundMat;

            // A whisper of atmosphere: sparse flakes and a few faint drifting clouds.
            Weather.Attach(transform);

            // Decorative cross-country skiers cruising the far background, behind the spawn line.
            SkierManager.Attach(transform, cam);
        }

        /// <summary>Frames the field for both landscape and portrait screens.</summary>
        void FitCamera()
        {
            float aspect = cam != null ? cam.aspect : 1.7f;
            if (aspect <= 0f) aspect = 1.7f;

            cam.transform.position = new Vector3(0f, 11f, -18f);
            cam.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 1.4f, 10f) - cam.transform.position, Vector3.up);

            // Vertical FOV is the fixed one, so widen it when the screen is tall and narrow.
            float baseV = 52f;
            cam.fieldOfView = aspect < 1f
                ? Mathf.Clamp(baseV / aspect * 0.82f, 52f, 88f)
                : baseV;
        }

        void Update()
        {
#if UNITY_EDITOR
            SmokeTick();
#endif
            // Re-fit the camera whenever the screen aspect changes (a phone rotation, a desktop
            // window resize). Without this the FOV stays at the orientation the run started in, so
            // after rotating to landscape and back the projection is stale: the lake lands in the
            // wrong corner at the wrong size and the cannon reads as much larger than it should.
            if (cam != null && !Mathf.Approximately(cam.aspect, lastFitAspect))
            {
                lastFitAspect = cam.aspect;
                FitCamera();
            }

            // Hit-stop juice: a brief global time-scale dip on a kill. Counted on unscaled time so it
            // always drains even while the field is slowed, then the shared time-scale is recomposed.
            if (hitStopTimer > 0f)
            {
                hitStopTimer -= Time.unscaledDeltaTime;
                if (hitStopTimer < 0f) hitStopTimer = 0f;
            }
            ApplyTimeScale();

            if (state == State.Playing)
            {
                UpdatePlaying();
            }
            else
            {
                UpdateGameOver();
            }
        }

        /// <summary>The single authority over Time.timeScale: the level-up overlay freezes the game,
        /// otherwise a pending hit-stop slows it, otherwise it runs at full speed.</summary>
        void ApplyTimeScale()
        {
            if (levelUpUI != null) Time.timeScale = 0f;
            else if (hitStopTimer > 0f) Time.timeScale = GameConfig.HitStopScale;
            else Time.timeScale = 1f;
        }

        void UpdatePlaying()
        {
            // ESC returns to the title screen (a second ESC on the menu is what quits). Release
            // the captured cursor first so the menu buttons are usable, then load the menu scene.
            if (Controls != null && Controls.IsEscapePressed())
            {
                ConfigureCursorForMenu();
                SceneManager.LoadScene(GameSession.MenuScene);
                return;
            }

            // Level timer, then auto-advance to a longer, faster level. In Classic mode a level
            // boundary pauses the game and offers a choice of three upgrades; Endless just hardens.
            levelTimer -= Time.deltaTime;
            if (levelTimer <= 0f)
            {
                if (mode == GameMode.Classic)
                {
                    CompleteObjective();
                    TriggerLevelUp();
                    return;
                }
                level++;
                levelTimer = GameConfig.LevelDuration(level);
                MaybeSpawnBoss();
            }

            // Compose the field slow from the upgrade baseline, the blizzard and any ball-chill.
            if (blizzardChill > 0f) blizzardChill -= Time.deltaTime;
            float bslow = weather != null ? weather.BlizzardSlow : 0f;
            GameRuntime.FieldSlow = Mathf.Clamp01(RunUpgrades.FieldSlowBase + bslow +
                                                 (blizzardChill > 0f ? GameConfig.BlizzardChillAmount : 0f));

            // Shot-select hotkeys (1/2/3 arm the premium shots, 0/` returns to basic).
            PollShotSelect();

            // Friendlies (kids / penguins) toddle across the near field on a lazy cadence.
            UpdateFriendlies();

            // The combo decays out once the player stops landing hits. This idle timeout is the
            // ONLY thing that zeroes the streak; a missed shot never does (it only demotes the tier).
            if (comboTimer > 0f)
            {
                comboTimer -= Time.deltaTime;
                if (comboTimer <= 0f) { comboStreak = 0; comboTier = 0; comboMissLock = 0; }
            }

            // Spawn loop.
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                SpawnSnowman();
                spawnTimer = GameConfig.SpawnInterval(level);
            }

            // Advance the living snowmen and look for a breach.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var sm = active[i];
                if (sm == null) { active.RemoveAt(i); continue; }

                if (sm.IsDone)
                {
                    Destroy(sm.gameObject);
                    active.RemoveAt(i);
                    continue;
                }

                // The run ends the instant a live snowman reaches the cannon, the lake (with its
                // grass bank), or the supply hose — not only when it crosses the defeat line. The
                // proximity test is done here (the director owns the geometry) rather than via physics
                // colliders, so it is deterministic and cannot be missed by a fast-moving snowman.
                if (!sm.IsDying)
                {
                    var sp = sm.transform.position;
                    bool breach = sp.z <= GameConfig.DefeatZ;
                    if (!breach && cannon != null)
                    {
                        var cp = cannon.transform.position;
                        float rr = cannon.FootprintRadius + sm.FootprintRadius;
                        float cdx = sp.x - cp.x, cdz = sp.z - cp.z;
                        if (cdx * cdx + cdz * cdz <= rr * rr) breach = true;
                    }
                    if (!breach && lake != null && lake.TouchesSnowman(sp, sm.FootprintRadius)) breach = true;
                    if (breach)
                    {
                        if (objective != null) objective.OnBreach();
                        GameOver(false);
                        return;
                    }
                }
            }

            UpdateBoss();

            RefreshHud();
        }

        void SpawnSnowman()
        {
            float size = Random.Range(GameConfig.SnowmanMinScale, GameConfig.SnowmanMaxScale);
            float speed = Random.Range(GameConfig.SnowmanMinSpeed, GameConfig.SnowmanMaxSpeed)
                          * (1f + (level - 1) * 0.20f);

            // A small depth stagger so the far wave fans across the screen instead of collapsing
            // into one straight line (which perspective squeezes toward the centre).
            float z = GameConfig.SpawnZ - Random.Range(0f, 3.5f);

            // Deal spawns out across the field in round-robin lanes (with a little jitter inside
            // each lane). The usable width is the field that is actually VISIBLE at this depth,
            // not the fixed logical half-width: because the camera is a pinhole, the far spawn
            // depth projects the logical field into only a narrow slice of the screen, so using
            // the visible half-width is what truly spreads the wave over the whole screen width.
            int lane = spawnLane % SpawnLanes;
            spawnLane++;
            float visibleHalf = GameConfig.VisibleHalfWidthAtZ(cam, z);
            float usable = Mathf.Max(2f, visibleHalf - 1.2f);
            float laneW = (usable * 2f) / SpawnLanes;
            float x = -usable + laneW * (lane + 0.5f) + Random.Range(-laneW * 0.32f, laneW * 0.32f);
            x = Mathf.Clamp(x, -usable, usable);

            // A healthy base diagonal so they never look like they only march straight down.
            var kind = PickKind(level);
            var sm = Snowman.Spawn(size, speed, -1, Mathf.Min(30f, 16f + (level - 1) * 4f), kind);
            sm.SetOwner(this);
            sm.transform.position = new Vector3(x, 0f, z);
            active.Add(sm);
            totalSpawned++;

            // Every snowman that arrives pours water back into the lake, so letting the wave
            // build reloads the cannon -- but a snowman that reaches the bottom ends the run.
            if (lake != null) lake.OnSnowmanSpawned();
#if UNITY_EDITOR
            if (smokeEnabled) SmokeLog("spawn@" + smokeElapsed.ToString("0.0") + " x=" + x.ToString("0.0") + " kind=" + kind);
#endif
        }

        /// <summary>Chooses a snowman archetype for the current level. Early levels are pure Runners;
        /// Tank, Splitter, Banner and Bomber are introduced one per level and their share of the mix
        /// grows, so the field escalates from a simple march into a tactical blend the player must
        /// read and prioritise.</summary>
        SnowmanKind PickKind(int lvl)
        {
            if (lvl <= 1) return SnowmanKind.Runner;
            int runner = 6;
            int tank = lvl >= 2 ? 3 : 0;
            int splitter = lvl >= 3 ? 3 : 0;
            int banner = lvl >= 4 ? 2 : 0;
            int bomber = lvl >= 5 ? 2 : 0;
            int total = runner + tank + splitter + banner + bomber;
            int r = Random.Range(0, total);
            if ((r -= runner) < 0) return SnowmanKind.Runner;
            if ((r -= tank) < 0) return SnowmanKind.Tank;
            if ((r -= splitter) < 0) return SnowmanKind.Splitter;
            if ((r -= banner) < 0) return SnowmanKind.Banner;
            return SnowmanKind.Bomber;
        }

        /// <summary>Spawns one snowman of the given kind at an explicit world position and registers
        /// it with the director (so it is tracked for breach/cleanup). Used by a Splitter's children,
        /// which must appear at the parent's spot rather than on the far spawn line.</summary>
        public Snowman SpawnAt(SnowmanKind kind, Vector3 pos, float size, float speed)
        {
            var sm = Snowman.Spawn(size, speed, -1, 30f, kind);
            sm.SetOwner(this);
            sm.transform.position = pos;
            active.Add(sm);
            totalSpawned++;
            if (lake != null) lake.OnSnowmanSpawned();
            return sm;
        }

        // ---- game over ----------------------------------------------------------

        void UpdateGameOver()
        {
            gameOverCooldown -= Time.deltaTime;
            if (gameOverCooldown > 0f) return;

            // Wait for a genuine fresh press. The old touch test used touches.Count > 0, which a
            // phantom or resting Windows touch point keeps true, so the panel vanished on its own.
            if (DismissPressedThisFrame())
            {
                SceneManager.LoadScene(GameSession.MenuScene);
            }
        }

        static bool DismissPressedThisFrame()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.press != null && ts.press.wasPressedThisFrame) return true;
            // ESC (and, on Android, the system back button, which the new Input System surfaces as
            // the escape key) dismisses the card back to the menu. Without this the back button
            // would fall through to Android's default "back quits the app" behaviour.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            return false;
        }

        void GameOver(bool survived)
        {
            if (state == State.GameOver) return;
            state = State.GameOver;

            // The run is over: bring the cursor back while the game-over card is shown.
            ConfigureCursorForMenu();

            GameSession.EndRun(score, level, hits, survived);
            comboStreak = 0; comboTier = 0; comboMissLock = 0; comboTimer = 0f;
            Time.timeScale = 1f;

            // Bank the run's score as lifetime coins for the shop and post it to the local board.
            Settings.DepositCoins(score);
            Leaderboard.Submit(score, level, mode == GameMode.Endless ? "ENDLESS" : "CLASSIC");

            if (finalScoreText != null)
            {
                finalScoreText.text =
                    "FINAL SCORE   " + score + "\n" +
                    "LEVEL REACHED   " + level + "\n" +
                    "COINS EARNED   +" + score + "\n\n" +
                    "HIGH SCORE   " + Settings.HighScore + "\n\n" +
                    "tap anywhere / press ESC to continue";
            }
            if (gameOverPanel != null) gameOverPanel.SetActive(true);
            gameOverCooldown = 0.5f;

            // Start the fan's spin-down on the same frame the card becomes visible, so the
            // machinery visibly loses power while the player reads the score.
            if (cannon != null) cannon.StopFan();
        }

        // ---- public hooks used by Cannon and Snowball -------------------------

        public void SpawnSnowball(Vector3 origin, Vector3 direction)
        {
            if (state != State.Playing) return;
            snowballsFired++;
            Snowball.Fire(origin, direction, this);
        }

        /// <summary>Read-only: can the cannon fire right now (the lake still holds water)? Unlike
        /// <see cref="TryConsumeLake"/> this spends nothing, so it is safe to poll every frame from
        /// the aim reticle.</summary>
        public bool CanFireNow => lake == null || lake.CanFire;

        /// <summary>The cannon asks before every shot: true only while the lake still holds
        /// water (one mark is spent). When the lake is dry the shot is refused and the basin
        /// flashes. With no lake present (e.g. a bare test scene) firing is unrestricted.</summary>
        public bool TryConsumeLake()
        {
            if (lake == null) return true;
            return lake.OnFireAttempt();
        }

        public void PlayThrowAt(Vector3 worldPosition)
        {
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayThrow();
        }

        public void PlayPopAt(Vector3 worldPosition)
        {
            if (AudioDirector.Instance != null) AudioDirector.Instance.PlayPop(worldPosition);
        }

        public void RegisterHit(IHitTarget target, int points)
        {
            // Advance the streak and refresh the idle window. The tier climbs at most one rung per
            // kill and never above what the streak has earned; a pending miss-lock (set by a recent
            // miss) spends one kill before the tier is allowed to climb again.
            comboStreak++;
            comboTimer = GameConfig.ComboIdleReset;
            int desired = GameConfig.TierFromStreak(comboStreak);
            if (comboMissLock > 0) comboMissLock--;
            else if (comboTier < desired) comboTier++;

            int mult = GameConfig.TierMultiplier(comboTier);
            int gained = points * mult;
            score += gained;
            hits++;

            // Hit-stop juice: a brief time dip so a connect has weight.
            hitStopTimer = GameConfig.HitStopTime;

            // Feed the level objective. A head hit scores PointsHead, a scooter scores PointsScooter,
            // so those values double as the event discriminators here.
            if (objective != null)
            {
                objective.OnHit();
                if (points == GameConfig.PointsHead) objective.OnHeadshot();
                else if (points == GameConfig.PointsScooter) objective.OnScooterHit();
                objective.OnComboTier(comboTier);
            }

            // Float the earned points (and the multiplier tag) up from the target's world position.
            var tr = target as Component;
            if (tr != null && popupRoot != null && cam != null)
            {
                Vector2 sp = cam.WorldToScreenPoint(tr.transform.position + Vector3.up * 1.2f);
                Color pc = comboTier >= 3 ? new Color(1f, 0.45f, 0.85f, 1f)
                           : comboTier == 2 ? new Color(1f, 0.7f, 0.25f, 1f)
                           : comboTier == 1 ? new Color(1f, 0.92f, 0.4f, 1f)
                           : Color.white;
                ScorePopup.Spawn(popupRoot, sp, mult > 1 ? "+" + gained + "  x" + mult : "+" + gained, pc);
            }
#if UNITY_EDITOR
            if (smokeEnabled) SmokeLog("REGISTERHIT pts=" + gained + " tier=" + comboTier + " hits=" + hits + " score=" + score);
#endif
            if (AudioDirector.Instance != null)
            {
                if (tr != null) AudioDirector.Instance.PlayScream(tr.transform.position);
                AudioDirector.Instance.Vibrate();
            }
        }

        /// <summary>A fired snowball left the field without connecting: demote the multiplier tier
        /// one rung (with a one-kill re-earn lock) but leave the streak intact. The streak is only
        /// ever zeroed by the idle timeout, never by a miss.</summary>
        public void RegisterMiss()
        {
            if (comboTier > 0) { comboTier--; comboMissLock = 1; }
        }

        // ---- Tier 2 / Tier 3 systems ------------------------------------------

        /// <summary>Reads the shot-select hotkeys. 1/2/3 arm SPRAY / LANCE / BLIZZARD, 0 returns to
        /// BASIC. Touch players use the on-screen shot buttons instead (see BuildHud).</summary>
        void PollShotSelect()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) SelectShot(ShotType.Spray);
            else if (kb.digit2Key.wasPressedThisFrame) SelectShot(ShotType.IceLance);
            else if (kb.digit3Key.wasPressedThisFrame) SelectShot(ShotType.Blizzard);
            else if (kb.digit0Key.wasPressedThisFrame) SelectShot(ShotType.Basic);
        }

        /// <summary>Spawns the kids / penguins that waddle across the near field on a lazy cadence.
        /// They are a hazard, not a target: hitting one costs points and water.</summary>
        void UpdateFriendlies()
        {
            for (int i = friendlies.Count - 1; i >= 0; i--)
            {
                var f = friendlies[i];
                if (f == null || f.IsDone)
                {
                    if (f != null) Destroy(f.gameObject);
                    friendlies.RemoveAt(i);
                }
            }

            friendlyTimer -= Time.deltaTime;
            if (friendlyTimer > 0f) return;
            friendlyTimer = Random.Range(7f, 13f);

            // Only let a couple be on screen at once so the field never gets crowded with no-hits.
            if (friendlies.Count >= 2) return;
            bool fromLeft = Random.value < 0.5f;
            float x = fromLeft ? -(GameConfig.FieldHalfWidth + 2f) : (GameConfig.FieldHalfWidth + 2f);
            float z = Random.Range(GameConfig.CannonMinZ + 1f, GameConfig.CannonMaxZ + 3f);
            var fr = Friendly.Spawn(this, cam, new Vector3(x, 0f, z), fromLeft ? 1f : -1f);
            if (fr != null) friendlies.Add(fr);
        }

        /// <summary>Spawns the boss on the levels that are a multiple of BossEveryLevels.</summary>
        void MaybeSpawnBoss()
        {
            if (boss != null && !boss.IsDone && !boss.IsDying) return;
            if (level < GameConfig.BossEveryLevels || level % GameConfig.BossEveryLevels != 0) return;
            boss = Boss.Spawn(this, cam, level);
            if (boss != null) boss.transform.SetParent(transform, false);
        }

        /// <summary>Tracks the live boss: ends the run if it breaches, and clears the reference once
        /// it has collapsed.</summary>
        void UpdateBoss()
        {
            if (boss == null) return;
            if (boss.IsDone) { if (boss != null) Destroy(boss.gameObject); boss = null; return; }
            if (boss.IsDying) return;

            var p = boss.transform.position;
            bool breach = p.z <= GameConfig.DefeatZ;
            if (!breach && cannon != null)
            {
                var cp = cannon.transform.position;
                float rr = cannon.FootprintRadius + 1.4f;
                float cdx = p.x - cp.x, cdz = p.z - cp.z;
                if (cdx * cdx + cdz * cdz <= rr * rr) breach = true;
            }
            if (breach) { if (objective != null) objective.OnBreach(); GameOver(false); }
        }

        /// <summary>Pauses the run and shows the level-up card. The shared time-scale authority
        /// (ApplyTimeScale) freezes the game while the overlay is up.</summary>
        void TriggerLevelUp()
        {
            if (levelUpUI != null) return;

            // Build a pool of the upgrades still below their cap, then offer up to three at random.
            var pool = new List<UpgradeType>();
            for (int i = 0; i < RunUpgrades.All.Length; i++)
                if (RunUpgrades.CanOffer(RunUpgrades.All[i])) pool.Add(RunUpgrades.All[i]);

            if (pool.Count == 0) { AdvanceLevel(); return; }   // everything maxed: just roll on

            // Fisher-Yates shuffle then take the first N.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            int n = Mathf.Min(GameConfig.LevelUpCardCount, pool.Count);
            var options = new UpgradeType[n];
            for (int i = 0; i < n; i++) options[i] = pool[i];

            levelUpUI = LevelUpUI.Show(options, _ => { levelUpUI = null; AdvanceLevel(); });
        }

        /// <summary>Commits a level advance after a pick (or when nothing was offerable).</summary>
        void AdvanceLevel()
        {
            level++;
            levelTimer = GameConfig.LevelDuration(level);
            objective = mode == GameMode.Classic ? Objective.Roll(level) : null;
            MaybeSpawnBoss();
        }

        /// <summary>Pays out the current objective's reward if it completed, then clears it.</summary>
        void CompleteObjective()
        {
            if (objective == null) return;
            if (objective.Complete)
            {
                int water, points;
                objective.Reward(out water, out points);
                score += points;
                if (lake != null) lake.GrantMarks(water);
                if (popupRoot != null)
                    ScorePopup.Spawn(popupRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.62f),
                                     "OBJECTIVE +" + points, new Color(0.6f, 1f, 0.7f, 1f));
            }
            objective = null;
        }

        // ---- HUD ----------------------------------------------------------------

        void BuildHud()
        {
            Ui.EnsureEventSystem();
            var canvas = Ui.CreateCanvas("HUD", 10);
            var root = Ui.NewRect("root", canvas.transform);
            Ui.Stretch(root);

            // A dedicated high-sort overlay canvas for floating score popups. It uses a constant
            // pixel size so a popup's anchored position maps 1:1 to screen pixels (the HUD canvas
            // itself scales with the screen, which would offset raw screen coordinates).
            var popCanvas = Ui.CreateCanvas("Popups", 20);
            var popRoot = Ui.NewRect("popupRoot", popCanvas.transform);
            Ui.Stretch(popRoot);
            var popScaler = popCanvas.GetComponent<CanvasScaler>();
            if (popScaler != null)
            {
                popScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                popScaler.scaleFactor = 1f;
            }
            popupRoot = popRoot;

            // Top-right scoreboard.
            var board = Ui.NewRect("board", root);
            Ui.Place(board, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-24f, -24f), new Vector2(320f, 250f));
            Ui.AddPanel(board, "bg", new Color(0f, 0f, 0f, 0.32f), false);

            levelText = AddLine(board, "level", 34, new Vector2(-16f, -34f));
            timeText = AddLine(board, "time", 46, new Vector2(-16f, -84f));
            scoreText = AddLine(board, "score", 34, new Vector2(-16f, -140f));
            waterText = AddLine(board, "water", 30, new Vector2(-16f, -188f));
            comboText = AddLine(board, "combo", 30, new Vector2(-16f, -232f));

            // ---- Tier 2 / Tier 3 HUD ------------------------------------------
            // A danger vignette: a soft red frame that fades in as a snowman nears the line.
            var vig = Ui.NewRect("vignette", root);
            Ui.Stretch(vig);
            dangerVignette = Ui.AddImage(vig, "vig", Ui.Circle, new Color(0.8f, 0.05f, 0.05f, 0f), false);
            dangerVignette.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dangerVignette.enabled = false;

            // Objective line, top-left.
            objectiveText = Ui.AddText(root, "objective", "", 26, new Color(0.7f, 1f, 0.78f, 1f), TextAnchor.MiddleLeft);
            Ui.Place(objectiveText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                     new Vector2(24f, -30f), new Vector2(520f, 40f));

            // Boss health bar, top-centre, hidden until a boss is live.
            var bossBar = Ui.NewRect("bossbar", root);
            Ui.Place(bossBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -34f), new Vector2(520f, 30f));
            Ui.AddPanel(bossBar, "bg", new Color(0f, 0f, 0f, 0.4f), false);
            var bossFillRt = Ui.NewRect("fill", bossBar);
            bossFillRt.anchorMin = Vector2.zero; bossFillRt.anchorMax = Vector2.one;
            bossFillRt.offsetMin = Vector2.zero; bossFillRt.offsetMax = Vector2.zero;
            bossBarImage = bossFillRt.gameObject.AddComponent<Image>();
            bossBarImage.sprite = Ui.White;
            bossBarImage.color = new Color(1f, 0.35f, 0.3f, 1f);
            bossBarImage.type = Image.Type.Filled;
            bossBarImage.fillMethod = Image.FillMethod.Horizontal;
            bossBarImage.fillClockwise = true;
            bossBarImage.raycastTarget = false;
            bossBarGo = bossBar.gameObject;
            bossBarGo.SetActive(false);

            // Shot-select buttons along the bottom-centre: BASIC / SPRAY / LANCE / BLIZZARD.
            BuildShotButtons(root);

            // Centre game-over card, hidden until the run ends.
            var over = Ui.NewRect("gameover", root);
            Ui.Stretch(over);
            Ui.AddPanel(over, "dim", new Color(0f, 0f, 0.05f, 0.62f), false);

            var card = Ui.NewRect("card", over);
            Ui.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(680f, 420f));
            Ui.AddPanel(card, "bg", new Color(0.05f, 0.12f, 0.2f, 0.92f), false);

            var title = Ui.AddText(card, "title", "GAME OVER", 64, GameConfig.CannonYellow,
                                  TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(title.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -40f), new Vector2(640f, 90f));

            finalScoreText = Ui.AddText(card, "final", "", 34, Color.white, TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(finalScoreText.gameObject), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     new Vector2(0f, -58f), new Vector2(640f, 300f));

            gameOverPanel = over.gameObject;
            gameOverPanel.SetActive(false);

            RefreshHud();
        }

        Text AddLine(RectTransform parent, string name, int size, Vector2 anchoredPos)
        {
            var t = Ui.AddText(parent, name, "", size, Color.white, TextAnchor.MiddleRight);
            Ui.Place(Ui.Rt(t.gameObject), new Vector2(1f, 1f), new Vector2(1f, 1f),
                     anchoredPos, new Vector2(300f, size + 14f));
            return t;
        }

        /// <summary>Builds the four shot-select buttons along the bottom-centre. Tapping one arms that
        /// shot (the fire button then fires it). They double as the touch equivalent of the 1/2/3/0
        /// hotkeys so the premium shots are reachable on a phone.</summary>
        void BuildShotButtons(RectTransform root)
        {
            shotButtons.Clear();
            var shots = new[] { ShotType.Basic, ShotType.Spray, ShotType.IceLance, ShotType.Blizzard };
            float bw = 150f, bh = 64f, gap = 14f;
            float total = shots.Length * bw + (shots.Length - 1) * gap;
            float startX = -total * 0.5f + bw * 0.5f;
            for (int i = 0; i < shots.Length; i++)
            {
                var s = shots[i];
                var captured = s;
                string label = (ShotDefs.Hotkey(s) == 0 ? "" : ShotDefs.Hotkey(s) + " ") + ShotDefs.Label(s);
                var btn = Ui.AddButton(root, "shot_" + s, label, new Vector2(bw, bh), 22,
                    new Color(0.1f, 0.24f, 0.4f, 1f), () => SelectShot(captured));
                Ui.Place(Ui.Rt(btn.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(startX + i * (bw + gap), 52f), new Vector2(bw, bh));
                shotButtons.Add(btn);
            }
            RefreshShotHud();
        }

        /// <summary>Highlights the armed shot's button and dims the rest so the current selection is
        /// obvious on both desktop and touch.</summary>
        void RefreshShotHud()
        {
            for (int i = 0; i < shotButtons.Count; i++)
            {
                var b = shotButtons[i];
                if (b == null) continue;
                var bg = b.targetGraphic;
                if (bg == null) continue;
                var c = bg.color;
                c.a = (i == (int)armedShot) ? 1f : 0.5f;
                bg.color = c;
            }
        }

        void RefreshHud()
        {
            if (levelText == null) return;
            levelText.text = "LEVEL  " + level;
            timeText.text = Mathf.CeilToInt(Mathf.Max(0f, levelTimer)).ToString();
            scoreText.text = "SCORE  " + score;
            if (waterText != null && lake != null)
            {
                int m = lake.Marks;
                waterText.text = "WATER  " + m + " / " + GameConfig.LakeMaxMarks;
                waterText.color = m <= 0 ? new Color(1f, 0.4f, 0.36f, 1f)
                             : m <= 10 ? new Color(1f, 0.82f, 0.32f, 1f)
                             : Color.white;
            }
            if (comboText != null)
            {
                if (comboTier > 0)
                {
                    int m = GameConfig.TierMultiplier(comboTier);
                    comboText.text = "COMBO  x" + m;
                    comboText.color = comboTier >= 3 ? new Color(1f, 0.45f, 0.85f, 1f)
                                    : comboTier == 2 ? new Color(1f, 0.7f, 0.25f, 1f)
                                    : new Color(1f, 0.92f, 0.4f, 1f);
                }
                else comboText.text = "";
            }

            // Objective line (Classic only).
            if (objectiveText != null)
                objectiveText.text = objective != null ? objective.Text : "";

            // Boss health bar: shown only while a boss is live, filled by its remaining HP fraction.
            if (bossBarGo != null)
            {
                bool live = boss != null && !boss.IsDone && !boss.IsDying;
                bossBarGo.SetActive(live);
                if (live && bossBarImage != null)
                    bossBarImage.fillAmount = Mathf.Clamp01((float)boss.Hp / Mathf.Max(1, boss.MaxHp));
            }

            // Danger vignette: fade a red frame in as the nearest live snowman (or the boss) closes
            // on the defeat line, so the player feels the threat before it breaches.
            if (dangerVignette != null)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < active.Count; i++)
                {
                    var sm = active[i];
                    if (sm == null || sm.IsDying || sm.IsDone) continue;
                    float z = sm.transform.position.z;
                    if (z < nearest) nearest = z;
                }
                if (boss != null && !boss.IsDying && !boss.IsDone)
                    nearest = Mathf.Min(nearest, boss.transform.position.z);

                float danger = 0f;
                if (nearest != float.MaxValue)
                {
                    // Ramps from 0 at mid-field to 1 at the line.
                    danger = Mathf.InverseLerp(GameConfig.DefeatZ + 16f, GameConfig.DefeatZ + 1f, nearest);
                }
                dangerVignette.enabled = danger > 0.02f;
                if (dangerVignette.enabled)
                {
                    var c = dangerVignette.color; c.a = danger * 0.5f; dangerVignette.color = c;
                }
            }
        }

        // ---- debug hooks used by the editor smoke test -------------------------

        public void DebugFire()
        {
            if (cannon == null) return;
            Vector3 dir = cannon.AimDirection;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            SpawnSnowball(cannon.MuzzleWorldPosition + dir * 0.4f, dir);
        }

        public void DebugForceBreach()
        {
            for (int i = 0; i < active.Count; i++)
            {
                var sm = active[i];
                if (sm != null && !sm.IsDying && !sm.IsDone)
                {
                    var p = sm.transform.position;
                    sm.transform.position = new Vector3(p.x, 0f, GameConfig.DefeatZ - 1f);
                    return;
                }
            }

            // Nothing alive to breach (the aimed shots cleared the field), so drop a fresh
            // snowman right on the defeat line to deterministically exercise the game-over path.
            var fresh = Snowman.Spawn(1f, 0f);
            fresh.transform.position = new Vector3(0f, 0f, GameConfig.DefeatZ - 1f);
            active.Add(fresh);
        }

        /// <summary>Manually SphereCasts from the cannon to the nearest snowman's center to
        /// definitively answer: is the collider detectable by the physics system?</summary>
        public void DebugProbeCollider()
        {
            Snowman target = null;
            float best = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var sm = active[i];
                if (sm == null || sm.IsDying || sm.IsDone) continue;
                float d = sm.transform.position.z;
                if (d < best) { best = d; target = sm; }
            }
            if (target == null) { colliderProbeResult = "no-target(active=" + active.Count + ")"; return; }

            Vector3 center = target.transform.position + new Vector3(0f, 1.0f, 0f);
            Vector3 origin = cannon != null ? cannon.MuzzleWorldPosition : new Vector3(0f, 1.4f, -4f);
            Vector3 dir = (center - origin).normalized;
            float dist = Vector3.Distance(origin, center) + 2f;

            RaycastHit hit;
            bool found = Physics.SphereCast(new Ray(origin, dir), GameConfig.SnowballRadius * 0.85f, out hit, dist);
            colliderProbeResult = (found ? "HIT:" + hit.transform.name : "MISS") + " tz=" + target.transform.position.z.ToString("0.0");
        }

        /// <summary>Fires a snowball aimed straight at the nearest living snowman's body, so the
        /// real SphereCast -> OnHit -> RegisterHit path is exercised deterministically.</summary>
        public void DebugHitNearest()
        {
            Snowman target = null;
            float best = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var sm = active[i];
                if (sm == null || sm.IsDying || sm.IsDone) continue;
                float d = sm.transform.position.z;
                if (d < best) { best = d; target = sm; }
            }
            if (target == null) return;

            Vector3 center = target.transform.position + new Vector3(0f, 1.0f, 0f);
            Vector3 origin = cannon != null ? cannon.MuzzleWorldPosition : new Vector3(0f, 1.4f, -4f);
            Vector3 dir = BallisticDir(origin, center, Snowball.LaunchSpeed, Snowball.Gravity);
            SpawnSnowball(origin, dir);
        }

        /// <summary>Closed-form low-arc ballistic launch direction that lands a projectile of
        /// the given speed on the target under gravity. Falls back to a straight line when no
        /// arc reaches (e.g. the target is out of range).</summary>
        static Vector3 BallisticDir(Vector3 origin, Vector3 target, float speed, float g)
        {
            Vector3 d = target - origin;
            Vector3 h = new Vector3(d.x, 0f, d.z);
            float dist = h.magnitude;
            if (dist < 0.001f) dist = 0.001f;
            h /= dist;
            float dy = d.y;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * dist * dist + 2f * dy * v2);
            float angle = disc < 0f
                ? Mathf.Atan2(dy, dist)
                : Mathf.Atan2(v2 - Mathf.Sqrt(disc), g * dist);
            return (h * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
        }

#if UNITY_EDITOR
        void SmokeTick()
        {
            if (!smokeEnabled) return;
            const string started = "c:/tmp/sc_smoke_started";
            if (!File.Exists(started)) return;

            smokeElapsed += Time.unscaledDeltaTime;
            phaseClock += Time.unscaledDeltaTime;
            if (++smokeFrame % 30 == 0) WriteSmoke("tick");

            switch (smokePhase)
            {
                case 0: // wait until a living snowman exists, then start aiming
                    if (CountLiving() >= 1) { SmokeLog("p0 alive@" + smokeElapsed.ToString("0.0")); smokePhase = 1; phaseClock = 0f; }
                    else if (phaseClock > 8f) { SmokeLog("p0 TIMEOUT no living; active=" + active.Count); smokePhase = 3; phaseClock = 0f; }
                    break;
                case 1: // fire aimed shots at the nearest, expect a real SphereCast hit
                    if (!capturedPlay) { capturedPlay = true; ScreenGrab.Capture("c:/tmp/sc_shot_play.png"); SmokeLog("cap play=grabbed"); }
                    if (smokeElapsed >= nextAim) { nextAim = smokeElapsed + 0.15f; DebugHitNearest(); }
                    if (hits > 0) { SmokeLog("p1 HIT ok hits=" + hits + " score=" + score); smokePhase = 2; phaseClock = 0f; }
                    else if (phaseClock > 4f) { SmokeLog("p1 NO-HIT after 4s fired=" + snowballsFired); smokePhase = 2; phaseClock = 0f; }
                    break;
                case 2: // probe the collider directly to explain any miss
                    DebugProbeCollider();
                    SmokeLog("p2 probe=" + colliderProbeResult);
                    smokePhase = 3; phaseClock = 0f;
                    break;
                case 3: // force a breach, expect the run to end
                    DebugForceBreach();
                    if (state == State.GameOver) { if (!capturedOver) { capturedOver = true; ScreenGrab.Capture("c:/tmp/sc_shot_over.png"); SmokeLog("cap over=grabbed"); } SmokeLog("p3 gameover@" + smokeElapsed.ToString("0.0")); smokePhase = 4; phaseClock = 0f; }
                    else if (phaseClock > 4f) { SmokeLog("p3 TIMEOUT state=" + state); smokePhase = 4; phaseClock = 0f; }
                    break;
                case 4:
                    if (!smokeFinished) { smokeFinished = true; WriteSmoke("final"); }
                    break;
            }
        }

        void SmokeLog(string m) { smokeEvents.Append(m).Append(" ;; "); }

        /// <summary>Renders the game camera to a texture and writes a PNG so the actual frame
        /// (sky, snow plane, cannon, snowmen) can be inspected outside the editor.</summary>
        public void DebugCaptureFrame(string path)
        {
            if (cam == null) { captureNote = "no-camera"; return; }
            int w = 720, h = 420;
            var rt = new RenderTexture(w, h, 24);
            rt.Create();
            var prevTarget = cam.targetTexture;
            var prevDisplay = cam.targetDisplay;
            cam.targetTexture = rt;
            cam.targetDisplay = -1;
            cam.Render();

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            tex.Apply();
            RenderTexture.active = null;

            byte[] png = ImageConversion.EncodeToPNG(tex);
            File.WriteAllBytes(path, png);

            cam.targetTexture = prevTarget;
            cam.targetDisplay = prevDisplay;
            int camCount = Camera.allCameras != null ? Camera.allCameras.Length : 0;
            captureNote = "ok cams=" + camCount + " main=" + (Camera.main != null);
            Object.Destroy(rt);
            Object.Destroy(tex);
        }

        int CountLiving()
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && !active[i].IsDying && !active[i].IsDone) n++;
            return n;
        }

        void WriteSmoke(string tag)
        {
            var sb = new StringBuilder();
            sb.Append(tag).Append('|')
              .Append("state=").Append(state).Append('|')
              .Append("level=").Append(level).Append('|')
              .Append("score=").Append(score).Append('|')
              .Append("hits=").Append(hits).Append('|')
              .Append("active=").Append(CountActive()).Append('|')
              .Append("spawned=").Append(totalSpawned).Append('|')
              .Append("fired=").Append(snowballsFired).Append('|')
              .Append("cam=").Append(cam != null).Append('|')
              .Append("cannon=").Append(cannon != null).Append('|')
              .Append("hud=").Append(levelText != null).Append('|')
              .Append("hudLive=").Append(HudLive()).Append('|')
              .Append("panelActive=").Append(gameOverPanel != null && gameOverPanel.activeInHierarchy).Append('|')
              .Append("panelText=").Append(PanelTextExcerpt()).Append('|')
              .Append("probe=").Append(colliderProbeResult).Append('|')
              .Append("events=").Append(smokeEvents.ToString()).Append('|')
              .Append("elapsed=").Append(smokeElapsed.ToString("0.0"));
            File.WriteAllText("c:/tmp/sc_smoke_heartbeat.txt", sb.ToString());
            if (tag == "final") File.WriteAllText("c:/tmp/sc_smoke_verdict.txt", sb.ToString());
        }

        int CountActive()
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && !active[i].IsDone) n++;
            return n;
        }

        int HudLive()
        {
            int n = 0;
            if (levelText != null && levelText.gameObject.activeInHierarchy) n++;
            if (timeText != null && timeText.gameObject.activeInHierarchy) n++;
            if (scoreText != null && scoreText.gameObject.activeInHierarchy) n++;
            return n;
        }

        string PanelTextExcerpt()
        {
            if (finalScoreText == null) return "null";
            string t = finalScoreText.text ?? "";
            t = t.Replace("\n", " / ");
            if (t.Length > 60) t = t.Substring(0, 60);
            return t;
        }
#endif
    }
}
