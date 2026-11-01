using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ID_Indetails.api.Data;

/// <summary>
/// Counts the database commands of the request it was started in, for the request cost log
/// (<see cref="Middlewares.RequestCostMiddleware"/>). Outside a started request it does nothing.
/// </summary>
public static class QueryCount
{
    private static readonly AsyncLocal<Counter?> Current = new();

    public static DbCommandInterceptor Interceptor { get; } = new Counting();

    /// <summary>Starts counting for the async flow it is called from: the request, and everything it awaits.</summary>
    public static Counter Start() => Current.Value = new Counter();

    public sealed class Counter
    {
        private int _commands;

        public int Commands => _commands;

        internal void Add() => Interlocked.Increment(ref _commands);
    }

    private sealed class Counting : DbCommandInterceptor
    {
        public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
        {
            Current.Value?.Add();
            return result;
        }
    }
}
