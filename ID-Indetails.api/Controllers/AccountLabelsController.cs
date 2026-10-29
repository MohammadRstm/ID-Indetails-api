using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The company-wide labels that payments and expenses are filed under. Super admins edit the list in Settings.</summary>
[ApiController]
[Route("api/account-labels")]
public sealed class AccountLabelsController(IAccountLabelService labels) : ControllerBase
{
    /// <summary>The labels that can be picked, first to last. Archived ones too with <c>includeArchived</c>, for super admins.</summary>
    [HttpGet]
    [Authorize]
    public Task<IReadOnlyList<AccountLabelResponse>> List([FromQuery] bool includeArchived, CancellationToken cancellationToken)
        => labels.ListAsync(includeArchived, cancellationToken);

    /// <summary>A new label, last in the list (<c>409 account_label_taken</c> when the name is used, whatever its case).</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public async Task<ActionResult<AccountLabelResponse>> Create(AccountLabelRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await labels.AddAsync(request, cancellationToken));

    [HttpPut("{id:long}")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<AccountLabelResponse> Rename(long id, AccountLabelRequest request, CancellationToken cancellationToken)
        => labels.RenameAsync(id, request, cancellationToken);

    /// <summary>Stops the label being picked; what already carries it keeps it.</summary>
    [HttpPost("{id:long}/archive")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<AccountLabelResponse> Archive(long id, CancellationToken cancellationToken)
        => labels.ArchiveAsync(id, cancellationToken);

    /// <summary>Puts an archived label back, last in the list.</summary>
    [HttpPost("{id:long}/restore")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<AccountLabelResponse> Restore(long id, CancellationToken cancellationToken)
        => labels.RestoreAsync(id, cancellationToken);

    /// <summary>The new order of the labels that can be picked: every one, once (<c>422 account_label_order_stale</c> otherwise).</summary>
    [HttpPut("order")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<IReadOnlyList<AccountLabelResponse>> Order(AccountLabelOrderRequest request, CancellationToken cancellationToken)
        => labels.OrderAsync(request, cancellationToken);
}
