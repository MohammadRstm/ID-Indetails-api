using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Services.Payroll.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Someone's own salary advances: asking for one, and taking a request back while it waits.</summary>
[ApiController]
[Route("api/me/salary-advances")]
[Authorize]
public sealed class MySalaryAdvancesController(ISalaryAdvanceService advances) : ControllerBase
{
    [HttpGet]
    public Task<MySalaryAdvancesResponse> Mine(CancellationToken cancellationToken) => advances.MineAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<MySalaryAdvanceResponse>> Ask(AskSalaryAdvanceRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await advances.AskAsync(request, cancellationToken));

    [HttpPost("{id:long}/withdraw")]
    public Task<MySalaryAdvanceResponse> Withdraw(long id, CancellationToken cancellationToken) => advances.WithdrawAsync(id, cancellationToken);
}
