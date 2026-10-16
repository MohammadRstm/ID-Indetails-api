using System.Diagnostics;
using ID_Indetails.api.Services.Common.Implementation;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class SteadyTimingTests
{
    [Fact]
    public async Task A_quick_answer_is_held_back_until_the_minimum_has_passed()
    {
        var started = Stopwatch.GetTimestamp();

        await SteadyTiming.WaitUntilAsync(started, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Stopwatch.GetElapsedTime(started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(195));
    }

    [Fact]
    public async Task A_slow_answer_is_not_held_back_further()
    {
        var started = Stopwatch.GetTimestamp() - Stopwatch.Frequency;

        var waiting = Stopwatch.StartNew();
        await SteadyTiming.WaitUntilAsync(started, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        waiting.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
    }
}
