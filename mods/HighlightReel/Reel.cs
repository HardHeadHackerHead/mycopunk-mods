using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HighlightReel
{
    internal sealed class Highlight
    {
        public RenderTexture Picture;
        public string Caption;
        public int Priority;   // (bosses first when the reel is full)
        public int Order;
        public bool Cancelled;   // (replaced before its picture was in: dropped when it is)
    }

    // This mission's highlights: pictures of the screen a moment after each one, kept on the graphics card (a copy to the GPU,
    // no reading back while you play), and saved as PNGs in the background if SaveToDisk is on.
    internal sealed class Reel : MonoBehaviour
    {
        public const int Width = 960, Height = 540;

        public static Reel Instance;
        public static bool Testing;   // (the test key's pictures: shown, never saved)
        public static readonly List<Highlight> Shots = new List<Highlight>();
        private static int order;

        public static void Clear()
        {
            foreach (Highlight h in Shots) if (h.Picture != null) { h.Picture.Release(); Destroy(h.Picture); }
            Shots.Clear();
            order = 0;
        }

        // Take a picture of what's on screen after `delay` seconds (so the kill's effects are in it). `replaces` is an earlier shot
        // to drop for this one (a TRIPLE KILL that became a QUAD KILL).
        // `keepIf`, when given, is asked `decideAfter` seconds later: a close call is only one if you're still alive.
        public static Highlight Capture(string caption, int priority, float delay, Highlight replaces = null, Func<bool> keepIf = null, float decideAfter = 0f)
        {
            if (Instance == null) return null;
            var h = new Highlight { Caption = caption, Priority = priority, Order = order++ };
            if (replaces != null) replaces.Cancelled = true;
            Instance.StartCoroutine(Instance.Take(h, delay, replaces, keepIf, decideAfter));
            return h;
        }

        private IEnumerator Take(Highlight h, float delay, Highlight replaces, Func<bool> keepIf, float decideAfter)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            yield return new WaitForEndOfFrame();
            RenderTexture full = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                h.Picture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "HighlightReel " + h.Caption };
                h.Picture.Create();
                // (the capture comes upside down where the graphics API starts textures at the top, as DirectX does)
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, h.Picture, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(full, h.Picture);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Highlight Reel: couldn't take the picture: " + e.Message); yield break; }
            finally { RenderTexture.ReleaseTemporary(full); }

            if (h.Cancelled) { h.Picture.Release(); Destroy(h.Picture); yield break; }
            if (keepIf != null)
            {
                yield return new WaitForSecondsRealtime(decideAfter);
                if (!keepIf()) { h.Picture.Release(); Destroy(h.Picture); yield break; }
            }

            if (replaces != null && Shots.Remove(replaces) && replaces.Picture != null) { replaces.Picture.Release(); Destroy(replaces.Picture); }
            Shots.Add(h);
            // a full reel keeps the best: drop the lowest priority (the oldest of those)
            while (Shots.Count > Plugin.MaxShots.Value)
            {
                Highlight worst = Shots.OrderBy(s => s.Priority).ThenBy(s => s.Order).First();
                Shots.Remove(worst);
                if (worst.Picture != null) { worst.Picture.Release(); Destroy(worst.Picture); }
            }
            Plugin.Log.LogInfo($"Highlight Reel: {h.Caption}");
            if (Plugin.SaveToDisk.Value && !Testing) Save(h);
        }

        // The PNG, read back from the graphics card without waiting on it, written on another thread.
        private static void Save(Highlight h)
        {
            string caption = h.Caption;
            AsyncGPUReadback.Request(h.Picture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                try
                {
                    byte[] png = ImageConversion.EncodeNativeArrayToPNG(request.GetData<byte>(), UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, Width, Height).ToArray();
                    string folder = Plugin.SaveFolder;
                    string name = $"{DateTime.Now:yyyy-MM-dd HH.mm.ss} {Safe(caption)}.png";
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name), png); }
                        catch (Exception e) { Plugin.Log.LogWarning("Highlight Reel: couldn't save " + name + ": " + e.Message); }
                    });
                }
                catch (Exception e) { Plugin.Log.LogWarning("Highlight Reel: couldn't save the picture: " + e.Message); }
            });
        }

        private static string Safe(string s) => new string(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
