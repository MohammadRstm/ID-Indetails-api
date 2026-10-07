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
    public static ProtonToolOutput Of(object data, params ProtonRef[] refs) => new(data, refs);
}

public enum ProtonRefType
{
    Project,
    Client,
    Person,
}

/// <summary>A record an answer is about. The client turns these into links; the model never writes URLs.</summary>
public sealed record ProtonRef(ProtonRefType Type, long Id, string Name, string? Domain = null);

/// <summary>The model sent input the tool can't use. The message is shown to the model so it can correct itself.</summary>
public sealed class ProtonToolInputException(string message) : Exception(message);
