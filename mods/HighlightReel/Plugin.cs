using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HighlightReel
{
    // Your best moments of a mission, caught as they happen (boss kills, multi-kills, close calls, being brought back), shown as a
    // photo strip on the mission's reward screen and saved as pictures. Client only: it looks at your screen, nothing else.
    [BepInPlugin(Guid, Title, Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.highlightreel";
        public const string Title = "Quad's Highlight Reel";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Bosses, MultiKills, CloseCalls, Revives, SaveToDiskEntry, TestKey;
        internal static ConfigEntry<int> MultiKillCount, MaxShots;
        internal static ConfigEntry<float> MultiKillWindow, CloseCallHealth;
        internal static ConfigEntry<string> Folder;
        internal static ConfigEntry<bool> SaveToDisk => SaveToDiskEntry;

        internal static string SaveFolder => string.IsNullOrWhiteSpace(Folder.Value)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Mycopunk Highlights")
            : Folder.Value;

        private Harmony harmony;
        private GameObject runner;
        private LevelData level;
        private float nextFileCheck;

        private void Awake()
        {
            Log = Logger;
            Bosses = Config.Bind("Moments", "Bosses", true, "A picture when you kill a boss or an Abomination.");
            MultiKills = Config.Bind("Moments", "MultiKills", true, "A picture when you kill several enemies in quick succession.");
            MultiKillCount = Config.Bind("Moments", "MultiKillCount", 3, new ConfigDescription("How many kills make a multi-kill.", new AcceptableValueRange<int>(2, 20)));
            MultiKillWindow = Config.Bind("Moments", "MultiKillWindow", 2f, new ConfigDescription("Seconds between kills that still count as one multi-kill.", new AcceptableValueRange<float>(0.5f, 10f)));
            CloseCalls = Config.Bind("Moments", "CloseCalls", true, "A picture when your health dips very low and you live through it.");
            CloseCallHealth = Config.Bind("Moments", "CloseCallHealth", 0.15f, new ConfigDescription("How low is \"very low\" (0.15 = 15% of your health).", new AcceptableValueRange<float>(0.02f, 0.5f)));
            Revives = Config.Bind("Moments", "Revives", true, "A picture when you're brought back up.");
            MaxShots = Config.Bind("Reel", "MaxShots", 6, new ConfigDescription("How many pictures a mission keeps (the best ones, bosses first).", new AcceptableValueRange<int>(1, 12)));
            SaveToDiskEntry = Config.Bind("Reel", "SaveToDisk", true, "Also save each picture as a PNG.");
            Folder = Config.Bind("Reel", "Folder", "", "Where to save them; empty means your Pictures folder, in \"Mycopunk Highlights\".");
            TestKey = Config.Bind("Debug", "TestKey", false,
                "F8 in the hub takes three sample pictures and opens the reward screen with them (never saved). " +
                "(A file BepInEx\\highlightreel-test.txt does the same, for automated tests.)");

            runner = new GameObject("HighlightReel");
            DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<Reel>();

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo(Title + " " + Version + " loaded");
        }

        // ScriptEngine (BepInEx\scripts) reloads a mod by destroying it and loading the new build: leave nothing behind.
        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Reel.Clear();
            if (runner != null) Destroy(runner);
        }

        private void Update()
        {
            // a new mission: a new reel (the last one stays until then, for its reward screen in the hub)
            if (LevelData.Instance != level)
            {
                level = LevelData.Instance;
                if (level != null && !LevelData.IsHub) { Reel.Clear(); Kills.Reset(); You.Reset(); }
            }
            You.Update();

            if (!TestKey.Value) return;
            bool go = Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
            if (Time.unscaledTime >= nextFileCheck)
            {
                nextFileCheck = Time.unscaledTime + 0.5f;
                string file = Path.Combine(Paths.BepInExRootPath, "highlightreel-test.txt");
                if (File.Exists(file)) { try { File.Delete(file); } catch (Exception) { } go = true; }
                // (and BepInEx\highlightreel-hover.txt with a picture's number points at it, -1 stops: the zoom without a mouse)
                string hover = Path.Combine(Paths.BepInExRootPath, "highlightreel-hover.txt");
                if (File.Exists(hover))
                {
                    try { Zoom.ForceIndex = int.TryParse(File.ReadAllText(hover).Trim(), out int n) ? n : -1; File.Delete(hover); } catch (Exception) { }
                }
            }
            if (go) StartCoroutine(Test());
        }

        // Three pictures of the hub with sample captions, then the reward screen to show them on (only in the hub: never pops up in
        // a mission). Nothing is saved, and the real reel comes back with your next mission.
        private IEnumerator Test()
        {
            if (PlayerLook.Instance == null || GameManager.Instance == null || !LevelData.IsHub)
            {
                Log.LogInfo("Highlight Reel: the test only runs in the hub");
                yield break;
            }
            Reel.Testing = true;
            Reel.Clear();
            Reel.Capture("BOSS DOWN: TEST", 100, 0f);
            yield return new WaitForSecondsRealtime(0.4f);
            Reel.Capture("TRIPLE KILL", 55, 0f);
            yield return new WaitForSecondsRealtime(0.4f);
            Reel.Capture("CLOSE CALL", 50, 0f);
            yield return new WaitForSecondsRealtime(0.6f);
            Reel.Testing = false;
            try
            {
                var rewards = new List<RewardInfo> { new RewardInfo(Player.LocalPlayer.Character, 321) };
                if (Player.LocalPlayer.Gear.Length > 0 && Player.LocalPlayer.Gear[0].Prefab is IUpgradable gear) rewards.Add(new RewardInfo(gear, 123));
                rewards.Add(new RewardInfo(Global.Instance.PlayerResources[0], 42));
                rewards.Sort();
                var window = (MissionRewardsWindow)GameManager.Instance.OpenPopupWindowFromPrefab(Global.Instance.MissionRewardsWindow);
                window.SetRewards(Global.Instance.Missions[0], Global.Instance.DefaultMissionContainer, rewards, MissionManager.MissionState.Completed, 0);
                Log.LogInfo($"Highlight Reel: test reward screen open ({Reel.Shots.Count} pictures)");
            }
            catch (Exception e) { Log.LogWarning("Highlight Reel: the test screen didn't open: " + e); }
        }
    }
}
