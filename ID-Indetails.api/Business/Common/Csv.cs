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

    private static string Field(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + value.Replace("\"", "\"\"") + '"' : value;
}
