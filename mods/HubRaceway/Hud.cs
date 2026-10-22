using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HubRaceway
{
    // The raceway's own HUD: lap and time at the top (under the game's health bar), the standings down the right in a race, the
    // start lights and countdown in the middle, a banner for sign-ups and results, your power-up near the bottom, and short
    // flashes for what just happened. The raceway sets the texts every frame; an empty text hides its part.
    internal sealed class Hud
    {
        private readonly GameObject canvas;
        private readonly TextMeshProUGUI top, list, center, lights, banner, item, flash;

        public Hud(TMP_FontAsset font)
        {
            canvas = new GameObject("QuadsHubRacewayHud");
            var c = canvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 40;
            var scaler = canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            top = Make(font, "Top", new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1000f, 160f), 34f, TextAlignmentOptions.Top);
            list = Make(font, "Standings", new Vector2(1f, 0.5f), new Vector2(-40f, 80f), new Vector2(420f, 520f), 28f, TextAlignmentOptions.TopRight);
            lights = Make(font, "Lights", new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(900f, 160f), 120f, TextAlignmentOptions.Center);
            center = Make(font, "Center", new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(1400f, 200f), 110f, TextAlignmentOptions.Center);
            banner = Make(font, "Banner", new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 220f), 30f, TextAlignmentOptions.Top);
            item = Make(font, "Item", new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1000f, 140f), 54f, TextAlignmentOptions.Bottom);
            flash = Make(font, "Flash", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1400f, 120f), 52f, TextAlignmentOptions.Center);
            lights.outlineWidth = 0.1f;
            canvas.SetActive(false);
        }

        private TextMeshProUGUI Make(TMP_FontAsset font, string name, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(0, 0, 0, 255);
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return t;
        }

        public void Show(string topText, string listText, string lightsText, string centerText, string bannerText, string itemText, string flashText)
        {
            bool any = !string.IsNullOrEmpty(topText) || !string.IsNullOrEmpty(listText) || !string.IsNullOrEmpty(lightsText) || !string.IsNullOrEmpty(centerText)
                || !string.IsNullOrEmpty(bannerText) || !string.IsNullOrEmpty(itemText) || !string.IsNullOrEmpty(flashText);
            if (canvas.activeSelf != any) canvas.SetActive(any);
            if (!any) return;
            Set(top, topText);
            Set(list, listText);
            Set(lights, lightsText);
            Set(center, centerText);
            Set(banner, bannerText);
            Set(item, itemText);
            Set(flash, flashText);
        }

        private static void Set(TextMeshProUGUI t, string text)
        {
            text = text ?? "";
            if (t.text != text) t.text = text;
        }

        public void Destroy()
        {
            if (canvas != null) Object.Destroy(canvas);
        }
    }
}
