using System.Net;
using FluentValidation;
using Howazit.Responses.Api.Common;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Sanitization;

namespace Howazit.Responses.Api.Responses;

public static class ResponseHandlers {
    private static readonly string[] Errors = ["X-Client-Id header does not match payload clientId."];

    public static async Task<IResult> PostAsync(
        HttpContext http,
        IngestRequest dto,
        IValidator<IngestRequest> validator,
        IBackgroundQueueService<IngestRequest> queue,
        ISanitizer sanitizer) {
        // Enforce header/payload clientId match if header is present
        if (http.Request.Headers.TryGetValue("X-Client-Id", out var headerClient) &&
            !string.Equals(headerClient.ToString(), dto.ClientId, StringComparison.Ordinal)) {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> {
                    ["clientId"] = Errors
                },
                statusCode: 400,
                title: "Validation failed");
        }

        // Run FluentValidation
        var vr = await validator.ValidateAsync(dto, http.RequestAborted);

        if (!vr.IsValid) {
            // Convert to ProblemDetails errors dictionary
            var errors = vr.ToProblemDictionary();

            // ---- Belt & suspenders: ensure JSON-keyed nested errors appear ----

            // 1) responses.nps_score must be 0..10 (if Responses present)
            if (dto.Responses is not null) {
                var nps = dto.Responses.NpsScore;
                if ((nps < 0 || nps > 10) && !errors.ContainsKey("responses.nps_score")) {
                    errors["responses.nps_score"] = new[] {
                        $"'nps_score' must be between 0 and 10. You entered {nps}."
                    };
                }
            }

            // 2) metadata.ip_address must parse if present
            if (!string.IsNullOrWhiteSpace(dto.Metadata?.IpAddress)) {
                if (!IPAddress.TryParse(dto.Metadata.IpAddress, out _) &&
                    !errors.ContainsKey("metadata.ip_address")) {
                    errors["metadata.ip_address"] = new[] {
                        "ip_address must be a valid IPv4 or IPv6."
                    };
                }
            }

            return Results.ValidationProblem(errors, statusCode: 400, title: "Validation failed");
        }

        // Build sanitized copy for enqueue
        var sanitizedCustom = dto.Responses.CustomFields is null
            ? null
            : dto.Responses.CustomFields
                .ToDictionary(kv => kv.Key, kv => kv.Value is string s ? (object?)sanitizer.Sanitize(s) : kv.Value);

        var sanitized = new IngestRequest {
            SurveyId = dto.SurveyId,
            ClientId = dto.ClientId,
            ResponseId = dto.ResponseId,
            Responses = new ResponsesPayload {
                NpsScore = dto.Responses.NpsScore,
                Satisfaction = dto.Responses.Satisfaction is null
                    ? null
                    : sanitizer.Sanitize(dto.Responses.Satisfaction),
                CustomFields = sanitizedCustom
            },
            Metadata = new MetadataPayload {
                Timestamp = dto.Metadata.Timestamp,
                UserAgent = dto.Metadata.UserAgent is null ? null : sanitizer.Sanitize(dto.Metadata.UserAgent),
                IpAddress = dto.Metadata.IpAddress is null ? null : sanitizer.Sanitize(dto.Metadata.IpAddress)
            }
        };

        await queue.EnqueueAsync(sanitized, http.RequestAborted);

        var location =
            $"/v1/responses/{Uri.EscapeDataString(sanitized.ClientId)}/{Uri.EscapeDataString(sanitized.ResponseId)}/status";
        return Results.Accepted(location, new { responseId = sanitized.ResponseId });
    }
}