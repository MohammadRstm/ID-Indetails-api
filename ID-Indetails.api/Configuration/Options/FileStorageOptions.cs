using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>Where uploads are kept. Integration tests point it at a temporary folder and switch the cleanup off.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>A folder on disk, relative to the app's content root unless absolute. Never a publicly served folder.</summary>
    [Required]
    public string RootPath { get; init; } = "App_Data/files";

    /// <summary>Whether the job that deletes uploads never attached to anything runs.</summary>
    public bool CleanupEnabled { get; init; } = true;
}
