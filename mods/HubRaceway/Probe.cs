using System.Collections;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace HubRaceway
{
    // For laying the track out round the hub: casts a ray straight down through every column of a grid over the hub and writes
    // every surface each one passes (hubraceway-probe.txt: "x z y1 y2 ..." per column, highest first). Only the hub's own
    // colliders are counted, never the raceway's. Debug only (TestFiles).
    internal static class Probe
    {
        // Every material the hub's own renderers use, with its shader, textures and how much of the hub wears it, into
        // hubraceway-materials.txt: for dressing the raceway in the station's own look.
        public static void Materials(Transform raceway)
        {
            var count = new System.Collections.Generic.Dictionary<Material, float>();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(raceway) || !r.enabled) continue;
                float size = r.bounds.size.magnitude;
                foreach (Material m in r.sharedMaterials)
                    if (m != null) count[m] = (count.TryGetValue(m, out float c) ? c : 0f) + size;
            }
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Material, float>>(count);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new StringBuilder();
            foreach (var kv in list)
            {
                Material m = kv.Key;
                sb.Append(kv.Value.ToString("0", CultureInfo.InvariantCulture)).Append(" | ").Append(m.name).Append(" | ").Append(m.shader != null ? m.shader.name : "?");
                Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                if (t != null) sb.Append(" | ").Append(t.name).Append(' ').Append(t.width).Append('x').Append(t.height);
                if (m.HasProperty("_BaseColor")) sb.Append(" | ").Append(m.GetColor("_BaseColor"));
                if (m.IsKeywordEnabled("_EMISSION")) sb.Append(" | emissive");
                sb.AppendLine();
            }
            string file = Path.Combine(Paths.BepInExRootPath, "hubraceway-materials-list.txt");
            File.WriteAllText(file, sb.ToString());
            Plugin.Log.LogInfo("Hub Raceway: " + list.Count + " materials written to " + file);
        }

        // Where the track runs into the hub: a box the size of the road (and the room above it) at every few samples, against
        // every collider on every layer, and against the bounds of every visible part of the hub (which may have no collider).
        // Runs of samples that hit something go to the log, with what they hit and where.
        public static IEnumerator Clearance(Transform raceway, Track track)
        {
            var parts = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(raceway) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                // (the planet, the sky and other huge things far off aren't in the way)
                if (r.bounds.size.magnitude > 400f) continue;
                parts.Add(r);
            }
            var hits = new Collider[64];
            int found = 0;
            string last = null;
            for (int i = 0; i < track.Count; i += 2)
            {
                float height = track.InTunnel(i) ? Track.TubeHeight + 1f : 9f;
                float half = (track.InTunnel(i) ? Track.TubeHalfWidth : Track.HalfWidth) + 1.5f;
                Vector3 center = track.At(i, 0f, height / 2f - 1f);
                var ext = new Vector3(half, height / 2f, track.Step);
                var what = new System.Collections.Generic.List<string>();
                int n = Physics.OverlapBoxNonAlloc(center, ext, hits, track.Facing(i), ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    Collider c = hits[k];
                    if (c.transform.IsChildOf(raceway) || c.attachedRigidbody != null) continue;
                    what.Add("collider " + c.name);
                }
                var box = new Bounds(center, new Vector3(half * 2f, height, half * 2f));
                foreach (Renderer r in parts)
                {
                    if (!r.bounds.Intersects(box)) continue;
                    // (bounds are boxes round the part: only count it if its middle is close, so big hollow things don't)
                    Vector3 local = Quaternion.Inverse(track.Facing(i)) * (r.bounds.ClosestPoint(center) - center);
                    if (Mathf.Abs(local.x) < half && local.y > -height / 2f && local.y < height / 2f) what.Add("mesh " + r.name);
                }
                string line = what.Count == 0 ? null : string.Join(", ", what.Distinct().Take(6));
                if (line != null && line != last)
                {
                    Vector3 p = track.Pos[i];
                    Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "Hub Raceway clearance: sample {0} ({1:0.000} round) at {2:0},{3:0},{4:0}: {5}",
                        i, i / (float)track.Count, p.x, p.y, p.z, line));
                    found++;
                }
                last = line;
                if (i % 40 == 0) yield return null;
            }
            Plugin.Log.LogInfo("Hub Raceway clearance: done, " + found + " places");
            // and how big each thing in the way is
            var named = new System.Collections.Generic.HashSet<string> { "Thrusters", "Front", "polySurface13", "CableCarRails", "Krask", "ring3", "ring4", "Atmos" };
            foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (named.Contains(c.name) && !c.transform.IsChildOf(raceway))
                    Plugin.Log.LogInfo("Hub Raceway clearance: collider " + c.name + " (" + c.GetType().Name + ", layer " + c.gameObject.layer + ") " + c.bounds.min.ToString("0") + " to " + c.bounds.max.ToString("0") + " under " + (c.transform.parent != null ? c.transform.parent.name : "-"));
            foreach (Renderer r in parts)
                if (named.Contains(r.name))
                    Plugin.Log.LogInfo("Hub Raceway clearance: mesh " + r.name + " " + r.bounds.min.ToString("0") + " to " + r.bounds.max.ToString("0") + " under " + (r.transform.parent != null ? r.transform.parent.name : "-"));
        }

        // The render pipeline's extra passes (its renderer features), found by reflection: listed with `log`, or switched off
        // and on (by name) round our photos.
        private static System.Collections.Generic.List<Object> Features()
        {
            var list = new System.Collections.Generic.List<Object>();
            object asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (asset == null) return list;
            var field = asset.GetType().GetField("m_RendererDataList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (!(field?.GetValue(asset) is System.Array datas)) return list;
            foreach (object data in datas)
            {
                if (data == null) continue;
                var prop = data.GetType().GetProperty("rendererFeatures");
                if (prop?.GetValue(data) is System.Collections.IEnumerable features)
                    foreach (object f in features) if (f is Object o && o != null) list.Add(o);
            }
            return list;
        }

        private static bool IsActive(Object f) => (bool)(f.GetType().GetProperty("isActive")?.GetValue(f) ?? true);
        private static void SetActive(Object f, bool on) => f.GetType().GetMethod("SetActive")?.Invoke(f, new object[] { on });

        public static void LogFeatures()
        {
            foreach (Object f in Features())
                Plugin.Log.LogInfo("Hub Raceway: render feature " + f.name + " (" + f.GetType().Name + ") " + (IsActive(f) ? "on" : "off"));
        }

        // A camera rendered once, with these passes (by name) switched off for it.
        private static System.Collections.Generic.List<Object> cachedFeatures;
        public static void RenderWithout(Camera cam, string[] passes)
        {
            if (cachedFeatures == null) cachedFeatures = Features();
            var hidden = new System.Collections.Generic.List<Object>();
            foreach (Object f in cachedFeatures)
                if (f != null && IsActive(f) && passes.Any(h => f.name == h || f.GetType().Name == h)) { SetActive(f, false); hidden.Add(f); }
            try { cam.Render(); }
            finally { foreach (Object f in hidden) SetActive(f, true); }
        }

        // The features to leave out of photos (the HUD's), named in the first line "# hide Name1,Name2" of the photo file.
        public static string[] HideInPhotos = new string[0];

        // Pictures for the README: each line of hubraceway-photo.txt is "name x y z yaw pitch [fov]" (world metres, degrees); a
        // camera of our own renders each at 1920x1080 without the HUD (the UI layers left out) into BepInEx\hubraceway-photos.
        public static IEnumerator Photos(string[] lines)
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "hubraceway-photos");
            Directory.CreateDirectory(dir);
            var go = new GameObject("HubRacewayPhoto");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 6000f;
            cam.cullingMask = ~((1 << 5) | (1 << 6) | (1 << 10));
            Camera main = Camera.main;
            if (main != null) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            foreach (string raw in lines)
            {
                if (raw.StartsWith("# hide ")) { HideInPhotos = raw.Substring(7).Split(','); continue; }
                string[] p = raw.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 6 || p[0].StartsWith("#")) continue;
                float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                go.transform.SetPositionAndRotation(new Vector3(F(1), F(2), F(3)), Quaternion.Euler(F(5), F(4), 0f));
                cam.fieldOfView = p.Length > 6 ? F(6) : 70f;
                // (a frame first, so the animated parts and the portal views are up to date)
                yield return new WaitForEndOfFrame();
                var hidden = new System.Collections.Generic.List<Object>();
                foreach (Object f in Features())
                    if (IsActive(f) && HideInPhotos.Any(h => h.Length > 0 && (f.name.Contains(h) || f.GetType().Name.Contains(h)))) { SetActive(f, false); hidden.Add(f); }
                try { cam.Render(); }
                finally { foreach (Object f in hidden) SetActive(f, true); }
                RenderTexture before = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply(false);
                RenderTexture.active = before;
                File.WriteAllBytes(Path.Combine(dir, p[0] + ".png"), tex.EncodeToPNG());
                Plugin.Log.LogInfo("Hub Raceway: photo " + p[0]);
            }
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(tex);
            Object.Destroy(go);
            Plugin.Log.LogInfo("Hub Raceway: photos done");
        }

        public static IEnumerator Run(Transform raceway, float minX, float maxX, float minZ, float maxZ, float step)
        {
            string file = Path.Combine(Paths.BepInExRootPath, "hubraceway-probe.txt");
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "# grid {0} {1} {2} {3} {4}", minX, maxX, minZ, maxZ, step));
            int rays = 0;
            var hits = new RaycastHit[64];
            for (float x = minX; x <= maxX; x += step)
            {
                for (float z = minZ; z <= maxZ; z += step)
                {
                    // stepping down through every surface (one cast only finds one hit on each collider, and a big mesh
                    // collider has many), its undersides too, on every layer
                    var ys = new System.Collections.Generic.List<float>();
                    float from = 800f;
                    bool backfaces = Physics.queriesHitBackfaces;
                    Physics.queriesHitBackfaces = true;
                    for (int g = 0; g < 60; g++)
                    {
                        if (!Physics.Raycast(new Vector3(x, from, z), Vector3.down, out RaycastHit hit, from + 400f, ~0, QueryTriggerInteraction.Ignore)) break;
                        if ((raceway == null || !hit.collider.transform.IsChildOf(raceway)) && hit.collider.attachedRigidbody == null) ys.Add(hit.point.y);
                        from = hit.point.y - 0.05f;
                    }
                    Physics.queriesHitBackfaces = backfaces;
                    if (ys.Count == 0) continue;
                    ys.Sort((a, b) => b.CompareTo(a));
                    sb.Append(x.ToString("0", CultureInfo.InvariantCulture)).Append(' ').Append(z.ToString("0", CultureInfo.InvariantCulture));
                    foreach (float y in ys) sb.Append(' ').Append(y.ToString("0.0", CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
                rays++;
                if (rays % 20 == 0) yield return null;
            }
            File.WriteAllText(file, sb.ToString());
            Plugin.Log.LogInfo("Hub Raceway: probe written to " + file);
        }
    }
}
