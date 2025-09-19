using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using Howazit.Responses.Application.Models;

namespace Howazit.Responses.Api.Common;

public static class ValidationExtensions
{
    public static Dictionary<string, string[]> ToProblemDictionary(this ValidationResult vr)
        => vr.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => NormalizeKey(typeof(IngestRequest), g.Key),
                g => g.Select(e => e.ErrorMessage).ToArray()
            );

    private static string NormalizeKey(Type rootType, string propertyPath)
    {
        // If it's already a JSON-ish key (underscores or starts with known roots), pass through.
        if (propertyPath.Contains('_') || propertyPath.StartsWith("responses.", StringComparison.OrdinalIgnoreCase)
                                       || propertyPath.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
        {
            return propertyPath;
        }

        // Otherwise, convert from C# path (SurveyId, Responses.NpsScore, etc.) to JSON names.
        var currentType = rootType;
        var parts = propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var jsonParts = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var pi = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi is null)
            {
                jsonParts.Add(char.ToLowerInvariant(part[0]) + part[1..]);
                continue;
            }

            var jsonName = pi.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                           ?? (char.ToLowerInvariant(pi.Name[0]) + pi.Name[1..]);

            jsonParts.Add(jsonName);
            currentType = pi.PropertyType;
        }

        return string.Join('.', jsonParts);
    }
}