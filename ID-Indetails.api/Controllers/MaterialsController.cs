using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Response.Materials;
using ID_Indetails.api.Services.Materials.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Materials of an Execution site. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/materials")]
public sealed class MaterialsController(IMaterialService materialService) : ControllerBase
{
    /// <summary>Client prices and markup are included only for callers who see money.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<MaterialListResponse> List(long projectId, [FromQuery] MaterialListQuery query, CancellationToken cancellationToken)
        => materialService.ListAsync(projectId, query, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.MaterialCreate)]
    public async Task<ActionResult<IReadOnlyList<MaterialResponse>>> Add(long projectId, AddMaterialsRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await materialService.AddAsync(projectId, request, cancellationToken));

    /// <summary>Your own materials with <c>material:create</c>, anyone's with <c>material:manage</c>.</summary>
    [HttpPut("{materialId:long}")]
    [HasAnyPermission(PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage)]
    public Task<MaterialResponse> Update(long projectId, long materialId, MaterialEntryRequest request, CancellationToken cancellationToken)
        => materialService.UpdateAsync(projectId, materialId, request, cancellationToken);

    [HttpDelete("{materialId:long}")]
    [HasAnyPermission(PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage)]
    public async Task<IActionResult> Delete(long projectId, long materialId, CancellationToken cancellationToken)
    {
        await materialService.DeleteAsync(projectId, materialId, cancellationToken);
        return NoContent();
    }

    /// <summary>Sets a material's own client price, or returns it to the project's markup.</summary>
    [HttpPut("{materialId:long}/client-price")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    public Task<MaterialResponse> SetPrice(long projectId, long materialId, SetMaterialPriceRequest request, CancellationToken cancellationToken)
        => materialService.SetPriceAsync(projectId, materialId, request, cancellationToken);
}
