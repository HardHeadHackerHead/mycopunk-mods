using Unity.Collections;
using Unity.Netcode;

namespace HubRaceway
{
    // The raceway's own messages between players, on one named channel: players send to the host, the host answers everyone.
    // The host's own messages to itself are handled straight away (the game's network doesn't loop them back). Everyone needs
    // the mod for races and power-ups that reach other players; without it on the host, nothing comes back.
    internal static class Net
    {
        private const string Channel = "QuadsHubRaceway1";

        public delegate void Write(ref FastBufferWriter w);
        public delegate void ServerHandler(ulong sender, ref FastBufferReader r);
        public delegate void ClientHandler(ref FastBufferReader r);

        public static ServerHandler OnServer;
        public static ClientHandler OnClient;
        private static NetworkManager registeredOn;

        public static NetworkManager Manager => NetworkManager.Singleton;
        public static bool Ready => Manager != null && Manager.IsListening && Manager.CustomMessagingManager != null;
        public static bool IsServer => Ready && Manager.IsServer;
        public static ulong Me => Ready ? Manager.LocalClientId : 0UL;

        // (again whenever the game starts a new session)
        public static void Ensure()
        {
            if (!Ready || registeredOn == Manager) return;
            Unregister();
            Manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
            registeredOn = Manager;
        }

        public static void Unregister()
        {
            try
            {
                if (registeredOn != null && registeredOn.CustomMessagingManager != null)
                    registeredOn.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            }
            catch (System.Exception) { }
            registeredOn = null;
        }

        private static void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                reader.ReadValueSafe(out byte toServer);
                if (toServer == 1) { if (IsServer) OnServer?.Invoke(sender, ref reader); }
                else OnClient?.Invoke(ref reader);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Hub Raceway: a message didn't read: " + e.Message); }
        }

        public static void ToServer(Write write)
        {
            if (!Ready) return;
            Ensure();
            var w = new FastBufferWriter(64, Allocator.Temp, 8192);
            try
            {
                w.WriteValueSafe((byte)1);
                write(ref w);
                if (Manager.IsServer) Loopback(w, true);
                else Manager.CustomMessagingManager.SendNamedMessage(Channel, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            }
            finally { w.Dispose(); }
        }

        // From the host to every player (the host included).
        public static void ToAll(Write write)
        {
            if (!IsServer) return;
            Ensure();
            var w = new FastBufferWriter(64, Allocator.Temp, 8192);
            try
            {
                w.WriteValueSafe((byte)0);
                write(ref w);
                foreach (ulong id in Manager.ConnectedClientsIds)
                    if (id != Manager.LocalClientId) Manager.CustomMessagingManager.SendNamedMessage(Channel, id, w, NetworkDelivery.ReliableSequenced);
                Loopback(w, false);
            }
            finally { w.Dispose(); }
        }

        private static void Loopback(FastBufferWriter w, bool toServer)
        {
            var r = new FastBufferReader(w, Allocator.Temp);
            try
            {
                r.ReadValueSafe(out byte _);
                if (toServer) OnServer?.Invoke(Manager.LocalClientId, ref r);
                else OnClient?.Invoke(ref r);
            }
            finally { r.Dispose(); }
        }
    }
}
