using MediCare.Application.Services;

namespace MediCare.Web.Models;

public static class LoginUserName
{
    /// <summary>
    /// Patient usernames are canonical Egyptian mobile numbers (+20XXXXXXXXXX). Local or
    /// alternative phone formats are mapped to that canonical form; anything else (e.g. email)
    /// is returned trimmed and unchanged.
    /// </summary>
    public static string Resolve(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        return EgyptianMobileNumber.TryNormalize(trimmed, out var canonical, out _)
            ? canonical
            : trimmed;
    }
}
