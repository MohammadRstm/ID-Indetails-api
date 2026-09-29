using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>How long an emailed reset link stays valid.</summary>
    [Range(5, 1440)]
    public int ExpirationMinutes { get; init; } = 30;
}
