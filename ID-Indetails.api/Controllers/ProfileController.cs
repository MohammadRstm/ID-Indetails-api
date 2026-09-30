using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Services.Employees.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>What signed-in people change about themselves.</summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class ProfileController(IEmployeeService employeeService) : ControllerBase
{
    /// <summary>Own name and phone. Email, position, team and pay are managed by the company.</summary>
    [HttpPut("profile")]
    public Task<CurrentUserResponse> UpdateProfile(UpdateMyProfileRequest request, CancellationToken cancellationToken)
        => employeeService.UpdateMyProfileAsync(request, cancellationToken);

    /// <summary>Own profile photo, from an upload of their own.</summary>
    [HttpPut("photo")]
    public Task<CurrentUserResponse> SetPhoto(AttachFileRequest request, CancellationToken cancellationToken)
        => employeeService.SetMyPhotoAsync(request, cancellationToken);

    [HttpDelete("photo")]
    public Task<CurrentUserResponse> RemovePhoto(CancellationToken cancellationToken)
        => employeeService.RemoveMyPhotoAsync(cancellationToken);
}
