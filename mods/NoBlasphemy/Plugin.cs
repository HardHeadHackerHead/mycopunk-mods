using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NoBlasphemy
{
    // Takes God's name used in vain out of what the characters say: the subtitle is cleaned and the spoken line is
    // silenced (a recording can't lose a single word). Client only: it changes what you hear and read, nothing else.
    [BepInPlugin(Guid, "No Blasphemy", Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "quadslab.mycopunk.noblasphemy";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> MuteVoice;
        internal static ConfigEntry<bool> CleanSubtitles;
        internal static ConfigEntry<string> KeepLines;
        internal static ConfigEntry<string> ExtraLines;
        internal static ConfigEntry<bool> LogLines;

        private void Awake()
        {
            Log = Logger;
            MuteVoice = Config.Bind("General", "MuteVoice", true,
                "Silence the spoken lines that use God's name in vain. A recording can't lose just one word, so the whole line is quiet; its subtitle still shows, cleaned.");
            CleanSubtitles = Config.Bind("General", "CleanSubtitles", true,
                "Take God's name out of those lines' subtitles (English). Takes effect at the next start.");
            KeepLines = Config.Bind("Lines", "Keep", "exch_faith_5",
                "Line IDs to leave alone, comma-separated: lines that speak of God with respect. Takes effect at the next start.");
            ExtraLines = Config.Bind("Lines", "Extra", "",
                "More line IDs to clean and silence, comma-separated, for any line the word list misses. Takes effect at the next start.");
            LogLines = Config.Bind("Debug", "LogLines", false,
                "Write every line changed (at start) and every line silenced (as it plays) to the BepInEx log, with its ID.");

            var runner = new GameObject("NoBlasphemy");
            DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<Later>();

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            // The game reads its text before or after the plugins load, depending on the loader: catch both.
            if (TextBlocks.strings.Count > 0) Filter.Apply();
        }
    }

    // Runs the "line finished" callbacks of silenced lines on time, so conversations and mission chatter carry on.
    internal sealed class Later : MonoBehaviour
    {
        private static readonly List<(float at, AkCallbackManager.EventCallback callback, object cookie)> pending =
            new List<(float, AkCallbackManager.EventCallback, object)>();

        public static void Call(float delay, AkCallbackManager.EventCallback callback, object cookie)
        {
            if (callback != null) pending.Add((Time.unscaledTime + delay, callback, cookie));
        }

        private void Update()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < pending[i].at) continue;
                var (_, callback, cookie) = pending[i];
                pending.RemoveAt(i);
                try { callback(cookie, AkCallbackType.AK_EndOfEvent, null); }
                catch (Exception e) { Plugin.Log.LogWarning("No Blasphemy: a line's finish callback failed: " + e.Message); }
            }
        }
    }
}
