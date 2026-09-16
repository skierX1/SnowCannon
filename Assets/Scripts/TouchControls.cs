using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SnowCannon
{
    /// <summary>
    /// On-screen controls for touch devices: a fixed analogue stick on the left that drives the
    /// cannon and a hold-to-fire pad on the right. They write straight into the shared
    /// PlayerControls, so keyboard, mouse and touch all coexist with no mode switch.
    ///
    /// The controls are positioned in raw screen-pixel space on a constant-pixel-size canvas and
    /// driven directly from the Input System <c>Touchscreen</c> device (the same device the
    /// gameplay input already proves works on the phone), rather than relying on uGUI pointer
    /// events, which are unreliable for continuous drag on some Android builds. A single finger on
    /// the stick and another on the fire pad are tracked independently by touch id, so you can steer
    /// and shoot at the same time. The whole layer is skipped on machines with no touchscreen.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        PlayerControls controls;
        Canvas canvas;

        // Screen-space geometry, filled in Start once the real screen size is known.
        Vector2 stickCenter, fireCenter;
        float stickRadius, fireRadius, travel;

        RectTransform knob;

        public static TouchControls Attach(SnowCannonGame owner)
        {
            var go = new GameObject("TouchControls");
            var tc = go.AddComponent<TouchControls>();
            tc.controls = owner.Controls;
            return tc;
        }

        static bool HasTouchDevice()
        {
            if (Application.platform == RuntimePlatform.Android
                || Application.platform == RuntimePlatform.IPhonePlayer) return true;
            return Touchscreen.current != null;
        }

        void Start()
        {
            // Desktop players use keyboard + mouse; do not clutter them with a virtual pad.
            if (!HasTouchDevice())
            {
                enabled = false;
                return;
            }

            Ui.EnsureEventSystem();
            canvas = Ui.CreateCanvas("TouchCanvas", 20);

            // Force a 1:1 pixel canvas so the screen-pixel coordinates used below map exactly to
            // the screen (the default ScaleWithScreenSize would scale them and push them off-screen).
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
            }

            var root = Ui.NewRect("root", canvas.transform);
            Ui.Stretch(root);

            float w = Mathf.Max(1, Screen.width);
            float h = Mathf.Max(1, Screen.height);
            float bottomY = h * 0.16f;
            float minDim = Mathf.Min(w, h);

            stickRadius = minDim * 0.17f;
            fireRadius = minDim * 0.15f;
            travel = stickRadius * 0.9f;
            // The stick sits a third of the way across the screen and the fire button two thirds,
            // so both are clear of the cannon in the middle and reachable by a thumb at the bottom.
            stickCenter = new Vector2(w / 3f, bottomY);
            fireCenter = new Vector2(w * 2f / 3f, bottomY);

            BuildStick(root);
            BuildFire(root);

            var driver = gameObject.AddComponent<TouchDriver>();
            driver.controls = controls;
            driver.owner = this;
            driver.travel = travel;
        }

        void BuildStick(RectTransform root)
        {
            float d = stickRadius * 2f;
            var zone = Ui.NewRect("stick_zone", root);
            // Bottom-left origin anchor: anchoredPosition is the absolute screen-pixel centre.
            Ui.Place(zone, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), stickCenter, new Vector2(d, d));

            var bg = zone.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Circle;
            bg.color = new Color(1f, 1f, 1f, 0.16f);
            bg.raycastTarget = false;
            bg.preserveAspect = false;

            var ring = Ui.NewRect("ring", zone);
            Ui.Place(ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(d * 0.8f, d * 0.8f));
            var ringImg = ring.gameObject.AddComponent<Image>();
            ringImg.sprite = Ui.Circle;
            ringImg.color = new Color(0.1f, 0.2f, 0.35f, 0.4f);
            ringImg.raycastTarget = false;

            var k = Ui.NewRect("knob", zone);
            Ui.Place(k, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                     Vector2.zero, new Vector2(d * 0.4f, d * 0.4f));
            var knobImg = k.gameObject.AddComponent<Image>();
            knobImg.sprite = Ui.Circle;
            knobImg.color = new Color(0.98f, 0.78f, 0.06f, 0.9f);
            knobImg.raycastTarget = false;
            knob = k;
        }

        void BuildFire(RectTransform root)
        {
            float d = fireRadius * 2f;
            var pad = Ui.NewRect("fire_pad", root);
            Ui.Place(pad, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), fireCenter, new Vector2(d, d));

            var bg = pad.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Circle;
            bg.color = new Color(0.98f, 0.78f, 0.06f, 0.55f);
            bg.raycastTarget = false;
            bg.preserveAspect = false;

            var label = Ui.AddText(pad, "label", "FIRE", 46, Color.white, TextAnchor.MiddleCenter);
            Ui.Stretch(Ui.Rt(label.gameObject));
        }

        /// <summary>Which control a screen-pixel point falls in (or None if neither).</summary>
        public TouchControlsZone ZoneAt(Vector2 screenPos)
        {
            float sr = stickRadius * 1.35f;
            float fr = fireRadius * 1.35f;
            if ((screenPos - stickCenter).sqrMagnitude <= sr * sr) return TouchControlsZone.Stick;
            if ((screenPos - fireCenter).sqrMagnitude <= fr * fr) return TouchControlsZone.Fire;
            return TouchControlsZone.None;
        }

        /// <summary>Move the stick knob to a normalised offset (-1..1 on each axis).</summary>
        public void SetKnob(Vector2 offset)
        {
            if (knob != null) knob.anchoredPosition = offset * travel;
        }

        public enum TouchControlsZone { None, Stick, Fire }
    }

    /// <summary>
    /// Reads the Input System <c>Touchscreen</c> device directly and maps each live finger to a
    /// control zone by touch id, so the stick and the fire pad respond independently and can be used
    /// together. This bypasses the uGUI pointer pipeline, which is unreliable for continuous drag on
    /// some Android builds.
    /// </summary>
    public sealed class TouchDriver : MonoBehaviour
    {
        public PlayerControls controls;
        public TouchControls owner;
        public float travel = 120f;

        sealed class TouchState
        {
            public TouchControls.TouchControlsZone zone;
            public Vector2 downPos;
        }

        readonly Dictionary<int, TouchState> active = new Dictionary<int, TouchState>();
        readonly List<int> live = new List<int>();

        void Update()
        {
            if (controls == null || owner == null) return;

            var ts = Touchscreen.current;
            if (ts == null || controls.pointerBlocked)
            {
                ReleaseAll();
                return;
            }

            live.Clear();
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t == null || !t.isInProgress) continue;
                int id = t.touchId.ReadValue();
                Vector2 pos = t.position.ReadValue();
                live.Add(id);

                if (!active.TryGetValue(id, out var st))
                {
                    var zone = owner.ZoneAt(pos);
                    if (zone == TouchControls.TouchControlsZone.None) continue;
                    st = new TouchState { zone = zone, downPos = pos };
                    active[id] = st;
                    if (zone == TouchControls.TouchControlsZone.Fire) controls.touchFireHeld = true;
                }

                if (st.zone == TouchControls.TouchControlsZone.Stick)
                {
                    Vector2 d = (pos - st.downPos) / travel;
                    Vector2 clamped = Vector2.ClampMagnitude(d, 1f);
                    controls.touchMove = clamped;
                    owner.SetKnob(clamped);
                }
            }

            // Release any tracked touch that is no longer live.
            if (active.Count > 0)
            {
                keysScratch.Clear();
                foreach (var kv in active) keysScratch.Add(kv.Key);
                for (int i = 0; i < keysScratch.Count; i++)
                {
                    int id = keysScratch[i];
                    if (live.Contains(id)) continue;
                    var st = active[id];
                    if (st.zone == TouchControls.TouchControlsZone.Stick)
                    {
                        controls.touchMove = Vector2.zero;
                        owner.SetKnob(Vector2.zero);
                    }
                    else if (st.zone == TouchControls.TouchControlsZone.Fire)
                    {
                        controls.touchFireHeld = false;
                    }
                    active.Remove(id);
                }
            }
        }

        readonly List<int> keysScratch = new List<int>();

        void ReleaseAll()
        {
            if (active.Count == 0) return;
            controls.touchMove = Vector2.zero;
            controls.touchFireHeld = false;
            owner.SetKnob(Vector2.zero);
            active.Clear();
        }
    }
}
