using System.Net;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Security;
using AI.Document.Converter.Web.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Web.Tests;

// SR-SEC-7, over real HTTP through the real pipeline.
//
// These are the tests the operator console increment shipped without. The
// authorization policy lives in Program.cs, so no service-level test touches
// it - the only previous proof that a password-only operator was refused was a
// manual run, which says nothing about whether the wiring still holds after the
// next edit. That wiring is now what stands between a stolen password and every
// tenant's data, so it needs a test that fails when someone breaks it.
[Collection(nameof(PostgresCollection))]
public sealed class OperatorAuthorizationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private WebAppFactory _factory = null!;

    public OperatorAuthorizationTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(_fixture.TestConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private const string Password = "harness-passphrase-8814";

    private sealed record Account(string Email, string? AuthenticatorKey);

    // Created through Identity itself rather than by inserting rows, so the
    // password hash, security stamp and role links are exactly what the running
    // application would produce.
    private async Task<Account> CreateAccountAsync(bool isOperator, bool withTwoFactor)
    {
        using var scope = _factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        var email = $"harness-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        // A workspace, because the pages under test resolve one for the signed-
        // in user and would otherwise divert to the no-workspace view.
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "Harness",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = DateTime.UtcNow
        });
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = WorkspaceRole.Owner,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        if (isOperator)
        {
            if (!await roles.RoleExistsAsync(OperatorPolicy.RoleName))
            {
                await roles.CreateAsync(new ApplicationRole { Name = OperatorPolicy.RoleName });
            }

            await users.AddToRoleAsync(user, OperatorPolicy.RoleName);
        }

        string? key = null;

        if (withTwoFactor)
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
        }

        return new Account(email, key);
    }

    // The password half only. Returns the response so a test can assert on
    // where it went, which is how the two-factor requirement shows up.
    private static async Task<HttpResponseMessage> SignInWithPasswordAsync(
        HttpClient client, Account account) =>
        await BrowserFlow.PostFormAsync(client, "/account/login", new Dictionary<string, string>
        {
            ["Email"] = account.Email,
            ["Password"] = Password
        });

    private static async Task CompleteTwoFactorAsync(HttpClient client, Account account)
    {
        var response = await BrowserFlow.PostFormAsync(
            client, "/account/two-factor", new Dictionary<string, string>
            {
                ["Code"] = BrowserFlow.ComputeTotp(account.AuthenticatorKey!),
                ["RememberMe"] = "false"
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/dashboard", BrowserFlow.LocationOf(response));
    }

    [Fact]
    public async Task AnAnonymousRequestForTheConsoleGoesToLogin()
    {
        var client = _factory.CreateSessionClient();

        var response = await client.GetAsync("/operator");

        Assert.True(
            BrowserFlow.RedirectsToLogin(response),
            $"Expected a redirect to login, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    [Fact]
    public async Task AnOrdinaryUserIsRefusedTheConsole()
    {
        var account = await CreateAccountAsync(isOperator: false, withTwoFactor: false);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);

        // Signed in successfully - the refusal is authorization, not
        // authentication.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dashboard")).StatusCode);

        var response = await client.GetAsync("/operator");

        Assert.True(
            BrowserFlow.RedirectsToAccessDenied(response),
            $"Expected access denied, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    // The other half of the policy, isolated.
    //
    // Added after mutation testing: deleting the role requirement from the
    // policy broke NO test, because every non-operator case here also lacked a
    // second factor and was being refused by the MFA rule instead. A customer
    // who happens to use two-factor is exactly the person the role check has to
    // stop, and nothing was checking it.
    [Fact]
    public async Task AUserWithTwoFactorButNoRoleIsStillRefusedTheConsole()
    {
        var account = await CreateAccountAsync(isOperator: false, withTwoFactor: true);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);
        await CompleteTwoFactorAsync(client, account);

        // Fully signed in, second factor and all.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dashboard")).StatusCode);

        var response = await client.GetAsync("/operator");

        Assert.True(
            BrowserFlow.RedirectsToAccessDenied(response),
            $"Expected access denied, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    // The rule that matters most: the role alone must not be enough, or a
    // stolen password reaches every tenant's data.
    [Fact]
    public async Task AnOperatorWithoutASecondFactorIsRefusedTheConsole()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: false);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);

        var response = await client.GetAsync("/operator");

        Assert.True(
            BrowserFlow.RedirectsToAccessDenied(response),
            $"Expected access denied, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    // A password-only session for an account that HAS two-factor should never
    // exist - the login flow must divert to the challenge instead of issuing a
    // cookie.
    [Fact]
    public async Task AnAccountWithTwoFactorCannotFinishSigningInWithAPasswordAlone()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: true);
        var client = _factory.CreateSessionClient();

        var response = await SignInWithPasswordAsync(client, account);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/two-factor", BrowserFlow.LocationOf(response));

        // Not signed in yet: the dashboard still bounces to login.
        Assert.True(BrowserFlow.RedirectsToLogin(await client.GetAsync("/dashboard")));
    }

    [Fact]
    public async Task AnOperatorWhoCompletesTheSecondFactorReachesTheConsole()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: true);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);
        await CompleteTwoFactorAsync(client, account);

        var response = await client.GetAsync("/operator");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Operator console", await response.Content.ReadAsStringAsync());
    }

    // A wrong code must not sign anyone in. Six digits is only a million
    // possibilities, so the failure path is worth pinning.
    [Fact]
    public async Task AWrongAuthenticatorCodeDoesNotSignYouIn()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: true);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/two-factor", new Dictionary<string, string>
            {
                ["Code"] = "000000",
                ["RememberMe"] = "false"
            });

        // The form is redisplayed rather than redirecting to the dashboard.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BrowserFlow.RedirectsToLogin(await client.GetAsync("/dashboard")));
    }

    // Two-factor cannot be reached without first proving the password.
    [Fact]
    public async Task TheTwoFactorChallengeCannotBeReachedWithoutThePasswordStep()
    {
        var client = _factory.CreateSessionClient();

        var response = await client.GetAsync("/account/two-factor");

        Assert.True(
            BrowserFlow.RedirectsToLogin(response),
            $"Expected a redirect to login, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    // Every POST in this suite carries a scraped token, so anti-forgery is
    // exercised throughout. This asserts it is actually on: without it, the
    // rest of the suite would pass just as happily with CSRF disabled.
    [Fact]
    public async Task APostWithoutAnAntiforgeryTokenIsRejected()
    {
        var account = await CreateAccountAsync(isOperator: false, withTwoFactor: false);
        var client = _factory.CreateSessionClient();

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Email"] = account.Email, ["Password"] = Password }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The inspection endpoint is behind the same policy as the console. Worth
    // its own test because it is the one that reveals customer filenames, and
    // a route added later could easily miss the attribute.
    [Fact]
    public async Task TheInspectionEndpointIsBehindTheSamePolicy()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: false);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);

        var response = await client.GetAsync($"/operator/jobs/{Guid.NewGuid()}/inspect");

        Assert.True(
            BrowserFlow.RedirectsToAccessDenied(response),
            $"Expected access denied, got {response.StatusCode} -> {BrowserFlow.LocationOf(response)}");
    }

    [Fact]
    public async Task TheAuditTrailIsBehindTheSamePolicy()
    {
        var account = await CreateAccountAsync(isOperator: true, withTwoFactor: false);
        var client = _factory.CreateSessionClient();

        await SignInWithPasswordAsync(client, account);

        Assert.True(BrowserFlow.RedirectsToAccessDenied(await client.GetAsync("/operator/audit")));
    }
}
