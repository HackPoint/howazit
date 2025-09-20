using System.Net;
using System.Text.RegularExpressions;

namespace Howazit.Responses.Infrastructure.Sanitization;

/// <summary>
/// Lightweight HTML sanitizer with zero external deps:
/// - strips <script> / <style>
/// - strips all HTML tags
/// - decodes HTML entities
/// - collapses whitespace
/// - leaves non-string values untouched
/// </summary>
public sealed partial class HtmlSanitizer : ISanitizer {
    public string Sanitize(string? input) {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input;

        // Remove dangerous blocks first
        s = ScriptRegex().Replace(s, string.Empty);
        s = StyleRegex().Replace(s, string.Empty);

        // Strip all remaining tags
        s = TagRegex().Replace(s, string.Empty);

        // Decode entities (&amp; -> &, etc.)
        s = WebUtility.HtmlDecode(s);

        // Remove NULs and collapse whitespace
        s = s.Replace("\0", string.Empty);
        s = WhiteRegex().Replace(s, " ").Trim();

        return s;
    }

    public void SanitizeInPlace(IDictionary<string, object?> dict) {
        if (dict is null) return;

        // Copy keys to avoid "collection modified" during writes
        var keys = dict.Keys.ToArray();
        foreach (var key in keys) {
            var value = dict[key];

            switch (value) {
                case null:
                    // leave nulls as-is
                    break;

                case string str:
                    dict[key] = Sanitize(str);
                    break;

                case IDictionary<string, object?> nested:
                    // recursively sanitize nested dictionaries
                    SanitizeInPlace(nested);
                    break;
                default:
                    // leave other types (numbers, bools, arrays, etc.) untouched
                    break;
            }
        }
    }

    [GeneratedRegex("<script.*?>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex("<style.*?>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhiteRegex();
}