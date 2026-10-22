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
        // Lines that speak of God sincerely, in English or in a translation: never changed. (The Wrangler on faith, and on the
        // Lord being a carpenter.)
        private static readonly string[] Reverent = { "exch_faith_5", "vl_building_2" };

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
            keep.UnionWith(Reverent);
            Dictionary<string, string> english = ReadEnglish();
            int language = TextBlocks.LanguageIndex;
            bool cleanText = Plugin.CleanSubtitles.Value;
            var everyLineBlocked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            int cleaned = 0;

            var found = new List<(string group, int index, string variant, string id)>();

            void Consider(TextBlocks.TextBlock block, string variant, string groupKey, int index)
            {
                string id = block.uniqueID;
                if (!english.TryGetValue(Key(id, variant), out string en) && !english.TryGetValue(Key(id, null), out en))
                    en = language == 0 ? block.text : null;
                bool kept = keep.Contains(id);
                // the voice is English in every language, so the English decides what is silenced; the subtitle is cleaned
                // when either says it (a translation can use the name where the English doesn't)
                bool bad = extra.Contains(id) || (!kept && Words.IsBlasphemous(en));
                bool badText = bad || (!kept && language != 0 && Words.IsBlasphemous(block.text, language));
                everyLineBlocked[id] = (!everyLineBlocked.TryGetValue(id, out bool all) || all) && bad;
                if (bad)
                {
                    blocked.Add(block);
                    found.Add((groupKey, index, variant, id));
                    durations[id] = Mathf.Clamp(1f + (en ?? block.text).Length * 0.055f, 2f, 12f);
                }
                if (!badText) return;
                if (cleanText)
                {
                    string before = block.text;
                    block.text = Words.Clean(block.text, language);
                    block.variations = null;   // (its cached copies with the parameters filled in)
                    if (block.text != before) cleaned++;
                }
                if (Plugin.LogLines.Value) Plugin.Log.LogInfo($"  {id}{(variant != null ? $" [{variant}]" : "")}{(bad ? "" : " (text only)")}: {block.text}");
            }

            // A line picked at random from several (a character's call for help) has the others to say instead of going quiet.
            void Alternatives(TextBlocks.TextBlock[] lines)
            {
                var clean = lines.Where(b => b != null && !blocked.Contains(b)).ToList();
                if (clean.Count == 0) return;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i] != null && blocked.Contains(lines[i])) alternatives[lines[i]] = clean[i % clean.Count];
            }

            alternatives.Clear();
            foreach (var entry in TextBlocks.strings)
            {
                TextBlocks.TextBlockGroup group = entry.Value;
                var seen = new HashSet<TextBlocks.TextBlock[]>();
                if (group.variations != null)
                    foreach (var pair in group.variations)
                    {
                        seen.Add(pair.Value);
                        for (int i = 0; i < pair.Value.Length; i++) if (pair.Value[i] != null) Consider(pair.Value[i], pair.Key, entry.Key, i);
                        Alternatives(pair.Value);
                    }
                if (group.blocks != null && !seen.Contains(group.blocks))
                {
                    for (int i = 0; i < group.blocks.Length; i++) if (group.blocks[i] != null) Consider(group.blocks[i], null, entry.Key, i);
                    Alternatives(group.blocks);
                }
            }
            foreach (var pair in everyLineBlocked) if (pair.Value) mutedNames.Add(pair.Key);
            Testable.Clear();
            Testable.AddRange(found.Where(f => mutedNames.Contains(f.id)).OrderBy(f => f.id));
            Plugin.Log.LogInfo($"{Plugin.Title} {Plugin.Version}: {blocked.Count} lines found, {cleaned} subtitles cleaned, " +
                               $"{(Plugin.SwapLines.Value ? alternatives.Count : 0)} swapped for another line when picked at random, " +
                               (Plugin.MuteVoice.Value ? "otherwise silenced" : "voice left on (MuteVoice = false)"));
        }

        // The line to say instead of one that uses the name, when there is one; otherwise the line itself.
        public static TextBlocks.TextBlock Swap(TextBlocks.TextBlock block)
        {
            if (block == null || !Plugin.SwapLines.Value || !alternatives.TryGetValue(block, out TextBlocks.TextBlock other)) return block;
            if (Plugin.LogLines.Value) Plugin.Log.LogInfo($"No Blasphemy: said another line instead of {block.uniqueID}");
            return other;
        }

        private static readonly Dictionary<TextBlocks.TextBlock, TextBlocks.TextBlock> alternatives =
            new Dictionary<TextBlocks.TextBlock, TextBlocks.TextBlock>();

        // The English text of every line, by ID and variant (the character in "vl_help [Glider]"), whatever language the game shows.
        private static Dictionary<string, string> ReadEnglish()
        {
            var english = new Dictionary<string, string>();
            TextAsset asset = Resources.Load<TextAsset>("TextBlocks");
            if (asset == null)
            {
                Plugin.Log.LogWarning("No Blasphemy: the game's text table wasn't found; only the shown language's text is checked");
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
