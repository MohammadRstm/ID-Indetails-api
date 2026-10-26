using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>
/// The project page's own rules for its files and client portal tabs, for Proton to open them only for those the page
/// shows them to: files on a Design project the person may view; the portal on a Design client project, to those who
/// manage what its client sees or who has access, or who take part in its discussion (worked out by the discussion
/// itself, never from a permission alone).
/// </summary>
public sealed class ProtonPortalTab(IProjectService projects, IPortalDiscussionService discussion)
{
    public static readonly ProtonEndpoint Files = ProtonEndpoint.Of<ProjectFilesController>(nameof(ProjectFilesController.Get));
    public static readonly ProtonEndpoint Content = ProtonEndpoint.Of<ProjectPortalContentController>(nameof(ProjectPortalContentController.Get));
    public static readonly ProtonEndpoint Clients = ProtonEndpoint.Of<ProjectPortalController>(nameof(ProjectPortalController.Clients));

    /// <summary>The files tab, on one of its sections or all of them.</summary>
    public async Task<bool> CanOpenFilesAsync(ProtonRef project, ProtonProjectPart? part, ProtonToolContext context)
        => project.Domain == DomainCodes.Design && part is null or ProtonProjectPart.Presentation or ProtonProjectPart.Model or ProtonProjectPart.Drawing
           && await context.CanCallAsync(Files);

    /// <summary>The client portal tab, on one of its parts or its first.</summary>
    public async Task<bool> CanOpenPortalAsync(ProtonRef project, ProtonProjectPart? part, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (project.Domain != DomainCodes.Design || !await HasClientAsync(project.Id, cancellationToken))
        {
            return false;
        }

        return part switch
        {
            ProtonProjectPart.Access => await context.CanCallAsync(Clients),
            ProtonProjectPart.Discussion => await TakesPartAsync(project.Id, cancellationToken),
            ProtonProjectPart.Presentation or ProtonProjectPart.Model or ProtonProjectPart.Drawing => await context.CanCallAsync(Content),
            _ => await context.CanCallAsync(Content) || await TakesPartAsync(project.Id, cancellationToken),
        };
    }

    /// <summary>Whether the person takes part in the project's discussion with its client: the discussion's own answer.</summary>
    public async Task<bool> TakesPartAsync(long projectId, CancellationToken cancellationToken)
    {
        try
        {
            await discussion.SummaryAsync(DiscussionSide.Staff, projectId, cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }

    private async Task<bool> HasClientAsync(long projectId, CancellationToken cancellationToken)
    {
        try
        {
            var project = await projects.GetAsync(projectId, cancellationToken);
            return project.BillingType == BillingType.Client && project.Client is not null;
        }
        catch (Exception exception) when (exception is NotFoundException or ForbiddenException)
        {
            return false;
        }
    }
}
