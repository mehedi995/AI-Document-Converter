using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Export;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// FR-027/028/046. The manifest is the part that matters: SaaS §6 requires that
// omissions and extraction gaps are visible in the export, not only on a web
// page the customer may never revisit.
[Collection(nameof(PostgresCollection))]
public sealed class ExportPackageTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public ExportPackageTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-export-test-{Guid.NewGuid():N}");
        _storage = new LocalFileSystemObjectStorage(
            Options.Create(new LocalFileSystemObjectStorageOptions { RootDirectory = _storageRoot }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private ExportPackageBuilder Builder(ConverterDbContext db) =>
        new(db, _storage, Options.Create(new RetentionPolicy()),
            NullLogger<ExportPackageBuilder>.Instance);

    private sealed record ItemSpec(
        string FileName, JobStatus Status, bool WithArtifact, bool WithErrorWarning,
        bool PurgeBytes = false, int ChunkCount = 0);

    private async Task<(Guid WorkspaceId, Guid JobId)> SeedAsync(
        ConverterDbContext db, JobStatus jobStatus, params ItemSpec[] specs)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = jobStatus,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        foreach (var spec in specs)
        {
            var documentId = Guid.NewGuid();
            db.SourceDocuments.Add(new SourceDocument
            {
                Id = documentId,
                WorkspaceId = workspaceId,
                OriginalFileName = spec.FileName,
                ContentType = "application/octet-stream",
                SizeBytes = 100,
                Sha256 = new string('e', 64),
                StorageKey = StorageKeys.ForSource(workspaceId, documentId),
                UploadedAtUtc = nowUtc,
                UploadedByUserId = Guid.NewGuid()
            });

            var itemId = Guid.NewGuid();
            db.ConversionJobItems.Add(new ConversionJobItem
            {
                Id = itemId,
                JobId = job.Id,
                WorkspaceId = workspaceId,
                SourceDocumentId = documentId,
                Status = spec.Status,
                ErrorCategory = spec.Status == JobStatus.Failed ? "corruptedDocument" : null,
                ErrorMessage = spec.Status == JobStatus.Failed ? "could not be opened" : null
            });

            if (spec.WithArtifact)
            {
                var artifactId = Guid.NewGuid();
                var key = StorageKeys.ForArtifact(workspaceId, artifactId);

                await using (var content = new MemoryStream(
                    Encoding.UTF8.GetBytes($"# converted {spec.FileName}")))
                {
                    await _storage.WriteAsync(key, content, CancellationToken.None);
                }

                db.Artifacts.Add(new Artifact
                {
                    Id = artifactId,
                    JobItemId = itemId,
                    WorkspaceId = workspaceId,
                    Kind = ArtifactKind.Markdown,
                    StorageKey = key,
                    FileName = Path.GetFileNameWithoutExtension(spec.FileName) + ".md",
                    SizeBytes = 20,
                    CreatedAtUtc = nowUtc,
                    BytesDeletedAtUtc = spec.PurgeBytes ? nowUtc : null
                });
            }

            if (spec.ChunkCount > 0)
            {
                var chunkArtifactId = Guid.NewGuid();
                var chunkKey = StorageKeys.ForArtifact(workspaceId, chunkArtifactId);

                var chunkSet = new
                {
                    sourceFileName = spec.FileName,
                    chunkCount = spec.ChunkCount,
                    chunks = Enumerable.Range(1, spec.ChunkCount).Select(i => new
                    {
                        sequenceNumber = i,
                        sourceFileName = spec.FileName,
                        tokenCount = 100,
                        overlapTokens = 0,
                        content = $"# heading {i}\n\nchunk body {i}\n"
                    })
                };

                await using (var content = new MemoryStream(
                    JsonSerializer.SerializeToUtf8Bytes(chunkSet)))
                {
                    await _storage.WriteAsync(chunkKey, content, CancellationToken.None);
                }

                db.Artifacts.Add(new Artifact
                {
                    Id = chunkArtifactId,
                    JobItemId = itemId,
                    WorkspaceId = workspaceId,
                    Kind = ArtifactKind.ChunkSet,
                    StorageKey = chunkKey,
                    FileName = Path.GetFileNameWithoutExtension(spec.FileName) + ".chunks.json",
                    SizeBytes = 200,
                    CreatedAtUtc = nowUtc
                });
            }

            if (spec.WithErrorWarning)
            {
                db.JobItemWarnings.Add(new JobItemWarning
                {
                    Id = Guid.NewGuid(),
                    JobItemId = itemId,
                    WorkspaceId = workspaceId,
                    Code = "noExtractableText",
                    Severity = "error",
                    Message = "No text could be extracted from 1 of 3 pages.",
                    PageNumber = 3,
                    DetailsJson = """{"totalPages":"3"}"""
                });
            }
        }

        await db.SaveChangesAsync();
        return (workspaceId, job.Id);
    }

    private async Task<ZipArchive> BuildAsync(ConverterDbContext db, Guid workspaceId, Guid jobId)
    {
        var buffer = new MemoryStream();
        var written = await Builder(db).TryWritePackageAsync(
            workspaceId, jobId, buffer, CancellationToken.None);

        Assert.True(written);
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static ExportManifest ReadManifest(ZipArchive archive)
    {
        using var stream = archive.GetEntry("manifest.json")!.Open();
        return JsonSerializer.Deserialize<ExportManifest>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task PackageContainsMarkdownMetadataManifestAndReadme()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed, new ItemSpec("report.pdf", JobStatus.Completed, true, false));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("markdown/report.md", names);
        Assert.Contains("metadata/report.json", names);
        Assert.Contains("manifest.json", names);
        Assert.Contains("README.txt", names);
    }

    // The requirement this whole class exists for: an incomplete extraction
    // must not be able to travel onward looking complete.
    [Fact]
    public async Task ManifestCarriesWarningsSoIncompleteOutputCannotLookComplete()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.CompletedWithWarnings,
            new ItemSpec("scanned.pdf", JobStatus.CompletedWithWarnings, true, WithErrorWarning: true));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var manifest = ReadManifest(archive);

        var file = Assert.Single(manifest.Files);
        var warning = Assert.Single(file.Warnings);

        Assert.Equal("noExtractableText", warning.Code);
        Assert.Equal("error", warning.Severity);
        Assert.Equal(3, warning.PageNumber);
        Assert.Equal("3", warning.Details!["totalPages"]);

        // And stated in prose too, for a human opening the package.
        Assert.Contains(manifest.Notes, n => n.Contains("NOT a complete extraction"));
    }

    // The absence of a warning must never be sold as proof of completeness.
    [Fact]
    public async Task ManifestAlwaysStatesThatNoWarningsIsNotAGuarantee()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed, new ItemSpec("clean.pdf", JobStatus.Completed, true, false));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var manifest = ReadManifest(archive);

        Assert.Empty(manifest.Files.SelectMany(f => f.Warnings));
        Assert.Contains(manifest.Notes, n => n.Contains("not a guarantee"));
    }

    // FR-036. Two sources reducing to the same base name must not overwrite
    // each other, and the disambiguated name should say which is which.
    [Fact]
    public async Task CollidingNamesAreDisambiguatedByTheSourceExtension()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed,
            new ItemSpec("report.docx", JobStatus.Completed, true, false),
            new ItemSpec("report.pdf", JobStatus.Completed, true, false));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        // BOTH are suffixed, not just the second. Giving the
        // alphabetically-first file the bare name reads as arbitrary.
        Assert.Contains("markdown/report-docx.md", names);
        Assert.Contains("markdown/report-pdf.md", names);
        Assert.DoesNotContain("markdown/report.md", names);

        // Whatever the names, the manifest must resolve each output to its
        // source unambiguously.
        var manifest = ReadManifest(archive);
        var pdf = manifest.Files.Single(f => f.SourceFileName == "report.pdf");
        Assert.Equal("markdown/report-pdf.md", pdf.Artifacts.Single().Path);
    }

    [Fact]
    public async Task FailedFilesAppearInTheManifestWithTheirReason()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.CompletedWithWarnings,
            new ItemSpec("good.pdf", JobStatus.Completed, true, false),
            new ItemSpec("broken.pdf", JobStatus.Failed, false, false));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var manifest = ReadManifest(archive);

        Assert.Equal(2, manifest.Summary.Total);
        Assert.Equal(1, manifest.Summary.Failed);

        var failed = manifest.Files.Single(f => f.SourceFileName == "broken.pdf");
        Assert.Equal("corruptedDocument", failed.ErrorCategory);
        Assert.Empty(failed.Artifacts);
        Assert.Contains(manifest.Notes, n => n.Contains("failed and produced no output"));
    }

    // A purged artifact is reported rather than silently absent - otherwise the
    // package looks like the output never existed.
    [Fact]
    public async Task PurgedContentIsReportedRatherThanSilentlyMissing()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Expired,
            new ItemSpec("old.pdf", JobStatus.Completed, true, false, PurgeBytes: true));

        using var archive = await BuildAsync(db, workspaceId, jobId);

        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("markdown/"));

        var manifest = ReadManifest(archive);
        var artifact = manifest.Files.Single().Artifacts.Single();
        Assert.Null(artifact.Path);
        Assert.Contains("retention", artifact.Unavailable);
    }

    [Fact]
    public async Task TheRunIdIsTheJobIdSoTwoExportsOfOneRunAgree()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed, new ItemSpec("a.pdf", JobStatus.Completed, true, false));

        using var first = await BuildAsync(db, workspaceId, jobId);
        using var second = await BuildAsync(db, workspaceId, jobId);

        Assert.Equal(jobId, ReadManifest(first).RunId);
        Assert.Equal(jobId, ReadManifest(second).RunId);
    }

    [Fact]
    public async Task AnotherTenantCannotExportThisWorkspacesJob()
    {
        await using var db = _fixture.CreateContext();
        var (_, jobId) = await SeedAsync(
            db, JobStatus.Completed, new ItemSpec("a.pdf", JobStatus.Completed, true, false));

        var buffer = new MemoryStream();
        var written = await Builder(db).TryWritePackageAsync(
            Guid.NewGuid(), jobId, buffer, CancellationToken.None);

        Assert.False(written);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public async Task ADeletedJobCannotBeExported()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed, new ItemSpec("a.pdf", JobStatus.Completed, true, false));

        var job = db.ConversionJobs.Single(j => j.Id == jobId);
        job.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var buffer = new MemoryStream();
        Assert.False(await Builder(db).TryWritePackageAsync(
            workspaceId, jobId, buffer, CancellationToken.None));
    }

    // FR-028: the desktop produces chunks/<name>/chunk_NNN.md, and a RAG
    // ingestion script expects to iterate over files rather than parse a blob.
    // The chunk set is stored as one JSON object and expanded here.
    [Fact]
    public async Task ChunkSetsAreExpandedIntoIndividualChunkFiles()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed,
            new ItemSpec("report.pdf", JobStatus.Completed, true, false, ChunkCount: 3));

        using var archive = await BuildAsync(db, workspaceId, jobId);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("chunks/report/chunk_001.md", names);
        Assert.Contains("chunks/report/chunk_002.md", names);
        Assert.Contains("chunks/report/chunk_003.md", names);

        var manifest = ReadManifest(archive);
        var chunkArtifact = manifest.Files.Single().Artifacts.Single(a => a.Kind == "ChunkSet");
        Assert.Equal(3, chunkArtifact.ChunkFiles!.Count);
    }

    // chunk_10 must not sort before chunk_2. Zero padding means a plain
    // alphabetical listing is also the correct reading order, which is how most
    // ingestion scripts will consume the folder.
    [Fact]
    public async Task ChunkFileNamesAreZeroPaddedSoAlphabeticalOrderIsReadingOrder()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId) = await SeedAsync(
            db, JobStatus.Completed,
            new ItemSpec("big.pdf", JobStatus.Completed, true, false, ChunkCount: 12));

        using var archive = await BuildAsync(db, workspaceId, jobId);

        var chunkNames = archive.Entries
            .Select(e => e.FullName)
            .Where(n => n.StartsWith("chunks/"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal("chunks/big/chunk_001.md", chunkNames.First());
        Assert.Equal("chunks/big/chunk_012.md", chunkNames.Last());
    }
}
