using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HighlightReel
{
    internal enum Kind { Boss, Melee, MultiKill, CloseCall, Revive }

    internal sealed class Highlight
    {
        public RenderTexture Picture;
        public string Caption;
        public Kind Kind;
        public int Priority;   // (bosses first when the reel is full)
        public int Order;
        public bool Saved;
    }

    // This mission's highlights: pictures of the screen a moment after each one, kept on the graphics card (a copy on the GPU, no
    // reading back while you play). The reel keeps a mix: a few of each kind at most, the best overall. It's saved as PNGs once, when
    // the mission's reward screen opens.
    internal sealed class Reel : MonoBehaviour
    {
        public const int Width = 960, Height = 540;

        public static Reel Instance;
        public static bool Testing;   // (the test key's pictures: shown, never saved)
        public static readonly List<Highlight> Shots = new List<Highlight>();
        private static int order;

        // How many of a kind one reel keeps, so six pictures aren't six of the same thing (a horde is one long multi-kill).
        private static int Cap(Kind kind) => kind == Kind.Boss ? 99 : kind == Kind.MultiKill || kind == Kind.Melee ? 2 : 1;

        public static void Clear()
        {
            foreach (Highlight h in Shots) Free(h);
            Shots.Clear();
            order = 0;
        }

        private static void Free(Highlight h)
        {
            if (h.Picture != null) { h.Picture.Release(); Destroy(h.Picture); h.Picture = null; }
        }

        // Take a picture of what's on screen after `delay` seconds (so the kill's effects are in it). `keepIf`, when given, is asked
        // `decideAfter` seconds later: a close call is only one if you're still alive.
        public static Highlight Capture(Kind kind, string caption, int priority, float delay, Func<bool> keepIf = null, float decideAfter = 0f)
        {
            if (Instance == null) return null;
            var h = new Highlight { Kind = kind, Caption = caption, Priority = priority, Order = order++ };
            Instance.StartCoroutine(Instance.Take(h, delay, keepIf, decideAfter));
            return h;
        }

        private IEnumerator Take(Highlight h, float delay, Func<bool> keepIf, float decideAfter)
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

            if (keepIf != null)
            {
                yield return new WaitForSecondsRealtime(decideAfter);
                if (!keepIf()) { Free(h); yield break; }
            }

            Shots.Add(h);
            // a kind over its cap loses its least: the lowest priority, then the oldest
            List<Highlight> same = Shots.Where(s => s.Kind == h.Kind).ToList();
            while (same.Count > Cap(h.Kind))
            {
                Highlight drop = same.OrderBy(s => s.Priority).ThenBy(s => s.Order).First();
                same.Remove(drop); Shots.Remove(drop); Free(drop);
            }
            // a full reel keeps the best overall
            while (Shots.Count > Plugin.MaxShots.Value)
            {
                Highlight worst = Shots.OrderBy(s => s.Priority).ThenBy(s => s.Order).First();
                Shots.Remove(worst);
                Free(worst);
            }
            Plugin.Log.LogInfo($"Highlight Reel: {h.Caption}");
        }

        // A reel made from saved pictures, for showing what a mission's reel looks like: "picture.png|Kind|CAPTION" a line. Never saved.
        public static void LoadSample(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                string[] p = line.Split('|');
                if (p.Length < 3 || !File.Exists(p[0].Trim()) || !Enum.TryParse(p[1].Trim(), out Kind kind)) continue;
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(File.ReadAllBytes(p[0].Trim()))) { Destroy(tex); continue; }
                var h = new Highlight { Kind = kind, Caption = p[2].Trim(), Priority = 50, Order = order++, Saved = true };
                h.Picture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "HighlightReel sample" };
                h.Picture.Create();
                Graphics.Blit(tex, h.Picture);
                Destroy(tex);
                Shots.Add(h);
            }
            Plugin.Log.LogInfo($"Highlight Reel: sample reel of {Shots.Count} saved pictures");
        }

        // The reel as PNGs, once each: read back from the graphics card without waiting on it, written on another thread.
        public static void SaveAll()
        {
            if (Testing || !Plugin.SaveToDisk.Value) return;
            foreach (Highlight h in Shots.Where(s => !s.Saved && s.Picture != null)) { h.Saved = true; Save(h); }
        }

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
