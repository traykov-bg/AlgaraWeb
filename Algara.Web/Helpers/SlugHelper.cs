using System.Text;
using System.Text.RegularExpressions;

namespace Algara.Web.Helpers
{
    /// <summary>
    /// Generates URL-friendly slugs from Bulgarian (Cyrillic) or Latin text.
    /// Example: "Мека мебел" -> "meka-mebel"
    /// </summary>
    public static class SlugHelper
    {
        private static readonly Dictionary<char, string> TranslitMap = new()
        {
            ['а'] = "a",   ['б'] = "b",   ['в'] = "v",   ['г'] = "g",
            ['д'] = "d",   ['е'] = "e",   ['ж'] = "zh",  ['з'] = "z",
            ['и'] = "i",   ['й'] = "y",   ['к'] = "k",   ['л'] = "l",
            ['м'] = "m",   ['н'] = "n",   ['о'] = "o",   ['п'] = "p",
            ['р'] = "r",   ['с'] = "s",   ['т'] = "t",   ['у'] = "u",
            ['ф'] = "f",   ['х'] = "h",   ['ц'] = "ts",  ['ч'] = "ch",
            ['ш'] = "sh",  ['щ'] = "sht", ['ъ'] = "a",   ['ь'] = "",
            ['ю'] = "yu",  ['я'] = "ya",
        };

        public static string Generate(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var sb = new StringBuilder();
            foreach (char ch in input.ToLowerInvariant())
            {
                if (TranslitMap.TryGetValue(ch, out var latin))
                    sb.Append(latin);
                else if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                    sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '_')
                    sb.Append('-');
                // всички останали символи се пропускат
            }

            return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        }
    }
}
