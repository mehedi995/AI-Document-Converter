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

// The second step of a two-factor sign-in. Separate from LoginViewModel so the
// password is not carried into a second round trip.
public sealed class TwoFactorChallengeViewModel
{
    [Required]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed record TwoFactorStatusViewModel(bool IsEnabled, bool IsOperator, bool SessionUsedTwoFactor);

public sealed class TwoFactorEnrolmentViewModel
{
    // Shown for manual entry when scanning is not possible.
    public string SharedKey { get; set; } = string.Empty;

    public string AuthenticatorUri { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Code from your authenticator app")]
    public string Code { get; set; } = string.Empty;
}

// Codes are only ever present immediately after generation. On a later visit
// the list is empty and only the remaining count is known, because they are
// stored hashed - showing them twice would mean keeping them in a stealable
// form.
public sealed record RecoveryCodesViewModel(IReadOnlyList<string> Codes, int Remaining);

// The way back in when the authenticator is gone. Separate from the ordinary
// challenge so the two cannot be confused: a recovery code is single-use and
// burning one is a notable event, not a routine sign-in.
public sealed class RecoveryCodeViewModel
{
    [Required]
    [Display(Name = "Recovery code")]
    public string Code { get; set; } = string.Empty;
}
