using System.Net;
using System.Text.RegularExpressions;
using FluentValidation;
using Howazit.Responses.Application.Models;

namespace Howazit.Responses.Application.Validations;

public partial class IngestRequestValidator : AbstractValidator<IngestRequest> {
    private static readonly Regex IdRegex = IdRegexValidator();

    public IngestRequestValidator() {
        // IDs (use JSON names in messages and keys)
        RuleFor(x => x.SurveyId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithName("surveyId").OverridePropertyName("surveyId")
            .Must(BeValidId).WithMessage("surveyId must be 1-100 chars of [A-Za-z0-9:_-].")
            .OverridePropertyName("surveyId");

        RuleFor(x => x.ClientId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithName("clientId").OverridePropertyName("clientId")
            .Must(BeValidId).WithMessage("clientId must be 1-100 chars of [A-Za-z0-9:_-].")
            .OverridePropertyName("clientId");

        RuleFor(x => x.ResponseId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithName("responseId").OverridePropertyName("responseId")
            .Must(BeValidId).WithMessage("responseId must be 1-100 chars of [A-Za-z0-9:_-].")
            .OverridePropertyName("responseId");

        // Presence of nested objects
        RuleFor(x => x.Responses)
            .NotNull().WithMessage("responses is required.")
            .OverridePropertyName("responses");

        RuleFor(x => x.Metadata)
            .NotNull().WithMessage("metadata is required.")
            .OverridePropertyName("metadata");

        // Timestamp basic rule (fires when Metadata present)
        RuleFor(x => x.Metadata!.Timestamp)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .WithMessage("timestamp cannot be in the far future.")
            .When(x => x.Metadata is not null)
            .OverridePropertyName("metadata.timestamp");

        // --- Belt & suspenders: explicit custom checks that always add JSON-keyed failures ---
        RuleFor(x => x).Custom((x, ctx) => {
            // responses.nps_score
            if (x.Responses is not null) {
                var nps = x.Responses.NpsScore;
                if (nps < 0 || nps > 10) {
                    ctx.AddFailure("responses.nps_score",
                        $"'nps_score' must be between 0 and 10. You entered {nps}.");
                }
            }

            // metadata.ip_address
            if (!string.IsNullOrWhiteSpace(x.Metadata?.IpAddress)) {
                if (!IPAddress.TryParse(x.Metadata.IpAddress, out _)) {
                    ctx.AddFailure("metadata.ip_address",
                        "ip_address must be a valid IPv4 or IPv6.");
                }
            }
        });
    }

    [GeneratedRegex("^[A-Za-z0-9:_\\-]{1,100}$", RegexOptions.Compiled)]
    private static partial Regex IdRegexValidator();

    private static bool BeValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && IdRegex.IsMatch(id);

}