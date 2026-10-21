using System.Text.RegularExpressions;

namespace NoBlasphemy
{
    // Which lines use God's name in vain, and the same line with it taken out. Plain C# (no game or Unity types), so it
    // can be checked against the game's whole text table outside the game.
    public static class Words
    {
        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // God's name, or "Lord", used as an exclamation or a curse.
        private static readonly Regex Detect = new Regex(
            @"\b(god|gawd|goddam\w*|jesus|christ|omg|lordy)\b|\b(my|oh|good|dear|sweet)\s+lord\b|\blord\s+almighty\b", Opts);

        // In order: the compound curse first, then the set phrases, then the name on its own.
        private static readonly (Regex pattern, MatchEvaluator replace)[] Rules =
        {
            (new Regex(@"\bgod[\s-]*dam(?:n|m)?(it|ed)?\b", Opts), m => Cased(m.Value, m.Groups[1].Value.ToLowerInvariant() == "it" ? "dang it" : "dang")),
            (new Regex(@"(?<=\bmy\s+)(god|lord|gawd)\b", Opts), m => Cased(m.Value, "gosh")),
            (new Regex(@"(?<=\bthank\s+)(god|gawd|the\s+lord)\b", Opts), m => Cased(m.Value, "goodness")),
            (new Regex(@"\bfor\s+god'?s\s+sake\b", Opts), m => Cased(m.Value, "for goodness' sake")),
            (new Regex(@"(?<=\bgood\s+)lord\b", Opts), m => Cased(m.Value, "grief")),
            (new Regex(@"(?<=\b(?:oh|dear|sweet)\s+)lord\b", Opts), m => Cased(m.Value, "gosh")),
            (new Regex(@"\blord\s+almighty\b", Opts), m => Cased(m.Value, "goodness gracious")),
            (new Regex(@"\bomg\b", Opts), m => Cased(m.Value, "oh my gosh")),
            (new Regex(@"\b(god|gawd|jesus|christ|lordy)\b", Opts), m => Cased(m.Value, "gosh")),
        };

        public static bool IsBlasphemous(string text) => !string.IsNullOrEmpty(text) && Detect.IsMatch(text);

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (pattern, replace) in Rules) text = pattern.Replace(text, replace);
            return text;
        }

        // The replacement in the original's case: shouted stays shouted, a capital at the start stays.
        private static string Cased(string original, string replacement)
        {
            string[] words = original.Split(' ', '-', '\t', '\n');
            string last = words[words.Length - 1];
            if (IsShouted(original) || (last.Length > 1 && IsShouted(last))) return replacement.ToUpperInvariant();
            if (char.IsUpper(original[0])) return char.ToUpperInvariant(replacement[0]) + replacement.Substring(1);
            return replacement;
        }

        private static bool IsShouted(string s)
        {
            bool letters = false;
            foreach (char c in s)
            {
                if (char.IsLower(c)) return false;
                if (char.IsLetter(c)) letters = true;
            }
            return letters && s.Length > 1;
        }
    }
}
