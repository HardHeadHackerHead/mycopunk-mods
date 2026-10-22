using System.Collections.Generic;
using Pigeon.Movement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PullYourselfTogether
{
    // What a crawling player sees: nothing but the way to their legs. While they crawl, the death screen's text, the mission's
    // list and every other ping are put away, and there's one big marker on their legs (their legs icon, YOUR LEGS and how far),
    // held at the edge of the screen with an arrow when the legs are off it, and one line of controls at the bottom. All of it
    // comes back as it was the moment they stop crawling (spectate a teammate, get carried, or get up).
    internal static class Hud
    {
        private static Canvas canvas;
        private static RectTransform marker, arrow;
        private static Image icon;
        private static TextMeshProUGUI label, hint;
        private static bool hidDeathScreen, hidMission;
        private static readonly List<Highlighter.Ping> hiddenPings = new List<Highlighter.Ping>();

        public static void Show(PlayerLook look, DeadPlayerPart torso)
        {
            if (torso == null) { Restore(look); return; }
            Player me = Player.LocalPlayer;

            // the clutter, away
            if (look.deathSpectateScreen != null && look.deathSpectateScreen.activeSelf) { look.deathSpectateScreen.SetActive(false); hidDeathScreen = true; }
            if (!look.DisableMissionHUD) { look.DisableMissionHUD = true; hidMission = true; }
            foreach (Highlighter.Ping ping in Highlighter.pings)
                if (ping?.graphic != null && ping.graphic.enabled) { ping.graphic.enabled = false; hiddenPings.Add(ping); }

            if (canvas == null) Build(look);
            canvas.gameObject.SetActive(true);
            bool canRespawn = LevelData.Instance != null && LevelData.Instance.LevelFlags.IsEnabled(LevelFlags.CanRespawnSelf);
            hint.SetText(canRespawn ? "WASD  crawl      SPACE  lunge      CLICK  watch your team      HOLD X  respawn" : "WASD  crawl      SPACE  lunge      CLICK  watch your team");

            DeadPlayerPart legs = Parts.Legs(me);
            Camera cam = PlayerLook.LocalCamera;
            if (legs == null || cam == null) { marker.gameObject.SetActive(false); return; }
            marker.gameObject.SetActive(true);
            Vector3 at = Parts.Centre(legs) + Vector3.up * 0.4f;
            float metres = Vector3.Distance(Parts.Centre(torso), Parts.Centre(legs));
            label.SetText(metres < 10f ? "YOUR LEGS\n{0:1} m" : "YOUR LEGS\n{0:0} m", metres);
            icon.color = me.Color;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 5f);
            marker.localScale = new Vector3(pulse, pulse, 1f);

            // on the screen: over the legs; off it (or behind): at the edge, toward them, with an arrow
            var area = (RectTransform)canvas.transform;
            Vector2 size = area.rect.size, half = size * 0.5f, margin = new Vector2(110f, 120f);
            Vector3 sp = cam.WorldToViewportPoint(at);
            bool behind = sp.z < 0f;
            Vector2 p = new Vector2((sp.x - 0.5f) * size.x, (sp.y - 0.5f) * size.y);
            if (behind) p = -p;
            bool onScreen = !behind && Mathf.Abs(p.x) < half.x - margin.x && Mathf.Abs(p.y) < half.y - margin.y;
            if (!onScreen)
            {
                if (p.sqrMagnitude < 1f) p = new Vector2(0f, -1f);
                float k = Mathf.Min((half.x - margin.x) / Mathf.Max(Mathf.Abs(p.x), 1e-3f), (half.y - margin.y) / Mathf.Max(Mathf.Abs(p.y), 1e-3f));
                p *= k;
            }
            marker.anchoredPosition = p;
            arrow.gameObject.SetActive(!onScreen);
            if (!onScreen)
            {
                Vector2 d = p.normalized;
                arrow.anchoredPosition = d * 70f;
                arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
            }
        }

        // not crawling (any more): everything back as it was
        public static void Restore(PlayerLook look)
        {
            if (canvas != null && canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            if (look == null) look = PlayerLook.Instance;
            Player me = Player.LocalPlayer;
            if (hidDeathScreen)
            {
                hidDeathScreen = false;
                if (look != null && look.deathSpectateScreen != null && me != null && !me.IsAlive) look.deathSpectateScreen.SetActive(true);
            }
            if (hidMission)
            {
                hidMission = false;
                if (look != null) look.DisableMissionHUD = false;
            }
            if (hiddenPings.Count > 0)
            {
                foreach (Highlighter.Ping ping in hiddenPings) if (ping?.graphic != null && Highlighter.pings.Contains(ping)) ping.graphic.enabled = true;
                hiddenPings.Clear();
            }
        }

        public static void Destroy()
        {
            Restore(null);
            if (canvas != null) Object.Destroy(canvas.gameObject);
            canvas = null;
        }

        private static void Build(PlayerLook look)
        {
            canvas = new GameObject("PullYourselfTogether HUD", typeof(RectTransform)).AddComponent<Canvas>();
            Object.DontDestroyOnLoad(canvas.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            TMP_Text style = look.killedByText;

            marker = Rect("Legs", canvas.transform, new Vector2(160f, 160f));
            icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            icon.transform.SetParent(marker, false);
            icon.sprite = Global.Instance.PlayerDeathIcons[1];
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            ((RectTransform)icon.transform).sizeDelta = new Vector2(72f, 72f);
            ((RectTransform)icon.transform).anchoredPosition = new Vector2(0f, 18f);
            label = Text("Label", marker, style, 26f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, -48f);

            arrow = Rect("Arrow", marker, new Vector2(34f, 30f));
            var tri = arrow.gameObject.AddComponent<Image>();
            tri.sprite = Triangle();
            tri.raycastTarget = false;

            hint = Text("Hint", canvas.transform, style, 24f);
            var hr = (RectTransform)hint.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1600f, 40f);
            hr.anchoredPosition = new Vector2(0f, 70f);
            hint.color = new Color(1f, 1f, 1f, 0.85f);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, TMP_Text style, float size)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; }
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            ((RectTransform)t.transform).sizeDelta = new Vector2(400f, size * 2.6f);
            return t;
        }

        // a white triangle pointing up, drawn once
        private static Sprite triangle;

        private static Sprite Triangle()
        {
            if (triangle != null) return triangle;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float half = (1f - (y + 0.5f) / n) * n * 0.5f;   // (wide at the bottom, a point at the top)
                    float d = half - Mathf.Abs(x + 0.5f - n * 0.5f);
                    byte a = (byte)(Mathf.Clamp01(d) * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            triangle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return triangle;
        }
    }
}
