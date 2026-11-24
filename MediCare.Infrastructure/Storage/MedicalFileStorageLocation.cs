namespace MediCare.Infrastructure.Storage;

/// <summary>
/// Resolves the configured attachment folder to an absolute path and refuses any location inside the web root, so that
/// stored medical files can never be served as static content.
/// </summary>
public static class MedicalFileStorageLocation
{
    public static string Resolve(string? configuredPath, string contentRootPath, string webRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("MedicalFiles:StoragePath is required.");
        if (string.IsNullOrWhiteSpace(contentRootPath))
            throw new InvalidOperationException("The content root path is required to resolve MedicalFiles:StoragePath.");

        var candidate = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath);
        var full = TrimTrailingSeparator(Path.GetFullPath(candidate));
        var web = TrimTrailingSeparator(Path.GetFullPath(string.IsNullOrWhiteSpace(webRootPath)
            ? Path.Combine(contentRootPath, "wwwroot")
            : webRootPath));

        if (IsSameOrInside(full, web))
            throw new InvalidOperationException(
                $"MedicalFiles:StoragePath resolves to '{full}', which is inside the web root '{web}'. Choose a folder outside wwwroot.");

        return full;
    }

    private static bool IsSameOrInside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        return path.Length > (root?.Length ?? 0) ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }
}
