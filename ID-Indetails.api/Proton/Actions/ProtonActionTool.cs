using System.ComponentModel;
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
/// <param name="OpensAfter">What the app opens once the change is done, when it will ("the new project"); shown on the card.</param>
public sealed record ProtonPreview(string Title, IReadOnlyList<ProtonChange> Changes, string? Note = null, string? OpensAfter = null);

/// <summary>A proposed change: the exact request to send (JSON), what the user sees, and the record it acts on.</summary>
public sealed record ProtonProposal(string Arguments, ProtonPreview Preview, ProtonRef? Target);

/// <summary>What the existing service did, in words, and the record it created or changed.</summary>
/// <param name="Opens">Where the result lives in the app; opened only when the proposal said it would.</param>
public sealed record ProtonActionDone(string Message, string ResultType, long ResultId, IReadOnlyList<ProtonRef> Refs, Ui.ProtonUiCommand? Opens = null);

/// <summary>The input every action takes besides its own.</summary>
public abstract record ProtonActionInput
{
    [Description("Open the result in the app after the user confirms: true when they asked to be taken there. " +
                 "Leave out for the usual (a new project opens; other changes don't).")]
    public bool? OpenAfter { get; init; }
}

/// <summary>An action tool whose model input is <typeparamref name="TInput"/> and whose stored request is <typeparamref name="TArgs"/>.</summary>
public abstract class ProtonActionTool<TInput, TArgs> : ProtonTool<TInput>, IProtonActionTool
    where TInput : ProtonActionInput
    where TArgs : class
{
    public abstract ProtonActionRisk Risk { get; }

    /// <summary>Whether the result opens after confirming when the model doesn't say: only for something new (decided by the user).</summary>
    protected virtual bool OpensByDefault => false;

    /// <summary>What the card says will open, from the record the change is about.</summary>
    protected virtual string? OpensLabel(TArgs args, ProtonRef? target) => target?.Name;

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
        if ((input.OpenAfter ?? OpensByDefault) && OpensLabel(args, target) is { } opens)
        {
            preview = preview with { OpensAfter = opens };
        }

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
