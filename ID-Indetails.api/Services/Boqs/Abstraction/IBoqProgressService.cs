using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;

namespace ID_Indetails.api.Services.Boqs.Abstraction;

/// <summary>
/// An Execution site's progress by value: executed quantities recorded on the lines of its agreed client BOQ and VOs, by
/// the site's own staff or by whoever manages its pricing, and the value executed against the agreed value.
/// </summary>
public interface IBoqProgressService
{
    /// <summary>The site's progress as it stood on <see cref="BoqProgressQuery.AsOf"/> (today when left out).</summary>
    Task<BoqProgressResponse> GetAsync(long projectId, BoqProgressQuery query, CancellationToken cancellationToken = default);

    /// <summary>Records executed quantities on a day; a line already recorded that day is replaced. Returns the progress as of today.</summary>
    Task<BoqProgressResponse> RecordAsync(long projectId, RecordBoqProgressRequest request, CancellationToken cancellationToken = default);
}
