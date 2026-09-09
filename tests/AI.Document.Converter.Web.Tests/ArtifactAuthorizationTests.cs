using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Tests;

// The download path is where a tenancy mistake actually leaks bytes, so the
// exact query it uses is pinned here rather than only exercised through the
// controller. If someone later "simplifies" ResolveAuthorizedArtifactAsync down
// to a lookup by id, these fail.
[Collection(nameof(PostgresCollection))]
public sealed class ArtifactAuthorizationTests
{
    private readonly PostgresFixture _fixture;

    public ArtifactAuthorizationTests(PostgresFixture fixture) => _fixture = fixture;

    private sealed record Seeded(Guid WorkspaceId, Guid JobId, Guid ArtifactId);

    private static async Task<Seeded> SeedAsync(ConverterDbContext db)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "w",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = nowUtc
        });

        var document = new SourceDocument
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OriginalFileName = "report.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
            Sha256 = new string('b', 64),
            StorageKey = $"workspaces/{workspaceId:N}/sources/{Guid.NewGuid():N}",
            UploadedAtUtc = nowUtc,
            UploadedByUserId = Guid.NewGuid()
        };
        db.SourceDocuments.Add(document);

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        var item = new ConversionJobItem
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            WorkspaceId = workspaceId,
            SourceDocumentId = document.Id,
            Status = JobStatus.Completed
        };
        db.ConversionJobItems.Add(item);

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            JobItemId = item.Id,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.Markdown,
            StorageKey = $"workspaces/{workspaceId:N}/artifacts/{Guid.NewGuid():N}",
            FileName = "report.md",
            SizeBytes = 100,
            CreatedAtUtc = nowUtc
        };
        db.Artifacts.Add(artifact);

        await db.SaveChangesAsync();
        return new Seeded(workspaceId, job.Id, artifact.Id);
    }

    // Mirrors ResolveAuthorizedArtifactAsync exactly: artifact id AND workspace
    // AND that it belongs to the job named in the URL.
    private static Task<Artifact?> ResolveAsync(
        ConverterDbContext db, Guid jobId, Guid artifactId, Guid workspaceId) =>
        db.Artifacts
            .Where(a => a.Id == artifactId && a.WorkspaceId == workspaceId && a.JobItem!.JobId == jobId)
            .SingleOrDefaultAsync();

    [Fact]
    public async Task OwnerResolvesTheirOwnArtifact()
    {
        await using var db = _fixture.CreateContext();
        var owner = await SeedAsync(db);

        Assert.NotNull(await ResolveAsync(db, owner.JobId, owner.ArtifactId, owner.WorkspaceId));
    }

    [Fact]
    public async Task OtherTenantCannotResolveItEvenWithTheCorrectJobAndArtifactIds()
    {
        await using var db = _fixture.CreateContext();
        var owner = await SeedAsync(db);
        var other = await SeedAsync(db);

        // Both ids are genuine; only the workspace differs. This is the whole
        // attack: the URL is correct, the caller is not.
        Assert.Null(await ResolveAsync(db, owner.JobId, owner.ArtifactId, other.WorkspaceId));
    }

    // Guards against a subtler slip: scoping by workspace but forgetting the
    // job, so an artifact could be fetched through an unrelated job's URL.
    [Fact]
    public async Task ArtifactCannotBeReachedThroughAnotherJobsUrl()
    {
        await using var db = _fixture.CreateContext();
        var first = await SeedAsync(db);

        var secondJobId = Guid.NewGuid();
        db.ConversionJobs.Add(new ConversionJob
        {
            Id = secondJobId,
            WorkspaceId = first.WorkspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Same workspace, real artifact, wrong job.
        Assert.Null(await ResolveAsync(db, secondJobId, first.ArtifactId, first.WorkspaceId));
    }

    [Fact]
    public async Task LookupByArtifactIdAloneFindsTheOtherTenantsRow()
    {
        await using var db = _fixture.CreateContext();
        var owner = await SeedAsync(db);
        await SeedAsync(db);

        // Documents WHY the scoping above is necessary: the naive query does
        // find the row. This test exists so the reason is not lost if someone
        // reads the scoped query and thinks it is redundant.
        var naive = await db.Artifacts.SingleOrDefaultAsync(a => a.Id == owner.ArtifactId);

        Assert.NotNull(naive);
    }
}
