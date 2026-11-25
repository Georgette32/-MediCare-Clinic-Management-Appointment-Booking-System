using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your mobile number (patients) or your staff username.")]
    [Display(Name = "Mobile number or username")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
