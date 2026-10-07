using System.Text.Json;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>
/// A tool that changes data — in two separate steps. When the model calls it, it only <em>proposes</em>: it reads the
/// target, checks the change, validates the request it would send, and returns what the user will confirm, inside the
/// same read-only guard as every read tool. <see cref="ExecuteAsync"/> sends that stored request to the existing write
/// service, and only the user's confirmation request reaches it: the model has no way to call it.
/// </summary>
public interface IProtonActionTool : IProtonTool
{
    ProtonActionRisk Risk { get; }

    Task<ProtonActionDone> ExecuteAsync(string arguments, ProtonToolContext context, CancellationToken cancellationToken);
}

/// <summary>One line of what will change. <paramref name="Before"/> is empty for something new.</summary>
public sealed record ProtonChange(string Label, string? Before, string After);

/// <summary>What the user is shown and confirms.</summary>
public sealed record ProtonPreview(string Title, IReadOnlyList<ProtonChange> Changes, string? Note = null);

/// <summary>A proposed change: the exact request to send (JSON), what the user sees, and the record it acts on.</summary>
public sealed record ProtonProposal(string Arguments, ProtonPreview Preview, ProtonRef? Target);

/// <summary>What the existing service did, in words, and the record it created or changed.</summary>
public sealed record ProtonActionDone(string Message, string ResultType, long ResultId, IReadOnlyList<ProtonRef> Refs);

/// <summary>An action tool whose model input is <typeparamref name="TInput"/> and whose stored request is <typeparamref name="TArgs"/>.</summary>
public abstract class ProtonActionTool<TInput, TArgs> : ProtonTool<TInput>, IProtonActionTool
    where TInput : class
    where TArgs : class
{
    public abstract ProtonActionRisk Risk { get; }

    /// <summary>Resolves the target and builds the request. Reads only; throws the usual exceptions when it can't be done.</summary>
    protected abstract Task<(TArgs Args, ProtonPreview Preview, ProtonRef? Target)> ProposeAsync(
        TInput input, ProtonToolContext context, CancellationToken cancellationToken);

    /// <summary>The existing request DTO inside the arguments, validated with its own validator on propose and on execute.</summary>
    protected abstract object? RequestOf(TArgs args);

    protected abstract Task<ProtonActionDone> ExecuteAsync(TArgs args, ProtonToolContext context, CancellationToken cancellationToken);

    protected sealed override async Task<ProtonToolOutput> RunAsync(TInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (args, preview, target) = await ProposeAsync(input, context, cancellationToken);
        await ValidateAsync(args, context, cancellationToken);
        return new ProtonToolOutput(preview, target is null ? [] : [target])
        {
            Proposal = new ProtonProposal(JsonSerializer.Serialize(args, ProtonJson.Options), preview, target),
        };
    }

    public async Task<ProtonActionDone> ExecuteAsync(string arguments, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<TArgs>(arguments, ProtonJson.Options)
            ?? throw new InvalidOperationException($"{Name}: stored arguments are empty.");
        await ValidateAsync(args, context, cancellationToken);
        return await ExecuteAsync(args, context, cancellationToken);
    }

    private async Task ValidateAsync(TArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (RequestOf(args) is { } request)
        {
            await context.ValidateAsync(request, cancellationToken);
        }
    }
}
