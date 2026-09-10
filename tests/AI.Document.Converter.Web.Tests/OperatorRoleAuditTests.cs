using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Operations;
using AI.Document.Converter.Web.Security;
using AI.Document.Converter.Web.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Web.Tests;

// Closes the other stated limitation of the operator console: role changes
// left no tamper-evident record.
//
// Operator status is what makes every other entry in the audit trail possible.
// Without these entries the log showed what operators DID but never how
// somebody became one - which is the first question an investigation asks, and
// the easiest thing for an attacker who gained shell access to leave no trace
// of.
//
// The command is exercised directly rather than by shelling out to `dotnet
// run`: what is under test is the audit write, not process startup.
[Collection(nameof(PostgresCollection))]
public sealed class OperatorRoleAuditTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private WebAppFactory _factory = null!;

    public OperatorRoleAuditTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(_fixture.TestConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Id, string Email)> CreateAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"rolecheck-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user, "role-audit-passphrase-3390");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        return (user.Id, email);
    }

    private async Task<int> RunAsync(params string[] args) =>
        await OperatorRoleCommand.RunAsync(_factory.Services, args);

    private async Task<OperatorAuditEntry?> EntryForAsync(Guid userId, string action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        return await db.OperatorAuditEntries
            .SingleOrDefaultAsync(e => e.TargetUserId == userId && e.Action == action);
    }

    [Fact]
    public async Task GrantingTheRoleIsRecorded()
    {
        var account = await CreateAccountAsync();

        Assert.Equal(0, await RunAsync("grant-operator", account.Email));

        var entry = await EntryForAsync(account.Id, OperatorAuditActions.GrantOperatorRole);

        Assert.NotNull(entry);
        Assert.Equal(account.Email, entry.TargetEmail);

        // No application user acted - this came from a shell. Recording
        // Guid.Empty would put a lie in the trail, so the field is null and
        // the actor is described instead.
        Assert.Null(entry.ActorUserId);
        Assert.StartsWith("command line:", entry.ActorEmail);
    }

    [Fact]
    public async Task RevokingTheRoleIsRecordedSeparately()
    {
        var account = await CreateAccountAsync();

        await RunAsync("grant-operator", account.Email);
        Assert.Equal(0, await RunAsync("revoke-operator", account.Email));

        Assert.NotNull(await EntryForAsync(account.Id, OperatorAuditActions.GrantOperatorRole));
        Assert.NotNull(await EntryForAsync(account.Id, OperatorAuditActions.RevokeOperatorRole));
    }

    // The grant has to actually happen as well as be recorded - a test that
    // only checked the audit row would pass on a command that logged and did
    // nothing.
    [Fact]
    public async Task TheRoleIsActuallyGrantedAndRemoved()
    {
        var account = await CreateAccountAsync();

        await RunAsync("grant-operator", account.Email);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(account.Id.ToString());
            Assert.True(await users.IsInRoleAsync(user!, OperatorPolicy.RoleName));
        }

        await RunAsync("revoke-operator", account.Email);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(account.Id.ToString());
            Assert.False(await users.IsInRoleAsync(user!, OperatorPolicy.RoleName));
        }
    }

    // An optional reason, so a grant made during an incident can say so.
    [Fact]
    public async Task AReasonGivenOnTheCommandLineIsStored()
    {
        var account = await CreateAccountAsync();

        await RunAsync("grant-operator", account.Email, "incident", "2291", "-", "on-call cover");

        var entry = await EntryForAsync(account.Id, OperatorAuditActions.GrantOperatorRole);

        Assert.Equal("incident 2291 - on-call cover", entry!.Reason);
    }

    // A failed grant must not leave a record saying it happened.
    [Fact]
    public async Task GrantingToAnUnknownAccountRecordsNothing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        var before = await db.OperatorAuditEntries.CountAsync();

        Assert.Equal(1, await RunAsync("grant-operator", "nobody@example.test"));

        Assert.Equal(before, await db.OperatorAuditEntries.CountAsync());
    }

    // The trail is what an investigator reads, so the entry has to be visible
    // through the same service the console renders from.
    [Fact]
    public async Task TheRoleChangeAppearsInTheAuditTrail()
    {
        var account = await CreateAccountAsync();
        await RunAsync("grant-operator", account.Email);

        using var scope = _factory.Services.CreateScope();
        var inspection = scope.ServiceProvider.GetRequiredService<OperatorInspectionService>();

        var trail = await inspection.GetAuditTrailAsync(100, CancellationToken.None);

        Assert.Contains(
            trail,
            e => e.Action == OperatorAuditActions.GrantOperatorRole && e.TargetEmail == account.Email);
    }
}
