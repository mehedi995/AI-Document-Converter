using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AI.Document.Converter.Web.Tests;

// SaaS §13.2: "a user cannot obtain another tenant's document/job/artifact by
// changing an identifier". Two REAL users with two REAL workspaces, against a
// real database - not mocks, because the thing under test is what the query
// actually does.
[Collection(nameof(PostgresCollection))]
public sealed class CrossTenantIsolationTests
{
    private readonly PostgresFixture _fixture;

    public CrossTenantIsolationTests(PostgresFixture fixture) => _fixture = fixture;

    private sealed record Tenant(ApplicationUser User, Workspace Workspace, ConversionJob Job, Artifact Artifact);

    private static async Task<Tenant> SeedTenantAsync(ConverterDbContext db, string email)
    {
        var nowUtc = DateTime.UtcNow;

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAtUtc = nowUtc
        };
        db.Users.Add(user);

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = $"{email} workspace",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = nowUtc
        };
        db.Workspaces.Add(workspace);

        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceRole.Owner,
            JoinedAtUtc = nowUtc
        });

        var document = new SourceDocument
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            OriginalFileName = "report.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Sha256 = new string('a', 64),
            StorageKey = $"src/{Guid.NewGuid():N}",
            UploadedAtUtc = nowUtc,
            UploadedByUserId = user.Id
        };
        db.SourceDocuments.Add(document);

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            CreatedByUserId = user.Id,
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        var item = new ConversionJobItem
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            WorkspaceId = workspace.Id,
            SourceDocumentId = document.Id,
            Status = JobStatus.Completed
        };
        db.ConversionJobItems.Add(item);

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            JobItemId = item.Id,
            WorkspaceId = workspace.Id,
            Kind = ArtifactKind.Markdown,
            StorageKey = $"out/{Guid.NewGuid():N}",
            FileName = "report.md",
            SizeBytes = 256,
            CreatedAtUtc = nowUtc
        };
        db.Artifacts.Add(artifact);

        await db.SaveChangesAsync();
        return new Tenant(user, workspace, job, artifact);
    }

    [Fact]
    public async Task UserCannotResolveAnotherTenantsWorkspaceByGuessingItsId()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");
        var bob = await SeedTenantAsync(db, $"bob-{Guid.NewGuid():N}@example.test");

        var access = new WorkspaceAccessService(db);

        // Alice asks for her own: allowed.
        Assert.NotNull(await access.GetAuthorizedWorkspaceAsync(
            alice.User.Id, alice.Workspace.Id, CancellationToken.None));

        // Alice supplies Bob's workspace id - the exact "change the identifier"
        // attack. A client-supplied id is not authorization.
        Assert.Null(await access.GetAuthorizedWorkspaceAsync(
            alice.User.Id, bob.Workspace.Id, CancellationToken.None));
    }

    // A workspace that exists but belongs to someone else must be
    // indistinguishable from one that does not exist, or any id becomes an
    // existence oracle.
    [Fact]
    public async Task ForeignWorkspaceAndNonexistentWorkspaceAreIndistinguishable()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");
        var bob = await SeedTenantAsync(db, $"bob-{Guid.NewGuid():N}@example.test");

        var access = new WorkspaceAccessService(db);

        var foreign = await access.GetAuthorizedWorkspaceAsync(
            alice.User.Id, bob.Workspace.Id, CancellationToken.None);
        var nonexistent = await access.GetAuthorizedWorkspaceAsync(
            alice.User.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(foreign);
        Assert.Null(nonexistent);
    }

    [Fact]
    public async Task WorkspaceListingReturnsOnlyTheUsersOwnWorkspaces()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");
        var bob = await SeedTenantAsync(db, $"bob-{Guid.NewGuid():N}@example.test");

        var access = new WorkspaceAccessService(db);
        var aliceWorkspaces = await access.GetWorkspacesForUserAsync(alice.User.Id, CancellationToken.None);

        Assert.Equal([alice.Workspace.Id], aliceWorkspaces.Select(w => w.Id).ToArray());
        Assert.DoesNotContain(aliceWorkspaces, w => w.Id == bob.Workspace.Id);
    }

    // The dashboard query shape: scoping by the resolved workspace must exclude
    // the other tenant's jobs even though both rows live in the same table.
    [Fact]
    public async Task JobQueryScopedByWorkspaceExcludesTheOtherTenantsJobs()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");
        var bob = await SeedTenantAsync(db, $"bob-{Guid.NewGuid():N}@example.test");

        var aliceJobs = await db.ConversionJobs
            .Where(j => j.WorkspaceId == alice.Workspace.Id)
            .Select(j => j.Id)
            .ToListAsync();

        Assert.Equal([alice.Job.Id], aliceJobs);
        Assert.DoesNotContain(bob.Job.Id, aliceJobs);
    }

    // The download path. Resolving an artifact by id ALONE would hand Bob's
    // bytes to Alice; it must be scoped by workspace as well, which is why
    // WorkspaceId is denormalized onto Artifact.
    [Fact]
    public async Task ArtifactLookupByIdAloneIsNotEnough_MustAlsoMatchWorkspace()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");
        var bob = await SeedTenantAsync(db, $"bob-{Guid.NewGuid():N}@example.test");

        // What a naive implementation would do - and it finds Bob's artifact.
        var byIdOnly = await db.Artifacts.SingleOrDefaultAsync(a => a.Id == bob.Artifact.Id);
        Assert.NotNull(byIdOnly);

        // What the download path must do instead.
        var scoped = await db.Artifacts.SingleOrDefaultAsync(
            a => a.Id == bob.Artifact.Id && a.WorkspaceId == alice.Workspace.Id);
        Assert.Null(scoped);
    }

    // Deliberately inserted with raw SQL rather than through the DbSet. Adding a
    // duplicate via EF is caught by the change tracker before it ever reaches
    // PostgreSQL, so that version of this test would pass even if the table had
    // no constraint at all - it would be testing EF, not the schema. Going
    // straight to the database is what actually proves the constraint exists.
    [Fact]
    public async Task MembershipIsUniquePerUserAndWorkspace_EnforcedByTheDatabase()
    {
        await using var db = _fixture.CreateContext();
        var alice = await SeedTenantAsync(db, $"alice-{Guid.NewGuid():N}@example.test");

        var duplicateInsert = async () => await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "WorkspaceMemberships" ("WorkspaceId", "UserId", "Role", "JoinedAtUtc")
             VALUES ({alice.Workspace.Id}, {alice.User.Id}, {(int)WorkspaceRole.Member}, {DateTime.UtcNow})
             """);

        var exception = await Assert.ThrowsAsync<PostgresException>(duplicateInsert);

        // 23505 = unique_violation. Asserting the specific code rather than "any
        // exception" so a typo in the SQL cannot masquerade as the constraint
        // firing.
        Assert.Equal("23505", exception.SqlState);
    }
}
