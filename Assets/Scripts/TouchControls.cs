using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace SnowCannon
{
    /// <summary>
    /// On-screen controls for touch devices: a floating analogue stick on the left that
    /// drives the cannon and a hold-to-fire pad on the right. They write straight into the
    /// shared PlayerControls, so keyboard, mouse and touch all coexist with no mode switch.
    /// The whole layer is hidden automatically on machines without a touchscreen.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        PlayerControls controls;
        Canvas canvas;

        public static TouchControls Attach(SnowCannonGame owner)
        {
            var go = new GameObject("TouchControls");
            var tc = go.AddComponent<TouchControls>();
            tc.controls = owner.Controls;
            return tc;
        }

        void Start()
        {
            // Desktop players use keyboard + mouse; do not clutter them with a virtual pad.
            bool touchDevice = Touchscreen.current != null;
            if (!touchDevice)
            {
                enabled = false;
                return;
            }

            Ui.EnsureEventSystem();
            canvas = Ui.CreateCanvas("TouchCanvas", 20);
            var root = Ui.NewRect("root", canvas.transform);
            Ui.Stretch(root);
            Ui.ApplySafeArea(root, canvas);

            BuildStick(root);
            BuildFire(root);
        }

        void BuildStick(RectTransform root)
        {
            // The whole left half is the drag surface, so the stick appears wherever a thumb lands.
            var zone = Ui.NewRect("stick_zone", root);
            Ui.Place(zone, new Vector2(0f, 0f), new Vector2(0f, 0f),
                     new Vector2(0f, 0f), new Vector2(Screen.width * 0.46f, Screen.height));
            zone.anchorMin = Vector2.zero;
            zone.anchorMax = new Vector2(0.46f, 1f);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;

            var bg = zone.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Circle;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = true;
            bg.preserveAspect = false;

            var stick = zone.gameObject.AddComponent<StickZone>();
            stick.controls = controls;
        }

        void BuildFire(RectTransform root)
        {
            var pad = Ui.NewRect("fire_pad", root);
            Ui.Place(pad, new Vector2(1f, 0f), new Vector2(1f, 0f),
                      new Vector2(-150f, 150f), new Vector2(210f, 210f));

            var bg = pad.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Circle;
            bg.color = new Color(0.98f, 0.78f, 0.06f, 0.55f);
            bg.raycastTarget = true;
            bg.preserveAspect = false;

            var label = Ui.AddText(pad, "label", "FIRE", 46, Color.white, TextAnchor.MiddleCenter);
            Ui.Stretch(Ui.Rt(label.gameObject));

            var fire = pad.gameObject.AddComponent<FireZone>();
            fire.controls = controls;
        }
    }

    /// <summary>A floating joystick: press anywhere in the zone, drag to steer.</summary>
    public sealed class StickZone : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public PlayerControls controls;
        Vector2 start;
        bool active;

        // Pixels of thumb travel that map to a full-magnitude move.
        const float Travel = 130f;

        public void OnPointerDown(PointerEventData e)
        {
            if (controls == null || controls.pointerBlocked) return;
            active = true;
            start = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!active || controls == null || controls.pointerBlocked) return;
            Vector2 d = (e.position - start) / Travel;
            controls.touchMove = Vector2.ClampMagnitude(d, 1f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            active = false;
            if (controls != null) controls.touchMove = Vector2.zero;
        }
    }

    /// <summary>A hold-to-fire pad.</summary>
    public sealed class FireZone : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public PlayerControls controls;

        public void OnPointerDown(PointerEventData e)
        {
            if (controls != null && !controls.pointerBlocked) controls.touchFireHeld = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (controls != null) controls.touchFireHeld = false;
        }
    }
}
