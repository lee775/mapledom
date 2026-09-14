using System.Diagnostics;

namespace MapleDom.Core.Time;

/// <summary>
/// 단조 증가(monotonic) 시계. 값은 절대 시각이 아니라 기준점으로부터의 경과 시간이며,
/// 시스템 시계 변경(NTP 보정, 수동 변경)의 영향을 받지 않는다.
/// 엔진과 타이머의 모든 시각 값(NextDue, ScheduledAt 등)은 이 시계 기준이다.
/// </summary>
public interface IClock
{
    TimeSpan Now { get; }
}

/// <summary>Stopwatch(QueryPerformanceCounter) 기반 고해상도 단조 시계.</summary>
public sealed class MonotonicClock : IClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);

    public static bool IsHighResolution => Stopwatch.IsHighResolution;

    public static long Frequency => Stopwatch.Frequency;
}
