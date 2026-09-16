using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SnowCannon
{
    /// <summary>
    /// The single input surface of the game. Keyboard, mouse and touch all feed the very
    /// same actions, so no mode switch is ever needed. The action map is assembled in code
    /// which keeps the project free of .inputactions assets and generated wrappers.
    /// </summary>
    public sealed class PlayerControls : IDisposable
    {
        public readonly InputActionAsset asset;
        public readonly InputActionMap map;

        public readonly InputAction moveUp;
        public readonly InputAction moveDown;
        public readonly InputAction moveLeft;
        public readonly InputAction moveRight;
        public readonly InputAction lookDelta;
        public readonly InputAction fire;
        public readonly InputAction escape;

        /// <summary>Set by the on-screen controls; merged with the keyboard/mouse vector.</summary>
        public Vector2 touchMove;
        public bool touchFireHeld;

        /// <summary>True while a modal panel owns the pointer, so clicks must not fire.</summary>
        public bool pointerBlocked;

        // Cached once: the desktop mouse needs a higher gain to feel responsive on a big screen.
        static readonly bool s_desktop = Application.platform != RuntimePlatform.Android
                                       && Application.platform != RuntimePlatform.IPhonePlayer;

        // Per-input gains so each control feels right on its own: keys 1.5x snappier than before,
        // the desktop mouse reaches full speed at half the physical travel, and the phone joystick
        // sweeps the barrel much faster than a thumb drag used to. The final clamp is raised so
        // these gains are not clipped back down to the old single-source speed.
        const float KeyGain = 1.5f;
        const float MouseDesktopGain = 6f;
        const float TouchGain = 2.2f;
        const float MaxMove = 1.8f;

        public PlayerControls()
        {
            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "SnowCannonInputs";

            map = asset.AddActionMap("Player");

            moveUp = map.AddAction("MoveUp", InputActionType.Value,
                binding: "<Keyboard>/wKey", expectedControlLayout: "Button");
            moveUp.AddBinding("<Keyboard>/upArrow");

            moveDown = map.AddAction("MoveDown", InputActionType.Value,
                binding: "<Keyboard>/sKey", expectedControlLayout: "Button");
            moveDown.AddBinding("<Keyboard>/downArrow");

            moveLeft = map.AddAction("MoveLeft", InputActionType.Value,
                binding: "<Keyboard>/aKey", expectedControlLayout: "Button");
            moveLeft.AddBinding("<Keyboard>/leftArrow");

            moveRight = map.AddAction("MoveRight", InputActionType.Value,
                binding: "<Keyboard>/dKey", expectedControlLayout: "Button");
            moveRight.AddBinding("<Keyboard>/rightArrow");

            lookDelta = map.AddAction("LookDelta", InputActionType.Value,
                binding: "<Mouse>/delta", expectedControlLayout: "Vector2");

            fire = map.AddAction("Fire", InputActionType.Button,
                binding: "<Keyboard>/space");
            fire.AddBinding("<Mouse>/leftButton");

            // ESC stops the run from anywhere in the play scene.
            escape = map.AddAction("Escape", InputActionType.Button,
                binding: "<Keyboard>/escape");

            asset.Enable();
        }

        /// <summary>
        /// Combines WASD, the mouse delta and the virtual stick into one normalised move
        /// vector where +y means "up the screen" (away from the camera).
        /// </summary>
        public Vector2 ReadMove()
        {
            // Movement is read the SAME way fire is (see IsFireHeld): via the action map's
            // IsPressed(), which is the mechanism proven to work for the fire button. The
            // Value-style ReadValue<float>() returns 0 for these button-bound actions, which
            // is why movement was dead while fire worked. Raw device reads are OR-ed in as a
            // second source so a stalled action map can never fully mute the keyboard.
            float dx = 0f, dy = 0f;
            if (moveRight.IsPressed()) dx += 1f;
            if (moveLeft.IsPressed()) dx -= 1f;
            if (moveUp.IsPressed()) dy += 1f;
            if (moveDown.IsPressed()) dy -= 1f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dx = Mathf.Max(dx, 1f);
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dx = Mathf.Min(dx, -1f);
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dy = Mathf.Max(dy, 1f);
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dy = Mathf.Min(dy, -1f);
            }
            // Keys carry their own 1.5x gain so keyboard aiming is noticeably quicker than before.
            var keys = new Vector2(Mathf.Clamp(dx, -1f, 1f), Mathf.Clamp(dy, -1f, 1f)) * KeyGain;

            // Mouse look: read the RAW per-frame device delta, which self-zeroes when the
            // pointer is still. The action map's <Mouse>/delta is a Value action and RETAINS
            // the last movement after the mouse stops, which would pin the aim and (via the
            // old "mouse wins" rule) suppress the keyboard — so the device is authoritative.
            var mouse = Vector2.zero;
            var md = Mouse.current;
            if (md != null)
                mouse = md.delta.ReadValue();
            else
                mouse = lookDelta.ReadValue<Vector2>();
            // On desktop (Windows) raise the mouse gain so the cannon tracks the pointer briskly;
            // touch devices keep the tuned 1x so the stick and swipe stay comfortable.
            mouse *= Settings.MouseSensitivity * 0.06f * (s_desktop ? MouseDesktopGain : 1f);
            if (Settings.InvertY) mouse.y = -mouse.y;
            // A resting hand must never make the cannon creep.
            if (mouse.sqrMagnitude < 0.0004f) mouse = Vector2.zero;
            // Allow the mouse to reach the full (raised) move magnitude so a brisk flick rotates
            // the barrel faster than the old single-source cap, not merely reaching it sooner.
            mouse = Vector2.ClampMagnitude(mouse, MaxMove);

            // Combine additively so a stuck or active mouse can never mask the keyboard and
            // vice-versa; the raised clamp lets the per-source gains (keys/mouse/joystick) express
            // their full speed instead of being clipped back to the old single-source magnitude.
            var move = keys + mouse;
            if (touchMove.sqrMagnitude > 0.0004f) move += touchMove * TouchGain;

            return Vector2.ClampMagnitude(move, MaxMove);
        }

        public bool IsFireHeld()
        {
            if (pointerBlocked) return false;
            if (touchFireHeld) return true;
            if (fire.IsPressed()) return true;
            // Direct fallback: space or left mouse button, independent of the action map.
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.isPressed) return true;
            var md = Mouse.current;
            if (md != null && md.leftButton.isPressed) return true;
            return false;
        }

        /// <summary>One-shot ESC press, used to stop the game.</summary>
        public bool IsEscapePressed()
        {
            if (escape != null && escape.WasPressedThisFrame()) return true;
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        public void Enable() { if (map != null && !map.enabled) map.Enable(); }

        public void Disable() { if (map != null && map.enabled) map.Disable(); }

        public void Dispose()
        {
            if (map != null && map.enabled) map.Disable();
            if (asset != null) UnityEngine.Object.Destroy(asset);
        }
    }
}
