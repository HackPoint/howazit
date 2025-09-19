using Howazit.Responses.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Howazit.Responses.Infrastructure.Persistence;

public class ResponsesDbContext(DbContextOptions<ResponsesDbContext> options) : DbContext(options) {
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();

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
    }
}