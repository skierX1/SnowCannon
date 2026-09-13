using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace SnowCannon
{
    /// <summary>
    /// The title screen. A procedurally drawn backdrop of snowmen marching toward a yellow
    /// cannon sits behind three buttons along the top: Play, Options and High Score. The two
    /// secondary buttons open overlay panels; Play loads the game scene.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        Canvas canvas;
        RectTransform root;

        GameObject optionsPanel;
        GameObject highScorePanel;
        GameObject soundPanel;

        Text soundLabel;
        Text musicLabel;
        Text qualityLabel;
        Text vibrationLabel;
        Text highScoreValue;
        Text volumeValue;
        Slider volumeSlider;
        bool refreshingVolume;
        Text snowmanSoundValue;
        Text cannonSoundValue;

        void Awake()
        {
            Settings.LoadAndApply();
            if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings();

            EnsureMenuCamera();
            Ui.EnsureEventSystem();
            canvas = Ui.CreateCanvas("MenuCanvas", 0);
            root = Ui.NewRect("root", canvas.transform);
            Ui.Stretch(root);
            Ui.ApplySafeArea(root, canvas);

            BuildBackdrop();
            BuildTitle();
            BuildButtons();
            BuildOptionsPanel();
            BuildSoundPanel();
            BuildHighScorePanel();

            ShowMain();
        }

        void Update()
        {
            // ESC leaves the game from the title screen too, mirroring the play scene.
            if (UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                GameFlow.Quit();
                return;
            }

#if UNITY_EDITOR
            SmokeUpdate();
#endif
        }

#if UNITY_EDITOR
        int menuSmokeStep;
        bool warmedUp;
        float menuCaptureAt = 2.6f;

        // Editor-only smoke hook: when the marker file is present, grab the composited menu,
        // then open + grab the Sound submenu (proving the new UI renders), then hand off to play.
        void SmokeUpdate()
        {
            if (!System.IO.File.Exists("c:/tmp/sc_smoke_enabled")) return;
            if (!warmedUp) { warmedUp = true; ScreenGrab.WarmUp(); }
            menuCaptureAt -= Time.unscaledDeltaTime;
            if (menuCaptureAt > 0f) return;

            if (menuSmokeStep == 0)
            {
                menuSmokeStep = 1;
                ScreenGrab.Capture("c:/tmp/sc_shot_menu.png");
                menuCaptureAt = 0.6f;
            }
            else if (menuSmokeStep == 1)
            {
                menuSmokeStep = 2;
                OnOpenSound();
                menuCaptureAt = 0.6f;
            }
            else if (menuSmokeStep == 2)
            {
                menuSmokeStep = 3;
                ScreenGrab.Capture("c:/tmp/sc_shot_sound.png");
                menuCaptureAt = 0.6f;
            }
            else if (menuSmokeStep == 3)
            {
                menuSmokeStep = 4;
                OnHighScore();
                menuCaptureAt = 0.6f;
            }
            else if (menuSmokeStep == 4)
            {
                menuSmokeStep = 5;
                ScreenGrab.Capture("c:/tmp/sc_shot_highscore.png");
                menuCaptureAt = 0.6f;
            }
            else if (menuSmokeStep == 5)
            {
                menuSmokeStep = 6;
                SceneManager.LoadScene(GameSession.PlayScene);
            }
        }
#endif

        // ---- backdrop -----------------------------------------------------------

        /// <summary>The menu is pure overlay UI, but the editor Game view only composites a
        /// Screen Space Overlay canvas when at least one camera renders to the display. The
        /// scene is built empty, so we add a do-nothing camera (culls nothing, clears to sky)
        /// purely to satisfy that requirement and kill "No cameras rendering".</summary>
        void EnsureMenuCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Menu Camera");
            go.tag = "MainCamera";
            var c = go.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = GameConfig.SkyBlue;
            c.nearClipPlane = 0.1f;
            c.farClipPlane = 100f;
            c.cullingMask = 0;
            c.cameraType = CameraType.Game;
            c.targetDisplay = 0;
            c.enabled = true;
        }

        void BuildBackdrop()
        {
            // Sky.
            Ui.AddImage(root, "sky", Ui.White, GameConfig.SkyBlue, false);
            Ui.Stretch(Ui.Rt(Find("sky")));

            // Snow ground band along the bottom.
            var ground = Ui.NewRect("ground", root);
            Ui.Place(ground, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 110f), new Vector2(1400f, 300f));
            var gi = ground.gameObject.AddComponent<Image>();
            gi.sprite = Ui.White;
            gi.color = GameConfig.GroundSnow;
            gi.raycastTarget = false;

            // A row of snowmen marching in from the left.
            float x = -520f;
            for (int i = 0; i < 5; i++)
            {
                AddSnowman(new Vector2(x, 150f), Random.Range(0.8f, 1.15f));
                x += Random.Range(150f, 210f);
            }

            // The cannon they march toward, on the right.
            AddCannon(new Vector2(520f, 150f), 1.1f);
        }

        void AddSnowman(Vector2 anchoredPos, float scale)
        {
            var holder = Ui.NewRect("snowman", root);
            Ui.Place(holder, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), anchoredPos,
                     Vector2.zero);

            Color snow = new Color(0.97f, 0.98f, 1f, 0.96f);
            Circle(holder, "b", 34f * scale, new Vector2(0f, 34f * scale), snow);
            Circle(holder, "m", 26f * scale, new Vector2(0f, 74f * scale), snow);
            Circle(holder, "h", 18f * scale, new Vector2(0f, 108f * scale), snow);
            // Carrot nose pointing toward the cannon (+x).
            Circle(holder, "nose", 6f * scale, new Vector2(18f * scale, 108f * scale),
                   GameConfig.CarrotOrange);
            // Coal eyes.
            Circle(holder, "el", 3f * scale, new Vector2(-6f * scale, 114f * scale), GameConfig.CoalBlack);
            Circle(holder, "er", 3f * scale, new Vector2(6f * scale, 114f * scale), GameConfig.CoalBlack);
            // A little pot on the head.
            var pot = Ui.AddImage(holder, "pot", Ui.White, GameConfig.PotMetal, false);
            Ui.Place(Ui.Rt(pot.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 132f * scale), new Vector2(30f * scale, 16f * scale));
        }

        void AddCannon(Vector2 anchoredPos, float scale)
        {
            var holder = Ui.NewRect("cannon", root);
            Ui.Place(holder, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), anchoredPos, Vector2.zero);

            var body = Ui.AddImage(holder, "body", Ui.Circle, GameConfig.CannonYellow, false);
            Ui.Place(Ui.Rt(body.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 40f * scale), new Vector2(150f * scale, 90f * scale));

            var barrel = Ui.AddImage(holder, "barrel", Ui.White, GameConfig.CannonYellowDark, false);
            Ui.Place(Ui.Rt(barrel.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(-70f * scale, 90f * scale), new Vector2(120f * scale, 34f * scale));
            Ui.Rt(barrel.gameObject).localEulerAngles = new Vector3(0f, 0f, 28f);

            var wheel = Ui.AddImage(holder, "wheel", Ui.Circle, GameConfig.CannonSteel, false);
            Ui.Place(Ui.Rt(wheel.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 18f * scale), new Vector2(60f * scale, 60f * scale));
        }

        void Circle(RectTransform parent, string name, float radius, Vector2 pos, Color color)
        {
            var img = Ui.AddImage(parent, name, Ui.Circle, color, false);
            Ui.Place(Ui.Rt(img.gameObject), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     pos, new Vector2(radius * 2f, radius * 2f));
        }

        GameObject Find(string name)
        {
            foreach (Transform t in root)
            {
                if (t.name == name) return t.gameObject;
            }
            return root.gameObject;
        }

        // ---- title + buttons ----------------------------------------------------

        void BuildTitle()
        {
            var title = Ui.AddText(root, "title", "SNOW  CANNON", 92, GameConfig.CannonYellow,
                                  TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(title.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -70f), new Vector2(900f, 130f));

            var sub = Ui.AddText(root, "sub", "blast the snowmen before they reach you", 30,
                                new Color(0.1f, 0.2f, 0.3f, 1f), TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(sub.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -150f), new Vector2(900f, 50f));
        }

        void BuildButtons()
        {
            // The three primary buttons sit along the top, under the title.
            Ui.AddButton(root, "play", "PLAY", new Vector2(260f, 96f), 46,
                         GameConfig.CannonYellow, OnPlay);
            Ui.Place(Rt("play"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(-300f, -250f), new Vector2(260f, 96f));

            Ui.AddButton(root, "options", "OPTIONS", new Vector2(260f, 96f), 40,
                         GameConfig.CannonSteel, OnOptions);
            Ui.Place(Rt("options"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -250f), new Vector2(260f, 96f));

            Ui.AddButton(root, "highscore", "HIGH SCORE", new Vector2(260f, 96f), 40,
                         GameConfig.CannonSteel, OnHighScore);
            Ui.Place(Rt("highscore"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(300f, -250f), new Vector2(260f, 96f));
        }

        RectTransform Rt(string name)
        {
            foreach (Transform t in root)
            {
                if (t.name == name) return (RectTransform)t;
            }
            return root;
        }

        // ---- options panel ------------------------------------------------------

        void BuildOptionsPanel()
        {
            optionsPanel = Ui.NewRect("optionsPanel", root).gameObject;
            Ui.Stretch(Ui.Rt(optionsPanel));
            Ui.AddPanel(Ui.Rt(optionsPanel), "dim", new Color(0f, 0f, 0.05f, 0.6f), false);

            var card = Ui.NewRect("card", Ui.Rt(optionsPanel));
            Ui.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(620f, 520f));
            Ui.AddPanel(card, "bg", new Color(0.06f, 0.13f, 0.21f, 0.96f), false);

            var head = Ui.AddText(card, "head", "OPTIONS", 52, GameConfig.CannonYellow, TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(head.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -30f), new Vector2(560f, 80f));

            soundLabel = AddToggleRow(card, "sound", "SOUND", 0, OnOpenSound);
            musicLabel = AddToggleRow(card, "music", "MUSIC", 1, OnToggleMusic);
            qualityLabel = AddToggleRow(card, "quality", "QUALITY", 2, OnCycleQuality);
            vibrationLabel = AddToggleRow(card, "vibration", "VIBRATION", 3, OnToggleVibration);

            Ui.AddButton(card, "back", "BACK", new Vector2(220f, 74f), 34,
                        GameConfig.CannonSteel, ShowMain);
            Ui.Place(RtChild(card, "back"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 55f), new Vector2(220f, 74f));
        }

        Text AddToggleRow(RectTransform card, string name, string title, int row, Action onClick)
        {
            float top = -150f - row * 78f;

            // The whole row is one wide button, so tapping the label OR the value both fire the
            // action. Previously only a small box on the far right was clickable and the ON/OFF
            // read-out was never positioned, so the row looked and felt inert.
            var rowBtn = Ui.AddButton(card, name + "_b", "", new Vector2(540f, 64f), 32,
                                      new Color(0.15f, 0.25f, 0.35f, 1f), onClick);
            Ui.Place(Ui.Rt(rowBtn.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, top), new Vector2(540f, 64f));

            var label = Ui.AddText(Ui.Rt(rowBtn.gameObject), name + "_t", title, 34,
                                  Color.white, TextAnchor.MiddleLeft);
            Ui.Place(Ui.Rt(label.gameObject), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                     new Vector2(24f, 0f), new Vector2(340f, 60f));

            var value = Ui.AddText(Ui.Rt(rowBtn.gameObject), name + "_v", "", 34,
                                  GameConfig.CannonYellow, TextAnchor.MiddleRight);
            Ui.Place(Ui.Rt(value.gameObject), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                     new Vector2(-24f, 0f), new Vector2(150f, 60f));
            return value;
        }

        RectTransform RtChild(RectTransform parent, string name)
        {
            foreach (Transform t in parent)
            {
                if (t.name == name) return (RectTransform)t;
            }
            return parent;
        }

        // ---- sound submenu ------------------------------------------------------

        void BuildSoundPanel()
        {
            soundPanel = Ui.NewRect("soundPanel", root).gameObject;
            Ui.Stretch(Ui.Rt(soundPanel));
            Ui.AddPanel(Ui.Rt(soundPanel), "dim", new Color(0f, 0f, 0.05f, 0.6f), false);

            var card = Ui.NewRect("card", Ui.Rt(soundPanel));
            Ui.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(620f, 520f));
            Ui.AddPanel(card, "bg", new Color(0.06f, 0.13f, 0.21f, 0.96f), false);

            var head = Ui.AddText(card, "head", "SOUND", 52, GameConfig.CannonYellow, TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(head.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -30f), new Vector2(560f, 80f));

            // VOLUME is a real drag slider with a live percentage read-out beside it.
            var volLabel = Ui.AddText(card, "volume_t", "VOLUME", 34, Color.white, TextAnchor.MiddleLeft);
            Ui.Place(Ui.Rt(volLabel.gameObject), new Vector2(0f, 1f), new Vector2(0f, 1f),
                     new Vector2(40f, -150f), new Vector2(170f, 60f));

            volumeSlider = Ui.AddSlider(card, "volume_s", new Vector2(250f, 26f),
                new Color(0.15f, 0.25f, 0.35f, 1f), GameConfig.CannonYellow, Color.white,
                OnVolumeChanged);
            Ui.Place(Ui.Rt(volumeSlider.gameObject), new Vector2(1f, 1f), new Vector2(1f, 1f),
                     new Vector2(-190f, -150f), new Vector2(250f, 26f));

            volumeValue = Ui.AddText(card, "volume_v", "", 34, GameConfig.CannonYellow, TextAnchor.MiddleRight);
            Ui.Place(Ui.Rt(volumeValue.gameObject), new Vector2(1f, 1f), new Vector2(1f, 1f),
                     new Vector2(-40f, -150f), new Vector2(120f, 60f));

            snowmanSoundValue = AddToggleRow(card, "snowman", "SNOWMAN SOUND", 1, OnToggleSnowmanSound);
            cannonSoundValue = AddToggleRow(card, "cannon", "CANNON SOUND", 2, OnToggleCannonSound);

            Ui.AddButton(card, "back", "BACK", new Vector2(220f, 74f), 34,
                        GameConfig.CannonSteel, OnSoundBack);
            Ui.Place(RtChild(card, "back"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 55f), new Vector2(220f, 74f));
        }

        // ---- high score panel ---------------------------------------------------

        void BuildHighScorePanel()
        {
            highScorePanel = Ui.NewRect("highScorePanel", root).gameObject;
            Ui.Stretch(Ui.Rt(highScorePanel));
            Ui.AddPanel(Ui.Rt(highScorePanel), "dim", new Color(0f, 0f, 0.05f, 0.6f), false);

            var card = Ui.NewRect("card", Ui.Rt(highScorePanel));
            Ui.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(560f, 430f));
            Ui.AddPanel(card, "bg", new Color(0.06f, 0.13f, 0.21f, 0.96f), false);

            var head = Ui.AddText(card, "head", "HIGH SCORE", 52, GameConfig.CannonYellow, TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(head.gameObject), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                     new Vector2(0f, -30f), new Vector2(500f, 80f));

            highScoreValue = Ui.AddText(card, "value", "0", 80, Color.white, TextAnchor.MiddleCenter);
            Ui.Place(Ui.Rt(highScoreValue.gameObject), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     new Vector2(0f, 80f), new Vector2(500f, 120f));

            Ui.AddButton(card, "reset", "RESET HIGH SCORE", new Vector2(420f, 70f), 30,
                        new Color(0.8f, 0.25f, 0.2f, 1f), OnResetHighScore);
            Ui.Place(RtChild(card, "reset"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 150f), new Vector2(420f, 70f));

            Ui.AddButton(card, "back", "BACK", new Vector2(220f, 74f), 34,
                        GameConfig.CannonSteel, ShowMain);
            Ui.Place(RtChild(card, "back"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                     new Vector2(0f, 55f), new Vector2(220f, 74f));
        }

        // ---- navigation ---------------------------------------------------------

        void ShowMain()
        {
            optionsPanel.SetActive(false);
            highScorePanel.SetActive(false);
            soundPanel.SetActive(false);
        }

        void OnPlay() { SceneManager.LoadScene(GameSession.PlayScene); }

        void OnOptions()
        {
            RefreshOptionLabels();
            optionsPanel.SetActive(true);
            highScorePanel.SetActive(false);
        }

        void OnHighScore()
        {
            if (highScoreValue != null) highScoreValue.text = Settings.HighScore.ToString();
            highScorePanel.SetActive(true);
            optionsPanel.SetActive(false);
        }

        // ---- option callbacks ---------------------------------------------------

        void OnToggleSound() { Settings.Sound = !Settings.Sound; if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings(); RefreshOptionLabels(); }

        void OnOpenSound()
        {
            RefreshSoundLabels();
            soundPanel.SetActive(true);
            optionsPanel.SetActive(false);
            highScorePanel.SetActive(false);
        }

        void OnSoundBack()
        {
            soundPanel.SetActive(false);
            optionsPanel.SetActive(true);
            highScorePanel.SetActive(false);
        }

        void OnVolumeChanged(float v)
        {
            // Ignore the echo fired while RefreshSoundLabels() re-seats the slider position.
            if (refreshingVolume) return;
            Settings.Volume = v;
            if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings();
            RefreshSoundLabels();
        }

        void OnToggleSnowmanSound()
        {
            Settings.SnowmanSound = !Settings.SnowmanSound;
            if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings();
            RefreshSoundLabels();
        }

        void OnToggleCannonSound()
        {
            Settings.CannonSound = !Settings.CannonSound;
            if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings();
            RefreshSoundLabels();
        }

        void RefreshSoundLabels()
        {
            if (volumeValue != null) volumeValue.text = Mathf.RoundToInt(Settings.Volume * 100f) + "%";
            if (volumeSlider != null)
            {
                refreshingVolume = true;
                volumeSlider.value = Settings.Volume;
                refreshingVolume = false;
            }
            if (snowmanSoundValue != null) snowmanSoundValue.text = Settings.SnowmanSound ? "ON" : "OFF";
            if (cannonSoundValue != null) cannonSoundValue.text = Settings.CannonSound ? "ON" : "OFF";
        }
        void OnToggleMusic() { Settings.Music = !Settings.Music; if (AudioDirector.Instance != null) AudioDirector.Instance.ApplySettings(); RefreshOptionLabels(); }
        void OnToggleVibration() { Settings.Vibration = !Settings.Vibration; RefreshOptionLabels(); }
        void OnCycleQuality() { Settings.Quality = (Settings.Quality + 1) % Settings.QualityLevelCount; RefreshOptionLabels(); }
        void OnResetHighScore() { Settings.ResetHighScore(); if (highScoreValue != null) highScoreValue.text = "0"; }

        void RefreshOptionLabels()
        {
            if (soundLabel != null) soundLabel.text = Settings.Sound ? "ON" : "OFF";
            if (musicLabel != null) musicLabel.text = Settings.Music ? "ON" : "OFF";
            if (vibrationLabel != null) vibrationLabel.text = Settings.Vibration ? "ON" : "OFF";
            if (qualityLabel != null)
            {
                string[] names = { "LOW", "MEDIUM", "HIGH" };
                int i = Mathf.Clamp(Settings.Quality, 0, names.Length - 1);
                qualityLabel.text = names[i];
            }
        }
    }
}
