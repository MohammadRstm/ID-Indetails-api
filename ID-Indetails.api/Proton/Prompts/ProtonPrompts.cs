using System.Security.Cryptography;
using System.Text;

namespace ID_Indetails.api.Proton.Prompts;

/// <summary>
/// Proton's instructions, kept as Markdown files next to this class and compiled into the assembly. The version is a
/// hash of the text, so every usage record shows exactly which prompt produced it.
/// </summary>
public static class ProtonPrompts
{
    /// <summary>Where the prompts name the app: replaced by the product name users know it by.</summary>
    public const string ProductToken = "{product}";

    /// <summary>For the text chat: who Proton is and how it sounds, the company, what it may see, what its money figures mean, how it analyses, how it moves around the app, how it reads files and the discussion with clients, how it proposes changes and messages, and how to answer in writing.</summary>
    public static string Chat { get; } = string.Join("\n\n",
        Read("persona.md"), Read("proton.md"), Read("data.md"), Read("money.md"), Read("analysis.md"), Read("navigation.md"), Read("files.md"), Read("actions.md"),
        Read("messages.md"), Read("present.md"), Read("chat.md"));

    public static string ChatVersion { get; } = Version(Chat);

    /// <summary>The chat prompt with the app's name filled in: the same for everyone, so it stays cached.</summary>
    public static string ChatFor(string productName) => Chat.Replace(ProductToken, productName);

    /// <summary>For the live voice model: when to hand a question to Proton, how to say its answers, and that speech confirms nothing.</summary>
    public static string Voice { get; } = Read("voice-live.md");

    public static string VoiceVersion { get; } = Version(Voice);

    public static string VoiceFor(string productName) => Voice.Replace(ProductToken, productName);

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
