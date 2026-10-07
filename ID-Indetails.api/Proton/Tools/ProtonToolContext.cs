using System.Security.Claims;
using FluentValidation;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>
/// What a running tool may lean on: the caller's authorization against other endpoints, the existing request
/// validators, and the company calendar for turning period words into days. One per question.
/// </summary>
public sealed class ProtonToolContext(
    ClaimsPrincipal caller,
    IAuthorizationService authorization,
    IServiceProvider services,
    DateOnly today,
    DayOfWeek firstDayOfWeek)
{
    private readonly Dictionary<string, bool> _policies = new(StringComparer.Ordinal);

    public DateOnly Today => today;

    public DayOfWeek FirstDayOfWeek => firstDayOfWeek;

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
}
