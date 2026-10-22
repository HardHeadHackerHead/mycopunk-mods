using System;
using System.Collections.Generic;
using System.Linq;
using CSVFile;
using UnityEngine;

namespace NoBlasphemy
{
    // Finds the lines in the game's text table (Resources/TextBlocks, one row per line, English in the third column)
    // that use God's name in vain, cleans their subtitles and remembers them for the sound patches.
    //
    // A spoken line plays the Wwise event named after its ID (TextBlock.uniqueID). Characters share IDs: "vl_help" is every
    // character's call for help, told apart by a Character switch. So a line is silenced two ways:
    //  - by its TextBlock, where the game hands it to Highlighter.PlayVoicelineSound (the players' own lines), and
    //  - by event, for IDs where every character's line is one to silence (mission control, the truck, and the rest).
    internal static class Filter
    {
        private static readonly HashSet<TextBlocks.TextBlock> blocked = new HashSet<TextBlocks.TextBlock>();
        private static readonly HashSet<string> mutedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> durations = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<uint, string> mutedIds;   // made on first use: Wwise has to be loaded to hash the names

        // The lines the test key can play: those silenced by event (the ones a character shares with clean lines would play
        // the other characters' voices when played outside their own conversation).
        public static readonly List<(string group, int index, string variant, string id)> Testable =
            new List<(string, int, string, string)>();

        public static bool IsMuted(TextBlocks.TextBlock block) => Plugin.MuteVoice.Value && block != null && blocked.Contains(block);

        public static bool IsMuted(string eventName) => Plugin.MuteVoice.Value && eventName != null && mutedNames.Contains(eventName);

        public static bool IsMuted(uint eventId, out string name)
        {
            name = null;
            if (!Plugin.MuteVoice.Value || mutedNames.Count == 0) return false;
            if (mutedIds == null)
            {
                mutedIds = new Dictionary<uint, string>();
                foreach (string n in mutedNames) mutedIds[AkUnitySoundEngine.GetIDFromString(n)] = n;
            }
            return mutedIds.TryGetValue(eventId, out name);
        }

        // About as long as the line would have taken to say.
        public static float Duration(string name) => name != null && durations.TryGetValue(name, out float d) ? d : 3f;

        public static void Apply()
        {
            blocked.Clear();
            mutedNames.Clear();
            durations.Clear();
            mutedIds = null;
            HashSet<string> keep = Ids(Plugin.KeepLines.Value), extra = Ids(Plugin.ExtraLines.Value);
            Dictionary<string, string> english = ReadEnglish();
            bool cleanText = Plugin.CleanSubtitles.Value && TextBlocks.LanguageIndex == 0;
            var everyLineBlocked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            int cleaned = 0;

            var found = new List<(string group, int index, string variant, string id)>();

            void Consider(TextBlocks.TextBlock block, string variant, string groupKey, int index)
            {
                string id = block.uniqueID;
                if (!english.TryGetValue(Key(id, variant), out string en) && !english.TryGetValue(Key(id, null), out en))
                    en = TextBlocks.LanguageIndex == 0 ? block.text : null;
                bool bad = extra.Contains(id) || (!keep.Contains(id) && Words.IsBlasphemous(en));
                everyLineBlocked[id] = (!everyLineBlocked.TryGetValue(id, out bool all) || all) && bad;
                if (!bad) return;
                blocked.Add(block);
                found.Add((groupKey, index, variant, id));
                durations[id] = Mathf.Clamp(1f + (en ?? block.text).Length * 0.055f, 2f, 12f);
                if (cleanText)
                {
                    string before = block.text;
                    block.text = Words.Clean(block.text);
                    block.variations = null;   // (its cached copies with the parameters filled in)
                    if (block.text != before) cleaned++;
                }
                if (Plugin.LogLines.Value) Plugin.Log.LogInfo($"  {id}{(variant != null ? $" [{variant}]" : "")}: {block.text}");
            }

            foreach (var entry in TextBlocks.strings)
            {
                TextBlocks.TextBlockGroup group = entry.Value;
                var seen = new HashSet<TextBlocks.TextBlock[]>();
                if (group.variations != null)
                    foreach (var pair in group.variations)
                    {
                        seen.Add(pair.Value);
                        for (int i = 0; i < pair.Value.Length; i++) if (pair.Value[i] != null) Consider(pair.Value[i], pair.Key, entry.Key, i);
                    }
                if (group.blocks != null && !seen.Contains(group.blocks))
                    for (int i = 0; i < group.blocks.Length; i++) if (group.blocks[i] != null) Consider(group.blocks[i], null, entry.Key, i);
            }
            foreach (var pair in everyLineBlocked) if (pair.Value) mutedNames.Add(pair.Key);
            Testable.Clear();
            Testable.AddRange(found.Where(f => mutedNames.Contains(f.id)).OrderBy(f => f.id));
            Plugin.Log.LogInfo($"No Blasphemy {Plugin.Version}: {blocked.Count} lines found, {cleaned} subtitles cleaned, " +
                               (Plugin.MuteVoice.Value ? "their voice silenced" : "voice left on (MuteVoice = false)"));
        }

        // The English text of every line, by ID and variant (the character in "vl_help [Glider]"), whatever language the game shows.
        private static Dictionary<string, string> ReadEnglish()
        {
            var english = new Dictionary<string, string>();
            TextAsset asset = Resources.Load<TextAsset>("TextBlocks");
            if (asset == null)
            {
                Plugin.Log.LogWarning("No Blasphemy: the game's text table wasn't found; only English text is checked");
                return english;
            }
            try
            {
                using (CSVReader reader = CSVReader.FromString(asset.text))
                    foreach (string[] row in reader.Lines())
                    {
                        if (row.Length < 3 || string.IsNullOrEmpty(row[0])) continue;
                        string id = row[0].Trim(), variant = null;
                        int open = id.LastIndexOf('[');
                        if (id.EndsWith("]") && open > 0)
                        {
                            variant = id.Substring(open + 1, id.Length - open - 2);
                            id = id.Substring(0, open).TrimEnd();
                        }
                        english[Key(id, variant)] = row[2];
                    }
            }
            finally { Resources.UnloadAsset(asset); }
            return english;
        }

        private static string Key(string id, string variant) => id + "\n" + variant;

        private static HashSet<string> Ids(string list) =>
            new HashSet<string>((list ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
    }
}
