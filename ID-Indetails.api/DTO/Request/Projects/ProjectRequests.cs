using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>Filters for the project list. Everything is optional.</summary>
public sealed record ProjectListQuery
{
    /// <summary>Matches the title; every word must match.</summary>
    public string? Search { get; init; }

    public ProjectStatus? Status { get; init; }

    public long? ClientId { get; init; }

    public BillingType? BillingType { get; init; }

    /// <summary>Must be a domain the caller can see, or nothing is returned.</summary>
    public string? Domain { get; init; }

    public long? OwnerId { get; init; }

    /// <summary>Projects open at some point during this year, or during <see cref="Quarter"/> of it.</summary>
    public int? Year { get; init; }

    /// <summary>1 to 4; needs <see cref="Year"/>.</summary>
    public int? Quarter { get; init; }

    public ProjectSort Sort { get; init; } = ProjectSort.Start;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record CreateProjectRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Where the work is: a site's address, a building, a town.</summary>
    public string? Location { get; init; }

    public string? Icon { get; init; }

    public string? Tint { get; init; }

    /// <summary>May be empty when the caller works in only one domain.</summary>
    public string? DomainCode { get; init; }

    /// <summary>Execution projects are always client work.</summary>
    public BillingType BillingType { get; init; } = BillingType.Client;

    /// <summary>Required for client work, empty for internal projects.</summary>
    public long? ClientId { get; init; }

    /// <summary>Defaults to the caller.</summary>
    public long? OwnerId { get; init; }

    public DateOnly? TargetDate { get; init; }

    /// <summary>Design client work only, and needs <c>financial:view</c>. Execution revenue is calculated from the agreed work price and supervision.</summary>
    public decimal? Revenue { get; init; }

    /// <summary>Always refused: an Execution site's work price comes from its agreed BOQs, never typed in. It starts at 0.</summary>
    public decimal? ProjectFee { get; init; }

    /// <summary>
    /// Execution only, and needs <c>project:pricing_manage</c>: the company's supervision on the work price, a fraction (0.10 is
    /// 10%), 0 to 1. Left out, a site starts at 10%.
    /// </summary>
    public decimal? SupervisionRate { get; init; }

    /// <summary>
    /// Client work only, and needs <c>financial:view</c>: TVA on what the client pays for the work, 0.11 is 11%, 0 to 1.
    /// Left out, a client project starts at 11%.
    /// </summary>
    public decimal? TvaRate { get; init; }

    /// <summary>Execution only: the crew size and day rate new labour weeks start from. Needs <c>labour_report:manage</c>.</summary>
    public SetLabourDefaultsRequest? LabourDefaults { get; init; }

    /// <summary>Execution only, required: the employees who oversee the site (at least one).</summary>
    public IReadOnlyList<long>? StaffIds { get; init; }
}

/// <summary>Completing a project.</summary>
public sealed record CompleteProjectRequest
{
    /// <summary>Whether what a client paid beyond the price becomes part of it before the project closes.</summary>
    public bool KeepPaidAhead { get; init; }
}

/// <summary>Replaces the project's details and owner.</summary>
public sealed record UpdateProjectRequest
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Where the work is: a site's address, a building, a town.</summary>
    public string? Location { get; init; }

    public string? Icon { get; init; }

    public string? Tint { get; init; }

    public DateOnly? TargetDate { get; init; }

    /// <summary>Left out, the start date stays as it is.</summary>
    public DateOnly? StartDate { get; init; }

    public long OwnerId { get; init; }
}

/// <summary>Design only: client work or internal, and the client. Becoming internal drops the payment to 0.</summary>
public sealed record UpdateProjectBillingRequest
{
    public BillingType BillingType { get; init; }

    public long? ClientId { get; init; }
}

/// <summary>Design only: a client project's customer payment, and why it changes.</summary>
public sealed record UpdateProjectPaymentRequest
{
    public decimal Revenue { get; init; }

    /// <summary>The TVA rate after the change (0 to 1); left out, it stays as it is.</summary>
    public decimal? TvaRate { get; init; }

    public string Description { get; init; } = string.Empty;
}

/// <summary>Execution only: the supervision rate and TVA, and why they change.</summary>
public sealed record UpdateProjectPricingRequest
{
    /// <summary>Always refused: the work price is set by agreeing a BOQ, never typed in.</summary>
    public decimal? ProjectFee { get; init; }

    /// <summary>The supervision rate after the change, a fraction (0 to 1); left out, it stays as it is.</summary>
    public decimal? SupervisionRate { get; init; }

    /// <summary>The TVA rate after the change (0 to 1); left out, it stays as it is.</summary>
    public decimal? TvaRate { get; init; }

    public string Description { get; init; } = string.Empty;
}

/// <summary>What the project report PDF covers.</summary>
public sealed record ProjectReportQuery
{
    public ProjectReportKind Kind { get; init; } = ProjectReportKind.Internal;

    /// <summary>The work it counts, by company day; money is always the whole project. Both default to the project's span.</summary>
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>Repeat the key for each (<c>sections=summary&amp;sections=billing</c>). Empty means every one but the work log.</summary>
    public IReadOnlyList<ProjectReportSection> Sections { get; init; } = [];
}

/// <summary>Execution only: the employees who oversee the site, all of them (at least one).</summary>
public sealed record UpdateProjectStaffRequest
{
    public IReadOnlyList<long> StaffIds { get; init; } = [];
}
