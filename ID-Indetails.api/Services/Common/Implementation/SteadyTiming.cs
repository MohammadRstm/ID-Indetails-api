using System.Diagnostics;

namespace ID_Indetails.api.Services.Common.Implementation;

/// <summary>Holds an answer back until a fixed time has passed, so its timing can't tell which branch was taken.</summary>
public static class SteadyTiming
{
    public static long Start() => Stopwatch.GetTimestamp();

    public static async Task WaitUntilAsync(long startedTimestamp, TimeSpan minimum, CancellationToken cancellationToken)
    {
        var remaining = minimum - Stopwatch.GetElapsedTime(startedTimestamp);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }
}
