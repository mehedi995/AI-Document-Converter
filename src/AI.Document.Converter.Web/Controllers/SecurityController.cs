using System.Text;
using System.Text.Encodings.Web;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AI.Document.Converter.Web.Controllers;

// Two-factor enrolment.
//
// Built because SR-SEC-7 requires MFA for production administrators, and the
// operator console is that administration. Offered to every account rather than
// only to operators: an account can be made an operator later, and a security
// feature that exists only for staff is one nobody has tested by the time it
// matters.
//
// TOTP (RFC 6238) via Identity's built-in authenticator support. No SMS: SIM
// swapping is a real and routine attack, and adding a channel we would then
// have to warn people not to trust is worse than not offering it.
[Authorize]
[Route("security")]
public sealed class SecurityController : Controller
{
    // Ten is Identity's own default and a reasonable balance: enough that
    // losing a phone is survivable, few enough that they can be written down.
    private const int RecoveryCodeCount = 10;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<SecurityController> _logger;

    public SecurityController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<SecurityController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [HttpGet("two-factor")]
    public async Task<IActionResult> TwoFactor()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        return View(new TwoFactorStatusViewModel(
            IsEnabled: await _userManager.GetTwoFactorEnabledAsync(user),
            IsOperator: await _userManager.IsInRoleAsync(user, Security.OperatorPolicy.RoleName),
            // Whether THIS session used the second factor. An operator whose
            // account has two-factor enabled but who signed in before enabling
            // it still cannot reach the console, and the page has to say so or
            // the refusal looks like a bug.
            SessionUsedTwoFactor: User.HasClaim(
                Security.OperatorPolicy.AuthenticationMethodClaim,
                Security.OperatorPolicy.MultiFactorValue)));
    }

    [HttpGet("two-factor/enable")]
    public async Task<IActionResult> Enable()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        return View(await BuildEnrolmentModelAsync(user));
    }

    [HttpPost("two-factor/enable")]
    public async Task<IActionResult> Enable(TwoFactorEnrolmentViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildEnrolmentModelAsync(user));
        }

        // Whitespace and separators stripped: authenticator apps display codes
        // as "123 456", and refusing the thing the user can see on their screen
        // is a self-inflicted support ticket.
        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!isValid)
        {
            // Enabling is refused until a code from the app actually verifies.
            // Turning it on first and trusting the setup would lock people out
            // of their own accounts when the QR scan silently failed.
            ModelState.AddModelError(
                nameof(model.Code),
                "That code was not accepted. Check your authenticator app and try the current code.");

            return View(await BuildEnrolmentModelAsync(user));
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        _logger.LogInformation("Two-factor authentication enabled for user {UserId}", user.Id);

        // Refreshed so the principal reflects the change, though it will NOT
        // carry amr=mfa - that claim can only come from actually completing a
        // two-factor sign-in. The confirmation page says as much.
        await _signInManager.RefreshSignInAsync(user);

        // Issued at the same moment two-factor is switched on, never later.
        // A second factor with no way back is not a security control, it is a
        // way to lose an account - and for an operator, recovering without
        // these means somebody editing the database by hand.
        return View("RecoveryCodes", await GenerateRecoveryCodesAsync(user));
    }

    [HttpGet("recovery-codes")]
    public async Task<IActionResult> RecoveryCodes()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return RedirectToAction(nameof(TwoFactor));
        }

        // The count only. The codes themselves are stored hashed and cannot be
        // shown again - displaying them a second time would mean we had kept
        // them in a form somebody could steal.
        return View(new RecoveryCodesViewModel(
            [], await _userManager.CountRecoveryCodesAsync(user)));
    }

    [HttpPost("recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return RedirectToAction(nameof(TwoFactor));
        }

        // Regenerating INVALIDATES the previous set. That is the point: it is
        // what you do when you think the old codes were seen by someone else,
        // and leaving them working would defeat the purpose.
        return View("RecoveryCodes", await GenerateRecoveryCodesAsync(user));
    }

    private async Task<RecoveryCodesViewModel> GenerateRecoveryCodesAsync(ApplicationUser user)
    {
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        _logger.LogInformation("Issued new two-factor recovery codes for user {UserId}", user.Id);

        return new RecoveryCodesViewModel(
            codes?.ToList() ?? [], await _userManager.CountRecoveryCodesAsync(user));
    }

    [HttpPost("two-factor/disable")]
    public async Task<IActionResult> Disable()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        // An operator may not switch off the control that lets them hold the
        // role. Removing the role is an out-of-band action, so this refuses
        // rather than silently demoting them - quietly stripping someone's
        // access as a side effect of a checkbox would be a surprising way to
        // lose production administration.
        if (await _userManager.IsInRoleAsync(user, Security.OperatorPolicy.RoleName))
        {
            TempData["TwoFactorError"] =
                "Two-factor authentication cannot be switched off while this account holds the "
                + "Operator role. Have the role removed first.";

            return RedirectToAction(nameof(TwoFactor));
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);

        // The old shared secret must not survive: leaving it in place would
        // mean re-enabling silently trusts a key that may have been captured
        // while two-factor was off. The recovery codes go with it - a code
        // that still signs you in after two-factor is off is a password that
        // nobody remembers having.
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);

        _logger.LogInformation("Two-factor authentication disabled for user {UserId}", user.Id);

        await _signInManager.RefreshSignInAsync(user);

        return RedirectToAction(nameof(TwoFactor));
    }

    private async Task<TwoFactorEnrolmentViewModel> BuildEnrolmentModelAsync(ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        return new TwoFactorEnrolmentViewModel
        {
            SharedKey = FormatKey(key!),
            AuthenticatorUri = BuildAuthenticatorUri(await _userManager.GetEmailAsync(user) ?? "user", key!)
        };
    }

    // Grouped in fours. The key is typed by hand whenever a camera is not
    // available, and an unbroken 32-character string is where transcription
    // errors come from.
    private static string FormatKey(string key)
    {
        var formatted = new StringBuilder();

        for (var position = 0; position < key.Length; position += 4)
        {
            formatted.Append(key.AsSpan(position, Math.Min(4, key.Length - position))).Append(' ');
        }

        return formatted.ToString().TrimEnd().ToLowerInvariant();
    }

    private static string BuildAuthenticatorUri(string email, string key) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            UrlEncoder.Default.Encode("AI Document Converter"),
            UrlEncoder.Default.Encode(email),
            key);
}
