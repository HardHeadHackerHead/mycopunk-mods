using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace MissionHaul
{
    // Every mission's rewards arrive here, all at once, when its reward screen opens: record them, then (once the screen has shown
    // its rewards) add what you've earned this session below them.
    [HarmonyPatch(typeof(MissionRewardsWindow), nameof(MissionRewardsWindow.SetRewards))]
    internal static class RecordMission
    {
        private static void Prefix(List<RewardInfo> rewards, MissionManager.MissionState completed) => Session.Record(rewards, completed);

        private static void Postfix(MissionRewardsWindow __instance, List<RewardInfo> rewards)
        {
            SessionPanel.Remove(__instance);   // (the game reuses this screen: last time's panel goes first, even with the panel off)
            if (!Plugin.ShowSession.Value) return;
            // (the test screen shows a sample session, built from its own made-up rewards; your real one is never touched)
            Tally tally = Session.Testing ? Session.Sample(rewards) : Session.Current;
            int resources = rewards.Count(r => r.reward is PlayerResource), upgrades = rewards.Count(r => r.reward is UpgradeInstance);
            // (from the second mission: after the first, the session is just this mission, the same as the rewards above)
            if (tally.Missions >= 2) __instance.StartCoroutine(SessionPanel.Show(__instance, tally, resources, upgrades));
        }
    }

    // A resource on the reward screen ("+40") gets where it stands now under it ("-> 1226"): the game adds what you pick up as you
    // go, and the completion pay just before this screen, so your total already includes this mission. Under, not beside: the game
    // spaces the rewards for short numbers.
    [HarmonyPatch(typeof(ResourceUI), nameof(ResourceUI.SetCount))]
    internal static class ShowTotal
    {
        private static void Postfix(ResourceUI __instance, int count, char prefix)
        {
            if (!Plugin.ShowTotals.Value || SessionPanel.Building || prefix != '+' || (object)__instance.resource == null || PlayerData.Instance == null) return;
            if (__instance.GetComponentInParent<MissionRewardsWindow>() == null) return;
            int total = PlayerData.Instance.GetResource(__instance.resource);
            TMP_Text text = __instance.GetCount();
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.verticalAlignment = VerticalAlignmentOptions.Top;   // (the second line goes down, not up into the icon)
            text.SetText("+" + count + "\n<size=70%><color=#B8C2CC>→ " + total + "</color></size>");
        }
    }

    // "This session": below the mission's rewards, what every mission since the game started has paid out.
    internal static class SessionPanel
    {
        public static bool Building;   // (the session row's own "+1240" numbers don't get a total)

        public static IEnumerator Show(MissionRewardsWindow window, Tally session, int resources, int upgrades)
        {
            // the screen brings its rewards and upgrades in one by one (a new upgrade last): wait until every one is there, so the
            // panel goes below them all
            float waited = 0f;
            while (waited < 15f && window != null && window.isActiveAndEnabled && !AllShown(window, resources, upgrades)) { waited += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            if (window == null || !window.isActiveAndEnabled) yield break;
            try { Build(window, session); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Mission Haul: the session panel didn't build: " + e); }
            finally { Building = false; }
        }

        private const string PanelName = "MissionHaulSession";

        public static void Remove(MissionRewardsWindow window)
        {
            foreach (Transform t in window.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == PanelName) Object.Destroy(t.gameObject);
        }

        // Every reward and upgrade of this mission on screen: as many as it has (the screen makes them after its title), all shown.
        private static bool AllShown(MissionRewardsWindow window, int resources, int upgrades)
        {
            if (window.resourceParent.childCount < resources || window.upgradeParent.childCount < upgrades) return false;
            foreach (Transform child in window.resourceParent) if (!child.gameObject.activeSelf) return false;
            foreach (Transform child in window.upgradeParent) if (!child.gameObject.activeSelf) return false;
            return true;
        }

        private static void Build(MissionRewardsWindow window, Tally session)
        {
            // Everything measured in the reward column's own space: the screen is a hologram in the world that turns to face the camera,
            // so world positions depend on where the player looks (and measuring in them skews the panel).
            RectTransform rewards = window.resourceParent, upgrades = window.upgradeParent;
            var column = (RectTransform)rewards.parent;
            var corners = new Vector3[4];
            float bottom = float.MaxValue, itemHeight = 0f, step = 0f;
            foreach (RectTransform parent in new[] { rewards, upgrades })
                foreach (RectTransform child in parent)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    child.GetWorldCorners(corners);
                    Vector3 low = column.InverseTransformPoint(corners[0]), high = column.InverseTransformPoint(corners[1]);
                    bottom = Mathf.Min(bottom, low.y);
                    if (parent == rewards) itemHeight = Mathf.Max(itemHeight, high.y - low.y);
                }
            if (rewards.childCount > 1)
                step = Mathf.Abs(column.InverseTransformPoint(rewards.GetChild(1).position).x - column.InverseTransformPoint(rewards.GetChild(0).position).x);
            Vector3 rewardsAt = column.InverseTransformPoint(rewards.position);
            if (bottom == float.MaxValue) { rewards.GetWorldCorners(corners); bottom = column.InverseTransformPoint(corners[0]).y; }
            if (itemHeight <= 0f) itemHeight = rewards.rect.height;
            if (step <= 0f) step = itemHeight * 1.2f;
            if (Plugin.LogLayout.Value)
                Plugin.Log.LogInfo($"Mission Haul layout (column space): rewards at {rewardsAt}, upgrades {upgrades.childCount}, bottom {bottom}, item height {itemHeight}, step {step}");

            var panel = new GameObject(PanelName, typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(column, false);
            panel.localRotation = Quaternion.identity;
            panel.localScale = Vector3.one;
            panel.localPosition = new Vector3(rewardsAt.x, bottom - itemHeight * 1.5f, 0f);
            // (the panel's units are the column's)
            float rowHeight = itemHeight, gap = step * 1.15f;

            TMP_Text style = window.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.text.Trim().ToUpperInvariant() == "REWARDS")
                             ?? window.missionText;
            float y = 0f;
            // a running total, said as one: "ALL 5 MISSIONS THIS SESSION", not another reward
            string results = session.Failed + session.Aborted == 0 ? "" :
                $" <size=70%><color=#B8C2CC>({session.Completed} DONE, {session.Failed + session.Aborted} NOT)</color></size>";
            Text(panel, style, $"ALL {session.Missions} MISSIONS THIS SESSION" + results, 1f, ref y);
            y -= rowHeight * 0.35f;

            // the resources, as the reward row shows them (the game's own icons, with their names when pointed at)
            var shown = session.Resources.Where(r => r.Value > 0).OrderBy(r => System.Array.IndexOf(Global.Instance.PlayerResources, r.Key)).ToList();
            if (shown.Count > 0)
            {
                Building = true;
                for (int i = 0; i < shown.Count; i++)
                {
                    ResourceUI item = Object.Instantiate(window.resourceInfo, panel);
                    item.gameObject.SetActive(true);
                    item.Setup(shown[i].Key, shown[i].Value, showIfCantAfford: false, '+');
                    var rt = (RectTransform)item.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.localPosition = new Vector3((i - (shown.Count - 1) * 0.5f) * gap, y - rowHeight * 0.5f, 0f);
                }
                Building = false;
                y -= rowHeight * 2.6f;   // (the icon, and the number under it)
            }

            // the upgrades by rarity, in the rarities' own colours, and the XP
            var lines = new List<string>();
            if (session.Upgrades.Count > 0 || session.Skins > 0)
                lines.Add("UPGRADES  " + string.Join("  ·  ", session.Upgrades.Reverse().Select(u =>
                {
                    ref RarityData rarity = ref Global.GetRarity(u.Key);
                    return $"<color=#{ColorUtility.ToHtmlStringRGB(rarity.color)}>{u.Value} {rarity.Name.ToUpperInvariant()}</color>";
                }).Concat(session.Skins > 0 ? new[] { $"{session.Skins} {(session.Skins == 1 ? "SKIN" : "SKINS")}" } : new string[0])));
            if (session.CharacterXP + session.GearXP > 0)
                lines.Add($"XP  +{session.CharacterXP:N0} <size=70%><color=#B8C2CC>CHARACTER</color></size>  ·  +{session.GearXP:N0} <size=70%><color=#B8C2CC>GEAR</color></size>");
            foreach (string line in lines) Text(panel, style, line, 0.7f, ref y);
        }

        // A line of text in the reward screen's own header style, centred under the last.
        private static void Text(RectTransform panel, TMP_Text style, string text, float scale, ref float y)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = style.font;
            t.fontSharedMaterial = style.fontSharedMaterial;
            t.fontSize = style.fontSize * scale;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Top;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.SetText(text);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(1200f, t.fontSize * 1.4f);
            rt.localPosition = new Vector3(0f, y, 0f);
            y -= t.fontSize * 1.5f;
        }
    }
}
