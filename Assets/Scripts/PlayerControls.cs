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
            // Keyboard: read the RAW device Button controls, which self-zero the instant a key
            // is released. The action map's Value actions can retain their last value after a
            // release (a stale map, a focus quirk), which would pin the aim — so the device is
            // authoritative and the action map is only a fallback when no device is present.
            var kb = Keyboard.current;
            Vector2 keys;
            if (kb != null)
            {
                float dx = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                         - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float dy = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                         - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                keys = new Vector2(dx, dy);
            }
            else
            {
                keys = new Vector2(
                    (moveRight.ReadValue<float>() > 0.5f ? 1f : 0f) - (moveLeft.ReadValue<float>() > 0.5f ? 1f : 0f),
                    (moveUp.ReadValue<float>() > 0.5f ? 1f : 0f) - (moveDown.ReadValue<float>() > 0.5f ? 1f : 0f));
            }

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
            mouse *= Settings.MouseSensitivity * 0.06f;
            if (Settings.InvertY) mouse.y = -mouse.y;
            // A resting hand must never make the cannon creep.
            if (mouse.sqrMagnitude < 0.0004f) mouse = Vector2.zero;
            mouse = Vector2.ClampMagnitude(mouse, 1f);

            // Combine additively so a stuck or active mouse can never mask the keyboard and
            // vice-versa; the clamp keeps the total magnitude sane.
            var move = keys + mouse;
            if (touchMove.sqrMagnitude > 0.0004f) move += touchMove;

            return Vector2.ClampMagnitude(move, 1f);
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
