using System.Text.Json;
using System.Text.Json.Nodes;
using ID_Indetails.api.Business.Proton;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// A read-only business question Proton can ask the system ("list projects", "who worked on this project").
/// It is provider neutral: any model (chat today, live voice later) uses the same tools through <see cref="ProtonToolbox"/>.
/// </summary>
public interface IProtonTool
{
    /// <summary>snake_case, as the model sees it.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>The endpoint whose authorization the tool inherits; the tool isn't offered to anyone who fails it.</summary>
    ProtonEndpoint Endpoint { get; }

    /// <summary>Set for tools whose data only one domain has (Design hours): offered only to people who see it.</summary>
    string? OnlyInDomain { get; }

    /// <summary>Loads every project's money or a domain's whole picture: only a few such calls are allowed per question.</summary>
    bool Heavy => false;

    JsonObject InputSchema { get; }

    Task<ProtonToolOutput> RunAsync(JsonElement input, ProtonToolContext context, CancellationToken cancellationToken);
}

/// <summary>A tool whose input is the record <typeparamref name="TInput"/>; its schema is generated from it.</summary>
public abstract class ProtonTool<TInput> : IProtonTool where TInput : class
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract ProtonEndpoint Endpoint { get; }

    public virtual string? OnlyInDomain => null;

    public virtual bool Heavy => false;

    public JsonObject InputSchema => ProtonJson.SchemaOf(typeof(TInput));

    public Task<ProtonToolOutput> RunAsync(JsonElement input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        TInput? typed;
        try
        {
            typed = input.Deserialize<TInput>(ProtonJson.Options);
        }
        catch (JsonException exception)
        {
            throw new ProtonToolInputException(exception.Message);
        }

        return RunAsync(typed ?? throw new ProtonToolInputException("The input is missing."), context, cancellationToken);
    }

    protected abstract Task<ProtonToolOutput> RunAsync(TInput input, ProtonToolContext context, CancellationToken cancellationToken);
}

/// <summary>What a tool found: the data the model reads, and the records it is about (for links and follow-ups).</summary>
public sealed record ProtonToolOutput(object Data, IReadOnlyList<ProtonRef> Refs, DayRange? Period = null)
{
    /// <summary>Set only by action tools: the change they propose, which the user must confirm before anything happens.</summary>
    public Actions.ProtonProposal? Proposal { get; init; }

    /// <summary>Set by analytical tools: what the figures rest on, shown under the answer.</summary>
    public ProtonBasis? Basis { get; init; }

    /// <summary>Set by UI tools: what the app should open or refresh once the answer is shown.</summary>
    public Ui.ProtonUiCommand? UiCommand { get; init; }

    /// <summary>Set by offer_choices: the options the user can pick from instead of typing.</summary>
    public IReadOnlyList<Ui.ProtonChoice>? Choices { get; init; }

    /// <summary>Set by offer_choices: answers to Proton's question, in the app's words.</summary>
    public IReadOnlyList<Ui.ProtonQuickReplyOption>? QuickReplies { get; init; }

    /// <summary>Set by suggest_replies: what the user might say next, sent back by its place in the list, never as text.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <summary>
    /// Set by tools whose result can be shown as a chart, a table or headline figures: the same data as typed tables,
    /// built in code. The conversation keeps them so present_data can show them without the model writing a value.
    /// </summary>
    public IReadOnlyList<Ui.ProtonDataset>? Datasets { get; init; }

    /// <summary>Set by present_data: the chart, table or figures shown with the answer.</summary>
    public Ui.ProtonBlock? Block { get; init; }

    public static ProtonToolOutput Of(object data, params ProtonRef[] refs) => new(data, refs);
}

public enum ProtonRefType
{
    Project,
    Client,
    Person,
    /// <summary>A team chat the user is in (find_recipient, the inbox): its id is the team's.</summary>
    Team,
    /// <summary>A project on the company's public website (not a Design or Execution project): the Website area's own.</summary>
    WebsiteProject,
}

/// <summary>A record an answer is about. The client turns these into links; the model never writes URLs.</summary>
public sealed record ProtonRef(ProtonRefType Type, long Id, string Name, string? Domain = null);

/// <summary>The model sent input the tool can't use. The message is shown to the model so it can correct itself.</summary>
public sealed class ProtonToolInputException(string message) : Exception(message);

/// <summary>
/// What an analytical result rests on, for the model to state and the user to see ("Based on …"): the exact days,
/// what they were compared with, the scope, the rules that judged anything, and what was left out and why.
/// </summary>
/// <param name="Label">What was analysed, in words ("Hours compared").</param>
/// <param name="Partial">The period is still running: its figures are "so far".</param>
/// <param name="Comparison">How <paramref name="ComparedWith"/> was chosen, in words.</param>
/// <param name="Rules">The rules behind any judgement in the result (notable, unusual, idle…).</param>
/// <param name="Omitted">What the result leaves out: what the user can't see, or data that doesn't exist.</param>
public sealed record ProtonBasis(
    string Label,
    DayRange? Period,
    DayRange? ComparedWith,
    bool Partial,
    string? Comparison,
    string? Scope,
    IReadOnlyList<string> Rules,
    IReadOnlyList<string> Omitted);
