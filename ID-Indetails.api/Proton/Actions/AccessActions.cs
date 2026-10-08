using System.ComponentModel;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Teams.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The details when proposed: if someone changed them since, the update is refused.</param>
public sealed record UpdateEmployeeArgs(long UserId, UpdateEmployeeRequest Request, UpdateEmployeeRequest Before);

public sealed record UpdateEmployeeInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    [Description("An empty string removes it.")]
    public string? Phone { get; init; }

    [Description("Their job title; an empty string removes it.")]
    public string? Position { get; init; }

    [Description("A team of their domain (from list_teams).")]
    public long? TeamId { get; init; }

    [Description("Take them out of their team.")]
    public bool RemoveTeam { get; init; }
}

/// <summary>
/// Changes someone's name, phone, position or team, through PUT /api/employees/{id} once confirmed. Their role,
/// domain, sign-in email and pay are never part of it.
/// </summary>
public sealed class ProposeUpdateEmployeeTool(IEmployeeService employees, ITeamService teams)
    : ProtonActionTool<UpdateEmployeeInput, UpdateEmployeeArgs>
{
    public override string Name => "propose_update_employee";

    public override string Description =>
        "Propose changing someone's name, phone, position or team. Role, domain, sign-in email and pay are changed in " +
        "the app. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<EmployeesController>(nameof(EmployeesController.Update));

    protected override async Task<(UpdateEmployeeArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateEmployeeInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        var before = new UpdateEmployeeRequest
        {
            FirstName = person.FirstName, LastName = person.LastName, Phone = person.Phone, Position = person.Position, TeamId = person.Team?.Id,
        };

        string? teamName = person.Team?.Name;
        if (input.TeamId is { } teamId)
        {
            var team = await TeamText.FindAsync(teams, teamId, cancellationToken);
            if (team.Domain != person.Domain)
            {
                throw new ProtonToolInputException($"{team.Name} is a team of another domain: pick one of {AccessText.Name(person)}'s domain.");
            }

            teamName = team.Name;
        }

        var after = new UpdateEmployeeRequest
        {
            FirstName = input.FirstName?.Trim() ?? before.FirstName,
            LastName = input.LastName?.Trim() ?? before.LastName,
            Phone = input.Phone is null ? before.Phone : AccessText.Clean(input.Phone),
            Position = input.Position is null ? before.Position : AccessText.Clean(input.Position),
            TeamId = input.RemoveTeam ? null : input.TeamId ?? before.TeamId,
        };

        var changes = new List<ProtonChange>();
        void Compare(string label, string? was, string? now)
        {
            if (was != now)
            {
                changes.Add(new ProtonChange(label, was ?? "none", now ?? "none"));
            }
        }

        Compare("First name", before.FirstName, after.FirstName);
        Compare("Last name", before.LastName, after.LastName);
        Compare("Phone", before.Phone, after.Phone);
        Compare("Position", before.Position, after.Position);
        if (after.TeamId != before.TeamId)
        {
            changes.Add(new ProtonChange("Team", person.Team?.Name ?? "none", after.TeamId is null ? "none" : teamName!));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new UpdateEmployeeArgs(person.Id, after, before), new ProtonPreview($"Change {AccessText.Name(person)}'s details", changes), AccessText.Ref(person));
    }

    protected override object RequestOf(UpdateEmployeeArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(UpdateEmployeeArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(args.UserId, cancellationToken);
        var now = new UpdateEmployeeRequest
        {
            FirstName = person.FirstName, LastName = person.LastName, Phone = person.Phone, Position = person.Position, TeamId = person.Team?.Id,
        };
        if (now != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Their details changed since Proton proposed this. Ask again.");
        }

        var updated = await employees.UpdateAsync(args.UserId, args.Request, cancellationToken);
        return new ProtonActionDone($"Updated {AccessText.Name(updated)}'s details.", "person", updated.Id, [AccessText.Ref(updated)]);
    }
}

/// <summary>What an access change does to someone, and the request it sends.</summary>
public enum ProtonAccessChange
{
    Deactivate,
    Reactivate,
    EndSessions,
    RestoreSignIn,
}

public sealed record AccessArgs(long UserId, ProtonAccessChange Change, DateOnly? LastWorkingDay, bool? PayGap);

public sealed record DeactivateInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("Their last (paid) day of work; today when not given.")]
    public DateOnly? LastWorkingDay { get; init; }
}

/// <summary>
/// Deactivates someone, through POST /api/employees/{id}/deactivate once confirmed: their sign-in is blocked, every
/// session ends, and their employment ends on their last working day. Their history is kept. The user types the
/// person's name to confirm.
/// </summary>
public sealed class ProposeDeactivateEmployeeTool(IEmployeeService employees) : ProtonActionTool<DeactivateInput, AccessArgs>
{
    public override string Name => "propose_deactivate_employee";

    public override string Description =>
        "Propose deactivating someone who is leaving: they can't sign in from then on and their employment ends on their " +
        "last working day; their history is kept. The user confirms by typing the person's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<EmployeesController>(nameof(EmployeesController.Deactivate));

    protected override async Task<(AccessArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DeactivateInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        if (person.Id == context.Access.UserId)
        {
            throw new ProtonToolInputException("People can't deactivate themselves.");
        }

        if (person.Status == UserStatus.Deactivated)
        {
            throw new ProtonToolInputException($"{AccessText.Name(person)} is already deactivated.");
        }

        var lastDay = input.LastWorkingDay ?? context.Today;
        var name = AccessText.Name(person);
        return (new AccessArgs(person.Id, ProtonAccessChange.Deactivate, lastDay, null),
            new ProtonPreview($"Deactivate {name}",
                [
                    new ProtonChange("Status", person.Status.ToString(), "Deactivated"),
                    new ProtonChange("Last working day", null, ProjectText.Date(lastDay)),
                ],
                "They are signed out everywhere and can't sign in. Their work, messages and history stay. They can be reactivated later.",
                Confirm: ProtonConfirm.Typed,
                Phrase: name),
            AccessText.Ref(person));
    }

    protected override object RequestOf(AccessArgs args) => new DeactivateEmployeeRequest { LastWorkingDay = args.LastWorkingDay };

    protected override async Task<ProtonActionDone> ExecuteAsync(AccessArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.DeactivateAsync(args.UserId, new DeactivateEmployeeRequest { LastWorkingDay = args.LastWorkingDay }, cancellationToken);
        return new ProtonActionDone($"Deactivated {AccessText.Name(person)}.", "person", person.Id, [AccessText.Ref(person)]);
    }
}

public sealed record ReactivateInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("Whether their time away is paid. Only when the tool asks for it: ask the user, in their words, and pass their answer.")]
    public bool? PayGap { get; init; }
}

/// <summary>
/// Reactivates someone, through POST /api/employees/{id}/reactivate once confirmed. Someone who left earlier this month
/// is asked whether their time away is paid; someone who never signed up shows as invited again.
/// </summary>
public sealed class ProposeReactivateEmployeeTool(IEmployeeService employees) : ProtonActionTool<ReactivateInput, AccessArgs>
{
    public override string Name => "propose_reactivate_employee";

    public override string Description =>
        "Propose reactivating someone who was deactivated; they start a new period of employment today. Nothing changes " +
        "until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<EmployeesController>(nameof(EmployeesController.Reactivate));

    protected override async Task<(AccessArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ReactivateInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        if (person.Status != UserStatus.Deactivated)
        {
            throw new ProtonToolInputException($"{AccessText.Name(person)} isn't deactivated.");
        }

        var asks = person.LeftOn is { } leftOn && EmploymentRules.AsksAboutGap(leftOn, context.Today);
        if (asks && input.PayGap is null)
        {
            throw new ProtonToolInputException(
                $"{AccessText.Name(person)} left on {ProjectText.Date(person.LeftOn)}, earlier this month: ask the user whether their time away is paid.");
        }

        var changes = new List<ProtonChange>
        {
            new("Status", "Deactivated", person.JoinedOn is null ? "Invited" : "Active"),
            new("Employment", null, $"from {ProjectText.Date(context.Today)}"),
        };
        if (asks)
        {
            changes.Add(new ProtonChange("Time away", null, input.PayGap == true ? "paid" : "unpaid"));
        }

        return (new AccessArgs(person.Id, ProtonAccessChange.Reactivate, null, asks ? input.PayGap : null),
            new ProtonPreview($"Reactivate {AccessText.Name(person)}", changes,
                person.JoinedOn is null ? "They never signed up, so they show as invited again." : "They can sign in again."),
            AccessText.Ref(person));
    }

    protected override object RequestOf(AccessArgs args) => new ReactivateEmployeeRequest { PayGap = args.PayGap };

    protected override async Task<ProtonActionDone> ExecuteAsync(AccessArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.ReactivateAsync(args.UserId, new ReactivateEmployeeRequest { PayGap = args.PayGap }, cancellationToken);
        return new ProtonActionDone($"Reactivated {AccessText.Name(person)}.", "person", person.Id, [AccessText.Ref(person)]);
    }
}

public sealed record SessionsInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("True to sign them out everywhere and block their sign-in; false to let them sign in again.")]
    public required bool End { get; init; }
}

/// <summary>
/// Ends someone's sessions (signed out everywhere, sign-in blocked) or restores their sign-in, through POST
/// /api/employees/{id}/sessions/end or /restore once confirmed.
/// </summary>
public sealed class ProposeSessionsTool(IEmployeeService employees) : ProtonActionTool<SessionsInput, AccessArgs>
{
    public override string Name => "propose_end_sessions";

    public override string Description =>
        "Propose signing someone out everywhere and blocking their sign-in (a lost phone, a suspected break-in), or " +
        "letting them sign in again afterwards. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // End and Restore share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<EmployeesController>(nameof(EmployeesController.EndSessions));

    protected override async Task<(AccessArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SessionsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        if (person.Id == context.Access.UserId)
        {
            throw new ProtonToolInputException("That's the user's own sign-in: they can sign out from their profile menu.");
        }

        var blocked = person.SessionsRevokedAt is not null;
        if (blocked == input.End)
        {
            throw new ProtonToolInputException(blocked ? $"{AccessText.Name(person)}'s sign-in is already blocked." : $"{AccessText.Name(person)} can already sign in.");
        }

        var name = AccessText.Name(person);
        return (new AccessArgs(person.Id, input.End ? ProtonAccessChange.EndSessions : ProtonAccessChange.RestoreSignIn, null, null),
            new ProtonPreview(input.End ? $"Sign {name} out everywhere" : $"Let {name} sign in again",
                [new ProtonChange("Sign-in", blocked ? "Blocked" : "Allowed", input.End ? "Blocked" : "Allowed")],
                input.End ? "Every session of theirs ends now, and they can't sign in until it is restored." : null),
            AccessText.Ref(person));
    }

    protected override object? RequestOf(AccessArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(AccessArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = args.Change == ProtonAccessChange.EndSessions
            ? await employees.EndSessionsAsync(args.UserId, cancellationToken)
            : await employees.RestoreSignInAsync(args.UserId, cancellationToken);
        return new ProtonActionDone(
            args.Change == ProtonAccessChange.EndSessions ? $"Signed {AccessText.Name(person)} out everywhere." : $"{AccessText.Name(person)} can sign in again.",
            "person", person.Id, [AccessText.Ref(person)]);
    }
}

internal static class AccessText
{
    public static string Name(EmployeeSummaryResponse p)
    {
        var name = $"{p.FirstName} {p.LastName}".Trim();
        return name.Length > 0 ? name : p.Email;
    }

    public static ProtonRef Ref(EmployeeSummaryResponse p) => new(ProtonRefType.Person, p.Id, Name(p), p.Domain);

    public static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
