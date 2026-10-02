namespace ID_Indetails.api.Models;

/// <summary>Invoice settings for one domain (HL for Design, EX for Execution).</summary>
public class BillingSettings
{
    public string DomainCode { get; set; } = string.Empty;

    public string InvoicePrefix { get; set; } = string.Empty;

    public int DefaultTermsDays { get; set; } = 30;

    public string? BankAccount { get; set; }

    public string? BankName { get; set; }

    public string? Iban { get; set; }

    public string? Swift { get; set; }

    public string? TermsText { get; set; }

    public string? Footer { get; set; }

    /// <summary>Whether the hourly run emails clients reminders of their overdue invoices.</summary>
    public bool RemindersEnabled { get; set; }

    /// <summary>Days after an invoice's due date its first reminder goes out (1: the day after).</summary>
    public int ReminderFirstAfterDays { get; set; } = 1;

    /// <summary>Days from one reminder to the next.</summary>
    public int ReminderEveryDays { get; set; } = 7;

    /// <summary>How many reminders an invoice gets at most.</summary>
    public int ReminderMax { get; set; } = 3;

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }

    public Domain Domain { get; set; } = null!;
}
