namespace ID_Indetails.api.DTO.Request.Execution;

/// <summary>Some of a site's supervision fee taken out of the project account.</summary>
public sealed record TakeSupervisionRequest
{
    /// <summary>USD, more than zero, with at most 2 decimals.</summary>
    public decimal AmountUsd { get; init; }

    /// <summary>The day it was taken: today or earlier.</summary>
    public DateOnly TakenOn { get; init; }

    public string? Note { get; init; }
}
