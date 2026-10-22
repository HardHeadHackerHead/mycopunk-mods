using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MissionHaul
{
    // What you've earned since the game started: every mission's rewards as the game hands them to its reward screen. Kept in a
    // small file too (BepInEx\cache\missionhaul-session.json), so a hot reload of the mod doesn't lose it; a new launch of the game
    // starts a new session.
    internal sealed class Tally
    {
        public int Missions, Completed, Failed, Aborted;
        public int CharacterXP, GearXP, Skins;
        public readonly Dictionary<PlayerResource, int> Resources = new Dictionary<PlayerResource, int>();
        public readonly SortedDictionary<Rarity, int> Upgrades = new SortedDictionary<Rarity, int>();

        public void Add(List<RewardInfo> rewards, MissionManager.MissionState state)
        {
            Missions++;
            if (state == MissionManager.MissionState.Completed) Completed++;
            else if (state == MissionManager.MissionState.Failed) Failed++;
            else Aborted++;
            foreach (RewardInfo r in rewards)
            {
                switch (r.reward)
                {
                    case Character _: CharacterXP += r.amount; break;
                    case IUpgradable _: GearXP += r.amount; break;
                    case PlayerResource resource:
                        Resources.TryGetValue(resource, out int had);
                        Resources[resource] = had + r.amount;
                        break;
                    case UpgradeInstance upgrade when upgrade.Upgrade.IsSkin():
                        Skins++;
                        break;
                    case UpgradeInstance upgrade:
                        Upgrades.TryGetValue(upgrade.Upgrade.Rarity, out int count);
                        Upgrades[upgrade.Upgrade.Rarity] = count + 1;
                        break;
                }
            }
        }
    }

    internal static class Session
    {
        public static bool Testing;   // (the test screen's made-up rewards: never counted)

        public static readonly Tally Current = new Tally();

        // For the test screen: what a session of three missions like the test one would look like (never your real session).
        public static Tally Sample(List<RewardInfo> rewards)
        {
            var t = new Tally();
            t.Add(rewards, MissionManager.MissionState.Completed);
            t.Add(rewards, MissionManager.MissionState.Completed);
            t.Add(rewards, MissionManager.MissionState.Failed);
            return t;
        }

        private static string FilePath => Path.Combine(BepInEx.Paths.CachePath, "missionhaul-session.json");

        // this launch of the game: its process and when it started
        private static string Launch
        {
            get
            {
                Process p = Process.GetCurrentProcess();
                return p.Id + "@" + p.StartTime.ToUniversalTime().Ticks;
            }
        }

        public static void Record(List<RewardInfo> rewards, MissionManager.MissionState state)
        {
            // (only the game's own reward screens: they always get its shared list, a test screen (this mod's or another's) its own)
            if (Testing || rewards == null || !ReferenceEquals(rewards, Global.rewardInfoBuffer)) return;
            Current.Add(rewards, state);
            Plugin.Log.LogInfo($"Mission Haul: mission {Current.Missions} ({state}) recorded");
            Save();
        }

        private static void Save()
        {
            try
            {
                var json = new JObject
                {
                    ["launch"] = Launch, ["missions"] = Current.Missions, ["completed"] = Current.Completed, ["failed"] = Current.Failed, ["aborted"] = Current.Aborted,
                    ["characterXP"] = Current.CharacterXP, ["gearXP"] = Current.GearXP, ["skins"] = Current.Skins,
                    ["resources"] = new JObject(Current.Resources.Select(r => new JProperty(r.Key.name, r.Value))),
                    ["upgrades"] = new JObject(Current.Upgrades.Select(u => new JProperty(u.Key.ToString(), u.Value))),
                };
                File.WriteAllText(FilePath, json.ToString());
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Mission Haul: couldn't save the session: " + e.Message); }
        }

        // The session saved by this same launch of the game (after a hot reload); anything older is a past session.
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                JObject json = JObject.Parse(File.ReadAllText(FilePath));
                if ((string)json["launch"] != Launch) return;
                Current.Missions = (int)json["missions"]; Current.Completed = (int)json["completed"]; Current.Failed = (int)json["failed"]; Current.Aborted = (int)json["aborted"];
                Current.CharacterXP = (int)json["characterXP"]; Current.GearXP = (int)json["gearXP"]; Current.Skins = (int?)json["skins"] ?? 0;
                if (Global.Instance != null)
                    foreach (JProperty r in ((JObject)json["resources"]).Properties())
                    {
                        PlayerResource resource = Global.Instance.PlayerResources.FirstOrDefault(p => p != null && p.name == r.Name);
                        if (resource != null) Current.Resources[resource] = (int)r.Value;
                    }
                foreach (JProperty u in ((JObject)json["upgrades"]).Properties())
                    if (System.Enum.TryParse(u.Name, out Rarity rarity)) Current.Upgrades[rarity] = (int)u.Value;
                Plugin.Log.LogInfo($"Mission Haul: this session's {Current.Missions} mission(s) picked up again");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Mission Haul: couldn't read the saved session: " + e.Message); }
        }
    }
}
