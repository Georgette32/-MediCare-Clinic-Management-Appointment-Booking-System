namespace MediCare.Application.Interfaces.Storage;

/// <summary>
/// Abstraction over where uploaded files physically live. The application only ever
/// refers to files by a generated name; the implementation decides where they are kept.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string storedFileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stored file for reading, or returns null when it does not exist.
    /// The caller owns (and must dispose) the returned stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
}
