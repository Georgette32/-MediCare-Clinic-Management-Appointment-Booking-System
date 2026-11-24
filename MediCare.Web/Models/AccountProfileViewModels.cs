using System.ComponentModel.DataAnnotations;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

public sealed class ProfilePageModel
{
    public AccountProfileView Profile { get; init; } = null!;
    public ProfileNameForm NameForm { get; init; } = new();
    public ProfileEmailForm EmailForm { get; init; } = new();
}

public sealed class ProfileNameForm
{
    [Required, StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;
}

public sealed class ProfileEmailForm
{
    [EmailAddress, StringLength(256)]
    [Display(Name = "Email (optional)")]
    public string? Email { get; set; }
}
