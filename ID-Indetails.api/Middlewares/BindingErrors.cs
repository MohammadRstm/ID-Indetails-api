using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// The field errors of a request that couldn't be bound. A JSON value that couldn't be read is described by its
/// field, never by the reader's own message, which names .NET types and byte positions: a number too large for
/// its field is "Amount is too large.", anything else "Amount isn't valid.".
/// </summary>
public static class BindingErrors
{
    private static readonly string[] NumericTypes = ["System.Decimal", "System.Int16", "System.Int32", "System.Int64", "System.Double", "System.Single", "System.Byte"];

    public static Dictionary<string, string[]> Of(ModelStateDictionary modelState)
    {
        var failed = modelState.Where(entry => entry.Value?.Errors.Count > 0).ToList();
        // A body that couldn't be read also leaves the action's argument unset ("The request field is required."):
        // only what broke it is worth saying.
        if (failed.Any(entry => IsJsonPath(entry.Key)))
        {
            failed = failed.Where(entry => IsJsonPath(entry.Key)).ToList();
        }

        return failed.ToDictionary(
            entry => entry.Key,
            entry => entry.Value!.Errors.Select(error => Describe(entry.Key, error)).Distinct().ToArray());
    }

    private static bool IsJsonPath(string key) => key.StartsWith('$');

    private static string Describe(string key, ModelError error)
    {
        if (!IsJsonPath(key))
        {
            return string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value is invalid." : error.ErrorMessage;
        }

        var field = FieldName(key);
        if (field is null)
        {
            return "The request couldn't be read.";
        }

        return IsNumberTooLarge(error.Exception) ? $"{field} is too large." : $"{field} isn't valid.";
    }

    // The reader fails with a FormatException when a JSON number doesn't fit the field's numeric type.
    private static bool IsNumberTooLarge(Exception? exception)
        => exception is JsonException { InnerException: FormatException or OverflowException } json
           && NumericTypes.Any(type => json.Message.Contains(type, StringComparison.Ordinal));

    /// <summary>"$.lines[0].unitPrice" is "Unit price"; null for the body as a whole ("$").</summary>
    private static string? FieldName(string path)
    {
        var last = path[(path.LastIndexOf('.') + 1)..];
        var bracket = last.IndexOf('[');
        var name = bracket >= 0 ? last[..bracket] : last;
        if (name.Length == 0 || name == "$")
        {
            return null;
        }

        var words = string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
