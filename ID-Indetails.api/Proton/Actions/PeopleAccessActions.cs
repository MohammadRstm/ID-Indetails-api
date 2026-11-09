using System.ComponentModel;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Invitations.Abstraction;
using ID_Indetails.api.Services.Permissions.Abstraction;
using ID_Indetails.api.Services.Teams.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonPersonStep
{
    ChangeRole,
    Move,
    RemovePhoto,
}

public enum ProtonRole
{
    Employee,
    Admin,
    SuperAdmin,
}

/// <param name="Before">The person as proposed (<see cref="PeopleText.Snapshot"/>): if someone changed them since, the change is refused.</param>
public sealed record PersonAccessArgs(ProtonPersonStep Step, long UserId, string Before, ChangeRoleRequest? Role, MoveEmployeeRequest? Move);

public sealed record PersonAccessInput : ProtonActionInput
{
    [Description("change_role, move (to the other business) or remove_photo (their profile photo).")]
    public required ProtonPersonStep Step { get; init; }

    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("change_role: the new role.")]
    public ProtonRole? Role { get; init; }

    [Description("change_role: only for a super admin who becomes an admin or employee (the business they join). move: the business to move them to.")]
    public ProtonDomain? Domain { get; init; }

    [Description("change_role: their new job title with the role; an empty string clears it. Leave out to keep it.")]
    public string? Position { get; init; }

    [Description("move: a department of the new business (from list_teams). A Design employee needs one; others may have none.")]
    public long? TeamId { get; init; }
}

/// <summary>
/// Changes someone's role, moves them to the other business or removes their profile photo, through PUT
/// /api/employees/{id}/role, PUT …/domain or DELETE …/photo once confirmed. Role and business changes hand them the new
/// defaults and clear their personal permission changes. Never oneself, and never pay: a promotion to admin only says
/// that their pay becomes a fixed monthly salary, which is set in the app. The user types the person's name.
/// </summary>
public sealed class ProposeEmployeeAccessTool(
    IEmployeeService employees,
    IEmployeeRepository payRecords,
    IPermissionService permissions,
    ITeamService teams)
    : ProtonActionTool<PersonAccessInput, PersonAccessArgs>
{
    public override string Name => "propose_employee_access";

    public override string Description =>
        "Propose changing someone's role (employee, admin, super admin), moving them to the other business (Design or " +
        "Execution), or removing their profile photo. A new role or business replaces their personal permission " +
        "changes with its defaults. Pay is never part of it. The user confirms by typing the person's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // role, domain and photo share one permission
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<EmployeesController>(nameof(EmployeesController.ChangeRole));

    protected override async Task<(PersonAccessArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PersonAccessInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        var name = AccessText.Name(person);
        var target = PeopleText.TargetOf(person);
        var before = PeopleText.Snapshot(person);
        var reference = AccessText.Ref(person);
        switch (input.Step)
        {
            case ProtonPersonStep.ChangeRole:
            {
                var role = PeopleText.RoleCode(input.Role ?? throw new ProtonToolInputException("Give the new role: employee, admin or super_admin."));
                var superAdmins = (await employees.ListAsync(
                    new EmployeeListQuery { Role = RoleCodes.SuperAdmin, Status = UserStatus.Active, PageSize = 1 }, cancellationToken)).TotalCount;
                var (decision, denial) = EmployeeRules.CheckRoleChange(context.Access, target, role, input.Domain.Code(), superAdmins);
                PeopleText.Refuse(denial);
                PeopleText.Refuse(EmployeeRules.CheckRoleHandOut(context.Access, await PeopleText.DefaultsAsync(permissions, decision!.Role, decision.Domain, cancellationToken), []));
                PeopleText.Refuse(PayRules.CheckRoleChange(
                    decision.Role, (await payRecords.GetPayInForceAsync(person.Id, context.Today, cancellationToken))?.Basis));

                // Design's employees and admins each belong to a department, and a new business clears it
                if (EmployeeRules.NeedsDepartment(decision.Role, decision.Domain) && (decision.Domain != person.Domain || person.Team is null))
                {
                    throw new ProtonToolInputException(decision.Domain != person.Domain
                        ? $"{name} would join Design with no department, and Design's employees and admins each need one. Change their role in the app (open their profile): the department is picked there with it."
                        : $"{name} has no department, and Design's employees and admins each need one. Give them a department first (propose_update_employee), then change the role.");
                }

                var position = input.Position is null ? person.Position : AccessText.Clean(input.Position);
                var changes = new List<ProtonChange> { new("Role", PeopleText.RoleWords(person.Role), PeopleText.RoleWords(decision.Role)) };
                if (decision.Domain != person.Domain)
                {
                    changes.Add(new ProtonChange("Business", PeopleText.DomainWords(person.Domain), PeopleText.DomainWords(decision.Domain)));
                    if (person.Team is not null)
                    {
                        changes.Add(new ProtonChange("Department", person.Team.Name, "none"));
                    }
                }

                if (position != person.Position)
                {
                    changes.Add(new ProtonChange("Position", person.Position ?? "none", position ?? "none"));
                }

                var note = "Their personal permission changes are cleared: they get the new role's default permissions.";
                if (PayRules.FixedPayFrom(person.Role, decision.Role, context.Today) is not null)
                {
                    note += " From the first of this month their pay is a fixed monthly salary, and they log no hours.";
                }

                return (new PersonAccessArgs(input.Step, person.Id, before,
                        new ChangeRoleRequest { Role = decision.Role, DomainCode = input.Domain.Code(), Position = input.Position is null ? null : position ?? string.Empty }, null),
                    new ProtonPreview($"Change {name}'s role to {PeopleText.RoleWords(decision.Role).ToLowerInvariant()}", changes, note,
                        Confirm: ProtonConfirm.Typed, Phrase: name, Area: decision.Domain ?? person.Domain),
                    reference);
            }
            case ProtonPersonStep.Move:
            {
                var domain = (input.Domain ?? throw new ProtonToolInputException("Give the business to move them to: design or execution.")).Code();
                PeopleText.Refuse(EmployeeRules.CheckMove(context.Access, target, domain));
                PeopleText.Refuse(EmployeeRules.CheckRoleHandOut(context.Access, await PeopleText.DefaultsAsync(permissions, person.Role, domain, cancellationToken), []));
                string? teamName = null;
                if (input.TeamId is { } teamId)
                {
                    var team = await TeamText.FindAsync(teams, teamId, cancellationToken);
                    if (team.Domain != domain)
                    {
                        throw new ProtonToolInputException($"{team.Name} is a department of {PeopleText.DomainWords(team.Domain)}: pick one of {PeopleText.DomainWords(domain)}.");
                    }

                    teamName = team.Name;
                }

                if (EmployeeRules.NeedsDepartment(person.Role, domain) && teamName is null)
                {
                    throw new ProtonToolInputException($"Design's employees and admins each need a department: give one of Design's (list_teams) to move {name} into.");
                }

                var changes = new List<ProtonChange> { new("Business", PeopleText.DomainWords(person.Domain), PeopleText.DomainWords(domain)) };
                if (person.Team is not null || teamName is not null)
                {
                    changes.Add(new ProtonChange("Department", person.Team?.Name ?? "none", teamName ?? "none"));
                }

                return (new PersonAccessArgs(input.Step, person.Id, before, null, new MoveEmployeeRequest { DomainCode = domain, TeamId = input.TeamId }),
                    new ProtonPreview($"Move {name} to {PeopleText.DomainWords(domain)}", changes,
                        "Their personal permission changes are cleared: they get the defaults of their role in the new business, and see its records instead.",
                        Confirm: ProtonConfirm.Typed, Phrase: name, Area: domain),
                    reference);
            }
            default:
            {
                PeopleText.Refuse(EmployeeRules.CheckCanChange(context.Access, target));
                if (person.PhotoFileId is null)
                {
                    throw new ProtonToolInputException($"{name} has no profile photo.");
                }

                return (new PersonAccessArgs(input.Step, person.Id, before, null, null),
                    new ProtonPreview($"Remove {name}'s profile photo", [new ProtonChange("Profile photo", "set", "removed")],
                        "The photo is deleted; a new one can be uploaded in the app.",
                        Confirm: ProtonConfirm.Typed, Phrase: name, Irreversible: true),
                    reference);
            }
        }
    }

    protected override object? RequestOf(PersonAccessArgs args) => args.Step switch
    {
        ProtonPersonStep.ChangeRole => args.Role,
        ProtonPersonStep.Move => args.Move,
        _ => null,
    };

    protected override async Task<ProtonActionDone> ExecuteAsync(PersonAccessArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (PeopleText.Snapshot(await employees.GetAsync(args.UserId, cancellationToken)) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Their details changed since Proton proposed this. Ask again.");
        }

        switch (args.Step)
        {
            case ProtonPersonStep.ChangeRole:
                var promoted = await employees.ChangeRoleAsync(args.UserId, args.Role!, cancellationToken);
                return new ProtonActionDone(
                    $"{AccessText.Name(promoted)} is now {PeopleText.RoleWords(promoted.Role).ToLowerInvariant()}." +
                    (promoted.FixedPayFrom is { } from ? $" Their pay is a fixed monthly salary from {ProjectText.Date(from)}: check it in the app." : ""),
                    "person", promoted.Id, [AccessText.Ref(promoted)]);
            case ProtonPersonStep.Move:
                var moved = await employees.MoveAsync(args.UserId, args.Move!, cancellationToken);
                return new ProtonActionDone($"Moved {AccessText.Name(moved)} to {PeopleText.DomainWords(moved.Domain)}.", "person", moved.Id, [AccessText.Ref(moved)]);
            default:
                var updated = await employees.RemovePhotoAsync(args.UserId, cancellationToken);
                return new ProtonActionDone($"Removed {AccessText.Name(updated)}'s profile photo.", "person", updated.Id, [AccessText.Ref(updated)]);
        }
    }
}

public sealed record PermissionChangeInput
{
    [Description("A permission code such as invoice:void (from get_permissions).")]
    public required string Code { get; init; }

    [Description("grant, revoke, or reset (back to what their role gives).")]
    public required PermissionChangeAction Action { get; init; }
}

/// <param name="Before">Their role and permissions as proposed: if they changed since, the change is refused.</param>
public sealed record PermissionsArgs(long UserId, UpdateUserPermissionsRequest Request, string Before);

public sealed record PermissionsInput : ProtonActionInput
{
    [Description("From find.")]
    public required long UserId { get; init; }

    [Description("What to grant, revoke or reset, at most 20, each once.")]
    public required IReadOnlyList<PermissionChangeInput> Changes { get; init; }

    [Description("Why, in the user's own words (kept with every change). Ask them if they didn't say.")]
    public required string Reason { get; init; }
}

/// <summary>
/// Grants, revokes or resets someone's permissions, through PUT /api/users/{id}/permissions once confirmed. The card
/// says what each one lets them do in words, and when the result matches the other role's defaults, that they take that
/// role. Never one's own, never a super admin's, and only permissions the user holds. The user types the person's name.
/// </summary>
public sealed class ProposePermissionsTool(
    IPermissionManagementService management,
    IPermissionService permissions,
    IEmployeeService employees,
    IEmployeeRepository payRecords)
    : ProtonActionTool<PermissionsInput, PermissionsArgs>
{
    private const int MaxChanges = 20;

    public override string Name => "propose_permissions";

    public override string Description =>
        "Propose granting, revoking or resetting someone's permissions (get_permissions has the codes and what they hold), " +
        "with why. Each permission is explained on the card; if the result equals the other role's defaults they take that " +
        "role. The user confirms by typing the person's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<PermissionsController>(nameof(PermissionsController.Update));

    protected override async Task<(PermissionsArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PermissionsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Changes.Count is 0 or > MaxChanges)
        {
            throw new ProtonToolInputException($"Give between 1 and {MaxChanges} changes; split a longer list into several cards.");
        }

        var reason = AccessText.Clean(input.Reason) ?? throw new ProtonToolInputException("Ask the user why: a reason is kept with every permission change.");
        var current = await management.GetForUserAsync(input.UserId, cancellationToken);
        var person = await employees.GetAsync(input.UserId, cancellationToken);
        var changes = input.Changes.Select(c => new PermissionChangeRequest { Code = c.Code.Trim(), Action = c.Action, Reason = reason }).ToList();

        var catalog = await permissions.GetCatalogAsync(cancellationToken);
        if (changes.Select(c => c.Code).Where(code => catalog.All(p => p.Code != code)).ToList() is { Count: > 0 } unknown)
        {
            throw new ProtonToolInputException($"Unknown permission: {string.Join(", ", unknown)}. get_permissions lists the codes.");
        }

        if (changes.Select(c => c.Code).Distinct().Count() != changes.Count)
        {
            throw new ProtonToolInputException("Each permission may appear only once.");
        }

        if (PermissionChangePolicy.Check(context.Access, person.Id, person.Role, person.Status, changes.Select(c => c.Code)) is { } denial)
        {
            throw new ProtonToolInputException(denial.Message);
        }

        var plan = PermissionChangePlanner.Build(
            current.Role, current.Domain, catalog, await permissions.GetGrantsByRoleAsync(cancellationToken),
            current.Overrides.Select(o => new EffectivePermissions.Override(o.Code, o.Effect)),
            changes.Select(c => new PermissionChangePlanner.Change(c.Code, c.Action)));
        if (changes.Where(c => plan.Applied.All(a => a.Code != c.Code)).Select(c => c.Code).ToList() is { Count: > 0 } unchanged)
        {
            throw new ProtonToolInputException(
                $"Nothing would change for {string.Join(", ", unchanged)}: they already hold or lack it, or only employees log hours. Leave it out.");
        }

        if (plan.PreviousRole is not null)
        {
            PeopleText.Refuse(PayRules.CheckRoleChange(plan.Role, (await payRecords.GetPayInForceAsync(person.Id, context.Today, cancellationToken))?.Basis));
        }

        var name = AccessText.Name(person);
        var held = current.Permissions.ToHashSet(StringComparer.Ordinal);
        var rows = new List<ProtonChange>();
        foreach (var change in plan.Applied)
        {
            var label = PeopleText.Describe(change.Code);
            var allowed = $"Allowed: {PermissionExplanations.Of(change.Code)}";
            // every applied change flips what they hold
            rows.Add(held.Contains(change.Code) ? new ProtonChange(label, allowed, "Not allowed") : new ProtonChange(label, "Not allowed", allowed));
        }

        rows.Add(new ProtonChange("Why", null, reason));

        var notes = new List<string>();
        var sensitive = plan.Applied
            .Where(c => !held.Contains(c.Code))
            .Select(c => PeopleText.SensitivityOf(c.Code))
            .Where(s => s != PermissionSensitivity.None)
            .Distinct()
            .Select(PeopleText.SensitivityWords)
            .ToList();
        if (sensitive.Count > 0)
        {
            notes.Add($"This hands out access to {string.Join(", ", sensitive)}.");
        }

        if (plan.PreviousRole is { } previous)
        {
            notes.Add($"They would then hold exactly the {PeopleText.RoleWords(plan.Role).ToLowerInvariant()} permissions, so they become {PeopleText.RoleWords(plan.Role).ToLowerInvariant()} (instead of {PeopleText.RoleWords(previous).ToLowerInvariant()}).");
            if (PayRules.FixedPayFrom(previous, plan.Role, context.Today) is not null)
            {
                notes.Add("From the first of this month their pay is a fixed monthly salary, and they log no hours.");
            }
        }

        var args = new PermissionsArgs(person.Id, new UpdateUserPermissionsRequest { Changes = changes }, PeopleText.Snapshot(current));
        return (args,
            new ProtonPreview($"Change {name}'s permissions", rows, notes.Count == 0 ? null : string.Join(" ", notes),
                Confirm: ProtonConfirm.Typed, Phrase: name, Area: person.Domain),
            AccessText.Ref(person));
    }

    protected override object RequestOf(PermissionsArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(PermissionsArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (PeopleText.Snapshot(await management.GetForUserAsync(args.UserId, cancellationToken)) != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "Their permissions changed since Proton proposed this. Ask again.");
        }

        var updated = await management.UpdateAsync(args.UserId, args.Request, cancellationToken);
        var person = await employees.GetAsync(args.UserId, cancellationToken);
        return new ProtonActionDone(
            $"Changed {AccessText.Name(person)}'s permissions." +
            (updated.PreviousRole is null ? "" : $" They are now {PeopleText.RoleWords(updated.Role).ToLowerInvariant()}."),
            "person", person.Id, [AccessText.Ref(person)]);
    }
}

public enum ProtonStaffInvitationStep
{
    Create,
    Resend,
}

/// <param name="Step">Create fills the rest; resend only <paramref name="UserId"/> and the address it goes to.</param>
public sealed record InvitationArgs(
    ProtonStaffInvitationStep Step, long? UserId, string Email, string? DomainCode, long? TeamId, string? Position);

public sealed record InvitationInput : ProtonActionInput
{
    [Description("create (invite someone new) or resend (a fresh link for someone invited who hasn't signed up yet).")]
    public required ProtonStaffInvitationStep Step { get; init; }

    [Description("The invitee's email address, exactly as the user typed it in this conversation: never one from the data. " +
                 "For resend, the address the invitation went to.")]
    public required string Email { get; init; }

    [Description("resend: the invited person (from find).")]
    public long? UserId { get; init; }

    [Description("create: the business they join; leave out when the user works in only one.")]
    public ProtonDomain? Domain { get; init; }

    [Description("create: a department of that business (from list_teams). A Design employee needs one.")]
    public long? TeamId { get; init; }

    [Description("create: their job title.")]
    public string? Position { get; init; }
}

/// <summary>
/// Emails a signup link to an employee, through POST /api/invitations or /{id}/resend once confirmed: nothing else. The
/// address must be one the user typed in this conversation and is typed again to confirm. Proton invites employees only
/// and never sets pay: an admin's or super admin's invitation carries their salary, so it is made in the app.
/// </summary>
public sealed class ProposeInvitationTool(
    IInvitationService invitations,
    IEmployeeService employees,
    IUserRepository users,
    IPermissionService permissions,
    ITeamService teams)
    : ProtonActionTool<InvitationInput, InvitationArgs>
{
    public override string Name => "propose_invitation";

    public override string Description =>
        "Propose emailing a signup invitation to a new employee (their address as the user typed it), or a fresh link to " +
        "someone invited who hasn't signed up. It only sends the email: pay is never set (admins and super admins are " +
        "invited in the app, because their salary goes with the invitation). The user types the address again to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // create and resend share their policies
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<InvitationsController>(nameof(InvitationsController.Create));

    protected override async Task<(InvitationArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        InvitationInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Normalize(input.Email);
        PeopleText.RequireTyped(email, context, "an email address");

        if (input.Step == ProtonStaffInvitationStep.Resend)
        {
            var invited = await employees.GetAsync(input.UserId ?? throw new ProtonToolInputException("Give the invited person (from find)."), cancellationToken);
            if (invited.Status != UserStatus.Invited)
            {
                throw new ProtonToolInputException($"{AccessText.Name(invited)} has already signed up or is deactivated: there is no invitation to resend.");
            }

            if (!string.Equals(invited.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProtonToolInputException("That isn't the address this person was invited at. Use the one the user typed for them.");
            }

            await CheckMayInviteAsync(context, invited.Role, invited.Domain, cancellationToken);
            return (new InvitationArgs(input.Step, invited.Id, email, null, null, null),
                new ProtonPreview($"Resend the invitation to {email}",
                    [new ProtonChange("Signup link", "sent earlier", "a new one is emailed; the old one stops working")],
                    "Sends one email now. It can't be taken back.",
                    Confirm: ProtonConfirm.Typed, Phrase: email, Area: invited.Domain, Irreversible: true, Reach: $"Sends one email to {email}."),
                new ProtonRef(ProtonRefType.Person, invited.Id, email, invited.Domain));
        }

        var domain = input.Domain.Code();
        var decision = await CheckMayInviteAsync(context, RoleCodes.Employee, domain, cancellationToken);
        if (input.UserId is not null)
        {
            throw new ProtonToolInputException("user_id is for resend; to invite someone new give their email address.");
        }

        if (input.TeamId is { } teamId)
        {
            var team = await TeamText.FindAsync(teams, teamId, cancellationToken);
            if (team.Domain != decision.DomainCode)
            {
                throw new ProtonToolInputException($"{team.Name} is a department of {PeopleText.DomainWords(team.Domain)}: pick one of {PeopleText.DomainWords(decision.DomainCode)}.");
            }
        }

        if (EmployeeRules.NeedsDepartment(RoleCodes.Employee, decision.DomainCode) && input.TeamId is null)
        {
            throw new ProtonToolInputException("A Design employee needs a department: give one of Design's (list_teams).");
        }

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ProtonToolInputException("Someone with this email is already registered or invited: resend their invitation instead.");
        }

        var position = input.Position is null ? null : AccessText.Clean(input.Position);
        var changes = new List<ProtonChange>
        {
            new("Email", null, email),
            new("Role", null, "Employee"),
            new("Business", null, PeopleText.DomainWords(decision.DomainCode)),
        };
        if (input.TeamId is not null)
        {
            changes.Add(new ProtonChange("Department", null, (await TeamText.FindAsync(teams, input.TeamId.Value, cancellationToken)).Name));
        }

        if (position is not null)
        {
            changes.Add(new ProtonChange("Position", null, position));
        }

        return (new InvitationArgs(input.Step, null, email, decision.DomainCode, input.TeamId, position),
            new ProtonPreview($"Invite {email}", changes,
                "Sends one email now with a one-time link to sign up. It can't be taken back. Proton sets no pay: it is set in the app once they join.",
                Confirm: ProtonConfirm.Typed, Phrase: email, Area: decision.DomainCode, Irreversible: true, Reach: $"Sends one email to {email}."),
            null);
    }

    protected override object? RequestOf(InvitationArgs args)
        => args.Step == ProtonStaffInvitationStep.Create
            ? new CreateInvitationRequest { Email = args.Email, Role = RoleCodes.Employee, DomainCode = args.DomainCode, TeamId = args.TeamId, Position = args.Position }
            : null;

    protected override async Task<ProtonActionDone> ExecuteAsync(InvitationArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var sent = args.Step == ProtonStaffInvitationStep.Create
            ? await invitations.CreateAsync((CreateInvitationRequest)RequestOf(args)!, cancellationToken)
            : await invitations.ResendAsync(args.UserId!.Value, cancellationToken);
        return new ProtonActionDone(
            args.Step == ProtonStaffInvitationStep.Create ? $"Sent the invitation to {sent.Email}." : $"Sent a new invitation link to {sent.Email}.",
            "invitation", sent.UserId, [new ProtonRef(ProtonRefType.Person, sent.UserId, sent.Email, sent.Domain)]);
    }

    /// <summary>Who may invite whom, and that the inviter holds everything the role hands out: as the service checks it.</summary>
    private async Task<InvitationPolicy.Decision> CheckMayInviteAsync(ProtonToolContext context, string role, string? domain, CancellationToken cancellationToken)
    {
        var decision = InvitationPolicy.Evaluate(context.Access, role, domain);
        if (!decision.Allowed)
        {
            throw new ProtonToolInputException(decision.DenialReason!);
        }

        if (InvitationPolicy.CheckDefaultsHeld(context.Access, await PeopleText.DefaultsAsync(permissions, role, decision.DomainCode, cancellationToken)) is { } notHeld)
        {
            throw new ProtonToolInputException(notHeld);
        }

        return decision;
    }
}

internal static class PeopleText
{
    public static EmployeeRules.Target TargetOf(EmployeeSummaryResponse p) => new(p.Id, p.Role, p.Domain, p.Status);

    /// <summary>What a change to a person is checked against at Confirm: if any of it differs, someone changed them since the card.</summary>
    public static string Snapshot(EmployeeSummaryResponse p)
        => string.Join('|', p.Role, p.Domain, p.Team?.Id, p.Position, p.Status, p.PhotoFileId);

    public static string Snapshot(DTO.Response.Permissions.UserPermissionsResponse p)
        => string.Join('|', p.Role, string.Join(',', p.Permissions), string.Join(',', p.Overrides.Select(o => $"{o.Code}:{o.Effect}").Order(StringComparer.Ordinal)));

    public static string RoleCode(ProtonRole role) => role switch
    {
        ProtonRole.Employee => RoleCodes.Employee,
        ProtonRole.Admin => RoleCodes.Admin,
        _ => RoleCodes.SuperAdmin,
    };

    public static string RoleWords(string role) => role switch
    {
        RoleCodes.SuperAdmin => "Super admin",
        RoleCodes.Admin => "Admin",
        _ => "Employee",
    };

    public static string DomainWords(string? domain) => domain switch
    {
        DomainCodes.Design => "Design",
        DomainCodes.Execution => "Execution",
        DomainCodes.Website => "Website",
        _ => "no business",
    };

    /// <summary>What a permission is called in the catalog; its code when the code doesn't list it.</summary>
    public static string Describe(string code) => PermissionCatalog.Definitions.FirstOrDefault(d => d.Code == code)?.Description ?? code;

    public static PermissionSensitivity SensitivityOf(string code)
        => PermissionCatalog.Definitions.FirstOrDefault(d => d.Code == code)?.Sensitivity ?? PermissionSensitivity.None;

    public static string SensitivityWords(PermissionSensitivity sensitivity) => sensitivity switch
    {
        PermissionSensitivity.Money => "money",
        PermissionSensitivity.Pay => "pay and hours",
        PermissionSensitivity.Access => "who can sign in and what people can do",
        _ => "company-wide rules",
    };

    /// <summary>The permissions a role hands out in a business, as the services work them out.</summary>
    public static async Task<IReadOnlyCollection<string>> DefaultsAsync(IPermissionService permissions, string role, string? domain, CancellationToken cancellationToken)
    {
        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        return EffectivePermissions.RoleDefaults(role, domain, grants.TryGetValue(role, out var roleGrants) ? roleGrants : []).ToList();
    }

    /// <summary>A rule's refusal, as the message the model passes on to the user.</summary>
    public static void Refuse(Business.Common.RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw new ProtonToolInputException(denial.Message);
        }
    }

    /// <summary>
    /// An address reaches outside the company: only one the user typed in this conversation, never one from the data
    /// (the same rule the invite and company forms follow).
    /// </summary>
    public static void RequireTyped(string value, ProtonToolContext context, string what)
    {
        if (!context.TypedText.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ProtonToolInputException($"Use {what} only when the user typed it in this conversation: ask them for it.");
        }
    }
}
