using System.Text;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Models;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AI.Document.Converter.Web.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ConverterDbContext _db;
    private readonly WorkspaceProvisioner _workspaceProvisioner;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ConverterDbContext db,
        WorkspaceProvisioner workspaceProvisioner,
        IEmailSender emailSender,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _workspaceProvisioner = workspaceProvisioner;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var nowUtc = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = model.Email,
            Email = model.Email,
            CreatedAtUtc = nowUtc
        };

        // The user and their personal workspace are created in ONE transaction.
        // A user without a workspace could not do anything at all and would have
        // to be repaired by hand, so partial success is not an acceptable
        // outcome here (SaaS §5.3).
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        _workspaceProvisioner.AddPersonalWorkspace(user, model.WorkspaceName, nowUtc);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await SendConfirmationEmailAsync(user, cancellationToken);

        _logger.LogInformation("Registered user {UserId} with a personal workspace", user.Id);

        return RedirectToAction(nameof(RegistrationPending));
    }

    [HttpGet("registration-pending")]
    [AllowAnonymous]
    public IActionResult RegistrationPending() => View();

    [HttpGet("confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> Confirm(string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            return View("ConfirmFailed");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            // Same view as an invalid token: whether an account exists is not
            // something an unauthenticated caller should be able to probe.
            return View("ConfirmFailed");
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return View("ConfirmFailed");
        }

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        return View(result.Succeeded ? "ConfirmSucceeded" : "ConfirmFailed");
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectToLocalOrDashboard(returnUrl);
        }

        if (result.IsNotAllowed)
        {
            // Identity returns IsNotAllowed for an unconfirmed email. Saying so
            // is a deliberate exception to the generic-error rule below: the
            // user already knows this address exists because they just tried to
            // register it, and without the hint they have no way to work out
            // why a correct password is being refused.
            ModelState.AddModelError(
                string.Empty,
                "This account's email address has not been confirmed yet. Check your inbox for the confirmation link.");
            return View(model);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(
                string.Empty, "This account is temporarily locked after too many attempts. Try again shortly.");
            return View(model);
        }

        // One message for both "no such user" and "wrong password", so the form
        // cannot be used to enumerate which email addresses are registered.
        ModelState.AddModelError(string.Empty, "Incorrect email or password.");
        return View(model);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();

    private async Task SendConfirmationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Identity's token contains characters that do not survive a URL
        // round-trip intact; base64url is the standard way to carry it.
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var confirmUrl = Url.Action(
            nameof(Confirm), "Account",
            new { userId = user.Id, token = encodedToken },
            Request.Scheme);

        await _emailSender.SendAsync(
            user.Email!,
            "Confirm your AI Document Converter account",
            $"""
             Confirm your email address to finish setting up your account:

             {confirmUrl}

             If you did not create this account, you can ignore this message.
             """,
            cancellationToken);
    }

    // Only ever redirect to a path within this application. An unchecked
    // returnUrl is an open-redirect: an attacker sends a login link that bounces
    // the user to a look-alike site straight after a genuine sign-in.
    private IActionResult RedirectToLocalOrDashboard(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
}
