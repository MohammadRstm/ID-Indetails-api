using System.ComponentModel;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Teams;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Teams.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record CreateTeamArgs(CreateTeamRequest Request);

public sealed record CreateTeamInput : ProtonActionInput
{
    public required string Name { get; init; }

    [Description("design or execution. Needed only when the user sees both: ask them if they didn't say.")]
    public string? Domain { get; init; }
}

/// <summary>Adds a team (department), through POST /api/teams once confirmed.</summary>
public sealed class ProposeCreateTeamTool(ITeamService teams) : ProtonActionTool<CreateTeamInput, CreateTeamArgs>
{
    public override string Name => "propose_create_team";

    public override string Description =>
        "Propose adding a team (department) to Design or Execution. People are put in it from their profile, not here. " +
        "Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<TeamsController>(nameof(TeamsController.Create));

    protected override async Task<(CreateTeamArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateTeamInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = ClientText.DomainOf(input.Domain, context);
        var request = new CreateTeamRequest { Name = input.Name.Trim(), DomainCode = domain };
        var taken = (await teams.ListAsync(cancellationToken)).Any(t => t.Domain == domain && t.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            throw new ProtonToolInputException($"There is already a team called {request.Name} there.");
        }

        return (new CreateTeamArgs(request), new ProtonPreview($"Add the team {request.Name}", [new ProtonChange("Team", null, request.Name)], Area: domain), null);
    }

    protected override object RequestOf(CreateTeamArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CreateTeamArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var team = await teams.CreateAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Added the team {team.Name}.", "team", team.Id, []);
    }
}

public sealed record RenameTeamArgs(long TeamId, RenameTeamRequest Request, string Before);

public sealed record RenameTeamInput : ProtonActionInput
{
    [Description("From list_teams.")]
    public required long TeamId { get; init; }

    public required string Name { get; init; }
}

/// <summary>Renames a team, through PUT /api/teams/{id} once confirmed.</summary>
public sealed class ProposeRenameTeamTool(ITeamService teams) : ProtonActionTool<RenameTeamInput, RenameTeamArgs>
{
    public override string Name => "propose_rename_team";

    public override string Description => "Propose renaming a team (use list_teams for its id). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<TeamsController>(nameof(TeamsController.Rename));

    protected override async Task<(RenameTeamArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RenameTeamInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var team = await TeamText.FindAsync(teams, input.TeamId, cancellationToken);
        var name = input.Name.Trim();
        if (name == team.Name)
        {
            throw new ProtonToolInputException($"The team is already called {name}.");
        }

        return (new RenameTeamArgs(team.Id, new RenameTeamRequest { Name = name }, team.Name),
            new ProtonPreview($"Rename the team {team.Name}", [new ProtonChange("Name", team.Name, name)],
                team.MemberCount > 0 ? $"Its {team.MemberCount} member(s) see the new name everywhere." : null),
            TeamText.Ref(team));
    }

    protected override object RequestOf(RenameTeamArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(RenameTeamArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await TeamText.FindAsync(teams, args.TeamId, cancellationToken);
        if (current.Name != args.Before)
        {
            throw new BusinessRuleException(Constants.ErrorCodes.ProtonActionStale, "The team was renamed since Proton proposed this. Ask again.");
        }

        var team = await teams.RenameAsync(args.TeamId, args.Request, cancellationToken);
        return new ProtonActionDone($"Renamed the team to {team.Name}.", "team", team.Id, []);
    }
}

internal static class TeamText
{
    public static ProtonRef Ref(TeamResponse t) => new(ProtonRefType.Team, t.Id, t.Name, t.Domain);

    /// <summary>A team of the user's visible domains; any other reads as not found.</summary>
    public static async Task<TeamResponse> FindAsync(ITeamService teams, long teamId, CancellationToken cancellationToken)
        => (await teams.ListAsync(cancellationToken)).FirstOrDefault(t => t.Id == teamId) ?? throw new NotFoundException("Team not found.");
}

public sealed record DeleteTeamArgs(long TeamId);

public sealed record DeleteTeamInput : ProtonActionInput
{
    [Description("From list_teams.")]
    public required long TeamId { get; init; }
}

/// <summary>
/// Deletes an empty team, through DELETE /api/teams/{id} once confirmed. It can't be undone, so the user types the
/// team's name to confirm.
/// </summary>
public sealed class ProposeDeleteTeamTool(ITeamService teams) : ProtonActionTool<DeleteTeamInput, DeleteTeamArgs>
{
    public override string Name => "propose_delete_team";

    public override string Description =>
        "Propose deleting a team nobody is in (move its people out first, from their profiles). It can't be undone; the " +
        "user confirms by typing the team's name.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<TeamsController>(nameof(TeamsController.Delete));

    protected override async Task<(DeleteTeamArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DeleteTeamInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var team = await TeamText.FindAsync(teams, input.TeamId, cancellationToken);
        if (team.MemberCount > 0)
        {
            throw new ProtonToolInputException($"{team.Name} still has {team.MemberCount} member(s): they move to another team from their profiles first.");
        }

        return (new DeleteTeamArgs(team.Id),
            new ProtonPreview($"Delete the team {team.Name}", [new ProtonChange("Team", team.Name, "deleted")],
                "A team whose chat has messages is kept: the app refuses to delete it.", Confirm: ProtonConfirm.Typed, Phrase: team.Name, Irreversible: true),
            TeamText.Ref(team));
    }

    protected override object? RequestOf(DeleteTeamArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(DeleteTeamArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await teams.DeleteAsync(args.TeamId, cancellationToken);
        return new ProtonActionDone("Deleted the team.", "team", args.TeamId, []);
    }
}
