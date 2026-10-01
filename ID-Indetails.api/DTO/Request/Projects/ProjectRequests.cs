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

    /// <summary>Design client work only. Execution revenue is calculated from the fee and materials.</summary>
    public decimal? Revenue { get; init; }

    /// <summary>Execution only, and required there.</summary>
    public decimal? ProjectFee { get; init; }

    /// <summary>Execution only, and required there. A fraction: 0.10 is 10%, at most 5 (500%).</summary>
    public decimal? MaterialMarkup { get; init; }

    /// <summary>Execution only: the client's initial payment, recorded on account. Needs <c>payment:record</c>.</summary>
    public InitialPaymentRequest? InitialPayment { get; init; }

    /// <summary>Execution only: the crew size and day rate new labour weeks start from. Needs <c>labour_report:manage</c>.</summary>
    public SetLabourDefaultsRequest? LabourDefaults { get; init; }
}

public sealed record InitialPaymentRequest
{
    public decimal Amount { get; init; }

    /// <summary>USD or LBP.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Units of the currency per US dollar. Defaults to the dated rate in force on the day received.</summary>
    public decimal? FxRate { get; init; }

    /// <summary>Today or earlier. Defaults to today.</summary>
    public DateOnly? ReceivedOn { get; init; }

    public PaymentMethod Method { get; init; } = PaymentMethod.Bank;

    public string? Reference { get; init; }

    public string? Note { get; init; }
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

    public long OwnerId { get; init; }
}

/// <summary>Design only: client work or internal, the client and the revenue.</summary>
public sealed record UpdateProjectBillingRequest
{
    public BillingType BillingType { get; init; }

    public long? ClientId { get; init; }

    public decimal? Revenue { get; init; }
}

/// <summary>Execution only.</summary>
public sealed record UpdateProjectPricingRequest
{
    public decimal ProjectFee { get; init; }

    public decimal MaterialMarkup { get; init; }
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
