using FluentAssertions;
using Howazit.Responses.Infrastructure.Resilience;
using Xunit;

namespace Howazit.Responses.Tests.Resiliency;

public class DbRetryPolicyTests {
    [Fact]
    public async Task DbWritePipelineRetriesOnTimeoutThenSucceeds() {
        var policies = new ResiliencePolicies();
        var attempts = 0;

        var ok = await policies.DbWrite.ExecuteAsync(_ => {
            attempts++;
            if (attempts < 3)
                throw new TimeoutException("simulated transient");

            return ValueTask.FromResult(true);
        });

        ok.Should().BeTrue();
        attempts.Should().Be(3);
    }
}