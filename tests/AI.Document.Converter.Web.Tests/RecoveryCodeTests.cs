using System.Net;
using System.Text.RegularExpressions;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Web.Tests;

// Recovery codes, through the real sign-in flow.
//
// Closes one of the operator console's stated limitations: without these, an
// operator who lost their authenticator needed somebody with database access to
// clear TwoFactorEnabled by hand. A second factor with no way back is not a
// security control - it is a way to lose an account.
//
// Driven over HTTP rather than through UserManager, because the thing that
// matters is that a person locked out of their authenticator can actually get
// back in, and that path runs through the controller, the challenge page and
// the cookie.
[Collection(nameof(PostgresCollection))]
public sealed partial class RecoveryCodeTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private WebAppFactory _factory = null!;

    public RecoveryCodeTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(_fixture.TestConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private const string Password = "recovery-passphrase-2260";

    [GeneratedRegex("<kbd>([^<]+)</kbd>")]
    private static partial Regex CodePattern();

    private sealed record Account(string Email, Guid UserId, string AuthenticatorKey);

    private async Task<Account> CreateAccountWithTwoFactorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"recovery-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);

        return new Account(email, user.Id, key!);
    }

    private async Task<IReadOnlyList<string>> IssueCodesAsync(Account account)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByIdAsync(account.UserId.ToString());
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user!, 10);

        return codes!.ToList();
    }

    private async Task<HttpClient> StartSignInAsync(Account account)
    {
        var client = _factory.CreateSessionClient();

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/login", new Dictionary<string, string>
            {
                ["Email"] = account.Email,
                ["Password"] = Password
            });

        Assert.Contains("/account/two-factor", BrowserFlow.LocationOf(response));

        return client;
    }

    [Fact]
    public async Task ARecoveryCodeSignsYouInWhenTheAuthenticatorIsGone()
    {
        var account = await CreateAccountWithTwoFactorAsync();
        var codes = await IssueCodesAsync(account);
        var client = await StartSignInAsync(account);

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = codes[0] });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/dashboard", BrowserFlow.LocationOf(response));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dashboard")).StatusCode);
    }

    // Single use is the whole security property. A code that keeps working is
    // a second password, written on paper, that nobody thinks of as one.
    [Fact]
    public async Task ARecoveryCodeCannotBeUsedTwice()
    {
        var account = await CreateAccountWithTwoFactorAsync();
        var codes = await IssueCodesAsync(account);

        var first = await StartSignInAsync(account);
        await BrowserFlow.PostFormAsync(
            first, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = codes[0] });

        var second = await StartSignInAsync(account);
        var response = await BrowserFlow.PostFormAsync(
            second, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = codes[0] });

        // The form comes back rather than redirecting, and no session exists.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BrowserFlow.RedirectsToLogin(await second.GetAsync("/dashboard")));
    }

    [Fact]
    public async Task AnUnrelatedCodeIsRejected()
    {
        var account = await CreateAccountWithTwoFactorAsync();
        await IssueCodesAsync(account);
        var client = await StartSignInAsync(account);

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = "aaaaa-bbbbb" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BrowserFlow.RedirectsToLogin(await client.GetAsync("/dashboard")));
    }

    // Using one must consume exactly one. Burning the whole set on a single
    // sign-in would strand somebody on their next attempt.
    [Fact]
    public async Task UsingOneCodeConsumesOnlyThatCode()
    {
        var account = await CreateAccountWithTwoFactorAsync();
        var codes = await IssueCodesAsync(account);
        var client = await StartSignInAsync(account);

        await BrowserFlow.PostFormAsync(
            client, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = codes[0] });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(account.UserId.ToString());

        Assert.Equal(codes.Count - 1, await users.CountRecoveryCodesAsync(user!));
    }

    // The recovery route must not be a way around the password.
    [Fact]
    public async Task TheRecoveryPageCannotBeReachedWithoutThePasswordStep()
    {
        var client = _factory.CreateSessionClient();

        Assert.True(BrowserFlow.RedirectsToLogin(await client.GetAsync("/account/recovery-code")));
    }

    // Enabling two-factor has to hand over the codes there and then. Issuing a
    // second factor and leaving recovery for later is how people get locked
    // out - they never come back for it.
    [Fact]
    public async Task EnablingTwoFactorShowsTheCodesImmediately()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"enrol-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow
        };
        await users.CreateAsync(user, Password);

        var client = _factory.CreateSessionClient();
        await BrowserFlow.PostFormAsync(client, "/account/login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = Password
        });

        // The key the enrolment page just handed out, used to produce a real
        // code - the same thing an authenticator app would do.
        var enrolPage = await client.GetStringAsync("/security/two-factor/enable");
        var key = CodePattern().Match(enrolPage).Groups[1].Value;

        var response = await BrowserFlow.PostFormAsync(
            client, "/security/two-factor/enable", new Dictionary<string, string>
            {
                ["Code"] = BrowserFlow.ComputeTotp(key)
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Save these now", html);

        // Ten codes on the page, not a promise to produce them later.
        var shown = CodePattern().Matches(html).Count;
        Assert.Equal(10, shown);
    }

    // Regeneration exists for the case where the old codes may have been seen.
    // Leaving them working would defeat the point of regenerating.
    [Fact]
    public async Task RegeneratingInvalidatesThePreviousCodes()
    {
        var account = await CreateAccountWithTwoFactorAsync();
        var original = await IssueCodesAsync(account);

        // Sign in properly first, since regeneration is an authenticated action.
        var client = await StartSignInAsync(account);
        await BrowserFlow.PostFormAsync(
            client, "/account/two-factor", new Dictionary<string, string>
            {
                ["Code"] = BrowserFlow.ComputeTotp(account.AuthenticatorKey),
                ["RememberMe"] = "false"
            });

        await BrowserFlow.PostFormAsync(
            client, "/security/recovery-codes", new Dictionary<string, string>());

        // A code from the old set must no longer sign anyone in.
        var stale = await StartSignInAsync(account);
        var response = await BrowserFlow.PostFormAsync(
            stale, "/account/recovery-code", new Dictionary<string, string> { ["Code"] = original[0] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BrowserFlow.RedirectsToLogin(await stale.GetAsync("/dashboard")));
    }
}
