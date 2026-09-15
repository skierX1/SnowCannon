using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnowCannon
{
    /// <summary>
    /// A belt-and-braces touch driver for uGUI buttons. On some Android builds the new Input
    /// System's UI pointer path does not reliably raise <c>Button.onClick</c> from a real finger
    /// tap (the mouse path on desktop is fine), which left the menu's PLAY button dead on the
    /// phone. This component reads the Input System <c>Touchscreen</c> device directly — the very
    /// same device the gameplay input already proves works on the device — and, on a fresh touch
    /// down, hit-tests the screen with the canvas's own <c>GraphicRaycaster</c> (so the result is
    /// correct for the canvas render mode and the UI scale factor) and invokes the button's action.
    ///
    /// It never fights the normal uGUI path: a button's own <c>onClick</c> is wrapped so that if
    /// this fallback already fired for that button within a short window, the uGUI release-click is
    /// ignored, so a single tap always triggers exactly one action. On a machine with no touchscreen
    /// (a desktop with only a mouse) <c>Touchscreen.current</c> is null and this stays entirely out
    /// of the way, leaving the standard mouse-driven uGUI buttons to work as before.
    /// </summary>
    public sealed class UiTouchFallback : MonoBehaviour
    {
        sealed class Entry
        {
            public Button button;
            public Action onClick;
            public double lastFire;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly List<RaycastResult> results = new List<RaycastResult>();
        PointerEventData ped;
        GraphicRaycaster raycaster;
        bool wasDown;

        // A tap's down (handled here) and its up (handled by uGUI) land within this window, so a
        // short de-dup interval collapses the pair into a single activation.
        const double DedupSeconds = 0.35;

        /// <summary>Register a button so a raw touch on it fires <paramref name="onClick"/>.</summary>
        public void Register(Button button, Action onClick)
        {
            if (button == null || onClick == null) return;
            entries.Add(new Entry { button = button, onClick = onClick });
            if (raycaster == null)
            {
                var cv = button.GetComponentInParent<Canvas>();
                if (cv != null) raycaster = cv.GetComponent<GraphicRaycaster>();
            }
        }

        /// <summary>True if this button was just fired by the raw path, so its uGUI onClick should stand down.</summary>
        public bool FiredRecently(Button button)
        {
            double now = Time.unscaledTimeAsDouble;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].button == button && now - entries[i].lastFire < DedupSeconds) return true;
            return false;
        }

        void Update()
        {
            // Prune buttons whose scene has been torn down (destroyed Unity objects compare null).
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].button == null) entries.RemoveAt(i);
            if (entries.Count == 0) return;

            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts == null) return;   // no touchscreen -> the mouse/uGUI path owns input

            bool down = ts.press.isPressed;
            bool freshPress = down && !wasDown;
            wasDown = down;
            if (!freshPress) return;

            if (raycaster == null || EventSystem.current == null) return;
            var module = EventSystem.current.currentInputModule;
            if (module == null) return;
            if (ped == null) ped = new PointerEventData(EventSystem.current);

            ped.Reset();
            ped.position = ts.position.ReadValue();
            ped.pointerId = 0;
            ped.button = PointerEventData.InputButton.Left;

            results.Clear();
            raycaster.Raycast(ped, results);

            // results is sorted topmost-first by the raycaster; take the first hit that is a live button.
            for (int i = 0; i < results.Count; i++)
            {
                var go = results[i].gameObject;
                if (go == null) continue;
                var btn = go.GetComponentInParent<Button>();
                if (btn == null || !btn.IsActive() || !btn.interactable) continue;

                for (int k = 0; k < entries.Count; k++)
                {
                    if (entries[k].button == btn)
                    {
                        entries[k].lastFire = Time.unscaledTimeAsDouble;
                        entries[k].onClick();
                        return;
                    }
                }
                return; // a live button was hit but it is not ours; do not fall through to one behind it
            }
        }
    }
}
