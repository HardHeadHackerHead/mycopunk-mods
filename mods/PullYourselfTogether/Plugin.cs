using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PullYourselfTogether
{
    // When you die your robot already comes apart, torso and legs, for your team to carry back together. This lets the torso
    // crawl back to its legs on its arms (Crawl), and lets two downed players be put back together wrong: one's torso on the
    // other's legs, one walking, one shooting (Mixup). Everyone who wants to crawl needs it; the host needs it for a smooth
    // crawl (without, the torso flops) and for mix-ups, which need both players to have it too.
    [BepInPlugin(Guid, Title, Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.pullyourselftogether";
        public const string Title = "Quad's Pull Yourself Together";
        public const string Version = "0.1.0";
        public const int Protocol = 1;   // (the messages between players: a change to them bumps it)

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled, Mixups;
        internal static ConfigEntry<float> CrawlSpeed, LookSensitivity, MixSeconds;
        internal static ConfigEntry<Key> SplitKey;
        internal static Vector3 PoseEuler = new Vector3(90f, 0f, 0f);

        private Harmony harmony;
        private GameObject runner;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("Crawling", "Enabled", true, "Crawl your torso back to your legs when you die (WASD, Space to lunge).");
            CrawlSpeed = Config.Bind("Crawling", "CrawlSpeed", 2.4f, new ConfigDescription("How fast a torso crawls, in metres a second (on the host).", new AcceptableValueRange<float>(0.5f, 6f)));
            LookSensitivity = Config.Bind("Crawling", "LookSensitivity", 0.1f, new ConfigDescription("How fast the mouse turns the crawling camera.", new AcceptableValueRange<float>(0.01f, 1f)));
            Mixups = Config.Bind("Mix-ups", "Enabled", true, "Two downed players can be put back together wrong: one's torso on the other's legs (on the host).");
            MixSeconds = Config.Bind("Mix-ups", "Seconds", 60f, new ConfigDescription("How long a mixed-up body holds together before it comes apart by itself (on the host).", new AcceptableValueRange<float>(10f, 600f)));
            SplitKey = Config.Bind("Mix-ups", "SplitKey", Key.H, "Hold it to pull a mixed-up body apart early.");
            runner = new GameObject("PullYourselfTogether");
            DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<Runner>();
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo(Title + " " + Version + " loaded");
        }

        // ScriptEngine (BepInEx\scripts) reloads a mod by destroying it and loading the new build: leave nothing behind.
        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Mixup.Clear();
            Puppet.Clear();
            Hud.Destroy();
            Net.Unregister();
            if (runner != null) Destroy(runner);
        }
    }

    internal sealed class Runner : MonoBehaviour
    {
        private float nextFileCheck;

        private void Update()
        {
            try
            {
                Net.Tick();
                Crawl.ClientUpdate();
                Puppet.Tick();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pull Yourself Together: " + e); }
            if (Time.unscaledTime >= nextFileCheck) { nextFileCheck = Time.unscaledTime + 0.5f; TestFiles(); }
        }

        private void FixedUpdate()
        {
            if (!Net.IsHost) return;
            try
            {
                Crawl.HostFixedUpdate();
                Mixup.HostFixedUpdate();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pull Yourself Together (host): " + e); }
        }

        private void LateUpdate()
        {
            try { Mixup.LateUpdate(); }
            catch (Exception e) { Plugin.Log.LogWarning("Pull Yourself Together: " + e); }
        }

        // For tests without hands on the keyboard, in the hub only (dying there costs nothing):
        //  BepInEx\pullyourselftogether-die.txt          kills you;
        //  BepInEx\pullyourselftogether-crawl.txt "x z s" crawls that way (camera-relative, like WASD) for s seconds, then
        //                                                 lunges if a 4th number is 1; "legs s" crawls to the legs;
        //  BepInEx\pullyourselftogether-pose.txt "x y z" sets how a crawling torso lies (Euler angles).
        private static void TestFiles()
        {
            if (!LevelData.IsHub) return;
            string Read(string name)
            {
                string file = Path.Combine(Paths.BepInExRootPath, "pullyourselftogether-" + name + ".txt");
                if (!File.Exists(file)) return null;
                try { string text = File.ReadAllText(file); File.Delete(file); return text; }
                catch (Exception) { return null; }
            }
            float[] Numbers(string text)
            {
                string[] bits = text.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var n = new float[bits.Length];
                for (int i = 0; i < bits.Length; i++) float.TryParse(bits[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]);
                return n;
            }

            if (Read("die") != null && Player.LocalPlayer != null && Player.LocalPlayer.IsAlive)
            {
                Plugin.Log.LogInfo("Pull Yourself Together test: dying");
                Player.LocalPlayer.RespawnPoint = null;   // (the hub's arena would bring us straight back)
                Player.LocalPlayer.Kill(null, default);
            }
            string crawl = Read("crawl");
            if (crawl != null)
            {
                float[] n = Numbers(crawl.Replace("legs", ""));
                if (crawl.Contains("legs")) Crawl.Force(Vector2.zero, n.Length > 0 ? n[0] : 10f, false, toLegs: true);
                else if (n.Length >= 3) Crawl.Force(new Vector2(n[0], n[1]), n[2], n.Length >= 4 && n[3] > 0.5f);
            }
            if (Read("bones") != null) Debug.DumpRig();
            string pose = Read("pose");
            if (pose != null)
            {
                float[] n = Numbers(pose);
                if (n.Length >= 3) { Crawl.Pose = Quaternion.Euler(n[0], n[1], n[2]); Plugin.Log.LogInfo("Pull Yourself Together test: pose " + Crawl.Pose.eulerAngles); }
            }
        }
    }
}
