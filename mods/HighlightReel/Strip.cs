using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighlightReel
{
    // The mission's highlights on its reward screen: a strip of pictures with their captions, under the XP column, in the order
    // they happened.
    [HarmonyPatch(typeof(MissionRewardsWindow), nameof(MissionRewardsWindow.SetRewards))]
    internal static class Strip
    {
        private const string Name = "HighlightReelStrip";

        private static void Postfix(MissionRewardsWindow __instance, List<RewardInfo> rewards)
        {
            Remove(__instance);   // (the game reuses this screen: last time's strip goes first)
            int xpRows = rewards.Count(r => r.reward is IUpgradable);
            if (Reel.Shots.Count > 0 || Reel.Instance != null) __instance.StartCoroutine(Show(__instance, xpRows));
        }

        private static void Remove(MissionRewardsWindow window)
        {
            foreach (Transform t in window.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name == Name || t.name == Zoom.Name)) Object.Destroy(t.gameObject);
        }

        private static IEnumerator Show(MissionRewardsWindow window, int xpRows)
        {
            // the XP rows come in one by one: wait for them (and for any picture still being taken), so the strip goes below them all
            float waited = 0f;
            while (waited < 12f && window != null && window.isActiveAndEnabled && !XPShown(window, xpRows)) { waited += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.3f);
            if (window == null || !window.isActiveAndEnabled || Reel.Shots.Count == 0) yield break;
            try { Build(window); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Highlight Reel: the strip didn't build: " + e); }
        }

        private static bool XPShown(MissionRewardsWindow window, int rows)
        {
            int shown = 0;
            foreach (Transform child in window.xpParent) if (child.gameObject.activeSelf) shown++;
            return shown >= rows;
        }

        private static void Build(MissionRewardsWindow window)
        {
            // measured in the XP column's own space (the screen is a hologram that turns with the camera)
            RectTransform xp = window.xpParent;
            var column = (RectTransform)xp.parent;
            var corners = new Vector3[4];
            float bottom = float.MaxValue;
            foreach (RectTransform child in xp)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                foreach (RectTransform part in child.GetComponentsInChildren<RectTransform>(false))
                {
                    part.GetWorldCorners(corners);
                    bottom = Mathf.Min(bottom, column.InverseTransformPoint(corners[0]).y);
                }
            }
            xp.GetWorldCorners(corners);
            float left = column.InverseTransformPoint(corners[0]).x, right = column.InverseTransformPoint(corners[3]).x;
            if (bottom == float.MaxValue) bottom = column.InverseTransformPoint(corners[0]).y;
            float width = Mathf.Max(right - left, 400f), center = (left + right) * 0.5f;

            var strip = new GameObject(Name, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(column, false);
            strip.localRotation = Quaternion.identity;
            strip.localScale = Vector3.one;
            strip.localPosition = new Vector3(center, bottom - 30f, 0f);

            TMP_Text style = window.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.text.Trim().ToUpperInvariant() == "XP") ?? window.missionText;
            float y = 0f;
            Text(strip, style, "HIGHLIGHTS", style.fontSize, Color.white, 0f, ref y);

            // one row, in the order they happened, as wide as the column allows
            List<Highlight> shots = Reel.Shots.Where(s => s.Picture != null).OrderBy(s => s.Order).ToList();
            var zoom = strip.gameObject.AddComponent<Zoom>();
            zoom.Setup(window, style);
            float gap = 16f;
            float w = Mathf.Min(260f, (width - gap * (shots.Count - 1)) / Mathf.Max(shots.Count, 1));
            float h = w * Reel.Height / Reel.Width;
            float x0 = -(shots.Count * w + (shots.Count - 1) * gap) * 0.5f + w * 0.5f;
            for (int i = 0; i < shots.Count; i++)
            {
                var frame = new GameObject("Shot", typeof(RectTransform)).GetComponent<RectTransform>();
                frame.SetParent(strip, false);
                frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 1f);
                frame.sizeDelta = new Vector2(w + 6f, h + 6f);
                frame.localPosition = new Vector3(x0 + i * (w + gap), y - h * 0.5f - 3f, 0f);
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

                float cy = y - h - 14f;
                var caption = Text(strip, style, shots[i].Caption, Mathf.Max(style.fontSize * 0.45f, 14f), CaptionColor(shots[i]), x0 + i * (w + gap), ref cy);
                caption.rectTransform.sizeDelta = new Vector2(w + gap, caption.rectTransform.sizeDelta.y);
                caption.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        internal static Color CaptionColor(Highlight h) =>
            h.Priority >= 80 ? new Color(1f, 0.82f, 0.25f) : h.Priority >= 45 ? new Color(0.35f, 0.95f, 1f) : new Color(0.85f, 0.85f, 0.85f);

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
