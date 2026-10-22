using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighlightReel
{
    // Where the reel is shown. After a successful mission: on the awards screen that follows the rewards (the player cards,
    // "Deadliest" and the like), in one big row above the cards (empty however many players there are). That screen only comes after a success, so after a failed or
    // aborted mission: on the reward screen, in the gap between the XP and Rewards columns. Either way it's saved when the reward
    // screen opens, and pointing at a picture shows it big.
    [HarmonyPatch(typeof(MissionRewardsWindow), nameof(MissionRewardsWindow.SetRewards))]
    internal static class OnRewards
    {
        private static void Postfix(MissionRewardsWindow __instance, MissionManager.MissionState completed)
        {
            Strip.Remove(__instance.transform);   // (the game reuses this screen: last time's reel goes first)
            Reel.SaveAll();
            // (a success: the awards screen comes next, and shows it there)
            if (completed != MissionManager.MissionState.Completed && Reel.Instance != null)
                __instance.StartCoroutine(Strip.Later(__instance, () => Strip.InTheGap(__instance)));
        }
    }

    [HarmonyPatch(typeof(MissionStatsWindow), "OnOpen")]
    internal static class OnAwards
    {
        private static void Postfix(MissionStatsWindow __instance)
        {
            Strip.Remove(__instance.transform);
            if (Reel.Instance != null) __instance.StartCoroutine(Strip.Later(__instance, () => Strip.AboveTheCards(__instance)));
        }
    }

    internal static class Strip
    {
        private const string Name = "HighlightReelStrip";

        public static void Remove(Transform window)
        {
            foreach (Transform t in window.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name == Name || t.name == Zoom.Name)) Object.Destroy(t.gameObject);
        }

        // after the screen has come in (and any picture still being taken is in)
        public static IEnumerator Later(MonoBehaviour window, System.Action build)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            if (window == null || !window.isActiveAndEnabled || Reel.Shots.Count == 0) yield break;
            try { build(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Highlight Reel: the reel didn't build: " + e); }
        }

        // The awards screen: a row as wide as the screen allows (wrapping after six), in the empty top of the screen, above the
        // player cards (and the event's points, when there are any).
        public static void AboveTheCards(MissionStatsWindow window)
        {
            var screen = (RectTransform)window.transform;
            var corners = new Vector3[4];
            float cardsTop = screen.rect.center.y;
            // (the cards and the points texts themselves: their containers stretch over the whole screen)
            var parts = new List<RectTransform>();
            foreach (MissionStat card in window.statDisplays)
                if (card != null && card.gameObject.activeInHierarchy) parts.AddRange(card.GetComponentsInChildren<RectTransform>(false));
            foreach (TMP_Text text in new[] { window.eventText1, window.eventText2, window.eventPointsText })
                if (text != null && text.gameObject.activeInHierarchy) parts.Add(text.rectTransform);
            foreach (RectTransform part in parts)
            {
                part.GetWorldCorners(corners);
                cardsTop = Mathf.Max(cardsTop, screen.InverseTransformPoint(corners[1]).y);
            }
            TMP_Text style = window.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.text.Trim().ToUpperInvariant() == "DEADLIEST")
                             ?? window.GetComponentsInChildren<TMP_Text>(true).First();
            Grid(screen, style, style.fontSize * 1.6f, screen.rect.center.x, screen.rect.yMax - screen.rect.height * 0.05f,
                 screen.rect.width * 0.9f, 6, 360f, cardsTop + 40f);
        }

        // The reward screen: a column in the gap between the XP and the Rewards (empty whatever the mission paid).
        public static void InTheGap(MissionRewardsWindow window)
        {
            var screen = (RectTransform)window.transform;
            var corners = new Vector3[4];
            window.xpParent.GetWorldCorners(corners);
            float gapLeft = screen.InverseTransformPoint(corners[3]).x, top = screen.InverseTransformPoint(corners[1]).y;
            window.upgradeParent.GetWorldCorners(corners);
            float gapRight = screen.InverseTransformPoint(corners[0]).x;
            window.resourceParent.GetWorldCorners(corners);
            gapRight = Mathf.Min(gapRight, screen.InverseTransformPoint(corners[0]).x);
            if (gapRight - gapLeft < 200f) { gapLeft = -150f; gapRight = 150f; }   // (columns closer than expected: the middle of the screen)
            TMP_Text style = window.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.text.Trim().ToUpperInvariant() == "XP") ?? window.missionText;
            Grid(screen, style, style.fontSize, (gapLeft + gapRight) * 0.5f, top, gapRight - gapLeft - 20f, 2, 240f,
                 screen.rect.yMin + screen.rect.height * 0.17f);   // (above the OK button)
        }

        // The pictures in order, `perRow` to a row at most, as big as `width` and the room down to `bottomLimit` allow, each framed in
        // its colour with its caption under it, under a HIGHLIGHTS header centred at (x, top).
        private static void Grid(RectTransform screen, TMP_Text style, float headerSize, float x, float top, float width, int perRow, float maxPicture, float bottomLimit)
        {
            var strip = new GameObject(Name, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(screen, false);
            strip.localRotation = Quaternion.identity;
            strip.localScale = Vector3.one;
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.localPosition = new Vector3(x, top, 0f);

            float y = 0f;
            Text(strip, style, "HIGHLIGHTS", headerSize, Color.white, 0f, ref y);
            y -= 8f;

            List<Highlight> shots = Reel.Shots.Where(s => s.Picture != null).OrderBy(s => s.Order).ToList();
            float gap = 16f, captionSize = Mathf.Max(style.fontSize * 0.5f, 14f);
            int columns = Mathf.Max(1, Mathf.Min(shots.Count, Mathf.Min(perRow, Mathf.FloorToInt((width + gap) / (140f + gap)))));
            int rows = Mathf.CeilToInt(shots.Count / (float)columns);
            float w = Mathf.Min(maxPicture, (width - gap * (columns - 1)) / columns);
            float rowExtra = captionSize * 1.6f + 10f;   // (the caption under each picture)
            float tall = top + y - bottomLimit;
            if (rows * (w * Reel.Height / Reel.Width + rowExtra) > tall) w = Mathf.Max(80f, (tall / rows - rowExtra) * Reel.Width / Reel.Height);
            float h = w * Reel.Height / Reel.Width;

            var zoom = strip.gameObject.AddComponent<Zoom>();
            zoom.Setup(screen, style);
            for (int i = 0; i < shots.Count; i++)
            {
                int col = i % columns, row = i / columns;
                int inRow = Mathf.Min(columns, shots.Count - row * columns);
                float px = (col - (inRow - 1) * 0.5f) * (w + gap);
                float rowTop = y - row * (h + rowExtra);

                var frame = new GameObject("Shot", typeof(RectTransform)).GetComponent<RectTransform>();
                frame.SetParent(strip, false);
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 1f);
                frame.sizeDelta = new Vector2(w + 6f, h + 6f);
                frame.localPosition = new Vector3(px, rowTop - h * 0.5f - 3f, 0f);
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
                var caption = Text(strip, style, shots[i].Caption, captionSize, CaptionColor(shots[i]), px, ref cy);
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
