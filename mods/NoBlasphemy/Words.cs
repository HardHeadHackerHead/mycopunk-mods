using System.Linq;
using System.Text.RegularExpressions;

namespace NoBlasphemy
{
    // Which lines use God's name in vain, and the same line with it taken out, in each of the game's languages (its language
    // index: 0 English, 1 French, 2 German, 3 Spanish, 4 Russian, 5 Portuguese, 6 and 7 Chinese, 8 Japanese, 9 Korean).
    // Plain C# (no game or Unity types), so it can be checked against the game's whole text table outside the game.
    //
    // Only the exclamations and curses: a sincere mention of God, "divine" or "holy", and the minced words every language has
    // ("gosh", "heavens", "Güte", "nossa", 天) are left alone; the replacements are those minced words.
    public static class Words
    {
        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static (Regex, MatchEvaluator) R(string pattern, string replacement) =>
            (new Regex(pattern, Opts), m => Cased(m.Value, replacement));

        // In order in each language: the compound curse first, then the set phrases, then the name on its own.
        private static readonly (Regex pattern, MatchEvaluator replace)[][] Rules =
        {
            new[]   // English
            {
                (new Regex(@"\bgod[\s-]*dam(?:n|m)?(it|ed)?\b", Opts), m => Cased(m.Value, m.Groups[1].Value.ToLowerInvariant() == "it" ? "dang it" : "dang")),
                R(@"(?<=\bmy\s+)(god|lord|gawd)\b", "gosh"),
                R(@"(?<=\bthank\s+)(god|gawd|the\s+lord)\b", "goodness"),
                R(@"\bfor\s+god'?s\s+sake\b", "for goodness' sake"),
                R(@"(?<=\bgood\s+)lord\b", "grief"),
                R(@"(?<=\b(?:oh|dear|sweet)\s+)lord\b", "gosh"),
                R(@"\blord\s+almighty\b", "goodness gracious"),
                R(@"\bomg\b", "oh my gosh"),
                R(@"\bgod\s+help\s+us\b", "heaven help us"),
                R(@"\b(god|gawd|jesus|christ|lordy)\b", "gosh"),
            },
            new[]   // French
            {
                R(@"\bnom\s+de\s+dieu\b", "nom d'une pipe"),
                R(@"\bbonté\s+divine\b", "bon sang"),
                R(@"\b(?:oh\s+)?mon\s+dieu\b", "oh là là"),
                R(@"\bseigneur(?=\s*[!,.…])", "bon sang"),
                R(@"\bdieu(?=\s*[!…])", "bon sang"),
                R(@"\bj[ée]sus\b", "purée"),
            },
            new[]   // German
            {
                R(@"\bgottverdammt", "verflixt"),
                R(@"\bum\s+gottes\s+willen\b", "um Himmels willen"),
                R(@"\bgott\s+sei\s+dank\b", "zum Glück"),
                R(@"\bmein\s+gott\b", "meine Güte"),
                R(@"\boh\s+gott\b", "oh Mann"),
                R(@"\bgrundgütiger\b", "meine Güte"),
                R(@"\bherrgott\w*", "Mensch"),
                R(@"\b(jessas|jesus)\b", "Mannomann"),
            },
            new[]   // Spanish
            {
                R(@"\bdios\s+m[ií]o\b", "madre mía"),
                R(@"\bgracias\s+a\s+dios\b", "menos mal"),
                R(@"\bpor\s+dios\b", "por favor"),
                R(@"\bque\s+dios\s+nos\s+\w+", "que haya suerte"),
                R(@"\bdios(?=\s*[,!…])", "madre mía"),
                R(@"\bhostias?\b", "ostras"),
                R(@"\bjes[uú]s\b", "caramba"),
            },
            new[]   // Russian
            {
                R(@"\bне\s+слава\s+богу\b", "не так"),
                R(@"\bслава\s+богу\b", "к счастью"),
                R(@"\b(?:о\s+)?боже(?:\s+мой)?\b", "ох"),
                R(@"\b(?:о\s+)?господи\b", "ох"),
            },
            new[]   // Portuguese
            {
                R(@"\bmeu\s+deus(?:\s+do\s+c[ée]u)?\b", "minha nossa"),
                R(@"\bgraças\s+a\s+deus(?=\s+\p{L})", "ainda bem que"),
                R(@"\bgraças\s+a\s+deus\b", "ainda bem"),
                R(@"\bpelo\s+amor\s+de\s+deus\b", "por favor"),
                R(@"\bdeus\s+do\s+c[ée]u\b", "caramba"),
                R(@"^senhor(?=\s*[,!])", "caramba"),
                R(@"\bjesus\b", "caramba"),
            },
            new[]   // Chinese (simplified)
            {
                R(@"愿上帝保佑我们", "但愿我们走运"),
                R(@"我的上帝", "我的天"),
                R(@"上帝", "老天"),
                R(@"耶稣", "天哪"),
            },
            new[]   // Chinese (traditional)
            {
                R(@"願主保佑我們", "但願我們走運"),
                R(@"我的上帝", "我的天"),
                R(@"上帝", "老天"),
                R(@"主啊", "天啊"),
                R(@"耶穌", "天哪"),
            },
            new[]   // Japanese
            {
                R(@"オーマイゴッド", "なんてこった"),
                R(@"ジーザス", "なんてこった"),
                R(@"神よ", "なんてこった"),
            },
            new[]   // Korean
            {
                R(@"하느님|하나님", "세상에"),
                R(@"오\s*마이\s*갓", "세상에"),
                R(@"지저스", "세상에"),
                R(@"주여", "세상에"),
            },
        };

        private static (Regex pattern, MatchEvaluator replace)[] For(int language) =>
            language >= 0 && language < Rules.Length ? Rules[language] : Rules[0];

        public static bool IsBlasphemous(string text, int language = 0) =>
            !string.IsNullOrEmpty(text) && For(language).Any(r => r.pattern.IsMatch(text));

        public static string Clean(string text, int language = 0)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (pattern, replace) in For(language)) text = pattern.Replace(text, replace);
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
