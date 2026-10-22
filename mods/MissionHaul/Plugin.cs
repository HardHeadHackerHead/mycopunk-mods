using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MissionHaul
{
    // The mission reward screen shows what you earned ("+40"); this adds where each resource stands now ("+40 -> 1226"), and
    // keeps what you've earned this session across missions. Client only: it reads the rewards the game already gives you.
    [BepInPlugin(Guid, Title, Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.missionhaul";
        public const string Title = "Quad's Mission Haul";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowTotals;
        internal static ConfigEntry<bool> ShowSession;
        internal static ConfigEntry<bool> TestKey;
        internal static ConfigEntry<bool> LogLayout;
        private Harmony harmony;
        private float nextFileCheck;

        private void Awake()
        {
            Log = Logger;
            ShowTotals = Config.Bind("General", "ShowTotals", true,
                "On the mission reward screen, show where each resource stands now beside what you earned: \"+40 -> 1226\".");
            ShowSession = Config.Bind("General", "ShowSession", true,
                "On the mission reward screen, show what you've earned this session (since the game started) below this mission's rewards.");
            LogLayout = Config.Bind("Debug", "LogLayout", false, "Write where the reward screen's parts are to the BepInEx log (for fixing the layout).");
            TestKey = Config.Bind("Debug", "TestKey", false,
                "F8 in the hub opens the mission reward screen with sample rewards (never counted in your session). " +
                "(A file BepInEx\\missionhaul-test.txt does the same, for automated tests.)");
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Session.Load();
            Log.LogInfo(Title + " " + Version + " loaded");
        }

        // ScriptEngine (BepInEx\scripts) reloads a mod by destroying it and loading the new build: leave nothing behind.
        private void OnDestroy() => harmony?.UnpatchSelf();

        private void Update()
        {
            if (!TestKey.Value) return;
            bool go = Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
            if (Time.unscaledTime >= nextFileCheck)
            {
                nextFileCheck = Time.unscaledTime + 0.5f;
                string file = System.IO.Path.Combine(Paths.BepInExRootPath, "missionhaul-test.txt");
                if (System.IO.File.Exists(file)) { try { System.IO.File.Delete(file); } catch (Exception) { } go = true; }
            }
            if (go) OpenTestScreen();
        }

        // The real reward screen with made-up rewards, to look at it without playing a mission. It shows them only (the game gives
        // rewards before it opens this screen), and Session.Testing keeps them out of your session.
        private static void OpenTestScreen()
        {
            if (PlayerLook.Instance == null || GameManager.Instance == null || Global.Instance == null)
            {
                Log.LogInfo("Mission Haul: the test screen works once you're in the game");
                return;
            }
            // (never in a mission: a reward screen popping up mid-fight gets in the player's way)
            if (!LevelData.IsHub)
            {
                Log.LogInfo("Mission Haul: the test screen only opens in the hub");
                return;
            }
            try
            {
                var rewards = new List<RewardInfo> { new RewardInfo(Player.LocalPlayer.Character, 1234) };
                PlayerResource[] resources = Global.Instance.PlayerResources;
                for (int i = 0; i < resources.Length && i < 5; i++) rewards.Add(new RewardInfo(resources[i], 12 * (i + 1) + 7));
                // a weapon's XP and a few of your own upgrades, shown only (nothing is given or taken)
                if (Player.LocalPlayer.Gear.Length > 0 && Player.LocalPlayer.Gear[0].Prefab is IUpgradable gear) rewards.Add(new RewardInfo(gear, 345));
                var upgrades = new List<UpgradeInstance>();
                PlayerData.QueryUpgrades(u => { if (upgrades.Count < 3 && u != null) upgrades.Add(u); });
                foreach (UpgradeInstance u in upgrades) rewards.Add(new RewardInfo(u));
                rewards.Sort();
                Session.Testing = true;
                var window = (MissionRewardsWindow)GameManager.Instance.OpenPopupWindowFromPrefab(Global.Instance.MissionRewardsWindow);
                window.SetRewards(Global.Instance.Missions[0], Global.Instance.DefaultMissionContainer, rewards, MissionManager.MissionState.Completed, 0);
                Log.LogInfo($"Mission Haul: test reward screen open ({upgrades.Count} of your upgrades shown)");
            }
            catch (Exception e) { Log.LogWarning("Mission Haul: the test screen didn't open: " + e); }
            finally { Session.Testing = false; }
        }
    }
}
