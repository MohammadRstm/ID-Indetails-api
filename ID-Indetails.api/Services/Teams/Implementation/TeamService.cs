using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Teams;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Teams.Abstraction;

namespace ID_Indetails.api.Services.Teams.Implementation;

/// <summary>
/// Teams belong to one domain, have a name unique within it, and can only be deleted while
/// empty. Teams of a domain the caller can't see read as not found.
/// </summary>
public sealed class TeamService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    ITeamRepository teams,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : ITeamService
{
    private const string NotFoundMessage = "Team not found.";

    public async Task<IReadOnlyList<TeamResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var rows = await teams.ListAsync(actor.VisibleDomains, cancellationToken);
        return rows.Select(r => new TeamResponse(r.Team.Id, r.Team.Name, r.Team.DomainCode, r.MemberCount)).ToList();
    }

    public async Task<TeamResponse> CreateAsync(CreateTeamRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var domain = request.DomainCode ?? (actor.VisibleBusinesses.Count == 1 ? actor.VisibleBusinesses[0] : null);
        if (domain is null)
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "domainCode", "Choose the domain the team belongs to.");
        }

        if (!actor.CanSee(domain))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You can only create teams in a domain you work in.");
        }

        var name = request.Name.Trim();
        await EnsureNameFreeAsync(domain, name, null, cancellationToken);

        var team = new Team { DomainCode = domain, Name = name, CreatedAt = clock.UtcNow };
        teams.Add(team);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(TeamEvent(team, ActivityEventKinds.TeamCreated, actor.UserId, new { name }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TeamResponse(team.Id, team.Name, team.DomainCode, 0);
    }

    public async Task<TeamResponse> RenameAsync(long teamId, RenameTeamRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var team = await VisibleTeamAsync(actor, teamId, cancellationToken);

        var name = request.Name.Trim();
        await EnsureNameFreeAsync(team.DomainCode, name, team.Id, cancellationToken);

        var previous = team.Name;
        team.Name = name;
        activity.Add(TeamEvent(team, ActivityEventKinds.TeamRenamed, actor.UserId, new { from = previous, to = name }));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var members = (await teams.ListAsync([team.DomainCode], cancellationToken))
            .Single(r => r.Team.Id == team.Id).MemberCount;
        return new TeamResponse(team.Id, team.Name, team.DomainCode, members);
    }

    public async Task DeleteAsync(long teamId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var team = await VisibleTeamAsync(actor, teamId, cancellationToken);

        if (await teams.HasPeopleAsync(team.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                ErrorCodes.TeamNotEmpty, "Only an empty team can be deleted. Move its people to another team first.");
        }

        // Its chat history is kept, and the chat can't outlive the team.
        if (await teams.HasChatAsync(team.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                ErrorCodes.TeamHasChat, "This team's chat has messages, so the team can't be deleted. Rename it instead.");
        }

        activity.Add(TeamEvent(team, ActivityEventKinds.TeamDeleted, actor.UserId, new { name = team.Name }));
        teams.Remove(team);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<Team> VisibleTeamAsync(UserAccess actor, long teamId, CancellationToken cancellationToken)
    {
        var team = await teams.GetForUpdateAsync(teamId, cancellationToken);
        return team is not null && actor.CanSee(team.DomainCode) ? team : throw new NotFoundException(NotFoundMessage);
    }

    private async Task EnsureNameFreeAsync(string domain, string name, long? exceptTeamId, CancellationToken cancellationToken)
    {
        if (await teams.NameExistsAsync(domain, name, exceptTeamId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.TeamNameTaken, "This domain already has a team with that name.");
        }
    }

    private ActivityEvent TeamEvent(Team team, string kind, long actorId, object payload) => new()
    {
        DomainCode = team.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        SubjectType = "team",
        SubjectId = team.Id,
        Payload = JsonSerializer.Serialize(payload),
    };
}
