using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

public interface IProjectService
{
    Task<PagedResponse<ProjectSummaryResponse>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken = default);

    /// <summary>The money of every project <paramref name="query"/> matches, whatever the page.</summary>
    Task<ProjectListTotalsResponse> TotalsAsync(ProjectListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Money is included only for callers with <c>financial:view</c>.</summary>
    Task<ProjectResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectResponse> UpdateAsync(long projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Design only, while revenue isn't locked: client work or internal, and the client.</summary>
    Task<ProjectResponse> UpdateBillingAsync(long projectId, UpdateProjectBillingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Design client projects only, while revenue isn't locked: the customer payment and why it changes.</summary>
    Task<ProjectResponse> UpdatePaymentAsync(long projectId, UpdateProjectPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>The project's payment changes, newest first; needs financial:view.</summary>
    Task<IReadOnlyList<ProjectPaymentChangeResponse>> PaymentChangesAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Execution only, while revenue isn't locked.</summary>
    Task<ProjectResponse> UpdatePricingAsync(long projectId, UpdateProjectPricingRequest request, CancellationToken cancellationToken = default);
}
