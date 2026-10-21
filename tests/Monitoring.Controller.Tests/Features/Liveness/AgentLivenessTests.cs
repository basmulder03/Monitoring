using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Liveness;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Liveness;

public class AgentLivenessTests
{
    [Fact]
    public void GetScopeLiveness_is_not_live_for_a_scope_no_agent_connected_for()
    {
        // Arrange
        var fixture = new LivenessFixture();

        // Act
        var liveness = fixture.Liveness.GetScopeLiveness("shop");

        // Assert
        liveness.IsLive.ShouldBeFalse();
        liveness.LastSeen.ShouldBeNull();
    }

    [Fact]
    public void GetScopeLiveness_is_live_once_an_agent_connected()
    {
        // Arrange
        var fixture = new LivenessFixture();

        // Act
        fixture.Liveness.Connected("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeTrue();
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(60, false)]
    public void GetScopeLiveness_is_live_until_the_heartbeat_timeout_passes_in_silence(int silentSeconds, bool expected)
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Clock.Advance(TimeSpan.FromSeconds(silentSeconds));

        // Act
        var live = fixture.Liveness.GetScopeLiveness("shop").IsLive;

        // Assert
        live.ShouldBe(expected);
    }

    [Fact]
    public void Seen_keeps_the_scope_live()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Liveness.Seen("shop", fixture.Connection);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));

        // Act
        var live = fixture.Liveness.GetScopeLiveness("shop").IsLive;

        // Assert: 20 s since connecting, but only 10 s since the last message
        live.ShouldBeTrue();
    }

    [Fact]
    public void Seen_moves_the_time_the_scope_was_last_heard_from()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var expected = fixture.Clock.GetUtcNow();

        // Act
        fixture.Liveness.Seen("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").LastSeen.ShouldBe(expected);
    }

    [Fact]
    public void Disconnected_makes_the_scope_not_live_at_once()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);

        // Act
        fixture.Liveness.Disconnected("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeFalse();
    }

    [Fact]
    public void Disconnected_records_when_the_agent_went_away_and_that_time_stays_fixed()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));
        var wentAway = fixture.Clock.GetUtcNow();
        fixture.Liveness.Disconnected("shop", fixture.Connection);

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var lastSeen = fixture.Liveness.GetScopeLiveness("shop").LastSeen;

        // Assert
        lastSeen.ShouldBe(wentAway);
    }

    [Fact]
    public void GetScopeLiveness_stays_live_while_another_connection_of_the_scope_is_alive()
    {
        // Arrange
        var fixture = new LivenessFixture();
        var other = Guid.NewGuid();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Liveness.Connected("shop", other);

        // Act
        fixture.Liveness.Disconnected("shop", fixture.Connection);

        // Assert: an agent that reconnects overlaps its old connection for a moment
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeTrue();
    }

    [Fact]
    public void GetScopeLiveness_ignores_a_connection_that_went_silent_while_another_stays_alive()
    {
        // Arrange
        var fixture = new LivenessFixture();
        var other = Guid.NewGuid();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Liveness.Connected("shop", other);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Liveness.Seen("shop", other);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));

        // Act
        var live = fixture.Liveness.GetScopeLiveness("shop").IsLive;

        // Assert
        live.ShouldBeTrue();
    }

    [Fact]
    public void Seen_ignores_a_connection_that_was_never_registered()
    {
        // Arrange
        var fixture = new LivenessFixture();

        // Act
        fixture.Liveness.Seen("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeFalse();
    }

    [Fact]
    public void Seen_does_not_bring_back_a_connection_that_ended()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Liveness.Disconnected("shop", fixture.Connection);

        // Act
        fixture.Liveness.Seen("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeFalse();
    }

    [Fact]
    public void GetScopeLiveness_judges_each_scope_on_its_own()
    {
        // Arrange
        var fixture = new LivenessFixture();
        fixture.Liveness.Connected("shop", fixture.Connection);
        fixture.Liveness.Connected("billing", Guid.NewGuid());

        // Act
        fixture.Liveness.Disconnected("shop", fixture.Connection);

        // Assert
        fixture.Liveness.GetScopeLiveness("shop").IsLive.ShouldBeFalse();
        fixture.Liveness.GetScopeLiveness("billing").IsLive.ShouldBeTrue();
    }

    [Fact]
    public void Timeout_is_the_missed_heartbeats_together()
    {
        // Arrange
        var options = new LivenessOptions { HeartbeatInterval = TimeSpan.FromSeconds(4), MissedHeartbeats = 5 };

        // Act
        var timeout = options.Timeout;

        // Assert
        timeout.ShouldBe(TimeSpan.FromSeconds(20));
    }

    /// <summary>Builds the tracker over a fake clock, with one connection id to use.</summary>
    private sealed class LivenessFixture
    {
        public LivenessFixture()
        {
            Liveness = new AgentLiveness(new LivenessOptions(), Clock);
        }

        public FakeTimeProvider Clock { get; } = new();

        public AgentLiveness Liveness { get; }

        public Guid Connection { get; } = Guid.NewGuid();
    }
}
