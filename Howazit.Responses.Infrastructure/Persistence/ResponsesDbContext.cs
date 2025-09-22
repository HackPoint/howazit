using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Domain.Entities;
using Howazit.Responses.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Howazit.Responses.Infrastructure.Persistence;

public class ResponsesDbContext : DbContext {
    private readonly IFieldProtector _protector;
    private readonly bool _encryptUserAgent;
    
    internal bool EncryptUserAgent => _encryptUserAgent;
    
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

    public ResponsesDbContext(
        DbContextOptions<ResponsesDbContext> options,
        IFieldProtector protector,
        DataProtectionFieldProtector.Options encOptions)
        : base(options) {
        _protector = protector;
        _encryptUserAgent = encOptions?.EncryptUserAgent ?? false;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        var e = modelBuilder.Entity<SurveyResponse>();
        e.ToTable("survey_responses");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();

        e.Property(x => x.ClientId).IsRequired().HasMaxLength(100);
        e.Property(x => x.SurveyId).IsRequired().HasMaxLength(100);
        e.Property(x => x.ResponseId).IsRequired().HasMaxLength(100);

        e.Property(x => x.NpsScore).IsRequired();
        e.Property(x => x.Satisfaction).HasMaxLength(100);
        e.Property(x => x.CustomFieldsJson);
        e.Property(x => x.Timestamp).IsRequired();
        e.Property(x => x.UserAgent).HasMaxLength(256);
        e.Property(x => x.IpAddress).HasMaxLength(64);
        e.Property(x => x.CreatedAtUtc).IsRequired();

        // Idempotency: one logical response per (ClientId, ResponseId)
        e.HasIndex(x => new { x.ClientId, x.ResponseId }).IsUnique();

        // Transparent encryption converters
        var protect = new ValueConverter<string?, string?>(
            v => _protector.Protect(v),
            v => _protector.Unprotect(v));

        modelBuilder.Entity<SurveyResponse>()
            .Property(x => x.IpAddress)
            .HasConversion(protect);


        // always encrypt IP at rest
        e.Property(p => p.IpAddress)
            .HasConversion(
                v => _protector.Protect(v),
                v => _protector.Unprotect(v));

        if (_encryptUserAgent) {
            e.Property(x => x.UserAgent)
                .HasConversion(
                    v => _protector.Protect(v),
                    v => _protector.Unprotect(v)
                );
        }
    }
}