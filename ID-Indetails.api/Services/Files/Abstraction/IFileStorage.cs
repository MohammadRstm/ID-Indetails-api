namespace ID_Indetails.api.Services.Files.Abstraction;

/// <summary>Where the bytes of uploads live: a folder on disk now, a bucket later. Keys are always server-generated.</summary>
public interface IFileStorage
{
    /// <summary>Stores the content under a new key and returns the key.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>A seekable stream, so videos can be served in ranges.</summary>
    Stream OpenRead(string key);

    /// <summary>How many bytes are stored under the key.</summary>
    long SizeOf(string key);

    /// <summary>Removes the content and its smaller copies; a missing key is not an error.</summary>
    void Delete(string key);

    /// <summary>The key of a photo's smaller copy (<paramref name="size"/> px on its longest side), kept beside it.</summary>
    string SizedKey(string key, int size);

    /// <summary>Whether something is stored under the key.</summary>
    bool Exists(string key);

    /// <summary>Stores a smaller copy under its <see cref="SizedKey"/>, replacing one made at the same moment.</summary>
    Task SaveAsAsync(string key, byte[] content, CancellationToken cancellationToken = default);
}
