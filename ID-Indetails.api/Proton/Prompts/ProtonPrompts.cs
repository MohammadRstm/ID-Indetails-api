using System.Security.Cryptography;
using System.Text;

namespace ID_Indetails.api.Proton.Prompts;

/// <summary>
/// Proton's instructions, kept as Markdown files next to this class and compiled into the assembly. The version is a
/// hash of the text, so every usage record shows exactly which prompt produced it.
/// </summary>
public static class ProtonPrompts
{
    /// <summary>For the text chat: who Proton is, what it may see, how it analyses, how it proposes changes, and how to answer in writing.</summary>
    public static string Chat { get; } = string.Join("\n\n", Read("proton.md"), Read("data.md"), Read("analysis.md"), Read("actions.md"), Read("chat.md"));

    public static string ChatVersion { get; } = Version(Chat);

    private static string Read(string file)
    {
        var name = $"{typeof(ProtonPrompts).Namespace}.{file}";
        using var stream = typeof(ProtonPrompts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Proton prompt {file} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static string Version(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
}
