using System.Globalization;
using System.Text;

namespace ID_Indetails.api.Business.Common;

/// <summary>CSV as spreadsheets open it: UTF-8 with a byte-order mark, CRLF line ends, fields quoted only when needed.</summary>
public static class Csv
{
    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows.Prepend(header))
        {
            text.Append(string.Join(',', row.Select(Field))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>
    /// Quoted only when needed. Text starting with =, +, -, @, a tab or a carriage return would run as a formula in a
    /// spreadsheet (descriptions and names are typed by people), so it gets a leading apostrophe; numbers stay numbers.
    /// </summary>
    private static string Field(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
