using FluentAssertions;
using Howazit.Responses.Domain.Entities;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Infrastructure.Realtime;
using Howazit.Responses.Infrastructure.Repositories;
using Howazit.Responses.Infrastructure.Resilience;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Howazit.Responses.Tests.Resiliency;

public class RedisRetryPolicyTests {
    [Fact]
    public async Task TryAddAsyncRetriesOnTransientFailureThenSucceeds() {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<ResponsesDbContext>()
            .UseSqlite(conn) // no extra package needed
            .Options;

        await using var ctx = new FlakyResponsesDbContext(options); // fails first SaveChangesAsync()
        await ctx.Database.EnsureCreatedAsync(); // create schema once per connection

        var repo = new EfResponseRepository(ctx, NullLogger<EfResponseRepository>.Instance);

        var entity = new SurveyResponse {
            SurveyId = "s1",
            ClientId = "c1",
            ResponseId = "r1",
            NpsScore = 10,
            Satisfaction = "great",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act: first SaveChangesAsync throws (simulated), then Polly retry should succeed
        var added = await repo.TryAddAsync(entity);

        // Assert
        added.Should().BeTrue();
        var count = await repo.CountForClientAsync("c1");
        count.Should().Be(1);
    }

    [Fact]
    public async Task RedisPipelineRetriesOnTransientFailuresAndUpdatesNps() {
        // Arrange
        var policies = new ResiliencePolicies();

        var dbMock = new Mock<IDatabase>(MockBehavior.Strict);
        var muxMock = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);

        // First call to HashIncrementAsync throws (simulate transient),
        // subsequent calls succeed. This proves the retry works.
        var incCall = 0;
        dbMock
            .Setup(d => d.HashIncrementAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Returns(() => {
                if (incCall++ == 0) throw new TimeoutException("simulated redis transient");
                return Task.FromResult(1L);
            });

        // For GetNpsAsync: first attempt throws, then succeeds returning
        // promoters=1, passives=0, detractors=0, total=1
        var getCall = 0;
        dbMock
            .Setup(d => d.HashGetAsync(
                It.IsAny<RedisKey>(),
                It.Is<RedisValue[]>(f => f.Length == 4),
                It.IsAny<CommandFlags>()))
            .Returns(() => {
                if (getCall++ == 0) throw new TimeoutException("simulated redis transient");
                // positions expected by store: promoters, passives, detractors, total
                return Task.FromResult(new RedisValue[] { 1, 0, 0, 1 });
            });

        muxMock
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        var store = new RedisAggregateStore(muxMock.Object, policies, NullLogger<RedisAggregateStore>.Instance);

        // Act
        await store.UpdateNpsAsync("c1", 10);
        var snap = await store.GetNpsAsync("c1");

        // Assert
        snap.Promoters.Should().Be(1);
        snap.Passives.Should().Be(0);
        snap.Detractors.Should().Be(0);
        snap.Total.Should().Be(1);
        snap.Nps.Should().Be(100.0);

        // Optional sanity: verify calls happened at least once (proves we hit Redis layer)
        dbMock.Verify(
            d => d.HashIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), 1, It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
        dbMock.Verify(d => d.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }
}

internal sealed class FlakyResponsesDbContext : ResponsesDbContext {
    private int _failuresLeft = 1;
    public FlakyResponsesDbContext(DbContextOptions<ResponsesDbContext> options) : base(options) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        if (_failuresLeft-- > 0) throw new DbUpdateException("simulated");
        return base.SaveChangesAsync(cancellationToken);
    }
}