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

    /// <summary>
    /// For a tool standing in for several endpoints whose policies differ (issue or void an invoice): each of them.
    /// The one a proposal goes through is required on propose and again on confirm, on top of <see cref="IProtonTool.Endpoint"/>.
    /// </summary>
    IReadOnlyList<ProtonEndpoint> StepEndpoints => [];

    Task<ProtonActionDone> ExecuteAsync(string arguments, ProtonToolContext context, CancellationToken cancellationToken);
}

/// <summary>One line of what will change. <paramref name="Before"/> is empty for something new.</summary>
public sealed record ProtonChange(string Label, string? Before, string After);

/// <summary>How a card is confirmed: one Confirm, a second step to look again, or the typed phrase on top.</summary>
public enum ProtonConfirm
{
    Standard,
    TwoStep,
    Typed,
}

/// <summary>What the user is shown and confirms.</summary>
/// <param name="OpensAfter">What the app opens once the change is done, when it will ("the new project"); shown on the card.</param>
/// <param name="Confirm">At least what the action's tier needs; the confirmation checks the tier itself, never this alone.</param>
/// <param name="Phrase">What the user types to confirm, for <see cref="ProtonConfirm.Typed"/>: the record's name, an invoice number.</param>
/// <param name="Area">The business it happens in, shown on the card.</param>
/// <param name="Irreversible">It can't be undone in the app.</param>
/// <param name="Reach">Who it affects or notifies, computed on the server ("23 people in Design").</param>
public sealed record ProtonPreview(
    string Title,
    IReadOnlyList<ProtonChange> Changes,
    string? Note = null,
    string? OpensAfter = null,
    ProtonConfirm Confirm = ProtonConfirm.Standard,
    string? Phrase = null,
    string? Area = null,
    bool Irreversible = false,
    string? Reach = null);

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

    public virtual IReadOnlyList<ProtonEndpoint> StepEndpoints => [];

    /// <summary>The step endpoint this change goes through, for a tool with <see cref="StepEndpoints"/>.</summary>
    protected virtual ProtonEndpoint? StepOf(TArgs args) => null;

    protected sealed override async Task<ProtonToolOutput> RunAsync(TInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var (args, preview, target) = await ProposeAsync(input, context, cancellationToken);
        await RequireStepAsync(args, context);
        await ValidateAsync(args, context, cancellationToken);
        if ((input.OpenAfter ?? OpensByDefault) && OpensLabel(args, target) is { } opens)
        {
            preview = preview with { OpensAfter = opens };
        }

        // a strong action is never confirmed with one click, whatever its spec drew
        if (ProtonCoverage.TierOf(Name) == ProtonTier.Strong && preview.Confirm == ProtonConfirm.Standard)
        {
            preview = preview with { Confirm = ProtonConfirm.TwoStep };
        }

        if (preview.Confirm == ProtonConfirm.Typed && string.IsNullOrWhiteSpace(preview.Phrase))
        {
            throw new InvalidOperationException($"{Name}: a typed confirmation needs its phrase.");
        }

        preview = preview with { Area = preview.Area ?? target?.Domain };

        return new ProtonToolOutput(preview, target is null ? [] : [target])
        {
            Proposal = new ProtonProposal(JsonSerializer.Serialize(args, ProtonJson.Options), preview, target),
        };
    }

    public async Task<ProtonActionDone> ExecuteAsync(string arguments, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var args = JsonSerializer.Deserialize<TArgs>(arguments, ProtonJson.Options)
            ?? throw new InvalidOperationException($"{Name}: stored arguments are empty.");
        await RequireStepAsync(args, context);
        await ValidateAsync(args, context, cancellationToken);
        return await ExecuteAsync(args, context, cancellationToken);
    }

    private async Task RequireStepAsync(TArgs args, ProtonToolContext context)
    {
        if (StepOf(args) is not { } step)
        {
            return;
        }

        if (!StepEndpoints.Contains(step))
        {
            throw new InvalidOperationException($"{Name}: {step.Name} is not one of its step endpoints.");
        }

        await context.RequireAsync(step);
    }

    private async Task ValidateAsync(TArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (RequestOf(args) is { } request)
        {
            await context.ValidateAsync(request, cancellationToken);
        }
    }
}
