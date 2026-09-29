using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>Keeps uploads under <see cref="FileStorageOptions.RootPath"/>, as <c>yyyy/MM/{random}{extension}</c>.</summary>
public sealed class LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment, IClock clock) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Stream OpenRead(string key) => new FileStream(PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read);

    public void Delete(string key)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Keys are ours, but a key that would leave the root is refused anyway.</summary>
    private string PathOf(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("A storage key pointed outside the file storage root.");
    }
}
