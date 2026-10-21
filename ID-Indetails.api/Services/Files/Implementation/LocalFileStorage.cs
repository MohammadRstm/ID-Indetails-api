using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// Keeps uploads under <see cref="FileStorageOptions.RootPath"/>, as <c>yyyy/MM/{random}{extension}</c>, and a photo's
/// smaller copies beside it as <c>{its name}.{size}.webp</c>.
/// </summary>
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

    public long SizeOf(string key) => new FileInfo(PathOf(key)).Length;

    public void Delete(string key)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var folder = Path.GetDirectoryName(path)!;
        if (Directory.Exists(folder))
        {
            foreach (var copy in Directory.EnumerateFiles(folder, $"{Path.GetFileName(path)}.*{Pictures.WebpExtension}"))
            {
                File.Delete(copy);
            }
        }
    }

    public string SizedKey(string key, int size) => $"{key}.{size}{Pictures.WebpExtension}";

    public bool Exists(string key) => File.Exists(PathOf(key));

    public async Task SaveAsAsync(string key, byte[] content, CancellationToken cancellationToken = default)
    {
        // written aside and moved in, so a copy is never read half-written
        var path = PathOf(key);
        var aside = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(aside, content, cancellationToken);
        File.Move(aside, path, overwrite: true);
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
