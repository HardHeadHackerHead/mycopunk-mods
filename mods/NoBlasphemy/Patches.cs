using HarmonyLib;
using UnityEngine;

namespace NoBlasphemy
{
    // The game (re)reads its text table: find and clean the lines again.
    [HarmonyPatch(typeof(TextBlocks), "Initialize")]
    internal static class TextLoaded
    {
        private static void Postfix() => Filter.Apply();
    }

    // A player character's line. Answering "no sound" makes the game pace the line by its subtitle instead.
    [HarmonyPatch(typeof(Highlighter), nameof(Highlighter.PlayVoicelineSound),
        new[] { typeof(TextBlocks.TextBlock), typeof(GameObject), typeof(string), typeof(AkCallbackManager.EventCallback), typeof(object) })]
    internal static class PlayerLine
    {
        private static bool Prefix(TextBlocks.TextBlock block, ref bool __result)
        {
            if (!Filter.IsMuted(block)) return true;
            if (Plugin.LogLines.Value) Plugin.Log.LogInfo($"No Blasphemy: silenced {block.uniqueID}");
            __result = false;
            return false;
        }
    }

    // Every other line goes straight to Wwise by event: not posted, and its "finished" callback still comes, on time.
    [HarmonyPatch(typeof(AkUnitySoundEngine), nameof(AkUnitySoundEngine.PostEvent), new[] { typeof(uint), typeof(GameObject) })]
    internal static class EventById
    {
        private static bool Prefix(uint in_eventID, ref uint __result) => Sounds.Allow(in_eventID, 0, null, null, ref __result);
    }

    [HarmonyPatch(typeof(AkUnitySoundEngine), nameof(AkUnitySoundEngine.PostEvent),
        new[] { typeof(uint), typeof(GameObject), typeof(uint), typeof(AkCallbackManager.EventCallback), typeof(object) })]
    internal static class EventByIdWithCallback
    {
        private static bool Prefix(uint in_eventID, uint in_uFlags, AkCallbackManager.EventCallback in_pfnCallback, object in_pCookie, ref uint __result) =>
            Sounds.Allow(in_eventID, in_uFlags, in_pfnCallback, in_pCookie, ref __result);
    }

    [HarmonyPatch(typeof(AkUnitySoundEngine), nameof(AkUnitySoundEngine.PostEvent), new[] { typeof(string), typeof(GameObject) })]
    internal static class EventByName
    {
        private static bool Prefix(string in_pszEventName, ref uint __result) => Sounds.Allow(in_pszEventName, 0, null, null, ref __result);
    }

    [HarmonyPatch(typeof(AkUnitySoundEngine), nameof(AkUnitySoundEngine.PostEvent),
        new[] { typeof(string), typeof(GameObject), typeof(uint), typeof(AkCallbackManager.EventCallback), typeof(object) })]
    internal static class EventByNameWithCallback
    {
        private static bool Prefix(string in_pszEventName, uint in_uFlags, AkCallbackManager.EventCallback in_pfnCallback, object in_pCookie, ref uint __result) =>
            Sounds.Allow(in_pszEventName, in_uFlags, in_pfnCallback, in_pCookie, ref __result);
    }

    internal static class Sounds
    {
        public static bool Allow(uint id, uint flags, AkCallbackManager.EventCallback callback, object cookie, ref uint result) =>
            !Filter.IsMuted(id, out string name) || Silence(name, flags, callback, cookie, ref result);

        public static bool Allow(string name, uint flags, AkCallbackManager.EventCallback callback, object cookie, ref uint result) =>
            !Filter.IsMuted(name) || Silence(name, flags, callback, cookie, ref result);

        private static bool Silence(string name, uint flags, AkCallbackManager.EventCallback callback, object cookie, ref uint result)
        {
            if (Plugin.LogLines.Value) Plugin.Log.LogInfo($"No Blasphemy: silenced {name}");
            if ((flags & (uint)AkCallbackType.AK_EndOfEvent) != 0) Later.Call(Filter.Duration(name), callback, cookie);
            result = 0;
            return false;
        }
    }
}
