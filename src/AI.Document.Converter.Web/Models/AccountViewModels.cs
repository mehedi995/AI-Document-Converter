using System.ComponentModel.DataAnnotations;

namespace AI.Document.Converter.Web.Models;

public sealed class RegisterViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    [Display(Name = "Workspace name")]
    public string WorkspaceName { get; set; } = string.Empty;

    // Length is validated by Identity's own password policy too; the attribute
    // is here so the user sees the requirement before submitting rather than
    // after a round trip.
    [Required]
    [StringLength(200, MinimumLength = 12,
        ErrorMessage = "Use at least 12 characters. Length matters more than symbols.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Stay signed in")]
    public bool RememberMe { get; set; }
}
