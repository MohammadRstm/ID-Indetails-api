using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>A Design variation order with what its PDF and email say about the client and the project.</summary>
public sealed record DesignVoDocument(DesignVoResponse Vo, string ProjectTitle, string? ProjectLocation, string ClientName);

/// <summary>
/// Variation orders of Design client projects: read with <c>financial:view</c>, made, confirmed and cancelled with
/// <c>financial:write</c>. Applying one changes the project's price through the profit floor.
/// </summary>
public interface IDesignVoService
{
    Task<IReadOnlyList<DesignVoResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The hours logged outside the agreed scope since the last applied VO, or the project's start.</summary>
    Task<OutsideScopeResponse> OutsideScopeAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a VO: applied at once, or pending until the client confirms it. Applied, a VO that lowers the price may be
    /// refused by the profit floor: it is kept pending, and whoever sees the money can force it.
    /// </summary>
    Task<DesignVoResponse> CreateAsync(long projectId, CreateDesignVoRequest request, CancellationToken cancellationToken = default);

    /// <summary>The client agreed: the pending VO joins the price.</summary>
    Task<DesignVoResponse> ConfirmAsync(long id, CancellationToken cancellationToken = default);

    Task<DesignVoResponse> CancelAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Adds the project's TVA to a VO that isn't cancelled, or takes it off (financial:write).</summary>
    Task<DesignVoResponse> SetTvaAsync(long id, SetDesignVoTvaRequest request, CancellationToken cancellationToken = default);

    /// <summary>For its PDF and email (<c>financial:view</c>).</summary>
    Task<DesignVoDocument> GetDocumentAsync(long id, CancellationToken cancellationToken = default);
}
