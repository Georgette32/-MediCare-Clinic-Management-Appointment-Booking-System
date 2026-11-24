using System.Security.Cryptography;
using MediCare.Application.Abstractions;
using MediCare.Domain.Entities;

namespace MediCare.Infrastructure.Storage;

/// <summary>
/// Stores attachment bytes in one folder outside wwwroot. Only server-generated, shape-validated names are ever combined
/// with the root, and the combined path must stay inside the root, so a crafted name cannot traverse out of it.
/// </summary>
public sealed class FileSystemMedicalFileStore : IMedicalFileStore
{
    private const int BufferBytes = 81920;
    private readonly string _root;

    public FileSystemMedicalFileStore(string absoluteRoot)
    {
        if (string.IsNullOrWhiteSpace(absoluteRoot) || !Path.IsPathFullyQualified(absoluteRoot))
            throw new ArgumentException("The medical file root must be an absolute path.", nameof(absoluteRoot));

        _root = Path.GetFullPath(absoluteRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<MedicalFileSaveResult> SaveAsync(Stream content, string storedFileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolvePath(storedFileName);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        long size = 0;
        // CreateNew: a stored name is never overwritten, even if it somehow collides.
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous);
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            size += read;
        }
        await target.FlushAsync(cancellationToken);

        return new MedicalFileSaveResult(size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storedFileName);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(storedFileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolvePath(string storedFileName)
    {
        if (!MedicalAttachment.IsValidStoredFileName(storedFileName))
            throw new ArgumentException("The stored file name is not in the expected format.", nameof(storedFileName));

        var full = Path.GetFullPath(Path.Combine(_root, storedFileName));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved medical file path is outside the storage folder.");
        return full;
    }
}
