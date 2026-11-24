namespace MediCare.Application.Abstractions;

/// <summary>
/// Storage for attachment bytes. Implementations place files outside wwwroot and must never use a caller-supplied
/// name on disk: the stored name is server-generated and validated before any path is built from it.
/// </summary>
public interface IMedicalFileStore
{
    /// <summary>Writes the content under the stored name and returns its size and SHA-256 (lowercase hex).</summary>
    Task<MedicalFileSaveResult> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading. The caller disposes the stream.</summary>
    Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken);

    /// <summary>Deletes a stored file. Deleting a file that does not exist is not an error.</summary>
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken);
}

public sealed record MedicalFileSaveResult(long SizeBytes, string Sha256Hex);
