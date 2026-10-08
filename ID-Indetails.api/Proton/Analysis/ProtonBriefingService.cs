using System.Security.Claims;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Proton;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Prompts;
using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Usage;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Analysis;

/// <summary>
/// What Proton mentions when the panel opens: the few things most worth a look, from the same rules and the same
/// read-only, permission-checked path as get_attention, without a model call, so it costs nothing and uses no question.
/// </summary>
public sealed class ProtonBriefingService(
    ProtonToolbox toolbox,
    ProtonAnalysisService analysis,
    ProtonUsageService usage,
    IOptions<ProtonOptions> options)
{
    private const int Items = 3;

    public async Task<ProtonBriefingResponse> BriefAsync(ClaimsPrincipal caller, long userId, CancellationToken cancellationToken)
    {
        if (!usage.IsSwitchedOn)
        {
            throw new ServiceUnavailableException(ErrorCodes.ProtonUnavailable, "Proton isn't switched on.");
        }

        var tools = await toolbox.OpenAsync(caller, userId, new ProtonOrigin(Guid.Empty, options.Value.Model, ProtonPrompts.ChatVersion), cancellationToken);
        // exactly when the user could ask for it: the briefing is get_attention's answer, read the same way
        if (!tools.Tools.Any(t => t is GetAttentionTool))
        {
            return new ProtonBriefingResponse([], 0);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ToolTimeoutSeconds));
        var scan = await ProtonReadOnly.RunAsync(() => analysis.SignalsAsync(tools.Context, null, timeout.Token));
        var (groups, more) = ProtonSignals.Rank(scan.Signals, Items);
        return new ProtonBriefingResponse(
            groups.Select(g => new ProtonBriefingItem(
                    g.ProjectId is { } id ? new ProtonRef(ProtonRefType.Project, id, g.ProjectName ?? "", g.Domain) : null,
                    g.Domain,
                    g.Signals.Select(s => s.Fact).ToList()))
                .ToList(),
            more);
    }
}
