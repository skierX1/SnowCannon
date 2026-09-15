using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnowCannon
{
    /// <summary>
    /// A tiny, dependency-free UI toolkit. Only Image, Text and Button are used, plus
    /// manual RectTransform placement, which keeps the surface away from the parts of the
    /// new uGUI package that are still in flux.
    /// </summary>
    public static class Ui
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1280f, 720f);

        static Sprite s_white;
        static Sprite s_circle;
        static Sprite s_rounded;

        public static Sprite White
        {
            get { if (s_white == null) s_white = TextureFactory.WhiteSprite(); return s_white; }
        }

        public static Sprite Circle
        {
            get { if (s_circle == null) s_circle = TextureFactory.CircleSprite(1.7f); return s_circle; }
        }

        /// <summary>A 9-sliced rounded rectangle for buttons and panels.</summary>
        public static Sprite Rounded
        {
            get { if (s_rounded == null) s_rounded = TextureFactory.RoundedRectSprite(); return s_rounded; }
        }

        static Font s_font;

        /// <summary>The one font every label uses. A Text with no font renders nothing, so
        /// this is loaded once from the built-in LegacyRuntime face, falling back to an OS
        /// font if that is ever unavailable.</summary>
        public static Font Font
        {
            get { if (s_font == null) s_font = LoadFont(); return s_font; }
        }

        static Font LoadFont()
        {
            // 1) A dynamic font from a face guaranteed to exist on the target OS. This is the
            //    PRIMARY path because it works identically in the editor and in a player build,
            //    and it never logs. The broad name list lets the OS pick the first installed face.
            try
            {
                var f = Font.CreateDynamicFontFromOSFont(
                    new[] { "Arial", "Segoe UI", "Segoe UI Variable Text VF", "Tahoma", "Verdana", "Microsoft Sans Serif" }, 16);
                if (f != null) return f;
            }
            catch { }

            // 2) Built-in legacy face, only as a last resort. In 6.6 this is stripped from the
            //    player build AND fails to resolve in the editor, and it LOGS an error instead of
            //    returning null quietly, so it must not be attempted first -- a try/catch cannot
            //    suppress that log because the API does not throw.
            try
            {
                var f = Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime") as Font;
                if (f != null) return f;
            }
            catch { }

            return null;
        }

        public static Canvas CreateCanvas(string name, int sort)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sort;
            canvas.pixelPerfect = false;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>The new Input System drives the EventSystem, never the legacy module.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.localEulerAngles = Vector3.zero;
            return rt;
        }

        public static RectTransform Rt(GameObject go)
        {
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot,
                                         Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        public static Image AddImage(RectTransform parent, string name, Sprite sprite,
                                     Color color, bool raycast)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            img.preserveAspect = false;
            return img;
        }

        public static Text AddText(RectTransform parent, string name, string content,
                                   int size, Color color, TextAnchor align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.supportRichText = false;
            t.raycastTarget = false;
            return t;
        }

/// <summary>A light, rounded button: a soft drop-shadow behind a rounded panel, a subtle
        /// top highlight for a gentle bevel, and a centred label. It brightens on hover and dips on
        /// press so it feels tactile on both mouse and touch.</summary>
        public static Button AddButton(RectTransform parent, string name, string label,
                                      Vector2 size, int fontSize, Color background, Action onClick)
        {
            var rt = NewRect(name, parent);
            rt.sizeDelta = size;

            // Soft drop-shadow, offset down-left a touch.
            var shadow = NewRect("shadow", rt);
            shadow.anchorMin = Vector2.zero; shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-4f, -8f);
            shadow.offsetMax = new Vector2(4f, -2f);
            var shImg = shadow.gameObject.AddComponent<Image>();
            shImg.sprite = Rounded;
            shImg.color = new Color(0f, 0f, 0.06f, 0.28f);
            shImg.raycastTarget = false;

            // The button body. Menu buttons read better as a translucent glass panel than as a
            // solid slab, so we keep the caller's hue but drop the fill's opacity and let the scene
            // show through. The hover/press tint below is derived from this same translucent base.
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Rounded;
            Color body = background;
            body.a *= 0.55f;
            bg.color = body;
            bg.raycastTarget = true;
            bg.preserveAspect = false;

            // A faint lighter cap along the top edge for a soft bevel.
            var gloss = NewRect("gloss", rt);
            gloss.anchorMin = new Vector2(0f, 0.5f); gloss.anchorMax = new Vector2(1f, 1f);
            gloss.offsetMin = new Vector2(6f, 0f); gloss.offsetMax = new Vector2(-6f, -3f);
            var glImg = gloss.gameObject.AddComponent<Image>();
            glImg.sprite = Rounded;
            glImg.color = new Color(1f, 1f, 1f, 0.16f);
            glImg.raycastTarget = false;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.ColorTint;
            var c = bg.color;
            btn.colors = new ColorBlock
            {
                normalColor = c,
                highlightedColor = Color.Lerp(c, Color.white, 0.22f),
                pressedColor = Color.Lerp(c, Color.black, 0.14f),
                selectedColor = c,
                disabledColor = new Color(c.r, c.g, c.b, 0.5f),
                colorMultiplier = 1f
            };

            var labelTxt = AddText(rt, "label", label, fontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(labelTxt.rectTransform);
            // A subtle text shadow so the label stays legible on the light fills.
            var txtShadow = labelTxt.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            txtShadow.effectColor = new Color(0f, 0f, 0.08f, 0.4f);
            txtShadow.effectDistance = new Vector2(0f, -2f);

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>A flat rectangular panel used as a backdrop for labels. It always fills
        /// its parent, so callers get a visible backdrop without having to stretch it.</summary>
        public static Image AddPanel(RectTransform parent, string name, Color color, bool raycast)
        {
            var img = AddImage(parent, name, White, color, raycast);
            Stretch(img.rectTransform);
            return img;
        }

        /// <summary>
        /// A classic uGUI slider: a track bar with a filled portion and a draggable knob.
        /// Built entirely from Images so it needs no extra assets.
        /// </summary>
        public static Slider AddSlider(RectTransform parent, string name, Vector2 size,
                                       Color trackColor, Color fillColor, Color knobColor,
                                       System.Action<float> onValueChanged)
        {
            var rt = NewRect(name, parent);
            rt.sizeDelta = size;

            var track = AddImage(rt, "track", White, trackColor, false);
            Stretch(track.rectTransform);

            var fillAreaRt = NewRect("fill_area", rt);
            Stretch(fillAreaRt);

            var fillRt = NewRect("fill", fillAreaRt);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = White;
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            var handleAreaRt = NewRect("handle_area", rt);
            Stretch(handleAreaRt);

            var handleRt = NewRect("handle", handleAreaRt);
            handleRt.sizeDelta = new Vector2(34f, size.y + 10f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            var handle = handleRt.gameObject.AddComponent<Image>();
            handle.sprite = Circle;
            handle.color = knobColor;
            handle.raycastTarget = true;

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.targetGraphic = track;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRt;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            if (onValueChanged != null) slider.onValueChanged.AddListener(v => onValueChanged(v));
            return slider;
        }

        /// <summary>
        /// Keeps UI clear of notches and rounded corners on phones. Reads the device
        /// safe area and converts it into the canvas' reference space.
        /// </summary>
        public static void ApplySafeArea(RectTransform rt, Canvas canvas)
        {
            if (rt == null || canvas == null) return;

            var scaler = canvas.GetComponent<CanvasScaler>();
            float scaleFactor = scaler != null ? scaler.scaleFactor : 1f;
            if (scaleFactor <= 0f) scaleFactor = 1f;

            Rect sa = Screen.safeArea;
            Rect res = new Rect(0, 0, Screen.width, Screen.height);
            if (res.width <= 0 || res.height <= 0) return;

            Vector2 refRes = scaler != null ? scaler.referenceResolution : ReferenceResolution;

            float left = sa.xMin / res.width * refRes.x;
            float right = (res.xMax - sa.xMax) / res.width * refRes.x;
            float bottom = sa.yMin / res.height * refRes.y;
            float top = (res.yMax - sa.yMax) / res.height * refRes.y;

            // Never inset more than a fifth of the screen, whatever the device reports.
            left = Mathf.Min(left, refRes.x * 0.2f);
            right = Mathf.Min(right, refRes.x * 0.2f);
            bottom = Mathf.Min(bottom, refRes.y * 0.2f);
            top = Mathf.Min(top, refRes.y * 0.2f);

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
