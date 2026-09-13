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
            // Primary path: the action map. If its per-frame processing ever fails to
            // deliver (a stale map, a focus quirk in the editor), the direct device read
            // below still drives the cannon, so input can never go fully dead.
            var keys = new Vector2(
                (moveRight.ReadValue<float>() > 0.5f ? 1f : 0f) - (moveLeft.ReadValue<float>() > 0.5f ? 1f : 0f),
                (moveUp.ReadValue<float>() > 0.5f ? 1f : 0f) - (moveDown.ReadValue<float>() > 0.5f ? 1f : 0f));

            // Direct keyboard fallback, OR-ed in so a broken action map cannot zero it out.
            var kb = Keyboard.current;
            if (kb != null)
            {
                float dx = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                         - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float dy = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                         - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                if (Mathf.Abs(dx) > Mathf.Abs(keys.x)) keys.x = dx;
                if (Mathf.Abs(dy) > Mathf.Abs(keys.y)) keys.y = dy;
            }

            var mouse = lookDelta.ReadValue<Vector2>();
            // Direct mouse fallback: the action map's delta control can lag or stall in
            // the editor while the raw device delta keeps flowing.
            var md = Mouse.current;
            if (md != null)
            {
                var rawDelta = md.delta.ReadValue();
                if (mouse.sqrMagnitude < 0.0001f && rawDelta.sqrMagnitude > 0.0001f)
                    mouse = rawDelta;
            }
            mouse *= Settings.MouseSensitivity * 0.06f;
            if (Settings.InvertY) mouse.y = -mouse.y;
            // A resting hand must never make the cannon creep.
            if (mouse.sqrMagnitude < 0.0004f) mouse = Vector2.zero;
            mouse = Vector2.ClampMagnitude(mouse, 1f);

            var move = keys;
            if (touchMove.sqrMagnitude > 0.0004f)
            {
                if (move.sqrMagnitude < 0.0001f) move = touchMove;
                else move = Vector2.ClampMagnitude(move + touchMove, 1f);
            }
            if (mouse.sqrMagnitude > move.sqrMagnitude) move = mouse;

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
