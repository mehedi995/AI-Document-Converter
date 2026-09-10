using System.Net;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Web.Tests;

// The upload pipeline over real HTTP.
//
// UploadValidatorTests already covers the validation rules. What it cannot
// cover is everything between the browser and that validator: multipart
// parsing, `List<IFormFile>` binding, the per-request file-count limit, the
// anti-forgery check, and the controller's own refusals. A regression in any of
// those would leave every validator test green while uploads broke - or worse,
// while an unvalidated file got through.
[Collection(nameof(PostgresCollection))]
public sealed class UploadOverHttpTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private WebAppFactory _factory = null!;

    public UploadOverHttpTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(_fixture.TestConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private const string Password = "upload-passphrase-6653";

    private sealed record Account(string Email, Guid UserId, Guid WorkspaceId);

    private async Task<Account> CreateAccountAsync(long includedCredits = 1_000)
    {
        using var scope = _factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        var nowUtc = DateTime.UtcNow;
        var email = $"upload-{Guid.NewGuid():N}@example.test";

        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = nowUtc
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "Uploader",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = nowUtc
        });
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspaceId, UserId = user.Id, Role = WorkspaceRole.Owner, JoinedAtUtc = nowUtc
        });
        db.UsagePeriods.Add(new UsagePeriod
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            StartsAtUtc = nowUtc.AddDays(-1),
            EndsAtUtc = nowUtc.AddDays(29),
            PlanCode = "standard@v1",
            IncludedCredits = includedCredits,
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync();
        return new Account(email, user.Id, workspaceId);
    }

    private async Task<HttpClient> SignInAsync(Account account)
    {
        var client = _factory.CreateSessionClient();

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/login", new Dictionary<string, string>
            {
                ["Email"] = account.Email,
                ["Password"] = Password
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static byte[] RealPdf()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AI.Document.Converter.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllBytes(Path.Combine(directory.FullName, "samples", "sample.pdf"));
    }

    private async Task<int> JobCountAsync(Guid workspaceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        return await db.ConversionJobs.CountAsync(j => j.WorkspaceId == workspaceId);
    }

    // The control. Everything below asserts a refusal, and refusals are
    // meaningless without proof the happy path works through the same route.
    [Fact]
    public async Task AValidUploadCreatesAJob()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("sample.pdf", RealPdf())],
            new Dictionary<string, string> { ["preset"] = "default" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/dashboard", BrowserFlow.LocationOf(response));
        Assert.Equal(1, await JobCountAsync(account.WorkspaceId));
    }

    [Fact]
    public async Task AnAnonymousUploadIsNotAccepted()
    {
        var client = _factory.CreateSessionClient();

        // No token available anonymously either, so the form is posted without
        // one - which is what an unauthenticated attempt actually looks like.
        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("sample.pdf", RealPdf())], includeAntiforgeryToken: false);

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.BadRequest,
            $"Expected a redirect to login or a rejected request, got {response.StatusCode}");
    }

    [Fact]
    public async Task AnUploadWithoutAnAntiforgeryTokenIsRejected()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("sample.pdf", RealPdf())], includeAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    [Fact]
    public async Task AnUploadWithNoFilesIsRefusedWithoutCreatingAJob()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(client, "/upload", []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("at least one file", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    // The count limit is server-enforced (NFR-013, SR-SEC-1). A browser can be
    // told to send any number of parts, so the only limit that matters is this
    // one - and it lives in the controller, where no service test reaches it.
    [Fact]
    public async Task MoreFilesThanTheLimitAreRefusedAsABatch()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var pdf = RealPdf();
        var tooMany = Enumerable.Range(0, 26).Select(i => ($"file-{i}.pdf", pdf)).ToList();

        var response = await BrowserFlow.PostFilesAsync(client, "/upload", tooMany);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("at most", await response.Content.ReadAsStringAsync());

        // Refused wholesale: not "the first 25 were accepted".
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    // SR-SEC-1: the extension is attacker-controlled, the content is not.
    [Fact]
    public async Task AFileWhoseContentDoesNotMatchItsExtensionIsRejected()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("invoice.pdf", "MZ this is actually an executable"u8.ToArray())]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was converted", html);
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    [Fact]
    public async Task AnUnsupportedExtensionIsRejected()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("payload.exe", "MZ"u8.ToArray())]);

        Assert.Contains("Nothing was converted", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    [Fact]
    public async Task AnEmptyFileIsRejected()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(client, "/upload", [("empty.pdf", [])]);

        Assert.Contains("Nothing was converted", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    // BR-006: one bad file does not sink the batch. Worth testing over HTTP
    // because the partial-success path renders differently from both the
    // all-good and all-bad ones.
    [Fact]
    public async Task OneBadFileDoesNotPreventTheGoodOnesFromConverting()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload",
            [("good.pdf", RealPdf()), ("bad.pdf", "not a pdf at all"u8.ToArray())]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, await JobCountAsync(account.WorkspaceId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        // Exactly one document stored - the rejected one left nothing behind.
        var documents = await db.SourceDocuments
            .Where(d => d.WorkspaceId == account.WorkspaceId)
            .ToListAsync();

        Assert.Single(documents);
        Assert.Equal("good.pdf", documents[0].OriginalFileName);
    }

    // The filename arrives from the client and ends up in stored metadata and
    // in the export. A path in it must never survive as one.
    [Fact]
    public async Task ADirectoryTraversalFilenameIsSanitised()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        await BrowserFlow.PostFilesAsync(
            client, "/upload", [("../../../etc/passwd.pdf", RealPdf())]);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        // Asserted unconditionally. An earlier version of this test only
        // checked the name "if a document was stored", which would have passed
        // silently had the upload been rejected outright - proving nothing
        // about sanitisation.
        var document = await db.SourceDocuments
            .SingleAsync(d => d.WorkspaceId == account.WorkspaceId);

        Assert.Equal("passwd.pdf", document.OriginalFileName);

        // The stored key is generated, never derived from the name, so a
        // traversal cannot reach the filesystem through it either.
        Assert.DoesNotContain("..", document.StorageKey);
    }

    // SR-BIL-5 at the HTTP boundary: refused for allowance, not silently
    // billed past the plan.
    [Fact]
    public async Task AnUploadBeyondTheAllowanceIsRefusedOverHttp()
    {
        // sample.pdf is 3 pages, so 2 credits cannot cover it.
        var account = await CreateAccountAsync(includedCredits: 2);
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("sample.pdf", RealPdf())]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Nothing was converted", html);
        Assert.Contains("credit", html, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, await JobCountAsync(account.WorkspaceId));
    }

    // The form submits a preset NAME and the server resolves it. A client that
    // sends something else must not be able to steer chunking - asking for a
    // one-token chunk size would turn one document into a hundred thousand
    // chunks.
    [Fact]
    public async Task AnUnknownPresetFallsBackToAServerChosenOne()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var response = await BrowserFlow.PostFilesAsync(
            client, "/upload", [("sample.pdf", RealPdf())],
            new Dictionary<string, string> { ["preset"] = "chunkSize=1;evil" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();

        var job = await db.ConversionJobs.SingleAsync(j => j.WorkspaceId == account.WorkspaceId);

        Assert.DoesNotContain("evil", job.PresetName);

        // Positively a catalogue entry, not merely "not the attacker's string".
        Assert.Contains(
            job.PresetName,
            AI.Document.Converter.Persistence.Presets.ConversionPresets.All.Select(p => p.Name));
    }

    // The upload page must state the retention promise before anything is
    // uploaded (SR-SEC-6), and it is rendered from the same policy object the
    // worker's sweep enforces.
    [Fact]
    public async Task TheUploadPageDisclosesRetentionBeforeUpload()
    {
        var account = await CreateAccountAsync();
        var client = await SignInAsync(account);

        var html = await client.GetStringAsync("/upload");

        Assert.Contains("Retention", html);
        Assert.Contains("24 hours", html);
    }
}
