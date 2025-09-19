namespace Howazit.Responses.Infrastructure.Sanitization;

public interface ISanitizer {
    string Sanitize(string? input);
    void SanitizeInPlace(IDictionary<string, object?> dict);
}