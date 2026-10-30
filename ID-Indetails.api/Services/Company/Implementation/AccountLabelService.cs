using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>The company's account labels (see <c>Business/Company/AccountLabelRules</c>).</summary>
public sealed class AccountLabelService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IAccountLabelRepository labels,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IAccountLabelService
{
    public async Task<IReadOnlyList<AccountLabelResponse>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        if (includeArchived)
        {
            (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.CompanySettingsManage);
        }

        return (await labels.ListAsync(includeArchived, cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<AccountLabelResponse> AddAsync(AccountLabelRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, null, cancellationToken);

        var label = new AccountLabel
        {
            Name = name,
            Position = AccountLabelRules.NextPosition((await labels.ListAsync(true, cancellationToken)).Select(l => l.Position)),
            CreatedAt = clock.UtcNow,
        };
        labels.Add(label);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(Event(label, "added"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(label);
    }

    public async Task<AccountLabelResponse> RenameAsync(long id, AccountLabelRequest request, CancellationToken cancellationToken = default)
    {
        var label = await FindAsync(id, cancellationToken);
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, id, cancellationToken);

        label.Name = name;
        activity.Add(Event(label, "renamed"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(label);
    }

    public async Task<AccountLabelResponse> ArchiveAsync(long id, CancellationToken cancellationToken = default)
    {
        var label = await FindAsync(id, cancellationToken);
        if (label.ArchivedAt is null)
        {
            label.ArchivedAt = clock.UtcNow;
            activity.Add(Event(label, "archived"));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(label);
    }

    public async Task<AccountLabelResponse> RestoreAsync(long id, CancellationToken cancellationToken = default)
    {
        var label = await FindAsync(id, cancellationToken);
        if (label.ArchivedAt is not null)
        {
            label.ArchivedAt = null;
            label.Position = AccountLabelRules.NextPosition((await labels.ListAsync(true, cancellationToken)).Select(l => l.Position));
            activity.Add(Event(label, "restored"));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(label);
    }

    public async Task<IReadOnlyList<AccountLabelResponse>> OrderAsync(AccountLabelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var all = await labels.ListAsync(true, cancellationToken);
        var active = all.Where(l => l.ArchivedAt is null).ToList();
        AccountLabelRules.CheckOrder(active.Select(l => l.Id), request.Ids).ThrowIfDenied();

        var byId = active.ToDictionary(l => l.Id);
        for (var index = 0; index < request.Ids.Count; index++)
        {
            byId[request.Ids[index]].Position = index + 1;
        }

        activity.Add(new ActivityEvent
        {
            Kind = ActivityEventKinds.AccountLabelChanged,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            SubjectType = "account_label",
            Payload = JsonSerializer.Serialize(new { change = "reordered" }),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await labels.ListAsync(true, cancellationToken)).Select(ToResponse).ToList();
    }

    private async Task<AccountLabel> FindAsync(long id, CancellationToken cancellationToken)
        => await labels.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException("Label not found.");

    private async Task EnsureNameFreeAsync(string name, long? exceptId, CancellationToken cancellationToken)
    {
        if (await labels.NameTakenAsync(name, exceptId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.AccountLabelTaken, "There is already a label with this name.");
        }
    }

    private ActivityEvent Event(AccountLabel label, string change) => new()
    {
        Kind = ActivityEventKinds.AccountLabelChanged,
        OccurredAt = clock.UtcNow,
        ActorId = currentUser.Id,
        SubjectType = "account_label",
        SubjectId = label.Id,
        Payload = JsonSerializer.Serialize(new { change, name = label.Name }),
    };

    private static AccountLabelResponse ToResponse(AccountLabel label)
        => new(label.Id, label.Name, label.Position, label.ArchivedAt is not null);
}
