using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// How a project's billing and money fields fit together, and who may own it:
/// <list type="bullet">
/// <item><b>Design</b> projects are client work (with a client) or internal (no client, no revenue).
/// They have no project fee or material markup.</item>
/// <item><b>Execution</b> projects are always client work, with a project fee and a material markup.
/// Their revenue is calculated from those (<see cref="ExecutionPricing"/>), never typed in.</item>
/// <item>A project's client belongs to its domain and is not archived when it is chosen.</item>
/// <item>The owner is anyone in the project's domain who isn't deactivated, or a super admin.</item>
/// <item>Once revenue is locked (the project was finalised), the billing, revenue, fee and markup
/// can't change until the project is reopened.</item>
/// </list>
/// </summary>
public static class ProjectRules
{
    /// <summary>Why a payment changed: required with every change, for the project report.</summary>
    public const int MaxPaymentDescriptionLength = 500;

    /// <summary>The description of the first change, the payment agreed when the project was created.</summary>
    public const string AgreedAtStart = "Agreed at the start";

    /// <summary>The description written when a Design project becomes internal work, which has no payment.</summary>
    public const string MadeInternal = "Turned into internal work";

    public sealed record Setup(
        string Domain,
        BillingType BillingType,
        long? ClientId,
        decimal? Revenue,
        decimal? ProjectFee,
        decimal? MaterialMarkup);

    public sealed record Person(string Role, string? Domain, UserStatus Status);

    public static RuleDenial? CheckSetup(Setup setup)
    {
        if (setup.Domain == DomainCodes.Execution)
        {
            if (setup.BillingType != BillingType.Client)
            {
                return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "billingType", "Execution projects are always client work.");
            }

            if (setup.Revenue is not null)
            {
                return RuleDenial.Invalid(
                    ErrorCodes.ValidationFailed, "revenue", "Execution revenue is calculated from the project fee and materials.");
            }
        }
        else if (setup.ProjectFee is not null || setup.MaterialMarkup is not null)
        {
            return RuleDenial.Invalid(
                ErrorCodes.ValidationFailed, "projectFee", "Only Execution projects have a project fee and a material markup.");
        }

        if (setup.BillingType == BillingType.Client)
        {
            return setup.ClientId is null
                ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "clientId", "Choose the client this project is for.")
                : null;
        }

        if (setup.ClientId is not null)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "clientId", "Internal projects have no client.");
        }

        return setup.Revenue is > 0
            ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "revenue", "Internal projects have no revenue.")
            : null;
    }

    /// <summary>Only client work has a customer payment: an internal project is made client work first.</summary>
    public static RuleDenial? CheckHasPayment(BillingType billingType)
        => billingType == BillingType.Client
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "revenue", "Internal projects have no customer payment: make it client work first.");

    /// <summary>A payment change changes an amount: a description alone isn't one.</summary>
    public static RuleDenial? CheckPaymentChanges(bool changes, string field)
        => changes ? null : RuleDenial.Invalid(ErrorCodes.ValidationFailed, field, "Nothing would change: the payment is already this.");

    public static RuleDenial? CheckClient(bool isArchived)
        => isArchived
            ? RuleDenial.BusinessRule(ErrorCodes.ClientIsArchived, "This client is archived. Restore it before starting work for it.")
            : null;

    public static RuleDenial? CheckInitialPayment(string domain, bool hasInitialPayment)
        => hasInitialPayment && domain != DomainCodes.Execution
            ? RuleDenial.Invalid(
                ErrorCodes.ValidationFailed, "initialPayment", "Only Execution projects take an initial payment when they are created.")
            : null;

    /// <summary>A project is owned by an admin of its business or a super admin, never an employee.</summary>
    /// <param name="owner">Null when no such person exists; answered the same as a disallowed owner so ids can't be probed.</param>
    public static RuleDenial? CheckOwner(Person? owner, string projectDomain)
    {
        var allowed = owner is not null
            && owner.Status != UserStatus.Deactivated
            && (owner.Role == RoleCodes.SuperAdmin || (owner.Role == RoleCodes.Admin && owner.Domain == projectDomain));

        return allowed
            ? null
            : RuleDenial.BusinessRule(
                ErrorCodes.OwnerNotAllowed, "The owner must be an active admin of the project's business, or a super admin.");
    }

    public static RuleDenial? CheckMoneyEditable(bool isLocked)
        => isLocked
            ? RuleDenial.BusinessRule(ErrorCodes.ProjectLocked, "This project is finalised. Reopen it to change its money.")
            : null;

    /// <summary>
    /// Invoices and payments keep the client they were made for, so a project that has any can't move
    /// to another client or become internal.
    /// </summary>
    public static RuleDenial? CheckClientChange(long? currentClientId, long? newClientId, bool hasBillingRecords)
        => currentClientId != newClientId && hasBillingRecords
            ? RuleDenial.BusinessRule(
                ErrorCodes.ProjectHasBilling, "This project already has invoices or payments, so its client can't change.")
            : null;
}
