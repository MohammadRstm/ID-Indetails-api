namespace ID_Indetails.api.Enums;

/// <summary>The parts a project report can include.</summary>
public enum ProjectReportSection
{
    Summary,
    Info,
    Timeline,

    /// <summary>Internal only: each person's hours and labour cost.</summary>
    Work,

    /// <summary>Internal only.</summary>
    Expenses,

    /// <summary>Internal only: value, cost, profit and margin.</summary>
    Financials,

    /// <summary>Client projects only: invoices and payments.</summary>
    Billing,
    Client,

    /// <summary>The most recent work entries. Left out unless asked for.</summary>
    Log,
}
