using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighlightReel
{
    // The mission's highlights on its reward screen: a grid of pictures with their captions in the gap between the XP column and
    // the Rewards column (empty whatever the mission paid: more XP rows or rewards don't push it off the screen), in the order they
    // happened. The reel is saved as pictures when this screen opens.
    [HarmonyPatch(typeof(MissionRewardsWindow), nameof(MissionRewardsWindow.SetRewards))]
    internal static class Strip
    {
        private const string Name = "HighlightReelStrip";

        private static void Postfix(MissionRewardsWindow __instance)
        {
            Remove(__instance);   // (the game reuses this screen: last time's strip goes first)
            Reel.SaveAll();
            if (Reel.Instance != null) __instance.StartCoroutine(Show(__instance));
        }

        private static void Remove(MissionRewardsWindow window)
        {
            foreach (Transform t in window.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name == Name || t.name == Zoom.Name)) Object.Destroy(t.gameObject);
        }

        private static IEnumerator Show(MissionRewardsWindow window)
        {
            // after the screen's title comes in (and any picture still being taken is in)
            yield return new WaitForSecondsRealtime(1.2f);
            if (window == null || !window.isActiveAndEnabled || Reel.Shots.Count == 0) yield break;
            try { Build(window); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Highlight Reel: the strip didn't build: " + e); }
        }

        private static void Build(MissionRewardsWindow window)
        {
            // measured in the screen's own space (it's a hologram that turns with the camera): the gap between the two columns
            var screen = (RectTransform)window.transform;
            var corners = new Vector3[4];
            window.xpParent.GetWorldCorners(corners);
            float gapLeft = screen.InverseTransformPoint(corners[3]).x, top = screen.InverseTransformPoint(corners[1]).y;
            window.upgradeParent.GetWorldCorners(corners);
            float gapRight = screen.InverseTransformPoint(corners[0]).x;
            window.resourceParent.GetWorldCorners(corners);
            gapRight = Mathf.Min(gapRight, screen.InverseTransformPoint(corners[0]).x);
            if (gapRight - gapLeft < 200f) { gapLeft = -150f; gapRight = 150f; }   // (columns closer than expected: the middle of the screen)
            float bottomLimit = screen.rect.yMin + screen.rect.height * 0.17f;   // (above the OK button)

            var strip = new GameObject(Name, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(screen, false);
            strip.localRotation = Quaternion.identity;
            strip.localScale = Vector3.one;
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.localPosition = new Vector3((gapLeft + gapRight) * 0.5f, top, 0f);

            TMP_Text style = window.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.text.Trim().ToUpperInvariant() == "XP") ?? window.missionText;
            float y = 0f;
            Text(strip, style, "HIGHLIGHTS", style.fontSize, Color.white, 0f, ref y);
            y -= 8f;

            // two to a row when the gap is wide enough, as big as the room above the OK button allows
            List<Highlight> shots = Reel.Shots.Where(s => s.Picture != null).OrderBy(s => s.Order).ToList();
            float gap = 14f, captionSize = Mathf.Max(style.fontSize * 0.42f, 13f), room = gapRight - gapLeft - 20f;
            int columns = room >= 2 * 150f + gap ? 2 : 1;
            int rows = Mathf.CeilToInt(shots.Count / (float)columns);
            float w = Mathf.Min(240f, (room - gap * (columns - 1)) / columns);
            float rowExtra = captionSize * 1.6f + 10f;   // (the caption under each picture)
            float tall = top + y - bottomLimit;
            if (rows * (w * Reel.Height / Reel.Width + rowExtra) > tall) w = Mathf.Max(80f, (tall / rows - rowExtra) * Reel.Width / Reel.Height);
            float h = w * Reel.Height / Reel.Width;

            var zoom = strip.gameObject.AddComponent<Zoom>();
            zoom.Setup(window, style);
            for (int i = 0; i < shots.Count; i++)
            {
                int col = i % columns, row = i / columns;
                int inRow = Mathf.Min(columns, shots.Count - row * columns);
                float x = (col - (inRow - 1) * 0.5f) * (w + gap);
                float rowTop = y - row * (h + rowExtra);

                var frame = new GameObject("Shot", typeof(RectTransform)).GetComponent<RectTransform>();
                frame.SetParent(strip, false);
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 1f);
                frame.sizeDelta = new Vector2(w + 6f, h + 6f);
                frame.localPosition = new Vector3(x, rowTop - h * 0.5f - 3f, 0f);
                var border = frame.gameObject.AddComponent<Image>();
                border.color = CaptionColor(shots[i]);
                border.raycastTarget = false;
                zoom.Add(frame, shots[i]);

                var pic = new GameObject("Picture", typeof(RectTransform)).GetComponent<RectTransform>();
                pic.SetParent(frame, false);
                pic.sizeDelta = new Vector2(w, h);
                var raw = pic.gameObject.AddComponent<RawImage>();
                raw.texture = shots[i].Picture;
                raw.raycastTarget = false;

                float cy = rowTop - h - 10f;
                var caption = Text(strip, style, shots[i].Caption, captionSize, CaptionColor(shots[i]), x, ref cy);
                caption.rectTransform.sizeDelta = new Vector2(w + gap, caption.rectTransform.sizeDelta.y);
            }
        }

        // gold for bosses, orange for melee, cyan for multi-kills and close calls, white for the rest
        internal static Color CaptionColor(Highlight h) =>
            h.Kind == Kind.Boss ? new Color(1f, 0.82f, 0.25f) : h.Kind == Kind.Melee ? new Color(1f, 0.55f, 0.2f) :
            h.Kind == Kind.Revive ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.35f, 0.95f, 1f);

        internal static TMP_Text Text(RectTransform parent, TMP_Text style, string text, float size, Color color, float x, ref float y)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = style.font;
            t.fontSharedMaterial = style.fontSharedMaterial;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Top;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.SetText(text);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);   // (placed by its top edge, so it sits under what's above it)
            rt.sizeDelta = new Vector2(1000f, size * 1.4f);
            rt.localPosition = new Vector3(x, y, 0f);
            y -= size * 1.5f;
            return t;
        }
    }
}
