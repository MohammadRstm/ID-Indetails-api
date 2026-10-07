using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ID_Indetails.api.Proton.Safety;

/// <summary>
/// The guarantee that Proton's tools can't change data, whatever service they call: while a tool runs, every
/// database command other than a read is refused before it reaches MySQL. The scope flows with the async call,
/// so writes elsewhere in the same request (the usage record, last-seen) are unaffected.
/// </summary>
public static class ProtonReadOnly
{
    private static readonly AsyncLocal<bool> Active = new();

    public static DbCommandInterceptor Interceptor { get; } = new Guard();

    public static bool IsActive => Active.Value;

    /// <summary>Runs <paramref name="action"/> with writes refused, and lets them through again afterwards.</summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        Active.Value = true;
        try
        {
            return await action();
        }
        finally
        {
            Active.Value = false;
        }
    }

    /// <summary>True for a SELECT (or WITH … SELECT), ignoring leading comments and whitespace.</summary>
    public static bool IsRead(string sql)
    {
        var text = sql.AsSpan().TrimStart();
        while (text.StartsWith("--"))
        {
            var lineEnd = text.IndexOf('\n');
            text = lineEnd < 0 ? [] : text[(lineEnd + 1)..].TrimStart();
        }

        return text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Guard : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Check(command, allowRead: true);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command, allowRead: true);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Check(command, allowRead: true);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Check(command, allowRead: true);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Check(command, allowRead: false);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(command, allowRead: false);
            return ValueTask.FromResult(result);
        }

        private static void Check(DbCommand command, bool allowRead)
        {
            if (IsActive && !(allowRead && IsRead(command.CommandText)))
            {
                throw new ProtonWriteRefusedException();
            }
        }
    }
}

/// <summary>A Proton tool tried to change data. Always a bug in the tool or the service it called.</summary>
public sealed class ProtonWriteRefusedException()
    : InvalidOperationException("Proton tools are read-only: a database write was refused.");
