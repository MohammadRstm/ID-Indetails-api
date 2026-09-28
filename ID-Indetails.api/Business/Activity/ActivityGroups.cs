using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Activity;

/// <summary>Which feed filter each event kind belongs to. Kinds not listed are <see cref="ActivityGroup.Other"/>.</summary>
public static class ActivityGroups
{
    private static readonly IReadOnlyDictionary<string, ActivityGroup> ByKind = new Dictionary<string, ActivityGroup>
    {
        [ActivityEventKinds.WorkEntryLogged] = ActivityGroup.Work,
        [ActivityEventKinds.WorkEntryUpdated] = ActivityGroup.Work,
        [ActivityEventKinds.WorkEntryDeleted] = ActivityGroup.Work,
        [ActivityEventKinds.WorkEntryRestored] = ActivityGroup.Work,
        [ActivityEventKinds.WorkEntryCommented] = ActivityGroup.Work,

        [ActivityEventKinds.RevenueSet] = ActivityGroup.Money,
        [ActivityEventKinds.RevenueChanged] = ActivityGroup.Money,
        [ActivityEventKinds.ExpenseAdded] = ActivityGroup.Money,
        [ActivityEventKinds.ExpenseRemoved] = ActivityGroup.Money,
        [ActivityEventKinds.ProjectFeeSet] = ActivityGroup.Money,
        [ActivityEventKinds.MaterialMarkupSet] = ActivityGroup.Money,
        [ActivityEventKinds.ProjectFinalized] = ActivityGroup.Money,

        [ActivityEventKinds.BillingSettingsUpdated] = ActivityGroup.Billing,
        [ActivityEventKinds.ExchangeRateSet] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceCreated] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceUpdated] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceDiscarded] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceIssued] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceSent] = ActivityGroup.Billing,
        [ActivityEventKinds.InvoiceVoided] = ActivityGroup.Billing,
        [ActivityEventKinds.PaymentRecorded] = ActivityGroup.Billing,
        [ActivityEventKinds.PaymentApplied] = ActivityGroup.Billing,
        [ActivityEventKinds.PaymentVoided] = ActivityGroup.Billing,

        [ActivityEventKinds.ProjectCreated] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectUpdated] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectOwnerSet] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectClientSet] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectHold] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectResumed] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectCompleted] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectArchived] = ActivityGroup.Projects,
        [ActivityEventKinds.ProjectReopened] = ActivityGroup.Projects,
        [ActivityEventKinds.ProgressSet] = ActivityGroup.Projects,
        [ActivityEventKinds.RequirementAdded] = ActivityGroup.Projects,
        [ActivityEventKinds.RequirementUpdated] = ActivityGroup.Projects,
        [ActivityEventKinds.RequirementDone] = ActivityGroup.Projects,
        [ActivityEventKinds.RequirementReopened] = ActivityGroup.Projects,
        [ActivityEventKinds.RequirementDeleted] = ActivityGroup.Projects,
        [ActivityEventKinds.MeetingCreated] = ActivityGroup.Projects,
        [ActivityEventKinds.MeetingUpdated] = ActivityGroup.Projects,
        [ActivityEventKinds.MeetingDeleted] = ActivityGroup.Projects,
        [ActivityEventKinds.ClientAdded] = ActivityGroup.Projects,
        [ActivityEventKinds.ClientUpdated] = ActivityGroup.Projects,
        [ActivityEventKinds.ClientArchived] = ActivityGroup.Projects,
        [ActivityEventKinds.ClientRestored] = ActivityGroup.Projects,
        [ActivityEventKinds.ClientDeleted] = ActivityGroup.Projects,

        [ActivityEventKinds.EmployeeUpdated] = ActivityGroup.People,
        [ActivityEventKinds.EmployeeDeactivated] = ActivityGroup.People,
        [ActivityEventKinds.EmployeeReactivated] = ActivityGroup.People,
        [ActivityEventKinds.RoleChanged] = ActivityGroup.People,
        [ActivityEventKinds.EmployeeMoved] = ActivityGroup.People,
        [ActivityEventKinds.PayChanged] = ActivityGroup.People,
        [ActivityEventKinds.PermissionGranted] = ActivityGroup.People,
        [ActivityEventKinds.PermissionRevoked] = ActivityGroup.People,
        [ActivityEventKinds.PermissionReset] = ActivityGroup.People,
        [ActivityEventKinds.RoleAutoPromoted] = ActivityGroup.People,
        [ActivityEventKinds.RoleAutoDemoted] = ActivityGroup.People,
        [ActivityEventKinds.TeamCreated] = ActivityGroup.People,
        [ActivityEventKinds.TeamRenamed] = ActivityGroup.People,
        [ActivityEventKinds.TeamDeleted] = ActivityGroup.People,
    };

    public static ActivityGroup GroupOf(string kind, string? domainCode)
        => ByKind.TryGetValue(kind, out var group) ? group : domainCode is null ? ActivityGroup.Company : ActivityGroup.Other;

    public static IReadOnlyList<string> KindsOf(ActivityGroup group) => ByKind.Where(k => k.Value == group).Select(k => k.Key).ToList();
}
