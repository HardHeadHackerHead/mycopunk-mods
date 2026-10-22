using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PullYourselfTogether
{
    // The mod's own messages, between the players who have it (the game's named messages, like its "amalgamation" one).
    // The host does the physics, so it hears from the others: "I have the mod", where a dead player wants to crawl, "pull us
    // apart". Everyone hears from the host: who's been put back together wrong, and when they come apart.
    // Without the host having the mod, a player who has it can still flop their torso along (Crawl's fallback).
    internal static class Net
    {
        private const string Hello = "QuadsPYT.hello", Input = "QuadsPYT.input", Mix = "QuadsPYT.mix", Unmix = "QuadsPYT.unmix", Split = "QuadsPYT.split";

        public static bool HostHasMod { get; private set; }
        public static readonly HashSet<ulong> Modded = new HashSet<ulong>();   // (host only) the players who have it, itself included

        private static CustomMessagingManager registered;
        private static bool helloSent;

        public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public static ulong Me => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        // every frame: (re)registers with whatever session is running, and says hello to its host once
        public static void Tick()
        {
            NetworkManager nm = NetworkManager.Singleton;
            CustomMessagingManager cmm = nm != null && nm.IsListening ? nm.CustomMessagingManager : null;
            if (cmm != registered)
            {
                Unregister();
                if (cmm != null)
                {
                    registered = cmm;
                    cmm.RegisterNamedMessageHandler(Hello, OnHello);
                    cmm.RegisterNamedMessageHandler(Input, OnInput);
                    cmm.RegisterNamedMessageHandler(Mix, OnMix);
                    cmm.RegisterNamedMessageHandler(Unmix, OnUnmix);
                    cmm.RegisterNamedMessageHandler(Split, OnSplit);
                }
            }
            if (registered == null) return;
            if (nm.IsServer)
            {
                HostHasMod = true;
                Modded.Add(nm.LocalClientId);
            }
            else if (!helloSent && nm.IsConnectedClient)
            {
                helloSent = true;
                Send(Hello, NetworkManager.ServerClientId, w => w.WriteValueSafe(Plugin.Protocol));
            }
        }

        public static void Unregister()
        {
            if (registered != null)
            {
                try
                {
                    foreach (string name in new[] { Hello, Input, Mix, Unmix, Split }) registered.UnregisterNamedMessageHandler(name);
                }
                catch (System.Exception) { }   // (the session may be gone already)
            }
            registered = null;
            helloSent = false;
            HostHasMod = false;
            Modded.Clear();
            Crawl.Inputs.Clear();
        }

        private delegate void Writer(FastBufferWriter w);

        private static void Send(string name, ulong to, Writer write, NetworkDelivery delivery = NetworkDelivery.Reliable)
        {
            if (registered == null) return;
            var w = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                write(w);
                registered.SendNamedMessage(name, to, w, delivery);
            }
            finally { w.Dispose(); }
        }

        private static void SendToAll(string name, Writer write)
        {
            if (registered == null) return;
            var w = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                write(w);
                registered.SendNamedMessageToAll(name, w, NetworkDelivery.Reliable);
            }
            finally { w.Dispose(); }
        }

        // --- a dead player's crawling, to the host ---

        public static void SendInput(Vector2 move, byte lunges)
        {
            if (IsHost) { Crawl.SetInput(Me, move, lunges); return; }
            Send(Input, NetworkManager.ServerClientId, w => { w.WriteValueSafe(move); w.WriteValueSafe(lunges); }, NetworkDelivery.UnreliableSequenced);
        }

        private static void OnInput(ulong sender, FastBufferReader r)
        {
            if (!IsHost) return;
            r.ReadValueSafe(out Vector2 move);
            r.ReadValueSafe(out byte lunges);
            Crawl.SetInput(sender, move, lunges);
        }

        private static void OnHello(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int protocol);
            if (IsHost)
            {
                if (protocol == Plugin.Protocol) Modded.Add(sender);
                Plugin.Log.LogInfo($"Pull Yourself Together: player {sender} has it too{(protocol == Plugin.Protocol ? "" : " (another version: no mix-ups with them)")}");
                Send(Hello, sender, w => w.WriteValueSafe(Plugin.Protocol));
            }
            else
            {
                HostHasMod = protocol == Plugin.Protocol;
                Plugin.Log.LogInfo("Pull Yourself Together: the host has it" + (HostHasMod ? "" : " (another version: flopping only)"));
            }
        }

        // --- mix-ups: the host decides, everyone applies ---

        public static void SendMix(ulong torso, ulong legs, float seconds)
        {
            SendToAll(Mix, w => { w.WriteValueSafe(torso); w.WriteValueSafe(legs); w.WriteValueSafe(seconds); });
            Mixup.Begin(torso, legs, seconds);   // (the host too: messages to all skip the sender)
        }

        public static void SendUnmix(ulong torso)
        {
            SendToAll(Unmix, w => w.WriteValueSafe(torso));
            Mixup.End(torso);
        }

        public static void SendSplit()
        {
            if (IsHost) { Mixup.SplitRequested(Me); return; }
            Send(Split, NetworkManager.ServerClientId, w => w.WriteValueSafe((byte)1));
        }

        private static void OnMix(ulong sender, FastBufferReader r)
        {
            if (IsHost) return;
            r.ReadValueSafe(out ulong torso);
            r.ReadValueSafe(out ulong legs);
            r.ReadValueSafe(out float seconds);
            Mixup.Begin(torso, legs, seconds);
        }

        private static void OnUnmix(ulong sender, FastBufferReader r)
        {
            if (IsHost) return;
            r.ReadValueSafe(out ulong torso);
            Mixup.End(torso);
        }

        private static void OnSplit(ulong sender, FastBufferReader r)
        {
            if (IsHost) Mixup.SplitRequested(sender);
        }
    }
}
