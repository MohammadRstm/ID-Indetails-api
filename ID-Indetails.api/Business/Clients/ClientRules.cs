using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Clients;

/// <summary>
/// Client rules that don't depend on HTTP or the database:
/// <list type="bullet">
/// <item>A contact is compared by its normalised value: emails ignore case and spaces, phones keep only digits.
/// A contact may belong to only one client of the domain (archived clients included).</item>
/// <item>Exactly one contact is primary: the one marked, or else the first.</item>
/// <item>A client can only be deleted while no project uses it; otherwise it is archived.</item>
/// </list>
/// </summary>
public static class ClientRules
{
    public sealed record ContactInput(ContactKind Kind, string Value, string? Label, bool IsWhatsapp, bool IsPrimary);

    public sealed record PreparedContact(ContactKind Kind, string Value, string NormalizedValue, string? Label, bool IsWhatsapp, bool IsPrimary);

    public static string Normalize(ContactKind kind, string value)
        => kind == ContactKind.Phone
            ? new string(value.Where(char.IsAsciiDigit).ToArray())
            : value.Trim().ToLowerInvariant();

    /// <summary>Trimmed, normalised contacts with one primary, or a denial when the list repeats a contact.</summary>
    public static (IReadOnlyList<PreparedContact> Contacts, RuleDenial? Denial) PrepareContacts(IReadOnlyList<ContactInput> input)
    {
        var primaryIndex = Math.Max(0, input.ToList().FindIndex(c => c.IsPrimary));
        var prepared = input
            .Select((c, i) => new PreparedContact(
                c.Kind,
                c.Value.Trim(),
                Normalize(c.Kind, c.Value),
                string.IsNullOrWhiteSpace(c.Label) ? null : c.Label.Trim(),
                c.Kind == ContactKind.Phone && c.IsWhatsapp,
                i == primaryIndex))
            .ToList();

        var repeated = prepared.GroupBy(c => (c.Kind, c.NormalizedValue)).Any(g => g.Count() > 1);
        return repeated
            ? ([], RuleDenial.Invalid(ErrorCodes.ValidationFailed, "contacts", "The same contact is listed twice."))
            : (prepared, null);
    }

    public static string ContactTakenMessage(ContactKind kind, string ownerName)
        => $"That {(kind == ContactKind.Email ? "email" : "phone number")} already belongs to {ownerName}.";

    public static RuleDenial? CheckDelete(int projectCount)
        => projectCount > 0
            ? RuleDenial.BusinessRule(ErrorCodes.ClientHasProjects, "A client with projects can't be deleted. Archive it instead.")
            : null;

    public static RuleDenial? CheckArchive(bool isArchived, bool archive) => (isArchived, archive) switch
    {
        (true, true) => RuleDenial.BusinessRule(ErrorCodes.ClientAlreadyArchived, "This client is already archived."),
        (false, false) => RuleDenial.BusinessRule(ErrorCodes.ClientNotArchived, "This client is not archived."),
        _ => null,
    };

    public static string DisplayName(string name, string? organisation)
        => string.IsNullOrWhiteSpace(organisation) ? name : organisation;
}
