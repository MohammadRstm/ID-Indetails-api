using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ID_Indetails.api.Data;

/// <summary>
/// Every DATETIME column holds UTC. MySQL does not store the kind, so values read back
/// would be <see cref="DateTimeKind.Unspecified"/>; this marks them as UTC so conversions to
/// the company time zone (Asia/Beirut) are always correct.
/// </summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        // Local times are converted; Unspecified is assumed to already be UTC.
        value => value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
