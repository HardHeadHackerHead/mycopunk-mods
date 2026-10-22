using System.Collections.Generic;
using System.Linq;
using Pigeon.Movement;
using Unity.Netcode;
using UnityEngine;

namespace HubRaceway
{
    internal enum Phase : byte { Idle, SignUp, Countdown, Racing, Results }

    internal sealed class Entrant
    {
        public ulong Id;
        public string Name;
        public byte Slot;
        public short Laps;
        public int Progress;
        public bool Finished;
        public float FinishTime;
    }

    // A real race, run by the host: someone steps on the RACE pad and sign-ups open for everyone; when they close, everyone
    // signed up is put on the grid in their car and held there through the lights (red, orange, yellow, then green); then laps,
    // places from everyone's progress round the track, a finish order, and the results. Every player's game shows the same race:
    // the host sends its state to everyone a few times a second, and each racer sends the host how far round they are.
    internal sealed class Race
    {
        public const float SignUpSeconds = 20f, CountdownSeconds = 7f, ResultsSeconds = 15f;
        private const float AfterFirstFinish = 45f, LongestRace = 900f;
        private const byte MsgJoin = 1, MsgLeave = 2, MsgProgress = 3, MsgState = 4;

        // what every player sees (from the host)
        public Phase Phase { get; private set; }
        public float PhaseEnds { get; private set; }   // local Time.time
        public float GoTime { get; private set; }      // local Time.time the lights went green
        public byte Laps { get; private set; } = 3;
        public readonly List<Entrant> Entrants = new List<Entrant>();
        public bool HeardFromHost { get; private set; }

        // the host's own copy of the race
        private Phase hostPhase;
        private float hostEnds, hostStart, hostFirstFinish;
        private readonly List<Entrant> hostEntrants = new List<Entrant>();
        private float nextBroadcast;

        public Entrant Me
        {
            get
            {
                ulong me = Net.Me;
                foreach (Entrant e in Entrants) if (e.Id == me) return e;
                return null;
            }
        }

        public bool IAmIn => Me != null;

        // The countdown ends on everyone's own clock, not when the host's next message comes.
        public bool Live => Phase == Phase.Racing || (Phase == Phase.Countdown && Time.time >= PhaseEnds);

        public void Join() => Net.ToServer((ref FastBufferWriter w) => w.WriteValueSafe(MsgJoin));
        public void Leave() => Net.ToServer((ref FastBufferWriter w) => w.WriteValueSafe(MsgLeave));

        public void SendProgress(short laps, int progress, bool finished, float time)
        {
            Net.ToServer((ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(MsgProgress);
                w.WriteValueSafe(laps);
                w.WriteValueSafe(progress);
                w.WriteValueSafe(finished);
                w.WriteValueSafe(time);
            });
        }

        // Standings: finishers by time, then everyone else by how far round they are.
        public List<Entrant> Standings()
        {
            var list = new List<Entrant>(Entrants);
            list.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                return b.Progress.CompareTo(a.Progress);
            });
            return list;
        }

        public int PlaceOf(ulong id)
        {
            List<Entrant> s = Standings();
            for (int i = 0; i < s.Count; i++) if (s[i].Id == id) return i + 1;
            return 0;
        }

        // ---------------------------------------------------------------- the host

        public void HostTick(int lapsSetting)
        {
            if (!Net.IsServer) return;
            float now = Time.time;
            // players who left the game leave the race
            for (int i = hostEntrants.Count - 1; i >= 0; i--)
                if (!Net.Manager.ConnectedClientsIds.Contains(hostEntrants[i].Id) && hostPhase != Phase.Results) hostEntrants.RemoveAt(i);
            Phase before = hostPhase;
            switch (hostPhase)
            {
                case Phase.SignUp:
                    if (hostEntrants.Count == 0) hostPhase = Phase.Idle;
                    else if (now >= hostEnds) { hostPhase = Phase.Countdown; hostEnds = now + CountdownSeconds; Laps = (byte)Mathf.Clamp(lapsSetting, 1, 20); }
                    break;
                case Phase.Countdown:
                    if (hostEntrants.Count == 0) hostPhase = Phase.Idle;
                    else if (now >= hostEnds) { hostPhase = Phase.Racing; hostStart = now; hostFirstFinish = 0f; }
                    break;
                case Phase.Racing:
                    bool allDone = true;
                    foreach (Entrant e in hostEntrants) if (!e.Finished) allDone = false;
                    if (hostEntrants.Count == 0) hostPhase = Phase.Idle;
                    else if (allDone || (hostFirstFinish > 0f && now > hostFirstFinish + AfterFirstFinish) || now > hostStart + LongestRace)
                    {
                        hostPhase = Phase.Results;
                        hostEnds = now + ResultsSeconds;
                    }
                    break;
                case Phase.Results:
                    if (now >= hostEnds) { hostPhase = Phase.Idle; hostEntrants.Clear(); }
                    break;
            }
            if (before != hostPhase || now >= nextBroadcast) Broadcast();
        }

        // (the type already read by the raceway, which sends power-up messages on to the power-ups)
        public void OnHostMessage(ulong sender, byte type, ref FastBufferReader r)
        {
            Entrant e = hostEntrants.Find(x => x.Id == sender);
            switch (type)
            {
                case MsgJoin:
                    if (hostPhase == Phase.Idle)
                    {
                        hostPhase = Phase.SignUp;
                        hostEnds = Time.time + SignUpSeconds;
                        hostEntrants.Clear();
                    }
                    if (hostPhase == Phase.SignUp && e == null && hostEntrants.Count < Builder.GridSlots)
                    {
                        Player p = GameManager.GetPlayer(sender);
                        hostEntrants.Add(new Entrant { Id = sender, Name = p != null ? p.Name : "Player", Slot = FreeSlot(), Progress = int.MinValue / 2 });
                    }
                    break;
                case MsgLeave:
                    if (e != null && (hostPhase == Phase.SignUp || hostPhase == Phase.Countdown || hostPhase == Phase.Racing)) hostEntrants.Remove(e);
                    break;
                case MsgProgress:
                    r.ReadValueSafe(out short laps);
                    r.ReadValueSafe(out int progress);
                    r.ReadValueSafe(out bool finished);
                    r.ReadValueSafe(out float time);
                    if (e != null && hostPhase == Phase.Racing && !e.Finished)
                    {
                        e.Laps = laps;
                        e.Progress = progress;
                        if (finished)
                        {
                            e.Finished = true;
                            e.FinishTime = time;
                            if (hostFirstFinish <= 0f) hostFirstFinish = Time.time;
                        }
                    }
                    break;
            }
            Broadcast();
        }

        private byte FreeSlot()
        {
            for (byte s = 0; s < Builder.GridSlots; s++)
                if (!hostEntrants.Exists(x => x.Slot == s)) return s;
            return 0;
        }

        private void Broadcast()
        {
            nextBroadcast = Time.time + (hostPhase == Phase.Racing ? 0.25f : 1f);
            float now = Time.time;
            Phase phase = hostPhase;
            float left = Mathf.Max(0f, hostEnds - now), elapsed = phase == Phase.Racing ? now - hostStart : 0f;
            byte laps = Laps;
            Net.ToAll((ref FastBufferWriter w) =>
            {
                w.WriteValueSafe(MsgState);
                w.WriteValueSafe((byte)phase);
                w.WriteValueSafe(left);
                w.WriteValueSafe(elapsed);
                w.WriteValueSafe(laps);
                w.WriteValueSafe((byte)hostEntrants.Count);
                foreach (Entrant e in hostEntrants)
                {
                    w.WriteValueSafe(e.Id);
                    w.WriteValueSafe(e.Name ?? "", false);
                    w.WriteValueSafe(e.Slot);
                    w.WriteValueSafe(e.Laps);
                    w.WriteValueSafe(e.Progress);
                    w.WriteValueSafe(e.Finished);
                    w.WriteValueSafe(e.FinishTime);
                }
            });
        }

        // ---------------------------------------------------------------- every player

        // Returns true when it was a race message.
        public bool OnMessage(byte type, ref FastBufferReader r)
        {
            if (type != MsgState) return false;
            r.ReadValueSafe(out byte phase);
            r.ReadValueSafe(out float left);
            r.ReadValueSafe(out float elapsed);
            r.ReadValueSafe(out byte laps);
            r.ReadValueSafe(out byte count);
            Entrants.Clear();
            for (int i = 0; i < count; i++)
            {
                var e = new Entrant();
                r.ReadValueSafe(out e.Id);
                r.ReadValueSafe(out e.Name, false);
                r.ReadValueSafe(out e.Slot);
                r.ReadValueSafe(out e.Laps);
                r.ReadValueSafe(out e.Progress);
                r.ReadValueSafe(out e.Finished);
                r.ReadValueSafe(out e.FinishTime);
                Entrants.Add(e);
            }
            HeardFromHost = true;
            Laps = laps;
            var p = (Phase)phase;
            float now = Time.time;
            // keep our own clock unless it drifts (the messages take a moment to arrive)
            if (p != Phase || Mathf.Abs(now + left - PhaseEnds) > 0.4f) PhaseEnds = now + left;
            if (p == Phase.Countdown) GoTime = PhaseEnds;
            if (p == Phase.Racing && (Phase != Phase.Racing && Phase != Phase.Countdown || Mathf.Abs(now - elapsed - GoTime) > 0.4f)) GoTime = now - elapsed;
            Phase = p;
            return true;
        }

        public static bool IsRaceMessage(byte type) => type >= MsgJoin && type <= MsgState;
    }
}
