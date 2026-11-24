using MediCare.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Storage;

/// <summary>
/// Startup validation for MedicalFiles: the storage path must resolve and sit outside the web root. Runs through
/// ValidateOnStart, so a bad configuration stops the application before it serves requests.
/// </summary>
public sealed class MedicalFileOptionsValidator(IWebHostEnvironment environment) : IValidateOptions<MedicalFileOptions>
{
    public ValidateOptionsResult Validate(string? name, MedicalFileOptions options)
    {
        try
        {
            MedicalFileStorageLocation.Resolve(options.StoragePath, environment.ContentRootPath, environment.WebRootPath);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
