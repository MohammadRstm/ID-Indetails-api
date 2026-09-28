using System.Globalization;
using System.Text;

namespace ID_Indetails.api.Services.Common.Implementation;

/// <summary>Writes CSV exports: quoted where needed, and safe to open in a spreadsheet.</summary>
public static class Csv
{
    public static void Line(StringBuilder csv, params string?[] fields)
        => csv.AppendLine(string.Join(',', fields.Select(Field)));

    public static string Hours(int? minutes) => minutes is { } m ? (m / 60m).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A leading =, +, - or @ would run as a formula in a spreadsheet (names are typed by users).
        if ("=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
