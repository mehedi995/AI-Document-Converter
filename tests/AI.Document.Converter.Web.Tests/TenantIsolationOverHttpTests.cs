using System.Net;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Web.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Document.Converter.Web.Tests;

// SR-SEC-2 and SaaS 13.2, through the real HTTP pipeline.
//
// CrossTenantIsolationTests already proves the QUERIES are workspace-scoped.
// That is necessary and not sufficient: a controller that simply forgot to pass
// the workspace filter would pass every one of those tests while happily
// serving another tenant's artifact. This drives the actual endpoints with a
// real signed-in cookie, which is the only way to catch that.
//
// Every route that takes an id from the URL is exercised, because the threat is
// literally "change the number in the address bar".
[Collection(nameof(PostgresCollection))]
public sealed class TenantIsolationOverHttpTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private WebAppFactory _factory = null!;

    public TenantIsolationOverHttpTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(_fixture.TestConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private const string Password = "tenant-passphrase-4417";

    private sealed record Tenant(string Email, Guid WorkspaceId, Guid JobId, Guid ArtifactId, string FileName);

    // A complete, realistic tenant: a confirmed account, its workspace, a
    // finished job, and an artifact whose bytes really exist in storage. The
    // bytes matter - a download that 404s because the file is missing would
    // look like isolation working when it is not.
    private async Task<Tenant> SeedTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

        var nowUtc = DateTime.UtcNow;
        var email = $"tenant-{Guid.NewGuid():N}@example.test";

        // Unique per tenant. Giving every tenant the same filename made the
        // leak assertion below pass on the intruder's OWN document, which is
        // exactly the kind of test that looks like coverage and is not.
        var fileName = $"board-pack-{Guid.NewGuid():N}.pdf";

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
            Name = "Tenant",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = nowUtc
        });
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspaceId, UserId = user.Id, Role = WorkspaceRole.Owner, JoinedAtUtc = nowUtc
        });

        var documentId = Guid.NewGuid();
        var sourceKey = StorageKeys.ForSource(workspaceId, documentId);
        db.SourceDocuments.Add(new SourceDocument
        {
            Id = documentId,
            WorkspaceId = workspaceId,
            OriginalFileName = fileName,
            ContentType = "application/pdf",
            SizeBytes = 12,
            Sha256 = new string('b', 64),
            StorageKey = sourceKey,
            UploadedAtUtc = nowUtc,
            UploadedByUserId = user.Id
        });

        var jobId = Guid.NewGuid();
        db.ConversionJobs.Add(new ConversionJob
        {
            Id = jobId,
            WorkspaceId = workspaceId,
            CreatedByUserId = user.Id,
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        });

        var itemId = Guid.NewGuid();
        db.ConversionJobItems.Add(new ConversionJobItem
        {
            Id = itemId,
            JobId = jobId,
            WorkspaceId = workspaceId,
            SourceDocumentId = documentId,
            Status = JobStatus.Completed,
            CompletedAtUtc = nowUtc
        });

        var artifactId = Guid.NewGuid();
        var artifactKey = StorageKeys.ForArtifact(workspaceId, artifactId);

        await using (var content = new MemoryStream("# Board pack\n\nSecret."u8.ToArray()))
        {
            await storage.WriteAsync(artifactKey, content, CancellationToken.None);
        }

        db.Artifacts.Add(new Artifact
        {
            Id = artifactId,
            JobItemId = itemId,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.Markdown,
            StorageKey = artifactKey,
            FileName = Path.ChangeExtension(fileName, ".md"),
            SizeBytes = 24,
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync();

        return new Tenant(email, workspaceId, jobId, artifactId, fileName);
    }

    private async Task<HttpClient> SignInAsync(Tenant tenant)
    {
        var client = _factory.CreateSessionClient();

        var response = await BrowserFlow.PostFormAsync(
            client, "/account/login", new Dictionary<string, string>
            {
                ["Email"] = tenant.Email,
                ["Password"] = Password
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    // The control. Without this, every "the intruder got 404" assertion below
    // would also pass if the URLs were simply wrong.
    [Fact]
    public async Task TheOwnerCanReachTheirOwnResultsAndDownload()
    {
        var owner = await SeedTenantAsync();
        var client = await SignInAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/results/{owner.JobId}")).StatusCode);

        var download = await client.GetAsync(
            $"/results/{owner.JobId}/artifact/{owner.ArtifactId}/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("Secret.", await download.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/results/{job}")]
    [InlineData("/results/{job}/artifact/{artifact}/preview")]
    [InlineData("/results/{job}/artifact/{artifact}/download")]
    [InlineData("/results/{job}/export")]
    public async Task AnotherTenantCannotReadAJobByPuttingItsIdInTheUrl(string template)
    {
        var owner = await SeedTenantAsync();
        var intruder = await SeedTenantAsync();

        var client = await SignInAsync(intruder);

        var path = template
            .Replace("{job}", owner.JobId.ToString())
            .Replace("{artifact}", owner.ArtifactId.ToString());

        var response = await client.GetAsync(path);

        // Not found rather than forbidden: telling an intruder that a job
        // exists but is not theirs confirms the id is real.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Reading is the obvious threat; writing is the damaging one. A cancel or
    // delete that skipped the workspace check would let anyone destroy another
    // tenant's work.
    [Theory]
    [InlineData("/results/{job}/cancel")]
    [InlineData("/results/{job}/retry")]
    [InlineData("/results/{job}/delete")]
    [InlineData("/history/{job}/reconvert")]
    public async Task AnotherTenantCannotActOnAJobByPuttingItsIdInTheUrl(string template)
    {
        var owner = await SeedTenantAsync();
        var intruder = await SeedTenantAsync();

        var client = await SignInAsync(intruder);

        var path = template.Replace("{job}", owner.JobId.ToString());

        // The token is scraped from the intruder's OWN dashboard, so this is a
        // genuine authenticated request with a valid anti-forgery token - the
        // refusal has to come from the ownership check, not from CSRF.
        var token = await BrowserFlow.GetAntiforgeryTokenAsync(client, "/dashboard");

        var response = await client.PostAsync(path, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And the job is untouched: a 404 that still performed the action would
        // be the worst of both.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConverterDbContext>();
        var job = db.ConversionJobs.Single(j => j.Id == owner.JobId);

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.CancelledAtUtc);
        Assert.Null(job.DeletedAtUtc);
    }

    // The artifact belongs to the owner's job. Pairing it with a job the
    // intruder legitimately owns must not launder access to it.
    [Fact]
    public async Task AnArtifactCannotBeReachedThroughAJobTheCallerDoesOwn()
    {
        var owner = await SeedTenantAsync();
        var intruder = await SeedTenantAsync();

        var client = await SignInAsync(intruder);

        var response = await client.GetAsync(
            $"/results/{intruder.JobId}/artifact/{owner.ArtifactId}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A foreign job and a made-up one must be indistinguishable, or the
    // response itself becomes an oracle for which ids exist.
    [Fact]
    public async Task AForeignJobAndANonexistentJobLookTheSame()
    {
        var owner = await SeedTenantAsync();
        var intruder = await SeedTenantAsync();

        var client = await SignInAsync(intruder);

        var foreign = await client.GetAsync($"/results/{owner.JobId}");
        var imaginary = await client.GetAsync($"/results/{Guid.NewGuid()}");

        Assert.Equal(imaginary.StatusCode, foreign.StatusCode);
    }

    [Fact]
    public async Task HistoryShowsOnlyTheCallersOwnWork()
    {
        var owner = await SeedTenantAsync();
        var intruder = await SeedTenantAsync();

        var client = await SignInAsync(intruder);

        var html = await client.GetStringAsync("/history");

        Assert.DoesNotContain(owner.JobId.ToString(), html);
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(owner.FileName), html);
    }
}
