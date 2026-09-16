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
        readonly List<Snowman> active = new List<Snowman>();

        int level = 1;
        int score;
        int hits;
        float levelTimer;
        float spawnTimer;

        Text levelText;
        Text timeText;
        Text scoreText;
        Text waterText;

        GameObject gameOverPanel;
        Text finalScoreText;
        float gameOverCooldown;

        Cannon cannon;
        Lake lake;
        int totalSpawned;
        int snowballsFired;

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

        void Awake()
        {
            Settings.LoadAndApply();
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

            // The 3D water pond lives in the world (bottom-left of the field) and feeds the cannon
            // through a yellow hose that runs along the bottom of the screen.
            lake = Lake.Create(transform, cam);
            if (lake != null && cannon != null) lake.ConnectHose(cannon.transform.position);

            // Give the solid props a soft ground shadow; keep the water and text decals out of it.
            if (cannon != null) Mat.SetShadows(cannon.gameObject, true, false);
            if (lake != null) Mat.SetShadows(lake.gameObject, false, false);

            GameSession.BeginRun();
            level = 1;
            score = 0;
            hits = 0;
            levelTimer = GameConfig.LevelDuration(level);
            spawnTimer = 0.6f; // a short grace beat before the first snowman
        }

        void OnDestroy()
        {
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
            if (state == State.Playing)
            {
                UpdatePlaying();
            }
            else
            {
                UpdateGameOver();
            }
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

            // Level timer, then auto-advance to a longer, faster level.
            levelTimer -= Time.deltaTime;
            if (levelTimer <= 0f)
            {
                level++;
                levelTimer = GameConfig.LevelDuration(level);
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
                        GameOver(false);
                        return;
                    }
                }
            }

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
            var sm = Snowman.Spawn(size, speed, -1, Mathf.Min(30f, 16f + (level - 1) * 4f));
            sm.transform.position = new Vector3(x, 0f, z);
            active.Add(sm);
            totalSpawned++;

            // Every snowman that arrives pours water back into the lake, so letting the wave
            // build reloads the cannon -- but a snowman that reaches the bottom ends the run.
            if (lake != null) lake.OnSnowmanSpawned();
#if UNITY_EDITOR
            if (smokeEnabled) SmokeLog("spawn@" + smokeElapsed.ToString("0.0") + " x=" + x.ToString("0.0"));
#endif
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

            if (finalScoreText != null)
            {
                finalScoreText.text =
                    "FINAL SCORE   " + score + "\n" +
                    "LEVEL REACHED   " + level + "\n\n" +
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
            score += points;
            hits++;
#if UNITY_EDITOR
            if (smokeEnabled) SmokeLog("REGISTERHIT pts=" + points + " hits=" + hits + " score=" + score);
#endif
            if (AudioDirector.Instance != null)
            {
                var tr = target as Component;
                if (tr != null) AudioDirector.Instance.PlayScream(tr.transform.position);
                AudioDirector.Instance.Vibrate();
            }
        }

        // ---- HUD ----------------------------------------------------------------

        void BuildHud()
        {
            Ui.EnsureEventSystem();
            var canvas = Ui.CreateCanvas("HUD", 10);
            var root = Ui.NewRect("root", canvas.transform);
            Ui.Stretch(root);

            // Top-right scoreboard.
            var board = Ui.NewRect("board", root);
            Ui.Place(board, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-24f, -24f), new Vector2(320f, 250f));
            Ui.AddPanel(board, "bg", new Color(0f, 0f, 0f, 0.32f), false);

            levelText = AddLine(board, "level", 34, new Vector2(-16f, -34f));
            timeText = AddLine(board, "time", 46, new Vector2(-16f, -84f));
            scoreText = AddLine(board, "score", 34, new Vector2(-16f, -140f));
            waterText = AddLine(board, "water", 30, new Vector2(-16f, -188f));

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
