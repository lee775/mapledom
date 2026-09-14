using MapleDom.Core.Text;
using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class DurationFormatTests
{
    [Theory]
    [InlineData(900, "15분")]
    [InlineData(1795, "29분 55초")]
    [InlineData(58, "58초")]
    [InlineData(3720, "1시간 2분")]
    [InlineData(0, "0초")]
    [InlineData(3661, "1시간 1분 1초")]
    public void Korean_formats(double seconds, string expected)
        => Assert.Equal(expected, DurationFormat.Korean(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(899, "14:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(0.4, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(60, "1:00")]
    public void Clock_formats(double seconds, string expected)
        => Assert.Equal(expected, DurationFormat.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Template_replaces_tokens()
    {
        var evt = new AlarmEvent("rune", "룬", AlarmKind.Reminder, 3, TimeSpan.Zero, TimeSpan.Zero);
        var snap = new TimerSnapshot("rune", "룬", TimerKind.Acknowledge, TimerStatus.Overdue,
            TimeSpan.Zero, null, TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(60),
            3, TimeSpan.FromMinutes(3), 4, null, TimeSpan.Zero);

        var text = AlarmTemplate.Format("{name} {interval} #{n} +{overdue}", evt, snap);

        Assert.Equal("룬 15분 #3 +3분", text);
        Assert.Equal("", AlarmTemplate.Format("", evt, snap));
    }
}
