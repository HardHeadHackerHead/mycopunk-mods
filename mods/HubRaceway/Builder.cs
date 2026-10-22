using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HubRaceway
{
    // Builds what you see and drive on, all in code: the road, its walls and lamps, the light tube you can drive round the inside of, the
    // corner signs, the start gantry with its lights and the grid, the paddock, the pads, boards and portals. Small parts are
    // merged into a few meshes (one per material) so hundreds of lamps cost little. Everything it makes (meshes, materials,
    // textures) is kept in Owned so the raceway can destroy it all when it goes (leaving the hub, or the mod reloading).
    internal sealed class Builder
    {
        public const float WallHeight = 1.6f;
        public const float WallThickness = 0.8f;
        public const float SlabDepth = 1.5f;
        // the invisible fence above the walls, so a car going over the top bounces back instead of into space
        public const float FenceHeight = 6f;
        public const float PaddockDepth = 44f;
        public const int GridSlots = 8;

        public static readonly Color Orange = new Color(1f, 0.36f, 0.12f);
        public static readonly Color Cyan = new Color(0.15f, 0.85f, 1f);
        public static readonly Color Magenta = new Color(1f, 0.2f, 0.75f);

        public readonly List<Object> Owned = new List<Object>();
        private readonly Transform root;
        private readonly Track track;
        private Shader shader;
        public TMP_FontAsset Font { get; private set; }

        public Material Road, Concrete, WallFace, Dark, Metal, Glow, EdgeGlow, Lamp, Checker, Paint, Chevron, BoostPad, ItemBox, Oil;
        // the hub's own materials where it has them (its painted panels, red pipes, floor tiles and light strips), so the
        // raceway looks part of the station; our own where it doesn't
        public Material Pipe, Rust, Tiles, HubGlow;
        // the tunnel's light ribs, in three groups whose brightness chases along; the start lights, left to right
        public readonly Material[] Chase = new Material[3];
        public readonly Material[] StartLights = new Material[4];

        // the stretch of the road (sample indexes) with the paddock beside it: no wall on that side there
        public int PaddockFrom, PaddockTo;
        public int TunnelFrom => track.TunnelFrom;
        public int TunnelTo => track.TunnelTo;
        private float roadTile;
        public Vector3 PaddockCenter, PaddockAcross, PaddockForward;

        // merged meshes waiting to be made, one per material
        private readonly Dictionary<Material, MeshData> batches = new Dictionary<Material, MeshData>();
        private readonly Dictionary<Material, MeshData> solidBatches = new Dictionary<Material, MeshData>();
        private readonly Dictionary<PrimitiveType, GameObject> primitives = new Dictionary<PrimitiveType, GameObject>();

        public Builder(Transform root, Track track)
        {
            this.root = root;
            this.track = track;
            PaddockTo = track.Count - 10;
            PaddockFrom = PaddockTo - Mathf.RoundToInt(80f / track.Step);
        }

        public bool InTunnel(int i) => track.InTunnel(i);

        public void MakeMaterials()
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                // (not in this build: borrow the shader of something in the hub)
                var any = Object.FindFirstObjectByType<MeshRenderer>();
                if (any != null && any.sharedMaterial != null) shader = any.sharedMaterial.shader;
            }
            foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.font != null) { Font = t.font; break; }
            }

            Road = MakeMaterial(Color.white, RoadTexture(), 0.35f);
            Concrete = MakeMaterial(new Color(0.8f, 0.8f, 0.78f), null, 0.15f);
            WallFace = MakeMaterial(Color.white, WallTexture(), 0.2f);
            Dark = MakeMaterial(new Color(0.12f, 0.11f, 0.13f), null, 0.2f);
            Metal = MakeMaterial(new Color(0.32f, 0.33f, 0.36f), null, 0.55f);
            Glow = MakeMaterial(Orange, null, 0.5f, Orange * 1.1f);
            EdgeGlow = MakeMaterial(Cyan, null, 0.5f, Cyan * 1.3f);
            Lamp = MakeMaterial(new Color(1f, 0.92f, 0.75f), null, 0.5f, new Color(1f, 0.85f, 0.6f) * 1.6f);
            Checker = MakeMaterial(Color.white, CheckerTexture(), 0.2f);
            Paint = MakeMaterial(new Color(0.95f, 0.95f, 0.95f), null, 0.2f, new Color(0.5f, 0.5f, 0.5f));
            Chevron = MakeMaterial(Color.white, ChevronTexture(), 0.3f, Color.white * 0.9f, emissionMap: true);
            BoostPad = MakeMaterial(Color.white, BoostTexture(), 0.4f, Color.white * 1.4f, emissionMap: true);
            ItemBox = MakeMaterial(Magenta, null, 0.8f, Magenta);
            Color[] chase = { Cyan, Magenta, Orange };
            for (int i = 0; i < 3; i++) Chase[i] = MakeMaterial(chase[i], null, 0.5f, chase[i]);
            for (int i = 0; i < 4; i++) StartLights[i] = MakeMaterial(new Color(0.08f, 0.08f, 0.08f), null, 0.7f, Color.black);
            Oil = MakeMaterial(new Color(0.05f, 0.03f, 0.09f), null, 0.95f);

            // dressed in the hub's own (shared, never changed: only these and our own are ever animated)
            var hub = new Dictionary<string, Material>();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && !hub.ContainsKey(m.name)) hub[m.name] = m;
            Material Hub(string name, Material fallback) => hub.TryGetValue(name, out Material m) ? m : fallback;
            Pipe = Hub("HubPipeRed", MakeMaterial(new Color(0.7f, 0.15f, 0.12f), null, 0.3f));
            Rust = Hub("HubPipeRusty", Metal);
            Tiles = Hub("ReactorTile2 Mat", Concrete);
            HubGlow = Hub("HubLight", Glow);
            WallFace = Hub("HubPanel", WallFace);
            Concrete = Hub("HubPanel Dark", Concrete);
            Dark = Hub("HubPanel Darker", Dark);
            Plugin.Log.LogInfo("Hub Raceway: dressed in the hub's materials: " + (hub.ContainsKey("HubPanel") ? "yes" : "no (our own)"));
        }

        public Material MakeMaterial(Color color, Texture2D texture, float smoothness, Color? emission = null, bool emissionMap = false)
        {
            var m = new Material(shader);
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            if (texture != null)
            {
                m.SetTexture("_BaseMap", texture);
                m.SetTexture("_MainTex", texture);
            }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionMap && texture != null) m.SetTexture("_EmissionMap", texture);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            Owned.Add(m);
            return m;
        }

        public static void SetEmission(Material m, Color c) => m.SetColor("_EmissionColor", c);

        private Texture2D NewTexture(int w, int h, bool mips)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 8;
            Owned.Add(tex);
            return tex;
        }

        // Dark asphalt with a dashed centre line, lane marks, and red-and-white kerbs at both edges; one tile is 16 m of road.
        private Texture2D RoadTexture()
        {
            const int w = 128, h = 256;
            var tex = NewTexture(w, h, true);
            var px = new Color32[w * h];
            var rng = new System.Random(7);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    if (x < 5 || x >= w - 5) c = (y / 32) % 2 == 0 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.92f, 0.92f, 0.9f);
                    else if (x == 6 || x == w - 7) c = new Color(0.9f, 0.9f, 0.88f);
                    else if ((x == w / 2 - 1 || x == w / 2) && (y % 64) < 32) c = new Color(0.95f, 0.8f, 0.3f);
                    else if ((x == w / 4 || x == w * 3 / 4) && (y % 64) < 16) c = new Color(0.55f, 0.55f, 0.55f);
                    else
                    {
                        float g = 0.15f + (float)rng.NextDouble() * 0.05f;
                        // a faint scuffed racing line down the middle
                        if (x > w * 0.3f && x < w * 0.7f) g -= 0.02f;
                        c = new Color(g, g, g * 1.05f);
                    }
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // The walls' faces: light panels with dark seams, a red band along the bottom (u = up the wall, v = along it, 6 m a tile).
        private Texture2D WallTexture()
        {
            const int w = 32, h = 64;
            var tex = NewTexture(w, h, true);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = new Color(0.86f, 0.86f, 0.84f);
                    if (x < 10) c = new Color(0.78f, 0.13f, 0.1f);
                    if (x == 10 || x == w - 2) c = new Color(0.25f, 0.25f, 0.27f);
                    if (y == 0 || y == h - 1) c = new Color(0.35f, 0.35f, 0.37f);
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private Texture2D CheckerTexture()
        {
            var tex = NewTexture(2, 2, false);
            tex.SetPixels32(new Color32[] { new Color32(20, 20, 20, 255), new Color32(240, 240, 240, 255), new Color32(240, 240, 240, 255), new Color32(20, 20, 20, 255) });
            tex.filterMode = FilterMode.Point;
            tex.Apply(false);
            return tex;
        }

        // Orange arrows on black, pointing right (+u), for the corner boards.
        private Texture2D ChevronTexture()
        {
            const int w = 64, h = 32;
            var tex = NewTexture(w, h, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fy = Mathf.Abs(y - h / 2f + 0.5f);
                    // three chevrons: the band where (x - |y|) falls in each arrow's stroke
                    float band = Mathf.Repeat(x - fy, 21.3f);
                    bool arrow = band < 8f && y > 3 && y < h - 4;
                    bool border = y < 2 || y >= h - 2;
                    px[y * w + x] = arrow ? (Color32)Orange : border ? new Color32(230, 230, 230, 255) : new Color32(12, 12, 14, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // Bright arrows pointing forward (+v) on a cyan field, for the boost pads (scrolled as they glow).
        private Texture2D BoostTexture()
        {
            const int w = 64, h = 64;
            var tex = NewTexture(w, h, true);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float fx = Mathf.Abs(x - w / 2f + 0.5f);
                    float band = Mathf.Repeat(y + fx * 0.9f, 32f);
                    bool arrow = band < 11f && fx < 26f;
                    px[y * w + x] = arrow ? new Color32(255, 255, 255, 255) : new Color32(10, 120, 170, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ---------------------------------------------------------------- the track

        public void BuildTrack()
        {
            float w = Track.HalfWidth, t = WallThickness, h = WallHeight, d = SlabDepth;
            int n = track.Count;
            roadTile = track.Length / Mathf.Round(track.Length / 16f);   // so the texture meets itself at the start line

            // the road surface, and the slab under it (the walls' outer faces close its sides); in the tube, its floor is the road
            var road = new MeshData();
            var slab = new MeshData();
            var edges = Batch(EdgeGlow);
            foreach ((int from, int to) in OutsideTube())
            {
                road.Strip(track, new Vector2(-w, 0f), new Vector2(w, 0f), 0f, 1f, 1f / roadTile, from, to);
                slab.Strip(track, new Vector2(w + t, -d), new Vector2(-w - t, -d), 0f, 1f, 0.1f, from, to);
                // glowing lines just inside both edges
                edges.Strip(track, new Vector2(-w + 0.6f, 0.025f), new Vector2(-w + 0.9f, 0.025f), 0f, 1f, 0.1f, from, to);
                edges.Strip(track, new Vector2(w - 0.9f, 0.025f), new Vector2(w - 0.6f, 0.025f), 0f, 1f, 0.1f, from, to);
            }
            Spawn("Road", road, Road, collide: true, SurfaceMaterial.Stone);
            slab.Strip(track, new Vector2(w, 0f), new Vector2(w, -d), 0f, 1f, 0.1f, PaddockFrom, PaddockTo, when: Track.PaddockSide > 0);
            slab.Strip(track, new Vector2(-w, -d), new Vector2(-w, 0f), 0f, 1f, 0.1f, PaddockFrom, PaddockTo, when: Track.PaddockSide < 0);
            Spawn("Underside", slab, Dark, collide: true, SurfaceMaterial.Metal);

            // the walls (with a gap by the paddock, none in the tube), the glowing strip on top, the invisible fence above
            var faces = new MeshData();
            var walls = new MeshData();
            var glow = Batch(HubGlow);
            var fence = new MeshData();
            foreach (int side in new[] { -1, 1 })
            {
                foreach ((int from, int to) in WallRuns(side))
                {
                    float s = side;
                    // inner face (u up the wall), top, outer face (each strip faces Cross(forward, b - a))
                    if (side < 0) faces.Strip(track, new Vector2(-w, h), new Vector2(-w, 0f), 1f, 0f, 1f / 6f, from, to);
                    else faces.Strip(track, new Vector2(w, 0f), new Vector2(w, h), 0f, 1f, 1f / 6f, from, to);
                    walls.Strip(track, new Vector2(s * (side < 0 ? w + t : w), h), new Vector2(s * (side < 0 ? w : w + t), h), 0f, 1f, 0.1f, from, to);
                    walls.Strip(track, new Vector2(s * (w + t), side < 0 ? -d : h), new Vector2(s * (w + t), side < 0 ? h : -d), 0f, 1f, 0.1f, from, to);
                    float g0 = s * (w + t * 0.5f - 0.18f), g1 = s * (w + t * 0.5f + 0.18f);
                    glow.Strip(track, new Vector2(Mathf.Min(g0, g1), h + 0.04f), new Vector2(Mathf.Max(g0, g1), h + 0.04f), 0f, 1f, 0.1f, from, to);
                    fence.Strip(track, new Vector2(s * w, h), new Vector2(s * w, h + FenceHeight), 0f, 1f, 0.1f, from, to);
                    fence.Strip(track, new Vector2(s * w, h + FenceHeight), new Vector2(s * w, h), 0f, 1f, 0.1f, from, to);
                }
            }
            Spawn("WallFaces", faces, WallFace, collide: true, SurfaceMaterial.Metal);
            Spawn("Walls", walls, Concrete, collide: true, SurfaceMaterial.Metal);
            Spawn("Fence", fence, null, collide: true, SurfaceMaterial.Metal);

            // the start/finish line: 1 m checks across the road
            var line = new MeshData();
            int lineEnd = Mathf.Max(1, Mathf.RoundToInt(4f / track.Step));
            line.Strip(track, new Vector2(-w, 0.03f), new Vector2(w, 0.03f), 0f, w, 1f / 2f, 0, lineEnd);
            Spawn("StartLine", line, Checker, collide: false, SurfaceMaterial.Stone);

            BuildTunnel();
            BuildLamps();
            BuildCornerSigns();
            BuildGrid();
            BuildGantry();
            BuildPaddock();
            FlushBatches();
        }

        // The stretches of road outside the tube.
        private IEnumerable<(int, int)> OutsideTube()
        {
            if (TunnelFrom < 0) { yield return (0, track.Count); yield break; }
            if (TunnelFrom > 0) yield return (0, TunnelFrom);
            if (TunnelTo < track.Count) yield return (TunnelTo, track.Count);
        }

        // The stretches of one side that have a wall: not the tube, not beside the paddock.
        private IEnumerable<(int, int)> WallRuns(int side)
        {
            var cuts = new List<(int, int)>();
            if (TunnelFrom >= 0) cuts.Add((TunnelFrom, TunnelTo));
            if (side == Track.PaddockSide) cuts.Add((PaddockFrom, PaddockTo));
            cuts.Sort();
            int at = 0;
            foreach ((int a, int b) in cuts)
            {
                if (a > at) yield return (at, a);
                at = Mathf.Max(at, b);
            }
            if (at < track.Count) yield return (at, track.Count);
        }

        // The tube: a rounded box round the road (see Track.TubeRound), its floor the road, its walls and ceiling drivable, dark
        // inside and ringed with lights that chase along it, metal outside, a glowing frame at each mouth.
        private void BuildTunnel()
        {
            int from = TunnelFrom, to = TunnelTo;
            if (from < 0) return;
            float W = Track.TubeHalfWidth, H = Track.TubeHeight, shell = 0.8f;
            int count = to - from + 1;
            var inner = new List<Vector2>[count];
            var outer = new List<Vector2>[count];
            var lineL = new List<Vector2>[count];
            var lineR = new List<Vector2>[count];
            for (int j = 0; j < count; j++)
            {
                float r = track.TubeRound(from + j), fx = W - Mathf.Max(r, 0.6f);
                inner[j] = Rounded(W, 0f, H, r);
                outer[j] = Rounded(W + shell, -shell, H + shell, r + shell);
                lineL[j] = new List<Vector2> { new Vector2(-fx + 0.25f, 0.03f), new Vector2(-fx + 0.55f, 0.03f) };
                lineR[j] = new List<Vector2> { new Vector2(fx - 0.55f, 0.03f), new Vector2(fx - 0.25f, 0.03f) };
            }
            int m = inner[0].Count;
            var floor = new MeshData();
            var walls = new MeshData();
            var skin = new MeshData();
            // (the outline goes floor, right wall, ceiling, left wall: each piece faces in; backwards, the skin faces out)
            floor.Sweep(track, inner, 0, 1, from, 0f, 1f, 1f / roadTile);
            for (int k = 1; k < m - 1; k++) walls.Sweep(track, inner, k, k + 1, from, 0f, 1f, 0.1f);
            for (int k = 0; k < m - 1; k++) skin.Sweep(track, outer, k + 1, k, from, 0f, 1f, 0.1f);
            Spawn("TubeFloor", floor, Road, collide: true, SurfaceMaterial.Stone);
            Spawn("TubeInside", walls, Dark, collide: true, SurfaceMaterial.Metal);
            Spawn("TubeOutside", skin, Rust, collide: false, SurfaceMaterial.Metal);
            Batch(EdgeGlow).Sweep(track, lineL, 0, 1, from, 0f, 1f, 0.1f);
            Batch(EdgeGlow).Sweep(track, lineR, 0, 1, from, 0f, 1f, 0.1f);

            // light rings every 8 m round the inside, in three groups that chase along; a bright frame round each mouth
            int every = Mathf.Max(1, Mathf.RoundToInt(8f / track.Step));
            int ring = 0;
            for (int i = from + every / 2; i < to; i += every, ring++)
            {
                float r = track.TubeRound(i);
                Ring(Batch(Chase[ring % 3]), i, Rounded(W - 0.03f, 0.03f, H - 0.03f, r), Rounded(W - 0.18f, 0.18f, H - 0.18f, Mathf.Max(r - 0.15f, 0f)), 0.7f);
            }
            foreach (int i in new[] { from, to - 1 })
                Ring(Batch(HubGlow), i, Rounded(W + shell + 1.2f, -shell - 1.2f, H + shell + 1.2f, 0f), Rounded(W - 0.3f, 0.3f, H - 0.3f, 0f), 1.6f);
        }

        // A rounded box's outline (across, up), all the way round: the floor from left to right, up the right wall, the ceiling
        // from right to left, down the left wall, back to the start; always the same number of points.
        private static List<Vector2> Rounded(float w, float y0, float y1, float r)
        {
            const int arc = 6;
            r = Mathf.Clamp(r, 0.001f, Mathf.Min(w, (y1 - y0) / 2f) - 0.001f);
            float fx = w - r;
            var pts = new List<Vector2> { new Vector2(-fx, y0) };
            void Arc(Vector2 c, float a0, float a1)
            {
                for (int k = 0; k <= arc; k++)
                {
                    float a = Mathf.Lerp(a0, a1, k / (float)arc) * Mathf.Deg2Rad;
                    pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            Arc(new Vector2(fx, y0 + r), -90f, 0f);
            Arc(new Vector2(fx, y1 - r), 0f, 90f);
            Arc(new Vector2(-fx, y1 - r), 90f, 180f);
            Arc(new Vector2(-fx, y0 + r), 180f, 270f);
            return pts;
        }

        // A band round the tube at a sample, between two outlines (`a` against the wall, `b` toward the middle), `width` along
        // the road; never across the floor (the cars drive there).
        private void Ring(MeshData m, int i, List<Vector2> a, List<Vector2> b, float width)
        {
            Vector3 f = track.Fwd[track.Wrap(i)] * (width / 2f);
            for (int k = 1; k < a.Count - 1; k++)
            {
                Vector3 p0 = track.At(i, a[k].x, a[k].y), p1 = track.At(i, a[k + 1].x, a[k + 1].y);
                Vector3 i0 = track.At(i, b[k].x, b[k].y), i1 = track.At(i, b[k + 1].x, b[k + 1].y);
                m.Quad(i0 + f, i1 + f, i1 - f, i0 - f);   // facing the middle
                m.Quad(p0 - f, p1 - f, p1 + f, p0 + f);   // facing out
                m.Quad(i0 - f, i1 - f, p1 - f, p0 - f);   // facing back along the road
                m.Quad(p0 + f, p1 + f, i1 + f, i0 + f);   // facing ahead
            }
        }

        // Lamp posts on the walls every 48 m, swapping sides, leaning over the road.
        private void BuildLamps()
        {
            int every = Mathf.RoundToInt(48f / track.Step);
            var poles = Batch(Pipe);
            var heads = Batch(Lamp);
            int k = 0;
            for (int i = every / 2; i < track.Count; i += every, k++)
            {
                if (InTunnel(i) || (i >= PaddockFrom - 4 && i <= PaddockTo + 4) || i < 10 || i > track.Count - 10) continue;
                float s = (k % 2 == 0) ? 1f : -1f;
                float x = s * (Track.HalfWidth + WallThickness / 2f);
                Quaternion rot = track.Facing(i);
                Vector3 across = track.Right[i] * -s;
                poles.Box(track.At(i, x, WallHeight + 4f), rot, new Vector3(0.35f, 8f, 0.35f));
                poles.Box(track.At(i, x, WallHeight + 8f) + across * 1.6f, rot, new Vector3(3.6f, 0.3f, 0.35f));
                heads.Box(track.At(i, x, WallHeight + 7.75f) + across * 3f, rot, new Vector3(1.5f, 0.25f, 0.7f));
            }
        }

        // Black-and-orange arrow boards along the outside of the sharp corners, pointing the way you drive.
        private void BuildCornerSigns()
        {
            var boards = Batch(Chevron);
            var backs = Batch(Dark);
            int every = Mathf.RoundToInt(14f / track.Step);
            for (int i = 0; i < track.Count; i += every)
            {
                float turn = track.Turn[i];
                if (Mathf.Abs(turn) < 0.75f || InTunnel(i) || (i >= PaddockFrom && i <= PaddockTo)) continue;
                int outside = turn > 0f ? -1 : 1;
                float x = outside * (Track.HalfWidth + WallThickness / 2f);
                Vector3 c = track.At(i, x - outside * 0.3f, WallHeight + 1.3f);
                Vector3 fwd = track.Fwd[i], up = track.Up[i];
                // facing the road: on the left wall screen-right is ahead, on the right wall it's behind (so flip the arrows)
                boards.Quad(c, outside < 0 ? fwd : -fwd, up, 4f, 1.8f, flipU: outside > 0);
                backs.Box(track.At(i, x, WallHeight + 1.3f), track.Facing(i), new Vector3(0.4f, 2f, 4.2f));
            }
        }

        // Where each grid slot is: staggered pairs behind the line.
        public Vector3 GridSlot(int slot, out Quaternion facing, out int sample)
        {
            int row = slot / 2, col = slot % 2;
            float back = 12f + row * 10f + col * 5f;
            sample = track.Wrap(-Mathf.RoundToInt(back / track.Step));
            facing = track.Facing(sample);
            return track.At(sample, col == 0 ? -5.5f : 5.5f, 0f);
        }

        private void BuildGrid()
        {
            var paint = Batch(Paint);
            for (int slot = 0; slot < GridSlots; slot++)
            {
                Vector3 c = GridSlot(slot, out Quaternion rot, out int s);
                Vector3 up = track.Up[s] * 0.035f, r = track.Right[s], f = track.Fwd[s];
                // a box outline, open at the back, 6 m wide, the slot's number in it
                paint.Quad(c + up + f * 2.5f, r, f, 6f, 0.3f, flipU: false);
                paint.Quad(c + up + r * 3f, r, f, 0.3f, 5f, flipU: false);
                paint.Quad(c + up - r * 3f, r, f, 0.3f, 5f, flipU: false);
                var label = Text((slot + 1).ToString(), c + track.Up[s] * 0.05f - f * 0.5f, Quaternion.LookRotation(track.Up[s], f), 2.2f);
                label.GetComponent<TextMeshPro>().color = new Color(1f, 1f, 1f, 0.85f);
            }
        }

        // Two red pillars and a beam over the start line, the mod's name on the beam both ways, four start lights facing the grid.
        private void BuildGantry()
        {
            float w = Track.HalfWidth + WallThickness + 1.5f;
            Quaternion facing = track.Facing(0);
            var dark = Batch(Dark);
            var pipe = Batch(Pipe);
            var glow = Batch(HubGlow);
            for (int s = -1; s <= 1; s += 2)
            {
                pipe.Box(track.At(0, s * w, 7.5f), facing, new Vector3(2.2f, 15f, 2.2f));
                glow.Box(track.At(0, s * (w - 1.15f), 7.5f), facing, new Vector3(0.12f, 13f, 0.5f));
            }
            dark.Box(track.At(0, 0f, 14f), facing, new Vector3(w * 2f + 2.2f, 3f, 2.2f));
            glow.Box(track.At(0, 0f, 12.4f), facing, new Vector3(w * 2f, 0.25f, 2.3f));
            // (Text's rotation faces the reader: the side you drive up to faces back along the road)
            Text("QUAD'S HUB RACEWAY", track.At(0, 0f, 14.3f) - track.Fwd[0] * 1.15f, facing * Quaternion.Euler(0f, 180f, 0f), 2f);
            Text("QUAD'S HUB RACEWAY", track.At(0, 0f, 14f) + track.Fwd[0] * 1.15f, facing, 2.2f);
            // the lights hang under the beam, facing the grid
            for (int k = 0; k < 4; k++)
            {
                Vector3 at = track.At(0, (k - 1.5f) * 3.2f, 10.2f) - track.Fwd[0] * 0.45f;
                dark.Box(at + track.Fwd[0] * 0.35f, facing, new Vector3(2.6f, 2.6f, 0.6f));
                var lamp = Copy(PrimitiveType.Cylinder, "StartLight");
                lamp.transform.SetPositionAndRotation(at, facing * Quaternion.Euler(90f, 0f, 0f));
                lamp.transform.localScale = new Vector3(2f, 0.2f, 2f);
                lamp.GetComponent<MeshRenderer>().sharedMaterial = StartLights[k];
            }
        }

        public const float DaisHeight = 0.4f;
        private float paddockLength;

        // The paddock beside the straight before the line (on Track.PaddockSide), where you arrive: an open launch deck with
        // the stars all round. You land on the arrivals ring in the middle; in front of you three stations on glowing daises
        // under red archways (your car on the left, the portal home in the middle, a race on the right), glowing stripes on the
        // floor leading to each; a low parapet with a light strip round three sides, bollards along the road, lamp masts at
        // the corners. The race board stands behind the race station.
        private void BuildPaddock()
        {
            int mid = (PaddockFrom + PaddockTo) / 2;
            float length = paddockLength = (PaddockTo - PaddockFrom) * track.Step;
            Vector3 along = new Vector3(track.Fwd[mid].x, 0f, track.Fwd[mid].z).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along) * Track.PaddockSide;
            // (boxes' x along the deck, z across it)
            Quaternion facing = Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up);
            Vector3 top = track.Pos[mid] + across * (Track.HalfWidth - 0.5f + PaddockDepth / 2f);
            top.y -= 0.03f;
            PaddockCenter = top;
            PaddockAcross = across;
            PaddockForward = along;
            Vector3 P(float x, float y, float z) => top + along * x + Vector3.up * y + across * z;
            float hx = length / 2f, hz = PaddockDepth / 2f;

            // the deck: the hub's white floor panels inside a red border, the mod's name across it, a thick edge under it
            Box("Paddock", P(0f, -SlabDepth / 2f, 0f), facing, new Vector3(length, SlabDepth, PaddockDepth), WallFace, collide: true);
            Batch(Rust).Box(P(0f, -SlabDepth - 0.6f, 0f), facing, new Vector3(length - 4f, 1.2f, PaddockDepth - 4f));
            var trim = Batch(Pipe);
            trim.Box(P(0f, 0.015f, -hz + 2f), facing, new Vector3(length - 2f, 0.03f, 0.5f));
            trim.Box(P(0f, 0.015f, hz - 2f), facing, new Vector3(length - 2f, 0.03f, 0.5f));
            for (int s = -1; s <= 1; s += 2) trim.Box(P(s * (hx - 2f), 0.015f, 0f), facing, new Vector3(0.5f, 0.03f, PaddockDepth - 4f));
            var name = Text("QUAD'S HUB RACEWAY", P(0f, 0.04f, -hz + 5.5f), Quaternion.LookRotation(Vector3.up, across), 3.2f);
            name.GetComponent<TextMeshPro>().color = new Color(0.75f, 0.12f, 0.1f, 0.85f);

            // a low parapet with a light strip on the far side and both ends; bollards along the road
            var wall = SolidBatch(Concrete);
            var glow = Batch(HubGlow);
            wall.Box(P(0f, 0.55f, hz - 0.4f), facing, new Vector3(length, 1.1f, 0.8f));
            glow.Box(P(0f, 1.13f, hz - 0.4f), facing, new Vector3(length, 0.06f, 0.4f));
            for (int s = -1; s <= 1; s += 2)
            {
                wall.Box(P(s * (hx - 0.4f), 0.55f, 0f), facing, new Vector3(0.8f, 1.1f, PaddockDepth));
                glow.Box(P(s * (hx - 0.4f), 1.13f, 0f), facing, new Vector3(0.4f, 0.06f, PaddockDepth));
            }
            for (float x = -hx + 3f; x <= hx - 3f; x += 5f)
            {
                SolidBatch(Pipe).Box(P(x, 0.45f, -hz + 0.6f), facing, new Vector3(0.35f, 0.9f, 0.35f));
                Batch(HubGlow).Box(P(x, 0.93f, -hz + 0.6f), facing, new Vector3(0.4f, 0.08f, 0.4f));
            }

            // lamp masts at the four corners
            for (int s = -1; s <= 1; s += 2)
                for (int t = -1; t <= 1; t += 2)
                {
                    Vector3 c = P(s * (hx - 1.5f), 0f, t * (hz - 1.5f));
                    SolidBatch(Pipe).Box(c + Vector3.up * 6f, facing, new Vector3(0.45f, 12f, 0.45f));
                    Batch(Pipe).Box(c + Vector3.up * 11.6f - across * (t * 1.2f), facing, new Vector3(0.3f, 0.3f, 2.8f));
                    Batch(Lamp).Box(c + Vector3.up * 11.35f - across * (t * 2.3f), facing, new Vector3(1.6f, 0.2f, 1.6f));
                }

            // the three stations: a dais with a glowing rim, a red archway behind with the station's name
            for (int k = -1; k <= 1; k++)
            {
                Vector3 c = Station(k);
                if (k != 0)
                {
                    var dais = Copy(PrimitiveType.Cylinder, "Dais");
                    dais.transform.SetPositionAndRotation(c + Vector3.up * (DaisHeight / 2f), facing);
                    dais.transform.localScale = new Vector3(11f, DaisHeight / 2f, 11f);
                    dais.GetComponent<MeshRenderer>().sharedMaterial = Concrete;
                    var col = dais.AddComponent<MeshCollider>();
                    col.sharedMesh = dais.GetComponent<MeshFilter>().sharedMesh;
                    col.convex = true;
                    FloorRing(c + Vector3.up * (DaisHeight - 0.02f), 5.55f, HubGlow);
                }
                else FloorRing(c + Vector3.up * 0.02f, 5.2f, HubGlow);
                // light pillars round it
                Material beam = k < 0 ? Glow : k > 0 ? EdgeGlow : HubGlow;
                for (int p = 0; p < 6; p++)
                {
                    float a = (p + 0.5f) * Mathf.PI * 2f / 6f;
                    Vector3 at = c + (along * Mathf.Cos(a) + across * Mathf.Sin(a)) * 6.3f;
                    if (k == 0 && Mathf.Sin(a) > 0.3f) continue;
                    Batch(Pipe).Box(at + Vector3.up * 0.3f, facing, new Vector3(0.6f, 0.6f, 0.6f));
                    Batch(beam).Box(at + Vector3.up * 2.6f, facing, new Vector3(0.16f, 4f, 0.16f));
                }
                float archZ = k == 0 ? 7f : 6f;
                for (int s = -1; s <= 1; s += 2)
                    SolidBatch(Pipe).Box(c + along * (s * 5.5f) + across * archZ + Vector3.up * 4f, facing, new Vector3(0.7f, 8f, 0.7f));
                Batch(Pipe).Box(c + across * archZ + Vector3.up * 8.2f, facing, new Vector3(12f, 0.6f, 0.7f));
            }

            // the arrivals ring, and stripes on the floor from it to each station
            FloorRing(Arrival, 3f, HubGlow);
            FloorRing(Arrival, 2.2f, EdgeGlow);
            Text("ARRIVALS", Arrival + Vector3.up * 0.05f - across * 4f, Quaternion.LookRotation(Vector3.up, across), 1.2f);
            Material[] lines = { Glow, Paint, EdgeGlow };
            for (int k = -1; k <= 1; k++)
            {
                Vector3 a = Arrival + Vector3.up * 0.03f, b = Station(k) + Vector3.up * 0.03f;
                Vector3 d = b - a;
                float len = d.magnitude - 3f - 5f;
                Vector3 dir = d.normalized;
                Vector3 mid2 = a + dir * (3f + len / 2f);
                Batch(lines[k + 1]).Quad(mid2, Vector3.Cross(Vector3.up, dir), dir, 0.5f, len, flipU: false);
            }
        }

        // The middle of a station's floor (-1 your car, 0 the portal home, 1 a race), along the deck's far half.
        public Vector3 Station(int k) => PaddockCenter + PaddockAcross * 6f + PaddockForward * (k * 24f);

        // The top of a station's dais (where its pad is).
        public Vector3 StationTop(int k) => Station(k) + Vector3.up * (k == 0 ? 0f : DaisHeight);

        // Where you arrive on foot: the middle of the deck, nearer the road, facing the stations.
        public Vector3 Arrival => PaddockCenter - PaddockAcross * 9f;

        // Where the race board stands: at the deck's far end past the race station, turned toward the arrivals ring.
        public Vector3 BoardSpot => PaddockCenter + PaddockAcross * 1f + PaddockForward * (paddockLength / 2f - 2.5f);

        // A station's name on its archway, facing the arrivals ring, with a light strip under it.
        public void StationSign(int k, string text, Material line)
        {
            float archZ = k == 0 ? 7f : 6f;
            Vector3 at = Station(k) + PaddockAcross * archZ + Vector3.up * 9.6f;
            Quaternion facing = Quaternion.LookRotation(PaddockAcross);
            Batch(Dark).Box(at, facing, new Vector3(11f, 2.2f, 0.4f));
            Batch(line).Box(at - Vector3.up * 1.15f - PaddockAcross * 0.05f, facing, new Vector3(11f, 0.14f, 0.45f));
            Text(text, at - PaddockAcross * 0.25f, Quaternion.LookRotation(-PaddockAcross), 1.3f);
        }

        private void FloorRing(Vector3 at, float radius, Material material)
        {
            var go = new GameObject("FloorRing");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.04f, Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(radius, radius, 0.4f);
            go.AddComponent<MeshFilter>().sharedMesh = Torus(1f, 0.08f, 48, 6);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------------------------------------------------------------- pads, boards, portals

        public sealed class PadParts
        {
            public Material Edge;
            public Color Color;
            public Transform Holo;
        }

        // A pad on the floor: a dark plate with a glowing edge (which pulses), light posts at its corners, and a sign floating
        // and turning over it. Its glow and sign are returned for the raceway to animate.
        public PadParts Pad(Vector3 floor, float halfSize, string top, string label, Color color)
        {
            float size = halfSize * 2f, e = 0.3f;
            Quaternion facing = Quaternion.LookRotation(PaddockAcross);
            var edge = MakeMaterial(color, null, 0.5f, color * 1.4f);
            var glow = Batch(edge);
            Batch(Dark).Box(floor + Vector3.up * 0.03f, facing, new Vector3(size, 0.06f, size));
            for (int s = -1; s <= 1; s += 2)
            {
                glow.Box(floor + facing * new Vector3(s * (halfSize - e / 2f), 0.05f, 0f), facing, new Vector3(e, 0.1f, size));
                glow.Box(floor + facing * new Vector3(0f, 0.05f, s * (halfSize - e / 2f)), facing, new Vector3(size, 0.1f, e));
                for (int t = -1; t <= 1; t += 2)
                    glow.Box(floor + facing * new Vector3(s * (halfSize - 0.2f), 1.3f, t * (halfSize - 0.2f)), facing, new Vector3(0.14f, 2.6f, 0.14f));
            }
            var holo = new GameObject("PadSign");
            holo.transform.SetParent(root, false);
            holo.transform.position = floor + Vector3.up * 3.6f;
            // (one face: the raceway turns it to whoever's looking)
            var a = Text(top, holo.transform.position + Vector3.up * 0.8f, Quaternion.identity, 0.45f);
            a.GetComponent<TextMeshPro>().color = color;
            a.transform.SetParent(holo.transform, true);
            Text(label, holo.transform.position, Quaternion.identity, 1.4f).transform.SetParent(holo.transform, true);
            return new PadParts { Edge = edge, Color = color, Holo = holo.transform };
        }

        // A big board standing on legs, its text (both faces) set by the raceway.
        public TextMeshPro Board(Vector3 floor, Quaternion facing, float width, float height, out TextMeshPro back)
        {
            var dark = Batch(Dark);
            var glow = Batch(HubGlow);
            var pipe = Batch(Pipe);
            Vector3 c = floor + Vector3.up * (height / 2f + 2.5f);
            dark.Box(c, facing, new Vector3(width, height, 0.4f));
            glow.Box(c + Vector3.up * (height / 2f + 0.1f), facing, new Vector3(width, 0.2f, 0.45f));
            glow.Box(c - Vector3.up * (height / 2f + 0.1f), facing, new Vector3(width, 0.2f, 0.45f));
            for (int s = -1; s <= 1; s += 2)
                pipe.Box(floor + facing * new Vector3(s * (width / 2f - 0.5f), (height + 2.5f) / 2f, 0f), facing, new Vector3(0.4f, height + 2.5f, 0.4f));
            TextMeshPro Face(float side)
            {
                var go = Text("", c + facing * new Vector3(0f, 0f, side * 0.25f), side < 0 ? facing * Quaternion.Euler(0f, 180f, 0f) : facing, 0.7f);
                var t = go.GetComponent<TextMeshPro>();
                t.rectTransform.sizeDelta = new Vector2(width - 1f, height - 0.6f);
                t.enableWordWrapping = true;
                t.alignment = TextAlignmentOptions.Top;
                // (shrinks to fit, so nothing spills off the board)
                t.enableAutoSizing = true;
                t.fontSizeMin = 2.5f;
                t.fontSizeMax = 7f;
                return t;
            }
            back = Face(1f);
            return Face(-1f);
        }

        // A portal standing on the floor: a red frame with glowing segments, two broken rings that turn, a surface showing a
        // live view of where it leads (both sides), a flat inlay round its foot, and (with a label) a sign over it. Its moving
        // parts are returned for the raceway to animate. At scale 1 it's 12 m across.
        public PortalParts Portal(string name, Vector3 floor, Quaternion facing, string label, string sub, Texture view, float scale)
        {
            float k = scale, radius = 5.5f * k, frame = 0.55f * k;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(floor, facing);
            // (the frame's bottom on the floor)
            Vector3 center = new Vector3(0f, radius + frame * 2f, 0f);
            var parts = new PortalParts { Root = go.transform };

            Part(go.transform, "Frame", Torus(radius + frame, frame, 64, 12), Pipe, center);
            Part(go.transform, "FrameGlow", Torus(radius + frame, frame * 1.12f, 64, 12, arcs: 8, fill: 0.5f), HubGlow, center);
            parts.RingA = Part(go.transform, "RingA", Torus(radius - 0.2f * k, 0.16f * k, 64, 8, arcs: 3, fill: 0.7f), EdgeGlow, center).transform;
            parts.RingB = Part(go.transform, "RingB", Torus(radius - 0.75f * k, 0.12f * k, 64, 8, arcs: 5, fill: 0.6f), Chase[1], center).transform;
            var disc = MakeMaterial(Color.black, null, 0.2f, view != null ? Color.white : Cyan * 0.6f);
            if (view != null) disc.SetTexture("_EmissionMap", view);
            parts.Surface = disc;
            Part(go.transform, "Surface", Disc(radius - 0.1f * k, 64, back: false), disc, center);
            Part(go.transform, "SurfaceBack", Disc(radius - 0.1f * k, 64, back: true), disc, center);

            // flat on the floor: a dark inlay with glowing lines either side, and a red cradle the frame sits in
            Batch(Dark).Box(floor + facing * new Vector3(0f, 0.02f, 0f), facing, new Vector3(radius * 2.6f, 0.04f, 6f * k + 1f));
            foreach (float z in new[] { -1f, 1f })
                Batch(HubGlow).Box(floor + facing * new Vector3(0f, 0.045f, z * (3f * k + 0.5f)), facing, new Vector3(radius * 2.6f, 0.03f, 0.18f));
            Batch(Pipe).Box(floor + facing * new Vector3(0f, 0.25f * k, 0f), facing, new Vector3(radius * 1.1f, 0.5f * k, frame * 3f));

            if (!string.IsNullOrEmpty(label))
            {
                // the sign over it, readable from both sides; the small line under the name is the raceway's to change
                Vector3 sign = floor + facing * new Vector3(0f, center.y + radius + frame * 2f + 1.4f * k, 0f);
                Batch(Dark).Box(sign, facing, new Vector3(radius * 2.3f, 2.4f * k, 0.4f));
                Batch(HubGlow).Box(sign + facing * new Vector3(0f, -1.25f * k, 0f), facing, new Vector3(radius * 2.3f, 0.12f, 0.45f));
                foreach (float side in new[] { -1f, 1f })
                {
                    Quaternion read = side < 0 ? facing * Quaternion.Euler(0f, 180f, 0f) : facing;
                    Text(label, sign + facing * new Vector3(0f, 0.35f * k, side * 0.25f), read, 1.3f * k).transform.SetParent(go.transform, true);
                    var s2 = Text(sub, sign + facing * new Vector3(0f, -0.7f * k, side * 0.25f), read, 0.5f * k);
                    var tmp = s2.GetComponent<TextMeshPro>();
                    tmp.color = new Color(1f, 0.75f, 0.55f);
                    s2.transform.SetParent(go.transform, true);
                    if (side < 0) parts.Status = tmp; else parts.StatusBack = tmp;
                }
            }
            FlushBatches();
            return parts;
        }

        public sealed class PortalParts
        {
            public Transform Root, RingA, RingB;
            public Material Surface;
            public TextMeshPro Status, StatusBack;
        }

        private GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // A ring round the z axis; with arcs, only `fill` of each of that many equal parts is there (a broken ring).
        public Mesh Torus(float radius, float tube, int segments, int sides, int arcs = 0, float fill = 1f)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var center = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    verts.Add(center + center.normalized * (Mathf.Cos(b) * tube) + Vector3.forward * (Mathf.Sin(b) * tube));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                if (arcs > 0 && Mathf.Repeat((i + 0.5f) / segments * arcs, 1f) > fill) continue;
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            }
            var mesh = new Mesh { name = "HubRacewayTorus" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Owned.Add(mesh);
            return mesh;
        }

        // A flat disc in the xy plane facing -z (the side you walk up to), or +z for the back; its uv the square round it,
        // the right way round from either side.
        private Mesh Disc(float radius, int segments, bool back)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f * (back ? -1f : 1f), 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 1; i <= segments; i++)
                tris.AddRange(back ? new[] { 0, i, i + 1 } : new[] { 0, i + 1, i });
            var mesh = new Mesh { name = "HubRacewayDisc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Owned.Add(mesh);
            return mesh;
        }

        public GameObject Box(string name, Vector3 center, Quaternion rotation, Vector3 size, Material material, bool collide)
        {
            var go = Copy(PrimitiveType.Cube, name);
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (collide)
            {
                go.AddComponent<BoxCollider>();
                go.AddComponent<SurfaceType>().Material = SurfaceMaterial.Metal;
            }
            return go;
        }

        // A primitive's look (no collider), under the raceway.
        public GameObject Copy(PrimitiveType type, string name)
        {
            if (!primitives.TryGetValue(type, out GameObject p) || p == null)
            {
                p = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.SetActive(false);
                p.transform.SetParent(root, false);
                primitives[type] = p;
            }
            var go = Object.Instantiate(p, root);
            go.name = name;
            go.SetActive(true);
            return go;
        }

        public GameObject Text(string text, Vector3 position, Quaternion rotation, float size)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(root, false);
            // (a TextMeshPro reads from behind its forward: turned round, it reads from the side the rotation faces)
            go.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 180f, 0f));
            var t = go.AddComponent<TextMeshPro>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size * 10f;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(60f, 5f);
            return go;
        }

        public MeshData Batch(Material m)
        {
            if (!batches.TryGetValue(m, out MeshData d)) batches[m] = d = new MeshData();
            return d;
        }

        private MeshData SolidBatch(Material m)
        {
            if (!solidBatches.TryGetValue(m, out MeshData d)) solidBatches[m] = d = new MeshData();
            return d;
        }

        // Makes the merged meshes collected so far.
        public void FlushBatches()
        {
            foreach (var kv in batches)
                if (!kv.Value.Empty) Spawn("Parts", kv.Value, kv.Key, collide: false, SurfaceMaterial.Metal);
            foreach (var kv in solidBatches)
                if (!kv.Value.Empty) Spawn("Solid", kv.Value, kv.Key, collide: true, SurfaceMaterial.Metal);
            batches.Clear();
            solidBatches.Clear();
        }

        private void Spawn(string name, MeshData data, Material material, bool collide, SurfaceMaterial surface)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            Mesh mesh = data.ToMesh("HubRaceway" + name);
            Owned.Add(mesh);
            if (material != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                if (!collide) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (collide)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<SurfaceType>().Material = surface;
            }
        }

        // Vertices and triangles, built a strip (or a box, or a quad) at a time.
        public sealed class MeshData
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> tris = new List<int>();

            public bool Empty => tris.Count == 0;

            // A strip from profile point a to b (across, up), swept from sample `from` to sample `to` (to = Count closes the
            // loop). It faces Cross(forward, b - a): a = left and b = right faces up. u runs a to b; v along the road.
            public void Strip(Track track, Vector2 a, Vector2 b, float u0, float u1, float vPerMetre, int from, int to, bool when = true)
            {
                if (!when || to <= from) return;
                int first = verts.Count;
                for (int j = from; j <= to; j++)
                {
                    float v = j * track.Step * vPerMetre;
                    verts.Add(track.At(j, a.x, a.y));
                    uvs.Add(new Vector2(u0, v));
                    verts.Add(track.At(j, b.x, b.y));
                    uvs.Add(new Vector2(u1, v));
                }
                for (int k = 0; k < to - from; k++)
                {
                    int r = first + k * 2;
                    tris.AddRange(new[] { r, r + 2, r + 3, r, r + 3, r + 1 });
                }
            }

            // A strip between point ka and point kb of an outline that changes along the road (one outline per sample from
            // `from`). It faces Cross(forward, b - a), as Strip does.
            public void Sweep(Track track, List<Vector2>[] outlines, int ka, int kb, int from, float u0, float u1, float vPerMetre)
            {
                int first = verts.Count;
                for (int j = 0; j < outlines.Length; j++)
                {
                    Vector2 a = outlines[j][ka], b = outlines[j][kb];
                    float v = (from + j) * track.Step * vPerMetre;
                    verts.Add(track.At(from + j, a.x, a.y));
                    uvs.Add(new Vector2(u0, v));
                    verts.Add(track.At(from + j, b.x, b.y));
                    uvs.Add(new Vector2(u1, v));
                }
                for (int k = 0; k < outlines.Length - 1; k++)
                {
                    int r = first + k * 2;
                    tris.AddRange(new[] { r, r + 2, r + 3, r, r + 3, r + 1 });
                }
            }

            // Four corners as seen from the front: bottom-left, top-left, top-right, bottom-right; uv the unit square.
            public void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br)
            {
                int i = verts.Count;
                verts.Add(bl); verts.Add(tl); verts.Add(tr); verts.Add(br);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            // A quad seen the right way round by someone whose screen-right is `right` and screen-up is `up`. (Lying on the
            // road, `right` across and `up` along it, it faces up.)
            public void Quad(Vector3 center, Vector3 right, Vector3 up, float width, float height, bool flipU)
            {
                Vector3 r = right.normalized * (width / 2f), u = up.normalized * (height / 2f);
                int i = verts.Count;
                verts.Add(center - r - u); verts.Add(center - r + u); verts.Add(center + r + u); verts.Add(center + r - u);
                float a = flipU ? 1f : 0f, b = flipU ? 0f : 1f;
                uvs.Add(new Vector2(a, 0)); uvs.Add(new Vector2(a, 1)); uvs.Add(new Vector2(b, 1)); uvs.Add(new Vector2(b, 0));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            // A box, each face's uv the unit square.
            public void Box(Vector3 center, Quaternion rotation, Vector3 size)
            {
                Vector3 x = rotation * Vector3.right * (size.x / 2f), y = rotation * Vector3.up * (size.y / 2f), z = rotation * Vector3.forward * (size.z / 2f);
                Quad(center - z - x - y, center - z - x + y, center - z + x + y, center - z + x - y);   // front (-z)
                Quad(center + z + x - y, center + z + x + y, center + z - x + y, center + z - x - y);   // back
                Quad(center - x + z - y, center - x + z + y, center - x - z + y, center - x - z - y);   // left
                Quad(center + x - z - y, center + x - z + y, center + x + z + y, center + x + z - y);   // right
                Quad(center + y - z - x, center + y + z - x, center + y + z + x, center + y - z + x);   // top
                Quad(center - y + z - x, center - y - z - x, center - y - z + x, center - y + z + x);   // bottom
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
