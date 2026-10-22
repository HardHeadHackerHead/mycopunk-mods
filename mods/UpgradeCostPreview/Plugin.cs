using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Pigeon;
using UnityEngine;

namespace UpgradeCostPreview
{
    // Turbocharging an upgrade (hold the key on an upgrade in a weapon's grid) hides its cost until you start holding, then
    // counts it down. This shows the cost as soon as you point at the upgrade, and every cost in the info panel against what
    // you have: "40 / 125". Client only: it changes what you see, nothing else.
    [BepInPlugin(Guid, Title, Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.upgradecostpreview";
        public const string Title = "Quad's Upgrade Cost Preview";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowTurbochargeCost;
        internal static ConfigEntry<bool> ShowOwned;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            ShowTurbochargeCost = Config.Bind("General", "ShowTurbochargeCost", true,
                "Show what turbocharging an upgrade costs as soon as you point at it, not only once you start holding the key.");
            ShowOwned = Config.Bind("General", "ShowOwned", true,
                "Show each cost in the info panel against how much you have: \"40 / 125\".");
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo(Title + " " + Version + " loaded");
        }

        // ScriptEngine (BepInEx\scripts) reloads a mod by destroying it and loading the new build: leave nothing behind.
        private void OnDestroy() => harmony?.UnpatchSelf();
    }

    // The one place the game hides a cost until you hold: turbocharging. (Its subclasses, the preview and the Ouroboros cleanse,
    // call this first, so they're covered too.)
    [HarmonyPatch(typeof(GearUpgradeUI), nameof(GearUpgradeUI.HasUnlockAction))]
    internal static class ShowCostUpfront
    {
        private static void Postfix(ref HoverInfo.UnlockActionParams data)
        {
            if (Plugin.ShowTurbochargeCost.Value) data.OnlyShowCostWhileUnlocking = false;
        }
    }

    // "300 / 1186" is wider than the 90 the game spaces each resource by: spread them out, and the panel with them. (The countdown
    // while you hold reads each one's position, so it follows.)
    [HarmonyPatch(typeof(UseResourceUI), nameof(UseResourceUI.Setup))]
    internal static class WiderCosts
    {
        private const float Spacing = 130f;

        private static void Postfix(UseResourceUI __instance)
        {
            if (!Plugin.ShowOwned.Value || __instance.costs.Count == 0) return;
            int n = __instance.costs.Count;
            for (int i = 0; i < n && i < __instance.resourceParent.childCount; i++)
            {
                var item = (RectTransform)__instance.resourceParent.GetChild(i);
                item.anchoredPosition = new Vector2((i - n * 0.5f) * Spacing + Spacing * 0.5f, item.anchoredPosition.y);
            }
            var panel = (RectTransform)__instance.transform;
            panel.sizeDelta = new Vector2(Spacing * n + 30f, panel.sizeDelta.y);
        }
    }

    // Each resource in the info panel's cost (UseResourceUI) gets " / what you have" after it, smaller and grey. The game sets
    // the number through SetCount, at the start and as it counts down while you hold, so this follows both. The need keeps the
    // game's colour (red when you can't afford it).
    [HarmonyPatch(typeof(ResourceUI), nameof(ResourceUI.SetCount))]
    internal static class ShowOwned
    {
        private static void Postfix(ResourceUI __instance, int count, char prefix)
        {
            if (!Plugin.ShowOwned.Value || (object)__instance.resource == null || PlayerData.Instance == null) return;
            if (__instance.GetComponentInParent<UseResourceUI>() == null) return;   // (only costs, not your resource counts elsewhere)
            int have = PlayerData.Instance.GetResource(__instance.resource);
            var text = __instance.GetCount();
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            text.overflowMode = TMPro.TextOverflowModes.Overflow;
            // (the game wrote the number with SetCharArray, which text.text doesn't read back: write it again, with what you have)
            text.SetText((prefix != '\0' ? prefix.ToString() : "") + count + "<size=70%><color=#B8C2CC> / " + Compact(have) + "</color></size>");
        }

        // Big amounts short, so they fit under their icon: 12.3k, 1.2M.
        private static string Compact(int n) =>
            n >= 1000000 ? (n / 1000000f).ToString(n >= 10000000 ? "0" : "0.#") + "M" :
            n >= 10000 ? (n / 1000f).ToString(n >= 100000 ? "0" : "0.#") + "k" : n.ToString();
    }
}
