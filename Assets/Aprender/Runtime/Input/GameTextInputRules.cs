using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Aprender
{
    /// <summary>Accented names follow the same Unicode rule as the session and server.</summary>
    public static class GameTextInputRules
    {
        static readonly Regex NamePattern = new Regex(@"^[\p{L}\p{N}\p{M} -]{2,20}$");

        public static int CharacterCount(string value) => string.IsNullOrEmpty(value) ? 0 : StringInfo.ParseCombiningCharacters(value).Length;

        public static string NormalizePlayerName(string value) => NormalizeCharacters(value).Trim();

        public static string NormalizeCharacters(string value)
        {
            try { return (value ?? "").Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return value ?? ""; }
        }

        public static bool IsValidPlayerName(string value)
        {
            value = NormalizePlayerName(value);
            var length = CharacterCount(value);
            // Keep the existing server's 20 UTF-16 unit ceiling after composition.
            // This avoids accepting a name here which GameSession would reject.
            return length >= 2 && length <= 20 && NamePattern.IsMatch(value);
        }

        public static string RemoveLastCharacter(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var starts = StringInfo.ParseCombiningCharacters(value);
            return value.Substring(0, starts[starts.Length - 1]);
        }
    }
}
