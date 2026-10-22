using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Pigeon.Movement;
using Unity.Netcode;
using UnityEngine;

namespace HubRaceway
{
    // A racetrack woven through the hub, for driving your car: a portal in the hub takes you there (in your car, if
    // you drive through it), with a light tube whose walls and ceiling you can drive, item boxes and boost pads, lap times, and real
    // races with everyone (sign-ups, the grid, the start lights, places). A portal on the track's paddock brings you back.
    [BepInPlugin(Guid, Title, Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.hubraceway";
        public const string Title = "Quad's Hub Raceway";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        private static ConfigEntry<string> portal;
        private static ConfigEntry<int> laps;
        private static ConfigEntry<bool> testFiles;
        private Raceway raceway;
        private LevelData failedFor;
        private float nextFileCheck;

        internal static int Laps => laps.Value;

        private void Awake()
        {
            Log = Logger;
            portal = Config.Bind("Raceway", "PortalPosition", "",
                "Where the portal to the raceway stands in the hub: \"x, y, z, facing\" (facing in degrees, the way you walk through " +
                "it). Empty: the place picked for it, by the Navigation Deck.");
            laps = Config.Bind("Raceway", "RaceLaps", 3,
                new ConfigDescription("How many laps a race is, when you're the host.", new AcceptableValueRange<int>(1, 20)));
            testFiles = Config.Bind("Debug", "TestFiles", false,
                "For testing: files in the BepInEx folder act in the hub. hubraceway-go.txt takes you to the track, " +
                "hubraceway-back.txt back to the hub portal, hubraceway-portal.txt moves the hub portal to in front of you, " +
                "hubraceway-car.txt brings your car.");
            Net.OnServer = (ulong sender, ref FastBufferReader r) => raceway?.OnHostMessage(sender, ref r);
            Net.OnClient = (ref FastBufferReader r) => raceway?.OnMessage(ref r);
            Log.LogInfo(Title + " " + Version + " loaded");
        }

        // ScriptEngine (BepInEx\scripts) reloads a mod by destroying it and loading the new build: leave nothing behind.
        private void OnDestroy()
        {
            Net.Unregister();
            Net.OnServer = null;
            Net.OnClient = null;
            if (raceway != null) Destroy(raceway.gameObject);
        }

        private void Update()
        {
            // the raceway exists while you're in the hub, and is rebuilt for each visit
            bool inHub = LevelData.Instance != null && LevelData.IsHub && Player.LocalPlayer != null && PlayerLook.Instance != null;
            if (raceway != null && (!inHub || raceway.Level != LevelData.Instance))
            {
                Destroy(raceway.gameObject);
                raceway = null;
            }
            // (a build that failed isn't tried again until the next visit, not every frame)
            if (inHub && raceway == null && failedFor != LevelData.Instance)
            {
                try { raceway = Raceway.Create(LevelData.Instance); }
                catch (Exception e)
                {
                    Log.LogError("Hub Raceway: couldn't build the raceway: " + e);
                    failedFor = LevelData.Instance;
                }
            }
            if (testFiles.Value && raceway != null) TestFiles();
        }

        private void TestFiles()
        {
            if (Time.unscaledTime < nextFileCheck) return;
            nextFileCheck = Time.unscaledTime + 0.5f;
            Player player = Player.LocalPlayer;
            Vehicle vehicle = Car.Mine();
            if (Take("hubraceway-go.txt")) raceway.ToTrack(player, vehicle);
            if (Take("hubraceway-back.txt")) raceway.ToHub(player, vehicle);
            if (Take("hubraceway-car.txt")) raceway.BringCarForTest(player);
            if (Take("hubraceway-materials.txt")) Probe.Materials(raceway.transform);
            string photos = Path.Combine(Paths.BepInExRootPath, "hubraceway-photo.txt");
            if (File.Exists(photos))
            {
                string[] lines = File.ReadAllLines(photos);
                Take("hubraceway-photo.txt");
                StartCoroutine(Probe.Photos(lines));
            }
            if (Take("hubraceway-features.txt")) Probe.LogFeatures();
            if (Take("hubraceway-clear.txt")) StartCoroutine(Probe.Clearance(raceway.transform, raceway.Track));
            if (Take("hubraceway-probe-go.txt")) StartCoroutine(Probe.Run(raceway.transform, -360f, 360f, -340f, 600f, 4f));
            if (Take("hubraceway-portal.txt"))
            {
                Vector3 p = player.transform.position;
                float yaw = player.transform.eulerAngles.y;
                // a few metres ahead of you, facing the way you look
                Vector3 floor = p + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 6f;
                portal.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}, {3:0}", floor.x, floor.y, floor.z, yaw);
                raceway.MoveHubPortal(floor, yaw);
                Log.LogInfo("Hub Raceway: hub portal moved to " + portal.Value);
            }
        }

        private static bool Take(string name)
        {
            string file = Path.Combine(Paths.BepInExRootPath, name);
            if (!File.Exists(file)) return false;
            try { File.Delete(file); } catch (Exception) { }
            return true;
        }

        internal static bool TryGetGate(out Vector3 floor, out float yaw)
        {
            float[] v = Numbers(portal.Value, 4);
            floor = v != null ? new Vector3(v[0], v[1], v[2]) : default;
            yaw = v != null ? v[3] : 0f;
            return v != null;
        }

        private static float[] Numbers(string text, int count)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] parts = text.Split(',');
            if (parts.Length != count) return null;
            var v = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }
    }
}
