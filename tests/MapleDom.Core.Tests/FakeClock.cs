using MapleDom.Core.Time;

namespace MapleDom.Core.Tests;

public sealed class FakeClock : IClock
{
    public TimeSpan Now { get; set; } = TimeSpan.FromHours(1);

    public void Advance(TimeSpan by) => Now += by;

    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
}
