namespace Howazit.Responses.Domain.Entities;

public class SurveyResponse {
    public long Id { get; set; }
    public string ClientId { get; set; } = null!;
    public string SurveyId { get; set; } = null!;
    public string ResponseId { get; set; } = null!;

    public int NpsScore { get; set; }               // validated 0..10
    public string? Satisfaction { get; set; }
    public string? CustomFieldsJson { get; set; } // stored as JSON text

    public DateTimeOffset Timestamp { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}