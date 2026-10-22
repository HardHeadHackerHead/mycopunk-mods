using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HighlightReel
{
    // Point at a picture in the reel and it shows big in the middle of the screen, with its caption, until you point away. Click it
    // to save that picture (nothing is saved otherwise). The pointer is checked every frame against each picture (however the game
    // routes its pointer events to this hologram).
    internal sealed class Zoom : MonoBehaviour
    {
        public const string Name = "HighlightReelZoom";

        public static int ForceIndex = -1;   // (the test's "point at picture n", for looking at it without a mouse)
        public static bool ForceClick;       // (and its "click it")

        private readonly List<(RectTransform frame, Highlight shot, TMP_Text tag)> frames = new List<(RectTransform, Highlight, TMP_Text)>();
        private RectTransform screen;
        private TMP_Text style;
        private RectTransform big;
        private RawImage bigPicture;
        private Image bigBorder;
        private TMP_Text bigCaption, bigHint;
        private int showing = -1;

        public void Setup(RectTransform screen, TMP_Text style)
        {
            this.screen = screen;
            this.style = style;
        }

        public void Add(RectTransform frame, Highlight shot, TMP_Text tag)
        {
            frames.Add((frame, shot, tag));
            Tag(frames.Count - 1);
        }

        private void Update()
        {
            int over = ForceIndex >= 0 && ForceIndex < frames.Count ? ForceIndex : Pointed();
            if (over != showing)
            {
                showing = over;
                if (over < 0) { if (big != null) big.gameObject.SetActive(false); }
                else Show(frames[over].shot);
            }
            bool click = ForceClick || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            ForceClick = false;
            if (click && over >= 0 && !frames[over].shot.Saved)
            {
                Reel.SaveOne(frames[over].shot);
                Show(frames[over].shot);
                Tag(over);
            }
        }

        private int Pointed()
        {
            if (Mouse.current == null || frames.Count == 0) return -1;
            Vector2 mouse = Mouse.current.position.ReadValue();
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? (canvas.rootCanvas.worldCamera ?? Camera.main) : null;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].frame != null && RectTransformUtility.RectangleContainsScreenPoint(frames[i].frame, mouse, cam)) return i;
            return -1;
        }

        // "SAVED" on a small picture once it is
        private void Tag(int i)
        {
            TMP_Text tag = frames[i].tag;
            if (tag == null) return;
            Transform back = tag.transform.parent;   // (the tag's dark backing)
            back.SetAsLastSibling();   // (over the picture, which is made after it)
            back.gameObject.SetActive(frames[i].shot.Saved);
        }

        private void Show(Highlight shot)
        {
            if (big == null) Build();
            big.gameObject.SetActive(true);
            big.SetAsLastSibling();
            bigPicture.texture = shot.Picture;
            bigBorder.color = Strip.CaptionColor(shot);
            bigCaption.color = Strip.CaptionColor(shot);
            bigCaption.SetText(shot.Caption);
            bigHint.SetText(shot.Saved ? "✓ SAVED" : "CLICK TO SAVE");
            bigHint.color = shot.Saved ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.8f);
        }

        // The big picture: over the middle of the screen, on top of everything, never catching the pointer itself (so pointing away
        // from the small one closes it).
        private void Build()
        {
            float w = Mathf.Min(screen.rect.width * 0.62f, 1400f), h = w * Reel.Height / Reel.Width;

            big = new GameObject(Name, typeof(RectTransform)).GetComponent<RectTransform>();
            big.SetParent(screen, false);
            big.localRotation = Quaternion.identity;
            big.localScale = Vector3.one;
            big.anchorMin = big.anchorMax = new Vector2(0.5f, 0.5f);
            big.sizeDelta = new Vector2(w + 10f, h + 10f);
            big.localPosition = new Vector3(screen.rect.center.x, screen.rect.center.y - h * 0.06f, 0f);
            bigBorder = big.gameObject.AddComponent<Image>();
            bigBorder.raycastTarget = false;

            var pic = new GameObject("Picture", typeof(RectTransform)).GetComponent<RectTransform>();
            pic.SetParent(big, false);
            pic.sizeDelta = new Vector2(w, h);
            bigPicture = pic.gameObject.AddComponent<RawImage>();
            bigPicture.raycastTarget = false;

            // the caption on a dark band along the bottom of the picture, and whether it's saved at the band's right end
            var band = new GameObject("Band", typeof(RectTransform)).GetComponent<RectTransform>();
            band.SetParent(pic, false);
            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(1f, 0f);
            band.pivot = new Vector2(0.5f, 0f);
            band.sizeDelta = new Vector2(0f, style.fontSize * 1.6f);
            band.anchoredPosition = Vector2.zero;
            var shade = band.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.65f);
            shade.raycastTarget = false;

            float y = 0f;
            bigCaption = Strip.Text(band, style, "", style.fontSize, Color.white, 0f, ref y);
            var rt = bigCaption.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            bigCaption.alignment = TextAlignmentOptions.Center;

            y = 0f;
            bigHint = Strip.Text(band, style, "", style.fontSize * 0.7f, Color.white, 0f, ref y);
            var hr = bigHint.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 0.5f);
            hr.sizeDelta = new Vector2(w * 0.4f, hr.sizeDelta.y);
            hr.anchoredPosition = new Vector2(-style.fontSize * 0.8f, 0f);
            bigHint.alignment = TextAlignmentOptions.Right;
        }

        private void OnDestroy()
        {
            if (big != null) Destroy(big.gameObject);
        }
    }
}
