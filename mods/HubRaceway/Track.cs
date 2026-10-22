using UnityEngine;

namespace HubRaceway
{
    // The track's shape: a closed loop through the control points below, smoothed (centripetal Catmull-Rom) and cut into samples
    // every couple of metres. Each sample has its own frame: forward along the road, up out of it (banked into the turns, tilted
    // with the hills, twisted where a point says so) and right across it. Everything else (the meshes, the lap timing, where the
    // car is pulled toward the road) is measured from these samples.
    internal sealed class Track
    {
        // Saved best laps belong to this layout: change it whenever the points change.
        public const string Id = "hub5";
        public const float HalfWidth = 14f;
        private const float TargetStep = 2f;
        private const float BankPerTurn = 14f;   // degrees of bank per degree of turn per metre
        private const float MaxBank = 22f;

        // A control point: where (world metres; the hub's middle is 0,0,0), how far the road twists on the way to the next point
        // (degrees, clockwise looking ahead), and whether that stretch is inside the tunnel.
        private struct Point
        {
            public Vector3 At;
            public float Twist;
            public bool Tunnel;
            public Point(float x, float y, float z, float twist = 0f, bool tunnel = false) { At = new Vector3(x, y, z); Twist = twist; Tunnel = tunnel; }
        }

        // In driving order, the start line at the first (mid-way under the station's legs, on the straight, so the grid and the
        // paddock before it are straight and level): west, down the west side past its wing, diving under the station's belly
        // beneath the north tower and its thrusters, climbing round the tower's far end (under its exhaust), down the far west
        // side (over the road you came by), east over the dome, through the tube out east, and home. Checked against the
        // hub's colliders (every surface, every layer) and the parts of it you can only see.
        private static readonly Point[] Points =
        {
            new Point(0, 50, -224), new Point(-120, 50, -222), new Point(-195, 48, -175), new Point(-215, 38, -60),
            new Point(-215, 15, 60), new Point(-170, -15, 170), new Point(-60, -30, 214), new Point(60, -30, 214),
            new Point(170, -24, 214), new Point(255, -8, 285), new Point(262, 24, 380), new Point(150, 56, 430),
            new Point(-30, 82, 425), new Point(-170, 100, 370), new Point(-255, 110, 255), new Point(-280, 110, 120),
            new Point(-235, 110, 40), new Point(-120, 110, 28), new Point(0, 112, 24), new Point(120, 110, 28),
            new Point(230, 100, 15), new Point(295, 88, -45, tunnel: true), new Point(305, 74, -150, tunnel: true),
            new Point(265, 60, -235), new Point(190, 52, -255), new Point(110, 50, -226),
        };

        // Which side of the road the paddock is on, before the line (-1 left): away from the station's legs.
        public const int PaddockSide = -1;

        public readonly int Count;
        public readonly float Step;
        public readonly float Length;
        public readonly Vector3[] Pos, Fwd, Right, Up;
        // inside the tunnel; pulled toward the road instead of down (the tunnel, and anywhere the road leans far over)
        public readonly bool[] Tunnel, Magnet;
        // how sharply the road turns here (degrees per metre, + right), for the corner signs
        public readonly float[] Turn;
        public readonly float MinY;
        public readonly int TunnelFrom = -1, TunnelTo = -1;

        // The tube: a rounded box round the road, the floor flat, its corners curving up into the walls and over into the ceiling
        // so you can drive right round the inside. Square at the mouths (the road runs straight in), rounding over the first metres.
        public const float TubeHalfWidth = 15f, TubeHeight = 18f, TubeCorner = 7f, TubeFlare = 35f;
        // +1 when the loop turns right overall (its inside is on the right), -1 when left
        public readonly int InsideSide;

        public Track()
        {
            // a dense polyline along the curve (remembering which stretch each part is on), then even steps along it
            const int perSegment = 64;
            int n = Points.Length;
            var dense = new Vector3[n * perSegment + 1];
            var denseRoll = new float[n * perSegment + 1];
            var denseTunnel = new bool[n * perSegment + 1];
            float roll = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = Points[(i - 1 + n) % n].At, p1 = Points[i].At, p2 = Points[(i + 1) % n].At, p3 = Points[(i + 2) % n].At;
                for (int k = 0; k < perSegment; k++)
                {
                    float t = k / (float)perSegment;
                    dense[i * perSegment + k] = CatmullRom(p0, p1, p2, p3, t);
                    // (eased, so the twist starts and stops gently)
                    denseRoll[i * perSegment + k] = roll + Points[i].Twist * (t * t * (3f - 2f * t));
                    denseTunnel[i * perSegment + k] = Points[i].Tunnel;
                }
                roll += Points[i].Twist;
            }
            dense[n * perSegment] = dense[0];
            denseRoll[n * perSegment] = roll;
            var cumulative = new float[dense.Length];
            for (int i = 1; i < dense.Length; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            Length = cumulative[dense.Length - 1];
            Count = Mathf.RoundToInt(Length / TargetStep);
            Step = Length / Count;

            Pos = new Vector3[Count];
            var rolls = new float[Count];
            Tunnel = new bool[Count];
            int seg = 0;
            for (int i = 0; i < Count; i++)
            {
                float d = i * Step;
                while (seg < dense.Length - 2 && cumulative[seg + 1] < d) seg++;
                float t = Mathf.InverseLerp(cumulative[seg], cumulative[seg + 1], d);
                Pos[i] = Vector3.Lerp(dense[seg], dense[seg + 1], t);
                rolls[i] = Mathf.Lerp(denseRoll[seg], denseRoll[seg + 1], t);
                Tunnel[i] = denseTunnel[seg];
            }

            // how fast the road turns at each sample (degrees per metre, + for right), smoothed so the banking eases in
            var turn = new float[Count];
            float totalTurn = 0f;
            for (int i = 0; i < Count; i++)
            {
                Vector3 a = Flat(Pos[i] - Pos[Wrap(i - 1)]), b = Flat(Pos[Wrap(i + 1)] - Pos[i]);
                turn[i] = Vector3.SignedAngle(a, b, Vector3.up) / Step;
                totalTurn += turn[i] * Step;
            }
            InsideSide = totalTurn >= 0f ? 1 : -1;
            Turn = new float[Count];
            const int reach = 12;
            for (int i = 0; i < Count; i++)
            {
                float sum = 0f;
                for (int k = -reach; k <= reach; k++) sum += turn[Wrap(i + k)];
                Turn[i] = sum / (reach * 2 + 1);
            }

            Fwd = new Vector3[Count];
            Right = new Vector3[Count];
            Up = new Vector3[Count];
            Magnet = new bool[Count];
            MinY = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                Vector3 fwd = (Pos[Wrap(i + 1)] - Pos[Wrap(i - 1)]).normalized;
                Vector3 flatRight = Vector3.Cross(Vector3.up, Flat(fwd)).normalized;
                // tilted toward the inside of the turn (the outside edge is the high one), but not in the tube (level, so both its walls are alike)
                float bank = Tunnel[i] ? 0f : Mathf.Clamp(Turn[i] * BankPerTurn, -MaxBank, MaxBank);
                Vector3 up = (Vector3.up + flatRight * Mathf.Tan(bank * Mathf.Deg2Rad)).normalized;
                up = Quaternion.AngleAxis(rolls[i], fwd) * up;
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                Fwd[i] = fwd;
                Right[i] = right;
                Up[i] = Vector3.Cross(fwd, right);
                Magnet[i] = Tunnel[i] || Up[i].y < 0.8f;
                MinY = Mathf.Min(MinY, Pos[i].y);
                if (Tunnel[i]) { if (TunnelFrom < 0) TunnelFrom = i; TunnelTo = i + 1; }
            }
        }

        public bool InTunnel(int i) => TunnelFrom >= 0 && i >= TunnelFrom && i < TunnelTo;

        // How round the tube's corners are at a sample (0 at the mouths, TubeCorner inside).
        public float TubeRound(int i)
        {
            if (!InTunnel(i)) return 0f;
            float d = Mathf.Min(i - TunnelFrom, TunnelTo - 1 - i) * Step;
            float t = Mathf.Clamp01(d / TubeFlare);
            return TubeCorner * t * t * (3f - 2f * t);
        }

        // For a point in the tube (across, up from the road's middle), which way the nearest wall is: where the car is pulled.
        public Vector2 TubeOut(int i, Vector2 p)
        {
            float r = Mathf.Max(TubeRound(i), 0.01f);
            float fx = TubeHalfWidth - r;
            var core = new Vector2(Mathf.Clamp(p.x, -fx, fx), Mathf.Clamp(p.y, r, TubeHeight - r));
            Vector2 d = p - core;
            if (d.sqrMagnitude > 1e-4f) return d.normalized;
            // (in the middle: toward whichever wall is closest)
            float floor = p.y, ceiling = TubeHeight - p.y, side = TubeHalfWidth - Mathf.Abs(p.x);
            if (floor <= ceiling && floor <= side) return Vector2.down;
            if (ceiling <= side) return Vector2.up;
            return new Vector2(Mathf.Sign(p.x), 0f);
        }

        public int Wrap(int i) => ((i % Count) + Count) % Count;

        // The sample that far round the loop (0..1 from the start line).
        public int AtFraction(float f) => Wrap(Mathf.RoundToInt(f * Count));

        // A point measured from a sample: across the road (right), up out of it.
        public Vector3 At(int i, float across, float up)
        {
            i = Wrap(i);
            return Pos[i] + Right[i] * across + Up[i] * up;
        }

        public Quaternion Facing(int i)
        {
            i = Wrap(i);
            return Quaternion.LookRotation(Fwd[i], Up[i]);
        }

        // Where a position is relative to a sample: across the road, up out of it, along it.
        public Vector3 Local(int i, Vector3 p)
        {
            Vector3 d = p - Pos[i];
            return new Vector3(Vector3.Dot(d, Right[i]), Vector3.Dot(d, Up[i]), Vector3.Dot(d, Fwd[i]));
        }

        // The sample nearest a position, looking near the last one first (where the track passes near itself, the right
        // stretch wins), and everywhere when that finds nothing close.
        public int Nearest(Vector3 p, int hint, out float sqrDistance)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            if (hint >= 0)
            {
                for (int k = -40; k <= 40; k++)
                {
                    int i = Wrap(hint + k);
                    float s = (Pos[i] - p).sqrMagnitude;
                    if (s < bestSqr) { bestSqr = s; best = i; }
                }
            }
            if (best < 0 || bestSqr > 30f * 30f)
            {
                for (int i = 0; i < Count; i++)
                {
                    float s = (Pos[i] - p).sqrMagnitude;
                    if (s < bestSqr) { bestSqr = s; best = i; }
                }
            }
            sqrDistance = bestSqr;
            return best;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;

        // centripetal Catmull-Rom (no loops or cusps where the points are uneven)
        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), 0.01f);
            float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), 0.01f);
            float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), 0.01f);
            float u = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }
    }
}
