using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    /// <summary>How long an emailed signup link stays valid.</summary>
    [Range(1, 60)]
    public int ExpirationDays { get; init; } = 7;
}
