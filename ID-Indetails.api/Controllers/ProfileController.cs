using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Permissions.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>What signed-in people change about themselves.</summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class ProfileController(IEmployeeService employeeService, IPermissionManagementService permissionService) : ControllerBase
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

    /// <summary>Whether Proton suggests replies to them in the discussions with clients: their own choice.</summary>
    [HttpPut("proton-suggestions")]
    public Task<CurrentUserResponse> SetProtonSuggestions(MyProtonSuggestionsRequest request, CancellationToken cancellationToken)
        => employeeService.SetMyProtonSuggestionsAsync(request, cancellationToken);

    /// <summary>
    /// Every permission they hold, and every one their role gives that was taken away from them, with what each allows
    /// and where it comes from.
    /// </summary>
    [HttpGet("permissions")]
    public Task<IReadOnlyList<MyPermissionResponse>> Permissions(CancellationToken cancellationToken)
        => permissionService.GetMineAsync(cancellationToken);
}
