using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;

namespace ID_Indetails.api.Services.Expenses.Implementation;

/// <summary>
/// Anyone who sees a business reads its categories; changing them takes <c>financial:write</c> (checked by the
/// endpoint) and seeing the business. A business the caller can't see reads as not found.
/// </summary>
public sealed class ExpenseCategoryService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IExpenseCategoryRepository categories,
    IUnitOfWork unitOfWork,
    IClock clock) : IExpenseCategoryService
{
    private const string NotFoundMessage = "Category not found.";

    public async Task<IReadOnlyList<ExpenseCategoryResponse>> ListAsync(string domain, CancellationToken cancellationToken = default)
    {
        var actor = await SeeingAsync(domain, cancellationToken);
        var list = await categories.ListAsync(domain, cancellationToken);
        if (!actor.Has(PermissionCodes.FinancialWrite))
        {
            return list.Select(c => ToResponse(c, null)).ToList();
        }

        var result = new List<ExpenseCategoryResponse>();
        foreach (var c in list)
        {
            result.Add(ToResponse(c, ExpenseCategoryRules.CheckRemovable(c.Code, await categories.UsesAsync(domain, c.Code, cancellationToken)) is null));
        }

        return result;
    }

    public async Task<ExpenseCategoryResponse> AddAsync(AddExpenseCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await SeeingAsync(request.Domain, cancellationToken);
        var existing = await categories.ListForUpdateAsync(request.Domain, cancellationToken);
        ExpenseCategoryRules.CheckName(request.Name, existing.Select(c => c.Name)).ThrowIfDenied();

        var category = new ExpenseCategory
        {
            DomainCode = request.Domain,
            Code = ExpenseCategoryRules.CodeFor(request.Name, existing.Select(c => c.Code).ToHashSet()),
            Name = request.Name.Trim(),
            SortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1,
            CreatedAt = clock.UtcNow,
        };
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(category, true);
    }

    public async Task<ExpenseCategoryResponse> RenameAsync(long id, RenameExpenseCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await FindAsync(id, cancellationToken);
        var others = (await categories.ListAsync(category.DomainCode, cancellationToken)).Where(c => c.Id != id).Select(c => c.Name);
        ExpenseCategoryRules.CheckName(request.Name, others).ThrowIfDenied();
        category.Name = request.Name.Trim();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(category, null);
    }

    public async Task<IReadOnlyList<ExpenseCategoryResponse>> OrderAsync(OrderExpenseCategoriesRequest request, CancellationToken cancellationToken = default)
    {
        await SeeingAsync(request.Domain, cancellationToken);
        var list = await categories.ListForUpdateAsync(request.Domain, cancellationToken);
        if (request.Ids.Count != list.Count || request.Ids.Distinct().Count() != list.Count || list.Any(c => !request.Ids.Contains(c.Id)))
        {
            RuleDenial.Invalid(ErrorCodes.ValidationFailed, "ids", "Give every one of the business's categories, once each.").ThrowIfDenied();
        }

        for (var i = 0; i < request.Ids.Count; i++)
        {
            list.Single(c => c.Id == request.Ids[i]).SortOrder = i;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListAsync(request.Domain, cancellationToken);
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        var category = await FindAsync(id, cancellationToken);
        ExpenseCategoryRules.CheckRemovable(category.Code, await categories.UsesAsync(category.DomainCode, category.Code, cancellationToken)).ThrowIfDenied();
        categories.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> CodesAsync(string domain, CancellationToken cancellationToken = default)
        => (await categories.ListAsync(domain, cancellationToken)).Select(c => c.Code).ToList();

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(string domain, CancellationToken cancellationToken = default)
        => (await categories.ListAsync(domain, cancellationToken)).ToDictionary(c => c.Code, c => c.Name);

    private async Task<UserAccess> SeeingAsync(string domain, CancellationToken cancellationToken)
    {
        ExpenseCategoryRules.CheckDomain(domain).ThrowIfDenied();
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return actor.CanSee(domain) ? actor : throw new NotFoundException("Business not found.");
    }

    private async Task<ExpenseCategory> FindAsync(long id, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return actor.CanSee(category.DomainCode) ? category : throw new NotFoundException(NotFoundMessage);
    }

    private static ExpenseCategoryResponse ToResponse(ExpenseCategory c, bool? removable)
        => new(c.Id, c.DomainCode, c.Code, c.Name, c.SortOrder, removable);
}
