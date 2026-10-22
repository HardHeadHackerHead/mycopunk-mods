using System.Collections.Generic;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PullYourselfTogether
{
    // Put back together wrong. When two players are down and one's torso reaches the other's legs (crawled there, carried
    // there or thrown there), the host brings both back as one body: the legs player walks it (and has no arms: no weapons,
    // nothing to carry); the torso player rides on top, looking and shooting where they like. Everyone else sees the legs
    // player's body, its waist twisted round to where the torso player's aiming. They come apart after a while, when
    // either one holds the split key, or when either goes down again.
    //  - the host spots it and revives both (the game's own revive), then tells everyone who's riding whom;
    //  - the torso player's own machine keeps them on the legs (movement locked, moved with the legs every frame), and every
    //    machine hides the torso player's body and lets the legs player walk through where it is.
    internal static class Mixup
    {
        private sealed class Pair
        {
            public ulong Torso, Legs;
            public float Started, Ends;
            public bool Locked, CollidersOff, Announced, SplitSent;
            public float Held;
            public readonly List<Renderer> Hidden = new List<Renderer>();
        }

        private static readonly List<Pair> pairs = new List<Pair>();

        private static Pair Find(ulong client)
        {
            foreach (Pair p in pairs) if (p.Torso == client || p.Legs == client) return p;
            return null;
        }

        public static bool IsLocalLegs(Player player)
        {
            if (player == null || !player.IsOwner) return false;
            foreach (Pair p in pairs) if (p.Legs == player.OwnerClientId && player.IsAlive) return true;
            return false;
        }

        // the legs player whose waist should turn to `torso`'s aim, if `legs` is one
        public static bool TorsoOn(Player legs, out Player torso)
        {
            torso = null;
            if (legs == null) return false;
            foreach (Pair p in pairs)
                if (p.Legs == legs.OwnerClientId)
                {
                    torso = GameManager.GetPlayer(p.Torso);
                    return torso != null && torso.IsAlive && legs.IsAlive;
                }
            return false;
        }

        // --- the host ---

        public static void HostFixedUpdate()
        {
            // pairs that are over
            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                Pair p = pairs[i];
                Player a = GameManager.GetPlayer(p.Torso), b = GameManager.GetPlayer(p.Legs);
                bool over = Time.time >= p.Ends || a == null || b == null || (Time.time - p.Started > 2f && (!a.IsAlive || !b.IsAlive));
                if (over) Net.SendUnmix(p.Torso);
            }
            if (!Plugin.Mixups.Value) return;

            // a moving torso (crawling, carried or thrown) reaching another downed player's legs
            foreach (Player a in GameManager.players)
            {
                if (a == null || a.IsAlive || Find(a.OwnerClientId) != null || !Net.Modded.Contains(a.OwnerClientId)) continue;
                DeadPlayerPart torso = Parts.Torso(a);
                if (torso == null || Time.time - torso.__spawnTime < 3f) continue;
                bool crawling = Crawl.Inputs.TryGetValue(a.OwnerClientId, out Crawl.Order order) && Time.time - order.Time < 0.4f;
                bool moving = crawling || torso.IsHeldByAnyPlayer || (torso.hasBeenThrown && torso.rigidbody != null && !torso.rigidbody.IsSleeping());
                if (!moving) continue;
                foreach (Player b in GameManager.players)
                {
                    if (b == null || b == a || b.IsAlive || Find(b.OwnerClientId) != null || !Net.Modded.Contains(b.OwnerClientId)) continue;
                    DeadPlayerPart legs = Parts.Legs(b);
                    if (legs == null || !Parts.Touching(torso, legs)) continue;
                    Vector3 at = Parts.Centre(legs);
                    Plugin.Log.LogInfo($"Pull Yourself Together: {a.Name}'s torso on {b.Name}'s legs");
                    b.Resurrect_ServerRpc(at);
                    a.Resurrect_ServerRpc(at);
                    Net.SendMix(a.OwnerClientId, b.OwnerClientId, Plugin.MixSeconds.Value);
                    return;
                }
            }
        }

        public static void SplitRequested(ulong client)
        {
            Pair p = Find(client);
            if (p != null) Net.SendUnmix(p.Torso);
        }

        // --- everyone ---

        public static void Begin(ulong torso, ulong legs, float seconds)
        {
            if (Find(torso) != null || Find(legs) != null) return;
            pairs.Add(new Pair { Torso = torso, Legs = legs, Started = Time.time, Ends = Time.time + seconds });
        }

        public static void End(ulong torso)
        {
            Pair p = pairs.Find(x => x.Torso == torso);
            if (p == null) return;
            pairs.Remove(p);
            Player a = GameManager.GetPlayer(p.Torso), b = GameManager.GetPlayer(p.Legs), me = Player.LocalPlayer;
            foreach (Renderer r in p.Hidden) if (r != null) r.enabled = true;
            if (a != null && a == me)
            {
                if (p.Locked) a.MovementLocks--;
                if (a.IsAlive && b != null) a.MovePosition(b.transform.position + b.transform.right * 1.3f + Vector3.up * 0.3f);
            }
            else if (a != null && p.CollidersOff && a.IsAlive) a.EnableColliders(true);
            if (b != null && b == me && b.IsAlive) b.EquipGear(b.LastSelectedGearSlot);
            if (p.Announced && (a == me || b == me)) Chat("<color=#ffb347>Pulled apart.</color> You're your own robot again.");
        }

        public static void LateUpdate()
        {
            Player me = Player.LocalPlayer;
            foreach (Pair p in pairs)
            {
                Player a = GameManager.GetPlayer(p.Torso), b = GameManager.GetPlayer(p.Legs);
                if (a == null || b == null || !a.IsAlive || !b.IsAlive) continue;   // (not back yet, or on the way out)

                if (!p.Announced)
                {
                    p.Announced = true;
                    int secs = Mathf.RoundToInt(p.Ends - Time.time);
                    string split = $"Hold {Plugin.SplitKey.Value} to pull apart (or wait {secs} s).";
                    if (a == me) Chat($"<color=#ffb347>Put back together wrong!</color> You're the torso on {b.Name}'s legs: they walk, you shoot. {split}");
                    else if (b == me) Chat($"<color=#ffb347>Put back together wrong!</color> You're the legs under {a.Name}'s torso: you walk, they shoot. {split}");
                    else Chat($"<color=#ffb347>{a.Name}'s torso is on {b.Name}'s legs.</color>");
                }

                if (a == me)
                {
                    if (!p.Locked) { a.MovementLocks++; p.Locked = true; }
                    a.MovePositionSoft(b.transform.position);
                    Hide(p, b);   // (we're inside their body: its head would be in our face)
                }
                else
                {
                    if (!p.CollidersOff) { a.EnableColliders(false); p.CollidersOff = true; }
                    Hide(p, a);
                }
                if (b == me && b.SelectedGear != null) b.UnequipNow();

                if ((a == me || b == me) && !p.SplitSent && Keyboard.current != null)
                {
                    p.Held = Keyboard.current[Plugin.SplitKey.Value].isPressed ? p.Held + Time.deltaTime : 0f;
                    if (p.Held >= 1.2f) { p.SplitSent = true; Net.SendSplit(); }
                }
            }
        }

        private static void Hide(Pair p, Player player)
        {
            ThirdPersonRig rig = player.Animator != null ? player.Animator.ThirdPersonRig : null;
            if (rig == null) return;
            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; if (!p.Hidden.Contains(r)) p.Hidden.Add(r); }
        }

        public static void Clear()
        {
            foreach (Pair p in pairs.ToArray()) End(p.Torso);
            pairs.Clear();
        }

        private static void Chat(string text)
        {
            if (PlayerLook.Instance != null) PlayerLook.Instance.AddTextChatMessage(text, null);
        }
    }

    // the legs have no arms: whatever they'd take out, they take out nothing
    [HarmonyPatch(typeof(Player), nameof(Player.SwapToSlot))]
    internal static class LegsHaveNoArms
    {
        private static void Prefix(Player __instance, ref int slot, ref IGear gearToHold)
        {
            if (!Mixup.IsLocalLegs(__instance)) return;
            slot = 0;
            gearToHold = null;
        }
    }

    // the legs player's waist turned round to where the torso player's facing (their own legs walk the other way)
    [HarmonyPatch(typeof(PlayerAnimation), "LateUpdate")]
    internal static class TwistedWaist
    {
        private static void Postfix(PlayerAnimation __instance)
        {
            if (__instance.thirdPersonWaist == null || !Mixup.TorsoOn(__instance.playerMovement, out Player torso)) return;
            float turn = Mathf.DeltaAngle(__instance.playerMovement.transform.eulerAngles.y, torso.transform.eulerAngles.y);
            __instance.thirdPersonWaist.rotation = Quaternion.AngleAxis(turn, Vector3.up) * __instance.thirdPersonWaist.rotation;
        }
    }
}
