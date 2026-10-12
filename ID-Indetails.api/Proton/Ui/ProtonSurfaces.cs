using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.Proton.Ui;

/// <summary>
/// What opening a surface needs: the endpoint whose permission it needs (the one its form saves through, or the one
/// its page reads), the record it is for, the business it lives in, and whether what it does emails someone.
/// </summary>
/// <param name="Endpoint">Null for what every signed-in person may open (the bug report, a chat, a settings page they can see).</param>
/// <param name="Record">The kind of record the surface is for, when it is for one: its id must be one the user can see.</param>
/// <param name="OnlyIn">The one business it exists in; null for both, or for a company surface.</param>
/// <param name="Company">It lives in the company area, not a business.</param>
/// <param name="EmailsSomeone">Submitting it emails someone outside the app: it opens only on a turn the user typed.</param>
public sealed record ProtonSurfaceSpec(
    string Label,
    ProtonEndpoint? Endpoint,
    ProtonRefType? Record = null,
    string? OnlyIn = null,
    bool Company = false,
    bool EmailsSomeone = false);

/// <summary>
/// Every form, dialog and drawer Proton can open, and what each needs. The app mirrors this list (its surfaces.ts)
/// and maps each to its own route and opener; one-click confirm dialogs are not on it, so they can't be opened.
/// </summary>
public static class ProtonSurfaces
{
    public static readonly IReadOnlyDictionary<ProtonSurface, ProtonSurfaceSpec> All = new Dictionary<ProtonSurface, ProtonSurfaceSpec>
    {
        [ProtonSurface.NewProject] = new("the new project form", ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Create))),
        [ProtonSurface.NewClient] = new("the new client form", ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Create))),
        [ProtonSurface.InvitePerson] = new("the invite form", ProtonEndpoint.OfWrite<InvitationsController>(nameof(InvitationsController.Create)), EmailsSomeone: true),
        [ProtonSurface.EditProject] = new("the project's details form", ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Update)), ProtonRefType.Project),
        [ProtonSurface.EditClient] = new("the client's details form", ProtonEndpoint.OfWrite<ClientsController>(nameof(ClientsController.Update)), ProtonRefType.Client),
        [ProtonSurface.ProjectReport] = new("the project report", ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Report)), ProtonRefType.Project, DomainCodes.Design),
        [ProtonSurface.ProjectPaymentTerms] = new("the project's payment", ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdateBilling)), ProtonRefType.Project, DomainCodes.Design),
        [ProtonSurface.AddProjectExpense] = new("the add expense form", ProtonEndpoint.OfWrite<ExpensesController>(nameof(ExpensesController.Create)), ProtonRefType.Project),
        [ProtonSurface.SitePricing] = new("the site's fee and markup", ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdatePricing)), ProtonRefType.Project, DomainCodes.Execution),
        [ProtonSurface.NewInvoice] = new("a new invoice", ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Create)), ProtonRefType.Project),
        [ProtonSurface.RecordPayment] = new("the record payment form", ProtonEndpoint.OfWrite<PaymentsController>(nameof(PaymentsController.Record)), ProtonRefType.Project),
        [ProtonSurface.NewStatement] = new("a new materials statement", ProtonEndpoint.OfWrite<StatementsController>(nameof(StatementsController.Create)), ProtonRefType.Project, DomainCodes.Execution),
        [ProtonSurface.NewMeeting] = new("the new meeting form", ProtonEndpoint.OfWrite<MeetingsController>(nameof(MeetingsController.Create))),
        [ProtonSurface.AddCompanyExpense] = new("the add company expense form", ProtonEndpoint.OfWrite<OverheadController>(nameof(OverheadController.Create)), Company: true),
        [ProtonSurface.MessagePerson] = new("a chat", null, ProtonRefType.Person),
        [ProtonSurface.MessageTeam] = new("the team chat", null, ProtonRefType.Team),
        [ProtonSurface.LogWork] = new("the log work form", ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.Create)), OnlyIn: DomainCodes.Design),
        [ProtonSurface.ReportBug] = new("the report a problem form", null, EmailsSomeone: true),
        [ProtonSurface.SettingsSection] = new("settings", null),
        [ProtonSurface.CompanyOverviewTab] = new("the company overview", ProtonEndpoint.Of<CompanyOverviewController>(nameof(CompanyOverviewController.Get)), Company: true),
    };
}
