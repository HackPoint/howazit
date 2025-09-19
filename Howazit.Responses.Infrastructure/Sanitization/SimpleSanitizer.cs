using System.Text.RegularExpressions;

namespace Howazit.Responses.Infrastructure.Sanitization;

/// <summary>
/// Minimal HTML/script stripper and whitespace normalizer.
/// Avoids extra NuGet dependencies; sufficient for interview scope.
/// </summary>
public sealed partial class SimpleSanitizer : ISanitizer {
    // Remove <script>...</script> first, then any tags.
    private static readonly Regex ScriptRegex =
        ScriptRegexDefinition();

    private static readonly Regex TagRegex = TagRegexDefinition();
    private static readonly Regex WsRegex = WsRegexDefinition();

    public string Sanitize(string? input) {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim();
        s = ScriptRegex.Replace(s, string.Empty);
        s = TagRegex.Replace(s, string.Empty);
        s = System.Net.WebUtility.HtmlDecode(s);
        s = WsRegex.Replace(s, " ").Trim();
        return s;
    }

    public void SanitizeInPlace(IDictionary<string, object?> dict) {
        foreach (var key in dict.Keys.ToList()) {
            var v = dict[key];
            if (v is string s) {
                dict[key] = Sanitize(s);
            }
        }
    }

    [GeneratedRegex(@"<script[\s\S]*?>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-IL")]
    private static partial Regex ScriptRegexDefinition();
    
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WsRegexDefinition();
    
    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegexDefinition();
}