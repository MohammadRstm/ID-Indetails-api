namespace ID_Indetails.api.Services.Files.Abstraction;

/// <summary>Where the bytes of uploads live: a folder on disk now, a bucket later. Keys are always server-generated.</summary>
public interface IFileStorage
{
    /// <summary>Stores the content under a new key and returns the key.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>A seekable stream, so videos can be served in ranges.</summary>
    Stream OpenRead(string key);

    /// <summary>Removes the content; a missing key is not an error.</summary>
    void Delete(string key);
}
