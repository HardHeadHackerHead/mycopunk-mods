using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Pigeon.Movement;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace HubRaceway
{
    // The raceway while you're in the hub: the track winding round, over and under the station, the portal in the hub that
    // takes you (and your car, if you drive through) to it, the paddock with its pads (your car; a race) and race board, the
    // portal back, lap times when practising, and real races with everyone (see Race), power-ups (see Items) and the HUD.
    internal sealed class Raceway : MonoBehaviour
    {
        private const float PortalHalfWidth = 4.6f, PortalHeight = 12f, PortalDepth = 1.8f;
        private const float PadHalfSize = 3f;
        // the portals' size (1: 12 m across)
        private const float PortalScale = 0.6f;
        private const float AreaRadius = 260f;
        // where the portal stands in the hub (feet, facing the way you walk through), unless the settings say otherwise
        private static readonly Vector3 DefaultPortal = new Vector3(10.2f, 26.2f, -134.5f);
        private const float DefaultPortalYaw = 95f;

        public LevelData Level { get; private set; }
        public Race Race { get; } = new Race();
        public bool NearTrack { get; private set; }

        private Track track;
        internal Track Track => track;
        private Builder builder;
        private Car car;
        private Items items;
        private Fx fx;
        private Builder.PadParts carPadParts, racePadParts;
        private Hud hud;

        private Vector3 hubPortal, paddockPortal, carPad, racePad;
        private Quaternion hubPortalFacing, paddockPortalFacing;
        private Builder.PortalParts hubParts, paddockParts;
        private Camera trackView, hubView;
        private TextMeshPro board, boardBack;

        private Vector3 lastPos;
        private bool hasLastPos, onCarPad, onRacePad;
        private float teleportCooldownUntil;
        private int sample = -1;
        private bool onRoad;
        // when you were last on the road: only someone who just was can have fallen off it (the track runs over the hub)
        private float lastOnRoad = -100f;

        // practising
        private readonly LapCounter practice = new LapCounter();
        private bool practising;
        private double lapStart;
        private int lap;
        private float lastLap = -1f, bestLap = -1f;

        // racing (this player)
        private readonly LapCounter raceLaps = new LapCounter();
        private bool placedOnGrid, raceStarted, raceFinished;
        private int lapsDone;
        private float finishTime, nextProgress, placeOnGridAt, nextBoard;
        private int lastLightBeep = -1;

        private string flash;
        private float flashUntil;

        public static Raceway Create(LevelData level)
        {
            var go = new GameObject("QuadsHubRaceway");
            var r = go.AddComponent<Raceway>();
            r.Level = level;
            try { r.Build(); }
            catch (Exception) { Destroy(go); throw; }
            return r;
        }

        private void Build()
        {
            track = new Track();
            builder = new Builder(transform, track);
            builder.MakeMaterials();
            builder.BuildTrack();
            car = new Car();
            fx = new Fx(builder, transform);
            items = new Items(this, track, builder, car, fx);
            items.Build();

            // the paddock's launch deck: your car, the portal home and a race on their stations in front of where you arrive;
            // the race board behind the race
            Vector3 across = builder.PaddockAcross;
            carPad = builder.StationTop(-1);
            racePad = builder.StationTop(1);
            carPadParts = builder.Pad(carPad, PadHalfSize, "STEP ON FOR", "YOUR CAR", Builder.Orange);
            racePadParts = builder.Pad(racePad, PadHalfSize, "STEP ON TO", "RACE!", Builder.Cyan);
            builder.StationSign(-1, "YOUR CAR", builder.Glow);
            builder.StationSign(0, "BACK TO THE HUB", builder.HubGlow);
            builder.StationSign(1, "RACE!", builder.EdgeGlow);
            Vector3 toBoard = builder.BoardSpot - builder.Arrival;
            board = builder.Board(builder.BoardSpot, Quaternion.LookRotation(new Vector3(toBoard.x, 0f, toBoard.z)), 14f, 7f, out boardBack);
            paddockPortal = builder.StationTop(0);
            paddockPortalFacing = Quaternion.LookRotation(across);

            if (!Plugin.TryGetGate(out hubPortal, out float yaw)) { hubPortal = DefaultPortal; yaw = DefaultPortalYaw; }
            hubPortalFacing = Quaternion.Euler(0f, yaw, 0f);
            hubPortal = OnFloor(ClearOfWall(hubPortal, hubPortalFacing));

            // live views through the portals: the start straight from the hub; the hub from the paddock
            int v = track.Wrap(-45);
            RenderTexture trackTex = NewView(out trackView, track.At(v, 0f, 7f), Quaternion.LookRotation(track.Fwd[v] - track.Up[v] * 0.12f, Vector3.up));
            RenderTexture hubTex = NewView(out hubView, hubPortal + Vector3.up * 3.5f + hubPortalFacing * Vector3.back * 1.5f, hubPortalFacing * Quaternion.Euler(8f, 180f, 0f));
            hubParts = builder.Portal("HubPortal", hubPortal, hubPortalFacing, "HUB RACEWAY", "STEP THROUGH TO RACE", trackTex, Mathf.Min(PortalScale, FitUnderCeiling(hubPortal)));
            paddockParts = builder.Portal("PaddockPortal", paddockPortal, paddockPortalFacing, "", "", hubTex, PortalScale);
            builder.FlushBatches();

            // (rebuilt under someone driving it, as when the mod reloads: they get put back on the road if they drop)
            if (Car.Mine() != null) lastOnRoad = Time.time;
            hud = new Hud(builder.Font);
            bestLap = LoadBest();
            Net.Ensure();
            Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Hub Raceway: track built, {0:0} m a lap ({1} samples), tunnel {2}-{3}, hub portal at {4:0.0},{5:0.0},{6:0.0} facing {7:0}",
                track.Length, track.Count, builder.TunnelFrom, builder.TunnelTo, hubPortal.x, hubPortal.y, hubPortal.z, yaw));
        }

        // The portal's inlay is 5 m deep: if a wall stands right behind where it's wanted, bring it forward off the wall.
        private static Vector3 ClearOfWall(Vector3 floor, Quaternion facing)
        {
            Vector3 fwd = facing * Vector3.forward;
            if (Physics.Raycast(floor + Vector3.up * 2f, fwd, out RaycastHit hit, 6f, 8193, QueryTriggerInteraction.Ignore) && hit.distance < 3f)
                floor -= fwd * (3f - hit.distance);
            return floor;
        }

        // Right on the floor under a point (so the portal stands on it, not in it or above it).
        private static Vector3 OnFloor(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, 8193, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            return at;
        }

        // How big the portal can be under the ceiling where it stands (16 m tall at full size, sign included).
        private static float FitUnderCeiling(Vector3 floor)
        {
            if (!Physics.Raycast(floor + Vector3.up, Vector3.up, out RaycastHit hit, 40f, 8193, QueryTriggerInteraction.Ignore)) return 1f;
            return Mathf.Clamp((hit.distance + 1f - 0.6f) / 17f, 0.4f, 1f);
        }

        private RenderTexture NewView(out Camera cam, Vector3 at, Quaternion rotation)
        {
            var rt = new RenderTexture(512, 512, 24) { name = "HubRacewayView" };
            builder.Owned.Add(rt);
            var go = new GameObject("PortalView");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(at, rotation);
            cam = go.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            cam.enabled = false;
            return rt;
        }

        private void OnDestroy()
        {
            try { car?.ReleaseAll(); } catch (Exception) { }
            // (the portal cameras let go of their pictures before the pictures go)
            if (trackView != null) trackView.targetTexture = null;
            if (hubView != null) hubView.targetTexture = null;
            items?.Clear();
            fx?.Clear();
            hud?.Destroy();
            if (Race.IAmIn && Race.Phase != Phase.Idle && Race.Phase != Phase.Results) try { Race.Leave(); } catch (Exception) { }
            if (builder != null) foreach (UnityEngine.Object o in builder.Owned) if (o != null) Destroy(o);
        }

        // ---------------------------------------------------------------- messages

        public void OnHostMessage(ulong sender, ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte type);
            if (Race.IsRaceMessage(type)) Race.OnHostMessage(sender, type, ref r);
            else items.OnHostMessage(sender, type, ref r);
        }

        public void OnMessage(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte type);
            if (!Race.OnMessage(type, ref r)) items.OnMessage(type, ref r);
        }

        // ---------------------------------------------------------------- every frame

        private void Update()
        {
            Player player = Player.LocalPlayer;
            if (player == null) return;
            Net.Ensure();
            Race.HostTick(Plugin.Laps);

            Vehicle vehicle = player.CurrentVehicle;
            Vehicle mine = Car.Mine();
            // a passenger goes wherever the driver takes the car
            bool passenger = vehicle != null && mine == null;
            Vector3 pos = Pos(player, mine);

            sample = track.Nearest(pos, sample, out _);
            Vector3 local = track.Local(sample, pos);
            // (in the tube: anywhere inside it, walls and ceiling too)
            onRoad = track.InTunnel(sample)
                ? Mathf.Abs(local.x) < Track.TubeHalfWidth + 1f && local.y > -2.5f && local.y < Track.TubeHeight + 2f
                : Mathf.Abs(local.x) < Track.HalfWidth + 1.5f && local.y > -2.5f && local.y < 7f;
            if (onRoad) lastOnRoad = Time.time;
            Vector3 flat = pos - track.Pos[sample];
            NearTrack = new Vector2(flat.x, flat.z).sqrMagnitude < AreaRadius * AreaRadius && Mathf.Abs(flat.y) < 150f;

            // the portals, and stepping on the pads
            if (!passenger && Time.time >= teleportCooldownUntil && hasLastPos && player.IsAlive)
            {
                if (Through(hubPortal, hubPortalFacing, lastPos, pos)) { ToTrack(player, mine); pos = Pos(player, mine); }
                else if (Through(paddockPortal, paddockPortalFacing, lastPos, pos)) { ToHub(player, mine); pos = Pos(player, mine); }
            }
            lastPos = pos;
            hasLastPos = true;
            bool onCar = vehicle == null && OnPad(pos, carPad), onRace = vehicle == null && OnPad(pos, racePad);
            if (onCar && !onCarPad && player.IsAlive) { PadFx(carPad, Builder.Orange); Guard(() => BringCar(player, -1)); }
            if (onRace && !onRacePad) { PadFx(racePad, Builder.Cyan); JoinRace(); }
            onCarPad = onCar;
            onRacePad = onRace;

            RaceTick(player, mine);
            if (!(Race.IAmIn && (Race.Phase == Phase.Countdown || Race.Live))) Practice();
            FellOff(player, mine, pos);
            items.Tick(mine, onRoad);
            fx.Tick();

            Animate();
            UpdateBoard();
            UpdateHud(mine);
        }

        private void FixedUpdate()
        {
            if (track == null) return;
            car.FixedTick(track, sample, onRoad);
        }

        private static Vector3 Pos(Player player, Vehicle mine) => mine != null ? mine.Rigidbody.position : player.PhysicsPosition;

        private static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.Log.LogError("Hub Raceway: " + e); }
        }

        private static bool OnPad(Vector3 p, Vector3 pad)
        {
            Vector3 d = p - pad;
            return new Vector2(d.x, d.z).sqrMagnitude < PadHalfSize * PadHalfSize && d.y > -1f && d.y < 3f;
        }

        // Stepping on a pad: it flashes and rings out.
        private void PadFx(Vector3 pad, Color c)
        {
            fx.Burst(pad + Vector3.up, Quaternion.identity, 0.5f);
            fx.Wave(pad + Vector3.up * 0.15f, Vector3.up, c == Builder.Cyan ? builder.EdgeGlow : builder.Glow, 1f, 9f, 0.5f);
            Sound.Play("UI_Directive_Active");
        }

        // Whether going from a to b passed through a portal (or ended inside it): fast cars skip past a thin box in one frame.
        private static bool Through(Vector3 floor, Quaternion facing, Vector3 a, Vector3 b)
        {
            Quaternion inv = Quaternion.Inverse(facing);
            Vector3 la = inv * (a - floor), lb = inv * (b - floor);
            if (InPortal(lb)) return true;
            if ((la.z < 0f) == (lb.z < 0f) || Mathf.Abs(la.z - lb.z) > 30f) return false;
            Vector3 cross = Vector3.Lerp(la, lb, la.z / (la.z - lb.z));
            return Mathf.Abs(cross.x) < PortalHalfWidth && cross.y > -1.5f && cross.y < PortalHeight;
        }

        private static bool InPortal(Vector3 l) => Mathf.Abs(l.x) < PortalHalfWidth && l.y > -1.5f && l.y < PortalHeight && Mathf.Abs(l.z) < PortalDepth;

        // ---------------------------------------------------------------- moving you about

        public void ToTrack(Player player, Vehicle driving)
        {
            if (driving != null)
            {
                // on the road behind the grid, a little to one side so two cars arriving together don't land in each other
                int at = track.Wrap(-70);
                MoveVehicle(driving, track.At(at, UnityEngine.Random.Range(-7f, 7f), 1.2f), track.Facing(at));
            }
            else
            {
                MovePlayer(player, builder.Arrival + Vector3.up * 0.3f, builder.PaddockAcross);
                fx.Wave(builder.Arrival + Vector3.up * 0.2f, Vector3.up, builder.HubGlow, 0.5f, 6f, 0.6f);
            }
            practice.Reset();
            practising = false;
            Sound.Play("Play_Teleport_Outro");
            Plugin.Log.LogInfo("Hub Raceway: to the track" + (driving != null ? " in your car" : ""));
        }

        public void ToHub(Player player, Vehicle driving)
        {
            // in front of the hub portal, facing away from it
            Vector3 back = hubPortalFacing * Vector3.back;
            Vector3 at = hubPortal + back * 6f;
            if (driving != null) MoveVehicle(driving, at + Vector3.up * 1.2f, Quaternion.LookRotation(back));
            else MovePlayer(player, at + Vector3.up * 0.3f, back);
            practice.Reset();
            practising = false;
            lastOnRoad = -100f;
            if (Race.IAmIn && Race.Phase != Phase.Results) { Race.Leave(); Flash("YOU LEFT THE RACE"); }
            Sound.Play("Play_Teleport_Outro");
            Plugin.Log.LogInfo("Hub Raceway: back to the hub" + (driving != null ? " in your car" : ""));
        }

        private void MovePlayer(Player player, Vector3 at, Vector3 look)
        {
            player.MovePosition(at, allowUndoTeleport: false);
            float yaw = Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y;
            PlayerLook.Instance?.RotateY(Mathf.DeltaAngle(player.transform.localEulerAngles.y, yaw));
            Arrived();
        }

        private void MoveVehicle(Vehicle vehicle, Vector3 at, Quaternion rotation)
        {
            Rigidbody body = vehicle.Rigidbody;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = at;
            body.rotation = rotation;
            vehicle.transform.SetPositionAndRotation(at, rotation);
            Physics.SyncTransforms();
            vehicle.OnWarpIn();
            Arrived();
        }

        private void Arrived()
        {
            teleportCooldownUntil = Time.time + 1.5f;
            hasLastPos = false;
            try { Menu.PlayRevealEffect(); } catch (Exception) { }
        }

        // Your own car (the one picked in your gear) onto the grid (a slot, or anywhere behind it) with you in the driver's
        // seat, the way the printer brings it: the one you have if nobody's driving it, otherwise a new one (the game takes the
        // old one away).
        private void BringCar(Player player, int slot)
        {
            Vector3 at;
            Quaternion rotation;
            if (slot >= 0) at = builder.GridSlot(slot, out rotation, out int gs) + track.Up[gs] * 1.2f;
            else
            {
                int s = track.Wrap(-70);
                at = track.At(s, UnityEngine.Random.Range(-7f, 7f), 1.2f);
                rotation = track.Facing(s);
            }
            Vehicle mine = Player.PersonalVehicle;
            if (mine != null && mine.IsOwner && mine.DefaultSeat.sittingPlayer == null)
            {
                MoveVehicle(mine, at, rotation);
                mine.DefaultSeat.Interact(player);
                Plugin.Log.LogInfo("Hub Raceway: your car brought to the grid");
                return;
            }
            Vehicle prefab = null;
            PlayerData.GearData gear = PlayerData.GetGearData(PlayerData.Instance.personalVehicleID);
            if (gear != null) prefab = gear.Gear as Vehicle;
            if (prefab == null) prefab = Global.Instance.DefaultVehicle;
            // (from the prefab itself, as the game does: NetworkBehaviour.NetworkObject only finds it on a spawned car)
            ulong id = prefab.GetComponent<NetworkObject>().PrefabIdHash;
            GameManager.Instance.SpawnPersonalVehicle_ServerRpc(id, player.OwnerClientId, at, rotation);
            Arrived();
            Plugin.Log.LogInfo("Hub Raceway: a new car on the grid (" + prefab.name + ")");
        }

        // ---------------------------------------------------------------- races

        private void JoinRace()
        {
            if (!Net.Ready) { Flash("RACES NEED AN ONLINE GAME"); return; }
            if (Race.Phase == Phase.Idle || Race.Phase == Phase.SignUp)
            {
                if (Race.IAmIn) { Flash("YOU'RE ALREADY SIGNED UP"); return; }
                Race.Join();
                Flash(Race.Phase == Phase.Idle ? "RACE SIGN-UPS OPEN!" : "SIGNED UP!");
                Sound.Play("UI_Directive_Active");
            }
            else Flash("A RACE IS ON: WAIT FOR THE NEXT ONE");
        }

        private void RaceTick(Player player, Vehicle mine)
        {
            Entrant me = Race.Me;
            Phase phase = Race.Phase;
            if (me == null || phase == Phase.Idle || phase == Phase.SignUp)
            {
                if (car.Frozen) car.Frozen = false;
                placedOnGrid = raceStarted = raceFinished = false;
                return;
            }

            if (phase == Phase.Countdown && !Race.Live)
            {
                // onto your grid slot, in your car, held there until the lights go green
                Vector3 slot = builder.GridSlot(me.Slot, out Quaternion rot, out int s) + track.Up[s] * 1.2f;
                if (mine != null)
                {
                    if (!placedOnGrid) { MoveVehicle(mine, slot, rot); placedOnGrid = true; }
                    car.Freeze(slot, rot);
                }
                else if (Time.time >= placeOnGridAt && player.IsAlive)
                {
                    placeOnGridAt = Time.time + 3f;
                    Guard(() => BringCar(player, me.Slot));
                    placedOnGrid = true;
                }
                raceStarted = raceFinished = false;
                return;
            }

            if (!Race.Live)
            {
                if (car.Frozen) car.Frozen = false;
                return;
            }
            if (!raceStarted)
            {
                raceStarted = true;
                car.Frozen = false;
                raceLaps.Reset();
                lapsDone = 0;
                Sound.Play("UI_Directive_Complete");
            }
            if (raceFinished) return;
            if (onRoad && raceLaps.Step(track, sample) == LapCounter.Event.Crossed && raceLaps.ThirdsDone)
            {
                lapsDone++;
                raceLaps.ResetThirds();
                if (lapsDone >= Race.Laps)
                {
                    raceFinished = true;
                    finishTime = Time.time - Race.GoTime;
                    Race.SendProgress((short)lapsDone, lapsDone * track.Count, true, finishTime);
                    Flash("FINISHED  " + Clock(finishTime));
                    Sound.Play("UI_Directive_Complete");
                    Plugin.Log.LogInfo("Hub Raceway: finished the race in " + Clock(finishTime));
                    return;
                }
                Flash(lapsDone == Race.Laps - 1 ? "FINAL LAP!" : "LAP " + (lapsDone + 1) + "/" + Race.Laps);
                Sound.Play("UI_Directive_Active");
            }
            if (Time.time >= nextProgress)
            {
                nextProgress = Time.time + 0.2f;
                int idx = raceLaps.Index < 0 ? track.Count - 1 : raceLaps.Index;
                // (behind the line before the first lap counts as behind it)
                if (!raceLaps.FirstThird && idx > track.Count * 0.8f) idx -= track.Count;
                Race.SendProgress((short)lapsDone, lapsDone * track.Count + idx, false, 0f);
            }
        }

        // ---------------------------------------------------------------- practising, falling off

        private void Practice()
        {
            if (!NearTrack) { practice.Reset(); practising = false; return; }
            if (!onRoad) return;
            LapCounter.Event ev = practice.Step(track, sample);
            if (ev == LapCounter.Event.Back) { practising = false; return; }
            if (ev != LapCounter.Event.Crossed) return;
            double now = Time.timeAsDouble;
            if (practising && practice.ThirdsDone)
            {
                lastLap = (float)(now - lapStart);
                if (bestLap < 0f || lastLap < bestLap)
                {
                    bestLap = lastLap;
                    SaveBest(bestLap);
                    Flash("NEW BEST  " + Clock(bestLap));
                }
                else Flash("LAP  " + Clock(lastLap));
                Plugin.Log.LogInfo("Hub Raceway: practice lap in " + Clock(lastLap));
                lap++;
            }
            else lap = 1;
            practising = true;
            lapStart = now;
            practice.ResetThirds();
        }

        private void FellOff(Player player, Vehicle mine, Vector3 pos)
        {
            if (mine == null && player.CurrentVehicle != null) return;
            if (!NearTrack || Time.time < teleportCooldownUntil || Time.time - lastOnRoad > 6f) return;
            Vector3 l = track.Local(sample, pos);
            // well below the road (or out of the tube, somehow)
            bool off = track.InTunnel(sample)
                ? l.y < -6f || l.y > Track.TubeHeight + 6f || Mathf.Abs(l.x) > Track.TubeHalfWidth + 6f
                : pos.y < track.Pos[sample].y - 30f || l.y < -25f;
            if (!off) return;
            LapCounter c = Race.IAmIn && Race.Live ? raceLaps : practice;
            int at = c.LastGood >= 0 ? track.Wrap(c.LastGood - 4) : track.Wrap(-70);
            // (not back onto the tube's ceiling: before it)
            if (builder.InTunnel(at)) at = track.Wrap(builder.TunnelFrom - 6);
            if (mine != null) MoveVehicle(mine, track.At(at, 0f, 1.2f), track.Facing(at));
            else MovePlayer(player, track.At(at, 0f, 0.5f), track.Fwd[at]);
            c.Index = at;
            Flash("BACK ON TRACK");
            Plugin.Log.LogInfo("Hub Raceway: fell off, back on the road");
        }

        // ---------------------------------------------------------------- looks

        private void Animate()
        {
            float t = Time.time;
            // the tunnel's lights chase along it
            Color[] chase = { Builder.Cyan, Builder.Magenta, Builder.Orange };
            for (int k = 0; k < 3; k++)
                Builder.SetEmission(builder.Chase[k], chase[k] * (0.25f + 2.2f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 7f - k * 2.094f)), 3f)));

            // the start lights: red, orange, yellow one by one in the last three seconds, then all green
            Color[] lit = { Color.black, Color.black, Color.black, Color.black };
            float left = Race.PhaseEnds - t;
            if (Race.Phase == Phase.Countdown && !Race.Live)
            {
                if (left <= 3f) lit[0] = new Color(1f, 0.05f, 0.05f) * 3f;
                if (left <= 2f) lit[1] = new Color(1f, 0.45f, 0f) * 3f;
                if (left <= 1f) lit[2] = new Color(1f, 0.9f, 0f) * 3f;
                int beep = left <= 1f ? 3 : left <= 2f ? 2 : left <= 3f ? 1 : 0;
                if (beep > 0 && beep != lastLightBeep && (NearTrack || Race.IAmIn)) Sound.Play("Play_Satellite_Alignment_Beep");
                lastLightBeep = beep;
            }
            else
            {
                if (Race.Live && t - Race.GoTime < 4f) for (int k = 0; k < 4; k++) lit[k] = new Color(0.1f, 1f, 0.2f) * 3f;
                lastLightBeep = -1;
            }
            for (int k = 0; k < 4; k++) Builder.SetEmission(builder.StartLights[k], lit[k]);

            // the portals turn and pulse; each shows its live view while you're near enough to see it
            Vector3 me = Player.LocalPlayer.transform.position;
            AnimatePortal(hubParts, t, (me - hubPortal).sqrMagnitude < 90f * 90f, trackView);
            AnimatePortal(paddockParts, t, (me - paddockPortal).sqrMagnitude < 90f * 90f, hubView);
            string status = PortalStatus();
            if (hubParts.Status != null && hubParts.Status.text != status) { hubParts.Status.text = status; hubParts.StatusBack.text = status; }

            // the pads glow in and out, their signs turn and bob
            foreach (Builder.PadParts p in new[] { carPadParts, racePadParts })
            {
                Builder.SetEmission(p.Edge, p.Color * (0.8f + 0.8f * (0.5f + 0.5f * Mathf.Sin(t * 3f))));
                // (facing you; Text faces the reader along its forward, so it looks away from the camera)
                Vector3 look = p.Holo.position - (Camera.main != null ? Camera.main.transform.position : me);
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f) p.Holo.rotation = Quaternion.LookRotation(-look);
                p.Holo.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t * 2f));
            }
        }

        private static void AnimatePortal(Builder.PortalParts p, float t, bool near, Camera view)
        {
            p.RingA.localRotation = Quaternion.Euler(0f, 0f, t * 35f);
            p.RingB.localRotation = Quaternion.Euler(0f, 0f, -t * 55f);
            Builder.SetEmission(p.Surface, Color.white * (0.85f + 0.15f * Mathf.Sin(t * 3f)));
            // rendered by hand (every other frame near it, every few seconds from afar), without the pass that copies the
            // player's HUD onto every camera
            if (view != null && (near ? Time.frameCount % 2 == 0 : Time.frameCount % 180 == 0)) Probe.RenderWithout(view, HudPasses);
        }

        private static readonly string[] HudPasses = { "BlitRenderTexture" };

        private string PortalStatus()
        {
            switch (Race.Phase)
            {
                case Phase.SignUp: return "RACE SIGN-UPS OPEN: " + Mathf.CeilToInt(Race.PhaseEnds - Time.time) + "s";
                case Phase.Countdown: return "RACE STARTING!";
                case Phase.Racing: return "RACE IN PROGRESS";
                case Phase.Results: return "RACE RESULTS ON THE PADDOCK";
                default: return "STEP THROUGH TO RACE";
            }
        }

        private void UpdateBoard()
        {
            if (Time.time < nextBoard) return;
            nextBoard = Time.time + 0.25f;
            var sb = new StringBuilder();
            sb.Append("<size=150%><color=#FF7A3D>RACE CONTROL</color></size>\n");
            switch (Race.Phase)
            {
                case Phase.Idle:
                    sb.Append("Step on the <color=#35D8FF>RACE!</color> pad to open sign-ups\nfor everyone. ").Append(Plugin.Laps).Append(" laps.\n\n");
                    sb.Append("Item boxes give power-ups: fire them with <color=#35D8FF>Q</color>.\n\n");
                    sb.Append("Your best lap: ").Append(bestLap > 0f ? Clock(bestLap) : "none yet");
                    break;
                case Phase.SignUp:
                    sb.Append("SIGN-UPS CLOSE IN <color=#35D8FF>").Append(Mathf.CeilToInt(Race.PhaseEnds - Time.time)).Append("</color>\n\n");
                    foreach (Entrant e in Race.Entrants) sb.Append("Grid ").Append(e.Slot + 1).Append(":  ").Append(e.Name).Append('\n');
                    break;
                case Phase.Countdown:
                    sb.Append("<size=130%>GET READY!</size>\n\n");
                    foreach (Entrant e in Race.Entrants) sb.Append("Grid ").Append(e.Slot + 1).Append(":  ").Append(e.Name).Append('\n');
                    break;
                default:
                    sb.Append(Race.Phase == Phase.Results ? "<size=130%>RESULTS</size>\n\n" : "<size=130%>RACE ON</size>\n\n");
                    sb.Append(StandingsText(true));
                    break;
            }
            string text = sb.ToString();
            if (board.text != text) { board.text = text; boardBack.text = text; }
        }

        private string StandingsText(bool times)
        {
            var sb = new StringBuilder();
            List<Entrant> s = Race.Standings();
            ulong me = Net.Me;
            for (int i = 0; i < s.Count; i++)
            {
                Entrant e = s[i];
                string name = e.Name.Length > 14 ? e.Name.Substring(0, 14) : e.Name;
                if (e.Id == me) sb.Append("<color=#FFD24A>");
                sb.Append(Ordinal(i + 1)).Append("  ").Append(name);
                if (e.Finished && times) sb.Append("  ").Append(Clock(e.FinishTime));
                else if (e.Finished) sb.Append("  <color=#9AFF9A>FIN</color>");
                else sb.Append("  <size=80%>LAP ").Append(Mathf.Clamp(e.Laps + 1, 1, Race.Laps)).Append("</size>");
                if (e.Id == me) sb.Append("</color>");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private void UpdateHud(Vehicle mine)
        {
            float t = Time.time;
            string top = "", list = "", lights = "", center = "", banner = "", item = "", flashText = "";
            Entrant me = Race.Me;

            if (me != null && Race.Live)
            {
                int place = Race.PlaceOf(Net.Me);
                string time = raceFinished ? Clock(finishTime) : Clock(t - Race.GoTime);
                top = "<size=170%><color=#FFD24A>" + Ordinal(place) + "</color></size><size=90%> / " + Race.Entrants.Count + "</size>      LAP "
                    + Mathf.Min(lapsDone + 1, Race.Laps) + "/" + Race.Laps + "      " + time;
                list = StandingsText(false);
                if (raceFinished) center = t - Race.GoTime - finishTime < 5f ? "<color=#FFD24A>" + Ordinal(place) + "!</color>" : "";
                else if (t - Race.GoTime < 1.2f) center = "<color=#3CFF5A>GO!</color>";
            }
            else if (me != null && Race.Phase == Phase.Countdown)
            {
                top = "GRID SLOT " + (me.Slot + 1) + "      " + Race.Laps + " LAPS";
                list = StandingsText(false);
                center = Race.PhaseEnds - t > 3f ? "GET READY" : "";
            }
            else if (NearTrack && practising)
            {
                top = "<size=130%>PRACTICE  LAP " + lap + "   " + Clock((float)(Time.timeAsDouble - lapStart)) + "</size>\nBEST "
                    + (bestLap > 0f ? Clock(bestLap) : "--:--.--") + (lastLap > 0f ? "     LAST " + Clock(lastLap) : "");
            }
            else if (NearTrack && onRoad)
            {
                top = "<size=110%>Cross the line to start a practice lap</size>\nBEST " + (bestLap > 0f ? Clock(bestLap) : "--:--.--");
            }

            // the lights, for anyone near the track
            if (NearTrack || me != null)
            {
                if (Race.Phase == Phase.Countdown && !Race.Live)
                {
                    float left = Race.PhaseEnds - t;
                    lights = Light(left <= 3f ? "#FF2020" : "#3A3A3A") + Light(left <= 2f ? "#FF7A00" : "#3A3A3A") + Light(left <= 1f ? "#FFE600" : "#3A3A3A") + Light("#3A3A3A");
                }
                else if (Race.Live && t - Race.GoTime < 1.5f) lights = Light("#3CFF5A") + Light("#3CFF5A") + Light("#3CFF5A") + Light("#3CFF5A");
            }

            // sign-ups reach everyone in the hub; results everyone near the track
            if (Race.Phase == Phase.SignUp)
            {
                int left = Mathf.CeilToInt(Race.PhaseEnds - t);
                banner = me != null
                    ? "<color=#3CFF5A>YOU'RE IN THE RACE!</color>  Grid slot " + (me.Slot + 1) + ".  Starts in " + left + "s"
                    : "<color=#35D8FF>RACE SIGN-UPS OPEN: " + left + "s</color>\n<size=80%>Go through the HUB RACEWAY portal and step on the RACE! pad on the paddock</size>";
            }
            else if (Race.Phase == Phase.Results && (NearTrack || me != null))
                banner = "<size=130%><color=#FFD24A>RACE RESULTS</color></size>\n" + StandingsText(true);

            if (mine != null)
            {
                if (items.RollingUntil > 0f) item = "<color=#FF7A3D>" + items.RollingName() + "</color>";
                else if (items.Held != Item.None)
                    item = "<size=70%><color=#35D8FF>[Q]</color></size>  " + Items.Name(items.Held) + (items.Charges > 1 ? "  x" + items.Charges : "");
            }
            if (flash != null && t < flashUntil) flashText = "<color=#FF7A3D>" + flash + "</color>";
            hud.Show(top, list, lights, center, banner, item, flashText);
        }

        private static string Light(string color) => "<color=" + color + ">●</color> ";

        public void Flash(string text)
        {
            flash = text;
            flashUntil = Time.time + 2.5f;
        }

        private static string Ordinal(int n)
        {
            if (n <= 0) return "-";
            int t = n % 100;
            string s = t >= 11 && t <= 13 ? "th" : (n % 10) == 1 ? "st" : (n % 10) == 2 ? "nd" : (n % 10) == 3 ? "rd" : "th";
            return n + s;
        }

        private static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            return m + ":" + (seconds - m * 60f).ToString("00.00", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- your best lap

        // Your best practice lap on this layout, kept beside the mod's settings.
        private static string BestFile => Path.Combine(Paths.ConfigPath, Plugin.Guid + ".times.txt");

        private static float LoadBest()
        {
            try
            {
                if (!File.Exists(BestFile)) return -1f;
                foreach (string line in File.ReadAllLines(BestFile))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length == 2 && parts[0] == Track.Id && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                        return s;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hub Raceway: couldn't read your best lap: " + e.Message); }
            return -1f;
        }

        private static void SaveBest(float seconds)
        {
            try { File.WriteAllText(BestFile, Track.Id + " " + seconds.ToString("0.000", CultureInfo.InvariantCulture) + "\n"); }
            catch (Exception e) { Plugin.Log.LogWarning("Hub Raceway: couldn't save your best lap: " + e.Message); }
        }

        // ---------------------------------------------------------------- for testing

        public void MoveHubPortal(Vector3 floor, float yaw)
        {
            hubPortalFacing = Quaternion.Euler(0f, yaw, 0f);
            hubPortal = ClearOfWall(floor, hubPortalFacing);
            if (hubParts != null) hubParts.Root.SetPositionAndRotation(hubPortal, hubPortalFacing);
            teleportCooldownUntil = Time.time + 2f;
        }

        public void BringCarForTest(Player player) => Guard(() => BringCar(player, -1));
    }

    // Counts laps round the track: where you are along it, whether you've been a third and two thirds of the way round (so a
    // lap can't be cut short), and crossing the line either way.
    internal sealed class LapCounter
    {
        public enum Event { None, Crossed, Back }

        public int Index = -1, LastGood = -1;
        public bool FirstThird { get; private set; }
        private bool secondThird;
        public bool ThirdsDone => FirstThird && secondThird;

        public Event Step(Track track, int near)
        {
            int prev = Index;
            Index = near;
            LastGood = near;
            if (prev < 0) return Event.None;
            int n = track.Count, third = n / 3;
            if (Mathf.Abs(near - third) < 30) FirstThird = true;
            if (FirstThird && Mathf.Abs(near - third * 2) < 30) secondThird = true;
            if (prev < 30 && near > n - 30) { ResetThirds(); return Event.Back; }
            if (prev > n - 30 && near < 30) return Event.Crossed;
            return Event.None;
        }

        public void ResetThirds() { FirstThird = secondThird = false; }

        public void Reset()
        {
            Index = -1;
            LastGood = -1;
            ResetThirds();
        }
    }
}
