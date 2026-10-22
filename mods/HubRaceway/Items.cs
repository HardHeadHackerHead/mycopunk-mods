using System.Collections.Generic;
using Pigeon.Movement;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace HubRaceway
{
    internal enum Item : byte { None, Turbo, TripleTurbo, Oil, Zap, Shock, Shield }

    // Power-ups, the Mario Kart way: rows of item boxes across the road give you a random one (better the further back you are
    // in a race), and you fire it with the throw key (Q). Boost pads on the road (and two on the tube's ceiling) give a free
    // turbo. Everything shows: boxes burst, turbos leave trails, the zap is a glowing orb, the shockwave a ring you can see
    // spread, oil splashes, the shield spins round you. What reaches other players goes through the host to everyone.
    internal sealed class Items
    {
        private const byte MsgItem = 20;
        private const byte FxOil = 1, FxOilGone = 2, FxZap = 3, FxShock = 4, FxShield = 5, FxTurbo = 6, FxHit = 7;
        private const float BoxRespawn = 4f, RollTime = 1.1f;

        private static readonly float[] BoxRows = { 0.11f, 0.25f, 0.735f };
        private static readonly float[] PadSpots = { 0.17f, 0.45f, 0.80f };

        private readonly Raceway raceway;
        private readonly Track track;
        private readonly Builder builder;
        private readonly Car car;
        private readonly Fx fx;

        private sealed class Box { public Transform T; public Vector3 Base; public float HiddenUntil; }
        private readonly List<Box> boxes = new List<Box>();
        private readonly List<int> pads = new List<int>(), ceilingPads = new List<int>();
        private float padCooldown;

        public Item Held { get; private set; }
        public int Charges { get; private set; }
        public float RollingUntil { get; private set; }
        private Item rolled;

        private readonly Dictionary<int, GameObject> hazards = new Dictionary<int, GameObject>();
        private sealed class Shot { public GameObject Go; public ulong Target; public float Until; }
        private readonly List<Shot> shots = new List<Shot>();

        public Items(Raceway raceway, Track track, Builder builder, Car car, Fx fx)
        {
            this.raceway = raceway;
            this.track = track;
            this.builder = builder;
            this.car = car;
            this.fx = fx;
        }

        public static string Name(Item i)
        {
            switch (i)
            {
                case Item.Turbo: return "TURBO";
                case Item.TripleTurbo: return "TRIPLE TURBO";
                case Item.Oil: return "OIL SLICK";
                case Item.Zap: return "HOMING ZAP";
                case Item.Shock: return "SHOCKWAVE";
                case Item.Shield: return "STAR SHIELD";
                default: return "";
            }
        }

        public void Build()
        {
            foreach (float f in BoxRows)
            {
                int s = track.AtFraction(f);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 at = track.At(s, (k - 1.5f) * 6f, 1.8f);
                    var go = builder.Copy(PrimitiveType.Cube, "ItemBox");
                    go.transform.position = at;
                    go.transform.localScale = Vector3.one * 2.2f;
                    go.GetComponent<MeshRenderer>().sharedMaterial = builder.ItemBox;
                    for (int face = 0; face < 4; face++)
                    {
                        Quaternion r = Quaternion.Euler(0f, face * 90f, 0f);
                        var q = builder.Text("?", at + r * Vector3.forward * 1.12f, r, 1.6f);
                        q.transform.SetParent(go.transform, true);
                        q.GetComponent<TextMeshPro>().fontStyle = FontStyles.Bold;
                    }
                    boxes.Add(new Box { T = go.transform, Base = at });
                }
            }
            foreach (float f in PadSpots)
            {
                int s = track.AtFraction(f);
                if (builder.InTunnel(s)) continue;
                pads.Add(s);
                builder.Batch(builder.BoostPad).Quad(track.At(s, 0f, 0.04f), track.Right[s], track.Fwd[s], 10f, 12f, flipU: false);
            }
            // two long ones along the tube's ceiling, the reward for driving up there (facing down, arrows ahead)
            if (track.TunnelFrom >= 0)
            {
                foreach (float f in new[] { 0.36f, 0.66f })
                {
                    int s = track.TunnelFrom + Mathf.RoundToInt((track.TunnelTo - track.TunnelFrom) * f);
                    ceilingPads.Add(s);
                    builder.Batch(builder.BoostPad).Quad(track.At(s, 0f, Track.TubeHeight - 0.04f), -track.Right[s], track.Fwd[s], 10f, 16f, flipU: false);
                }
            }
            builder.FlushBatches();
        }

        // Every frame: the boxes spin and glow, pickups, pads, oil, the item key, and the shots in flight.
        public void Tick(Vehicle mine, bool onRoad)
        {
            float now = Time.time;
            Color hue = Color.HSVToRGB(Mathf.Repeat(now * 0.25f, 1f), 0.75f, 1f);
            Builder.SetEmission(builder.ItemBox, hue * 1.2f);
            builder.ItemBox.SetColor("_BaseColor", hue);
            builder.BoostPad.SetTextureOffset("_BaseMap", new Vector2(0f, -now * 1.5f));
            builder.BoostPad.SetTextureOffset("_EmissionMap", new Vector2(0f, -now * 1.5f));
            Vector3 carPos = mine != null ? mine.Rigidbody.position : Vector3.zero;
            foreach (Box b in boxes)
            {
                bool show = now >= b.HiddenUntil;
                if (b.T.gameObject.activeSelf != show) b.T.gameObject.SetActive(show);
                if (!show) continue;
                float pop = Mathf.Clamp01((now - b.HiddenUntil) * 3f);
                b.T.localScale = Vector3.one * 2.2f * pop;
                b.T.SetPositionAndRotation(b.Base + Vector3.up * Mathf.Sin(now * 2f + b.Base.x) * 0.3f, Quaternion.Euler(20f, now * 90f, 10f));
                if (mine != null && (carPos - b.T.position).sqrMagnitude < 3.6f * 3.6f)
                {
                    b.HiddenUntil = now + BoxRespawn;
                    fx.Burst(b.T.position, Quaternion.identity, 0.45f);
                    fx.Debris(b.T.position, builder.ItemBox, 12, 14f, 0.7f);
                    if (Held == Item.None && RollingUntil <= 0f) StartRoll();
                }
            }

            if (RollingUntil > 0f && now >= RollingUntil)
            {
                RollingUntil = 0f;
                Held = rolled;
                Charges = rolled == Item.TripleTurbo ? 3 : 1;
                Sound.Play("UI_Directive_Claim");
                raceway.Flash("GOT " + Name(Held) + "!  PRESS Q");
            }

            if (mine != null)
            {
                foreach (int s in pads)
                {
                    if (now < padCooldown || !onRoad) break;
                    Vector3 l = track.Local(s, carPos);
                    if (Mathf.Abs(l.x) < 5.5f && Mathf.Abs(l.z) < 7f && l.y < 4f) { Boost(mine, 1.4f); padCooldown = now + 1.2f; }
                }
                foreach (int s in ceilingPads)
                {
                    if (now < padCooldown || !onRoad) break;
                    Vector3 l = track.Local(s, carPos);
                    if (Mathf.Abs(l.x) < 6f && Mathf.Abs(l.z) < 9f && l.y > Track.TubeHeight - 5f)
                    {
                        Boost(mine, 2.5f);
                        raceway.Flash("CEILING BOOST!");
                        padCooldown = now + 1.2f;
                    }
                }
                var gone = new List<int>();
                foreach (var kv in hazards)
                {
                    if (kv.Value == null || (kv.Value.transform.position - carPos).sqrMagnitude > 3.4f * 3.4f) continue;
                    gone.Add(kv.Key);
                    if (car.Shielded) raceway.Flash("BLOCKED!");
                    else { Hit(mine, "SPUN OUT!"); Sound.Play("Play_Disc_Impact"); }
                }
                foreach (int id in gone) Send(FxOilGone, id, 0UL, Vector3.zero, Vector3.zero);
                if (Held != Item.None && RollingUntil <= 0f && PlayerInput.Controls.Player.Throw.WasPressedThisFrame()) Use(mine);
            }

            TickShots(now);
        }

        // A turbo for your car, with its trails and flash shown to everyone.
        private void Boost(Vehicle mine, float seconds)
        {
            car.Turbo(seconds);
            Send(FxTurbo, 0, 0UL, mine.Rigidbody.position, new Vector3(seconds, 0f, 0f));
        }

        // You got hit (spun out): the spin, a flash where you are, for everyone.
        private void Hit(Vehicle mine, string text)
        {
            car.Spin();
            raceway.Flash(text);
            PlayerLook.Instance?.ShakeRotationMin(3f);
            Send(FxHit, 0, 0UL, mine.Rigidbody.position, mine.transform.up);
        }

        private void StartRoll()
        {
            RollingUntil = Time.time + RollTime;
            // the further back in a race, the better the odds (0 leading, 1 last; 0.5 when practising)
            float t = 0.5f;
            Race race = raceway.Race;
            if (race.IAmIn && race.Live && race.Entrants.Count > 1) t = (race.PlaceOf(Net.Me) - 1f) / (race.Entrants.Count - 1f);
            float[] w =
            {
                0f,
                30f,                 // turbo
                5f + 25f * t,        // triple turbo
                30f - 20f * t,       // oil
                10f + 15f * t,       // zap
                2f + 10f * t,        // shock
                5f + 12f * t,        // shield
            };
            float total = 0f;
            foreach (float x in w) total += x;
            float pick = Random.value * total;
            rolled = Item.Turbo;
            for (int i = 1; i < w.Length; i++)
            {
                if (pick < w[i]) { rolled = (Item)i; break; }
                pick -= w[i];
            }
            Sound.Play("UI_Favorite");
        }

        // What the HUD shows while it rolls: the names flicking past.
        public string RollingName() => Name((Item)(1 + (int)(Time.time * 14f) % 6));

        private void Use(Vehicle mine)
        {
            Item item = Held;
            if (--Charges <= 0) { Held = Item.None; Charges = 0; }
            Vector3 pos = mine.Rigidbody.position;
            switch (item)
            {
                case Item.Turbo:
                case Item.TripleTurbo:
                    Boost(mine, 1.6f);
                    raceway.Flash("TURBO!");
                    break;
                case Item.Oil:
                    {
                        int s = track.Nearest(pos, -1, out _);
                        Vector3 at = pos - mine.transform.forward * 6f;
                        int ss = track.Nearest(at, s, out _);
                        Vector3 l = track.Local(ss, at);
                        float across = Mathf.Clamp(l.x, -12f, 12f);
                        // (in the tube it lies on whatever wall you're on)
                        Vector3 spot = track.InTunnel(ss) ? at - mine.transform.up * 1.2f : track.At(ss, across, 0.05f);
                        Vector3 up = track.InTunnel(ss) ? mine.transform.up : track.Up[ss];
                        Send(FxOil, Random.Range(1, int.MaxValue), 0UL, spot, up);
                        raceway.Flash("OIL DROPPED");
                        break;
                    }
                case Item.Zap:
                    {
                        ulong target = TargetAhead(mine);
                        Send(FxZap, 0, target, pos, mine.transform.forward);
                        raceway.Flash(target == ulong.MaxValue ? "ZAP FIRED (NO ONE AHEAD)" : "ZAP FIRED!");
                        break;
                    }
                case Item.Shock:
                    Send(FxShock, 0, 0UL, pos, Vector3.zero);
                    raceway.Flash("SHOCKWAVE!");
                    break;
                case Item.Shield:
                    car.Shield(6f);
                    Send(FxShield, 0, 0UL, pos, new Vector3(6f, 0f, 0f));
                    raceway.Flash("STAR SHIELD!");
                    break;
            }
        }

        // The racer just ahead of you (in a race), or the nearest other car ahead on the track within 300 m (practising).
        private ulong TargetAhead(Vehicle mine)
        {
            Race race = raceway.Race;
            if (race.IAmIn && race.Live)
            {
                List<Entrant> s = race.Standings();
                int me = s.FindIndex(e => e.Id == Net.Me);
                for (int i = me - 1; i >= 0; i--) if (!s[i].Finished && VehicleOf(s[i].Id) != null) return s[i].Id;
                return ulong.MaxValue;
            }
            ulong best = ulong.MaxValue;
            float bestD = 300f;
            foreach (Player p in GameManager.players)
            {
                if (p == null || p.IsLocalPlayer || p.CurrentVehicle == null) continue;
                Vector3 d = p.CurrentVehicle.transform.position - mine.transform.position;
                if (Vector3.Dot(d, mine.transform.forward) < 0f) continue;
                if (d.magnitude < bestD) { bestD = d.magnitude; best = p.OwnerClientId; }
            }
            return best;
        }

        private static Vehicle VehicleOf(ulong id)
        {
            if (id == ulong.MaxValue) return null;
            Player p = GameManager.GetPlayer(id);
            return p != null ? p.CurrentVehicle : null;
        }

        private void Send(byte kind, int id, ulong target, Vector3 pos, Vector3 dir)
        {
            if (!Net.Ready)
            {
                // (offline: just here)
                Apply(kind, Net.Me, id, target, pos, dir);
                return;
            }
            Net.ToServer((ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(MsgItem);
                w.WriteValueSafe(kind);
                w.WriteValueSafe(id);
                w.WriteValueSafe(target);
                w.WriteValueSafe(pos);
                w.WriteValueSafe(dir);
            });
        }

        // The host passes every power-up on to everyone, saying who it came from.
        public bool OnHostMessage(ulong sender, byte type, ref FastBufferReader r)
        {
            if (type != MsgItem) return false;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out int id);
            r.ReadValueSafe(out ulong target);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out Vector3 dir);
            Net.ToAll((ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(MsgItem);
                w.WriteValueSafe(kind);
                w.WriteValueSafe(sender);
                w.WriteValueSafe(id);
                w.WriteValueSafe(target);
                w.WriteValueSafe(pos);
                w.WriteValueSafe(dir);
            });
            return true;
        }

        public bool OnMessage(byte type, ref FastBufferReader r)
        {
            if (type != MsgItem) return false;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out ulong from);
            r.ReadValueSafe(out int id);
            r.ReadValueSafe(out ulong target);
            r.ReadValueSafe(out Vector3 pos);
            r.ReadValueSafe(out Vector3 dir);
            Apply(kind, from, id, target, pos, dir);
            return true;
        }

        private void Apply(byte kind, ulong from, int id, ulong target, Vector3 pos, Vector3 dir)
        {
            Vehicle v = VehicleOf(from);
            switch (kind)
            {
                case FxTurbo:
                    if (v != null)
                    {
                        fx.Trails(v.transform, builder.Glow, Mathf.Max(0.5f, dir.x), 1.1f);
                        fx.Burst(v.transform.position - v.transform.forward * 2f, v.transform.rotation, 0.5f);
                        fx.Wave(v.transform.position - v.transform.forward * 2.5f + v.transform.up, v.transform.forward, builder.Glow, 1f, 9f, 0.45f);
                    }
                    break;
                case FxHit:
                    fx.Burst(pos, Quaternion.identity, 0.6f);
                    fx.Wave(pos, dir, builder.Chase[1], 1f, 12f, 0.5f);
                    fx.Debris(pos, builder.Glow, 10, 10f, 0.5f);
                    break;
                case FxOil:
                    {
                        if (hazards.ContainsKey(id)) break;
                        var oil = builder.Copy(PrimitiveType.Cylinder, "Oil");
                        oil.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, dir));
                        oil.transform.localScale = new Vector3(5f, 0.03f, 5f);
                        oil.GetComponent<MeshRenderer>().sharedMaterial = builder.Oil;
                        var rim = new GameObject("OilRim");
                        rim.transform.SetParent(oil.transform, false);
                        rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        rim.transform.localScale = new Vector3(0.5f, 0.5f, 3f);
                        rim.AddComponent<MeshFilter>().sharedMesh = builder.Torus(1f, 0.05f, 48, 6);
                        rim.AddComponent<MeshRenderer>().sharedMaterial = builder.ItemBox;
                        hazards[id] = oil;
                        fx.Debris(pos + dir, builder.Oil, 14, 9f, 0.6f);
                        fx.Wave(pos + dir * 0.1f, dir, builder.ItemBox, 0.5f, 5f, 0.4f);
                        // (slicks dry up after a minute)
                        Object.Destroy(oil, 60f);
                        break;
                    }
                case FxOilGone:
                    if (hazards.TryGetValue(id, out GameObject h))
                    {
                        if (h != null) { fx.Debris(h.transform.position, builder.Oil, 8, 8f, 0.5f); Object.Destroy(h); }
                        hazards.Remove(id);
                    }
                    break;
                case FxZap:
                    {
                        var go = builder.Copy(PrimitiveType.Sphere, "Zap");
                        go.transform.position = pos + dir * 4f + Vector3.up;
                        go.transform.localScale = Vector3.one * 2f;
                        go.GetComponent<MeshRenderer>().sharedMaterial = builder.Chase[1];
                        var trail = go.AddComponent<TrailRenderer>();
                        trail.time = 0.6f;
                        trail.startWidth = 1.8f;
                        trail.endWidth = 0f;
                        trail.sharedMaterial = builder.Chase[1];
                        var halo = new GameObject("ZapHalo");
                        halo.transform.SetParent(go.transform, false);
                        halo.transform.localScale = Vector3.one * 1.1f;
                        halo.AddComponent<MeshFilter>().sharedMesh = builder.Torus(1f, 0.08f, 32, 6, arcs: 3, fill: 0.6f);
                        halo.AddComponent<MeshRenderer>().sharedMaterial = builder.EdgeGlow;
                        shots.Add(new Shot { Go = go, Target = target, Until = Time.time + 8f });
                        fx.Burst(go.transform.position, Quaternion.identity, 0.4f);
                        Sound.Play("Play_Photon_Disc_Shoot");
                        break;
                    }
                case FxShock:
                    {
                        // a ring of lightning spreading from whoever set it off, flat and upright, for everyone to see
                        Vector3 at = v != null ? v.transform.position : pos;
                        fx.Wave(at, Vector3.up, builder.EdgeGlow, 2f, 220f, 1.4f);
                        fx.Wave(at, Vector3.up, builder.Chase[0], 2f, 140f, 1f);
                        fx.Burst(at, Quaternion.identity, 1f);
                        if (from == Net.Me) break;
                        Vehicle mine = Car.Mine();
                        if (mine != null && raceway.NearTrack)
                        {
                            if (car.Shielded) raceway.Flash("SHOCKWAVE BLOCKED!");
                            else
                            {
                                car.Slow(3f);
                                fx.Bubble(mine.transform, builder.EdgeGlow, 3f, 3.2f);
                                raceway.Flash("SHOCKED BY " + NameOf(from) + "!");
                                PlayerLook.Instance?.ShakeRotationMin(2.5f);
                                Sound.Play("Play_Lightning_Charge");
                            }
                        }
                        break;
                    }
                case FxShield:
                    if (v != null)
                    {
                        fx.Bubble(v.transform, builder.ItemBox, Mathf.Max(1f, dir.x), 3.8f);
                        fx.Trails(v.transform, builder.ItemBox, Mathf.Max(1f, dir.x), 1.2f);
                        fx.Burst(v.transform.position, Quaternion.identity, 0.7f);
                    }
                    break;
            }
        }

        private void TickShots(float now)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                Shot s = shots[i];
                Vehicle v = VehicleOf(s.Target);
                if (s.Go == null || now > s.Until) { if (s.Go != null) Object.Destroy(s.Go); shots.RemoveAt(i); continue; }
                Transform halo = s.Go.transform.childCount > 0 ? s.Go.transform.GetChild(0) : null;
                if (halo != null) halo.localRotation = Quaternion.Euler(now * 400f, now * 250f, 0f);
                Vector3 p = s.Go.transform.position;
                Vector3 goal = v != null ? v.transform.position + v.transform.up : p + s.Go.transform.forward * 10f;
                Vector3 d = goal - p;
                float step = 85f * Time.deltaTime;
                if (v != null && d.magnitude <= Mathf.Max(step, 2.5f))
                {
                    if (s.Target == Net.Me)
                    {
                        Vehicle mine = Car.Mine();
                        if (car.Shielded) raceway.Flash("ZAP BLOCKED!");
                        else if (mine != null) Hit(mine, "ZAPPED!");
                    }
                    fx.Burst(goal, Quaternion.identity, 0.6f);
                    fx.Wave(goal, Vector3.up, builder.Chase[1], 1f, 14f, 0.5f);
                    Object.Destroy(s.Go);
                    shots.RemoveAt(i);
                    continue;
                }
                if (d.sqrMagnitude > 0.01f) s.Go.transform.rotation = Quaternion.LookRotation(d);
                s.Go.transform.position = p + d.normalized * step;
            }
        }

        private static string NameOf(ulong id)
        {
            Player p = GameManager.GetPlayer(id);
            return p != null ? p.Name.ToUpperInvariant() : "SOMEONE";
        }

        public void Clear()
        {
            foreach (var kv in hazards) if (kv.Value != null) Object.Destroy(kv.Value);
            hazards.Clear();
            foreach (Shot s in shots) if (s.Go != null) Object.Destroy(s.Go);
            shots.Clear();
        }
    }
}
