using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Closed months reopened for logging. Everyone signed in reads which are open (the log window lets them pick those
/// days); reopening and ending one is the super admins' (<c>company_settings:manage</c>). Months are <c>yyyy-MM</c>.
/// </summary>
[ApiController]
[Route("api/reopened-months")]
[Authorize]
public sealed class ReopenedMonthsController(IReopenedMonthService months) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReopenedMonthResponse>> Open(CancellationToken cancellationToken)
        => months.OpenAsync(cancellationToken);

    [HttpPut("{month}")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<ReopenedMonthResponse> Reopen(string month, ReopenMonthRequest request, CancellationToken cancellationToken)
        => months.ReopenAsync(month, request, cancellationToken);

    [HttpDelete("{month}")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public async Task<IActionResult> End(string month, CancellationToken cancellationToken)
    {
        await months.EndAsync(month, cancellationToken);
        return NoContent();
    }
}
