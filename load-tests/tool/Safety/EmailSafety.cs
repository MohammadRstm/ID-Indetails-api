using System.Text.RegularExpressions;

namespace ID_Indetails.LoadTests.Tool.Safety;

/// <summary>
/// Every email address in the load-test database ends in <c>@loadtest.invalid</c>. The <c>.invalid</c> top-level
/// domain is reserved never to exist (RFC 2606), so mail to these people can't be delivered even if it ever got past
/// the load-test mail catcher.
/// </summary>
public static partial class EmailSafety
{
    public const string Domain = "loadtest.invalid";

    /// <summary>
    /// An "@" that isn't followed by the reserved domain and the end of the address. Written so it means the same to
    /// .NET and to MySQL's (ICU) REGEXP, which checks the database with it.
    /// </summary>
    public const string DeliverablePattern = @"@(?!loadtest\.invalid(?:$|[\s,;>]))";

    /// <summary>The columns the API sends mail to, or takes recipients from.</summary>
    public static readonly IReadOnlyList<(string Table, string Column)> AddressColumns =
    [
        ("users", "email"),
        ("client_contacts", "value"),
        ("client_contacts", "normalized_value"),
        ("invoices", "send_to"),
        ("invoice_deliveries", "recipient"),
        ("boq_deliveries", "recipient"),
    ];

    /// <summary>Whether the text holds an address that a real mail server could deliver to.</summary>
    public static bool HasDeliverableAddress(string? text) => text is not null && Deliverable().IsMatch(text);

    [GeneratedRegex(DeliverablePattern, RegexOptions.IgnoreCase)]
    private static partial Regex Deliverable();
}
