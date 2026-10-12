using System.Security.Claims;
using FluentValidation;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// Who wrote the question, as the server knows it: the user typed it, picked one of the options, tapped a reply
/// Proton suggested, or said it in a voice session. Only a typed turn may start the gated kinds of change (see
/// ProtonCoverage.NeedsTypedTurn): a transcript can mishear an amount or an address.
/// </summary>
public enum ProtonTurnOrigin
{
    /// <summary>Not a question from the user; also what a context starts as, so a forgotten origin fails closed.</summary>
    Continue,
    Choice,
    Suggestion,
    Typed,
    Spoken,
}

/// <summary>
/// What a running tool may lean on: the caller's authorization against other endpoints, the existing request
/// validators, and the company calendar for turning period words into days. One per question.
/// </summary>
public sealed class ProtonToolContext(
    ClaimsPrincipal caller,
    UserAccess access,
    IAuthorizationService authorization,
    IServiceProvider services,
    DateOnly today,
    DayOfWeek firstDayOfWeek)
{
    private readonly Dictionary<string, bool> _policies = new(StringComparer.Ordinal);

    /// <summary>The caller's role, permissions and visible domains, for choosing what to read; the endpoints still decide.</summary>
    public UserAccess Access => access;

    public DateOnly Today => today;

    public DayOfWeek FirstDayOfWeek => firstDayOfWeek;

    public ProtonTurnOrigin Turn { get; set; } = ProtonTurnOrigin.Continue;

    /// <summary>
    /// What the user typed in this conversation, this turn included: an email address or a recipient Proton uses must
    /// come from it, never from text in the data.
    /// </summary>
    public string TypedText { get; set; } = string.Empty;

    /// <summary>A proposal is waiting for the user's Confirm: no yes/no or suggested replies then, so none can pass for one.</summary>
    public bool ActionPending { get; set; }

    /// <summary>While a confirmed action runs: its row, so what it writes can say it came through Proton (a message).</summary>
    public long? ConfirmedActionId { get; set; }

    private readonly Dictionary<(ProtonRefType, long), ProtonRef> _known = [];

    /// <summary>
    /// The records tools have returned to this user in this conversation: the only ones Proton may offer as choices,
    /// so an option is always something the user's own access already found.
    /// </summary>
    public IReadOnlyCollection<ProtonRef> Known => _known.Values;

    public void Remember(IEnumerable<ProtonRef> refs)
    {
        foreach (var record in refs)
        {
            _known[(record.Type, record.Id)] = record;
        }
    }

    public ProtonRef? KnownRecord(ProtonRefType type, long id) => _known.GetValueOrDefault((type, id));

    private readonly Dictionary<string, Ui.ProtonHeldDataset> _datasets = new(StringComparer.Ordinal);
    private readonly List<Ui.ProtonHeldDataset> _newDatasets = [];

    /// <summary>The number the next dataset's id takes ("d" and a number, counted per conversation).</summary>
    public int NextDataset { get; private set; } = 1;

    /// <summary>The datasets this question's tools returned, for the conversation to keep.</summary>
    public IReadOnlyList<Ui.ProtonHeldDataset> NewDatasets => _newDatasets;

    /// <summary>Charts, tables and figures shown in this question's answer so far.</summary>
    public int BlocksShown { get; set; }

    /// <summary>The datasets the conversation holds from earlier questions.</summary>
    public void Hold(IEnumerable<Ui.ProtonHeldDataset> datasets, int next)
    {
        foreach (var held in datasets)
        {
            _datasets[held.Id] = held;
        }

        NextDataset = Math.Max(NextDataset, next);
    }

    /// <summary>Keeps a dataset a tool returned, under a new id.</summary>
    public Ui.ProtonHeldDataset HoldDataset(string tool, Ui.ProtonDataset dataset, DateTime at)
    {
        var held = new Ui.ProtonHeldDataset($"d{NextDataset++}", tool, dataset, at);
        _datasets[held.Id] = held;
        _newDatasets.Add(held);
        return held;
    }

    /// <summary>Forgets datasets whose tool call didn't succeed after all (its result was too large).</summary>
    public void Drop(IEnumerable<Ui.ProtonHeldDataset> datasets)
    {
        foreach (var held in datasets)
        {
            _datasets.Remove(held.Id);
            _newDatasets.Remove(held);
        }
    }

    /// <summary>A dataset a tool returned to this user in this conversation; null for any other id.</summary>
    public Ui.ProtonHeldDataset? Dataset(string id) => _datasets.GetValueOrDefault(id);

    /// <summary>Whether the caller passes every authorization policy of <paramref name="endpoint"/>.</summary>
    public async Task<bool> CanCallAsync(ProtonEndpoint endpoint)
    {
        foreach (var policy in endpoint.Policies)
        {
            if (!_policies.TryGetValue(policy, out var allowed))
            {
                allowed = (await authorization.AuthorizeAsync(caller, policy)).Succeeded;
                _policies[policy] = allowed;
            }

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>For a tool that also reads a second endpoint: refused exactly as that endpoint would refuse.</summary>
    public async Task RequireAsync(ProtonEndpoint endpoint)
    {
        if (!await CanCallAsync(endpoint))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "This user isn't allowed to see that.");
        }
    }

    /// <summary>Runs the validator the endpoint's request would go through, if it has one.</summary>
    public async Task ValidateAsync<T>(T query, CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(IValidator<T>)) is IValidator<T> validator)
        {
            await validator.ValidateAndThrowAsync(query, cancellationToken);
        }
    }

    /// <summary>The same, for a request known only at run time — as the MVC validation filter finds its validator.</summary>
    public async Task ValidateAsync(object request, CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(IValidator<>).MakeGenericType(request.GetType())) is IValidator validator)
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(request), cancellationToken);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }
    }

    public DayRange Period(ProtonPeriod? period, DateOnly? from, DateOnly? to, ProtonPeriod fallback, int maxDays = 366)
    {
        var (range, denial) = ProtonPeriods.Resolve(period ?? fallback, from, to, today, firstDayOfWeek, maxDays);
        return range ?? throw new ProtonToolInputException(denial!.Message);
    }

    /// <summary>A period and the one it is compared with (<see cref="ProtonPeriods.Compare"/>), the previous one by default.</summary>
    public ProtonPeriodPair Compare(
        ProtonPeriod? period, DateOnly? from, DateOnly? to, ProtonPeriod fallback,
        ProtonComparison? comparison, DateOnly? compareFrom, DateOnly? compareTo)
    {
        var current = Period(period, from, to, fallback);
        var (pair, denial) = ProtonPeriods.Compare(period ?? fallback, current, comparison ?? ProtonComparison.Previous, compareFrom, compareTo);
        return pair ?? throw new ProtonToolInputException(denial!.Message);
    }
}
