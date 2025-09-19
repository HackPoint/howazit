using System.Text.Json.Serialization;

namespace Howazit.Responses.Application.Models;

public sealed class IngestRequest {
    [JsonPropertyName("surveyId")] public string SurveyId { get; init; } = null!;

    [JsonPropertyName("clientId")] public string ClientId { get; init; } = null!;

    [JsonPropertyName("responseId")] public string ResponseId { get; init; } = null!;

    [JsonPropertyName("responses")] public ResponsesPayload Responses { get; init; } = null!;

    [JsonPropertyName("metadata")] public MetadataPayload Metadata { get; init; } = null!;
}

public sealed class ResponsesPayload {
    [JsonPropertyName("nps_score")] public int? NpsScore { get; init; }

    [JsonPropertyName("satisfaction")] public string? Satisfaction { get; init; }

    [JsonPropertyName("custom_fields")] public Dictionary<string, object?>? CustomFields { get; init; }
}

public sealed class MetadataPayload {
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("user_agent")] public string? UserAgent { get; init; }

    [JsonPropertyName("ip_address")] public string? IpAddress { get; init; }
}