using System.Text.Json;

namespace ID_Indetails.LoadTests.Tool.Verification;

/// <summary>
/// The lines the k6 scripts print to their console output (k6 run --console-output): LEDGER for every change they
/// made, ERROR for every unexpected answer, AFTERWARDS for the race checks, UNLISTED for unlisted endpoints.
/// </summary>
public sealed class RunLog
{
    public List<JsonElement> Ledger { get; } = [];

    public List<JsonElement> Errors { get; } = [];

    public List<JsonElement> Afterwards { get; } = [];

    public List<string> Unlisted { get; } = [];

    public static RunLog Read(string path)
    {
        var log = new RunLog();
        foreach (var raw in File.ReadLines(path))
        {
            var line = Unwrap(raw);
            if (Json(line, "LEDGER ") is { } ledger)
            {
                log.Ledger.Add(ledger);
            }
            else if (Json(line, "ERROR ") is { } error)
            {
                log.Errors.Add(error);
            }
            else if (Json(line, "AFTERWARDS ") is { } afterwards)
            {
                log.Afterwards.Add(afterwards);
            }
            else if (line.StartsWith("UNLISTED ", StringComparison.Ordinal))
            {
                log.Unlisted.Add(line["UNLISTED ".Length..]);
            }
        }

        return log;
    }

    /// <summary>The message itself, whether k6 wrote it raw or as a logfmt msg="..." field.</summary>
    private static string Unwrap(string line)
    {
        var start = line.IndexOf("msg=\"", StringComparison.Ordinal);
        if (start < 0)
        {
            return line.Trim();
        }

        var message = line[(start + 5)..];
        var end = message.LastIndexOf("\" ", StringComparison.Ordinal);
        message = end >= 0 ? message[..end] : message.TrimEnd('"');
        return message.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static JsonElement? Json(string line, string tag)
    {
        if (!line.StartsWith(tag, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(line[tag.Length..]).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
