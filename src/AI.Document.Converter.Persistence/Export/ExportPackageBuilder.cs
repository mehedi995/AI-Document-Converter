using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Persistence.Export;

// FR-027/028/046. Builds the download package for one job.
//
// Built on demand and streamed rather than stored as another artifact: a stored
// package would duplicate every byte, need its own retention window, and become
// stale the moment a failed file is retried. Rebuilding is cheap because the
// pieces already exist.
//
// The MANIFEST is the point of this class, not the zipping. SaaS §6 requires
// that omissions, missing values and extraction gaps are visible in the export
// manifest - not only on a web page the customer may never revisit. A package
// that contains only the Markdown would let an incomplete extraction travel
// onward looking complete.
public sealed class ExportPackageBuilder
{
    private const string ManifestVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly RetentionPolicy _retentionPolicy;
    private readonly ILogger<ExportPackageBuilder> _logger;

    public ExportPackageBuilder(
        ConverterDbContext db,
        IObjectStorage storage,
        IOptions<RetentionPolicy> retentionPolicy,
        ILogger<ExportPackageBuilder> logger)
    {
        _db = db;
        _storage = storage;
        _retentionPolicy = retentionPolicy.Value;
        _logger = logger;
    }

    // Returns false when the job does not exist in this workspace. Scoped by
    // workspace, like every other read of tenant data (SR-SEC-2).
    public async Task<bool> TryWritePackageAsync(
        Guid workspaceId, Guid jobId, Stream destination, CancellationToken cancellationToken)
    {
        var job = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            .Include(j => j.Items).ThenInclude(i => i.SourceDocument)
            .Include(j => j.Items).ThenInclude(i => i.Artifacts)
            .Include(j => j.Items).ThenInclude(i => i.Warnings)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null || job.DeletedAtUtc is not null)
        {
            return false;
        }

        // leaveOpen so the caller controls the response stream's lifetime.
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileEntries = new List<ManifestFile>();

        foreach (var item in job.Items.OrderBy(i => i.SourceDocument!.OriginalFileName))
        {
            var document = item.SourceDocument!;
            var baseName = MakeCollisionSafeName(document.OriginalFileName, usedNames);

            var artifactEntries = new List<ManifestArtifact>();

            foreach (var artifact in item.Artifacts)
            {
                // An artifact whose bytes have been purged is reported in the
                // manifest but has no entry in the ZIP. Silently omitting it
                // would make the package look like it never existed.
                if (artifact.BytesDeletedAtUtc is not null)
                {
                    artifactEntries.Add(new ManifestArtifact(
                        null, artifact.Kind.ToString(), artifact.SizeBytes,
                        "Content was deleted under the retention policy and is not in this package."));
                    continue;
                }

                await using var content = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
                if (content is null)
                {
                    artifactEntries.Add(new ManifestArtifact(
                        null, artifact.Kind.ToString(), artifact.SizeBytes,
                        "Content was not available when this package was built."));
                    continue;
                }

                var entryPath = $"markdown/{baseName}.md";
                var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                {
                    await content.CopyToAsync(entryStream, cancellationToken);
                }

                artifactEntries.Add(new ManifestArtifact(
                    entryPath, artifact.Kind.ToString(), artifact.SizeBytes, null));
            }

            var warnings = item.Warnings
                .Select(w => new ManifestWarning(
                    w.Code, w.Severity, w.Message, w.PageNumber, w.SlideNumber, w.SheetName,
                    w.DetailsJson is null
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(w.DetailsJson)))
                .ToList();

            var manifestFile = new ManifestFile(
                document.OriginalFileName,
                document.Sha256,
                document.SizeBytes,
                item.Status.ToString(),
                item.ErrorCategory,
                item.ErrorMessage,
                artifactEntries,
                warnings);

            fileEntries.Add(manifestFile);

            // Per-file metadata alongside the manifest (FR-046), so a consumer
            // processing one document at a time does not have to parse the
            // whole manifest to find its provenance.
            await WriteJsonEntryAsync(
                archive, $"metadata/{baseName}.json", manifestFile, cancellationToken);
        }

        var manifest = BuildManifest(job, fileEntries);
        await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken);
        await WriteReadmeAsync(archive, manifest, cancellationToken);

        _logger.LogInformation(
            "Built export package for job {JobId} with {FileCount} file(s)", jobId, fileEntries.Count);

        return true;
    }

    private ExportManifest BuildManifest(ConversionJob job, List<ManifestFile> files)
    {
        var counts = new ManifestSummary(
            files.Count,
            files.Count(f => f.Status == nameof(JobStatus.Completed)),
            files.Count(f => f.Status == nameof(JobStatus.CompletedWithWarnings)),
            files.Count(f => f.Status == nameof(JobStatus.Failed)),
            files.Count(f => f.Status == nameof(JobStatus.Cancelled)));

        var notes = new List<string>();

        // The manifest states limitations explicitly. A downstream RAG pipeline
        // reads this file, not our web UI, and it must not have to infer
        // completeness from the absence of a warning.
        if (counts.CompletedWithWarnings > 0)
        {
            notes.Add(
                "One or more files completed with warnings. Their output is NOT a complete "
                + "extraction of the source document - see each file's warnings.");
        }

        if (counts.Failed > 0)
        {
            notes.Add("One or more files failed and produced no output.");
        }

        notes.Add(
            "Warnings describe content this pipeline knows it did not recover. Their absence is "
            + "not a guarantee that the parser recovered every fact from the source.");

        notes.Add(_retentionPolicy.DescribeForCustomer());

        return new ExportManifest(
            ManifestVersion,
            // The job id is the immutable run identifier. A retry changes item
            // state but never the run it belongs to, so two packages exported
            // from the same job are always attributable to the same run.
            job.Id,
            job.PresetName,
            job.Status.ToString(),
            job.CreatedAtUtc,
            DateTime.UtcNow,
            counts,
            files,
            notes);
    }

    // Two source files that reduce to the same base name must not both become
    // report.md and silently overwrite each other inside the archive (FR-036).
    //
    // On collision the SOURCE EXTENSION is used to disambiguate before falling
    // back to a counter. A batch containing report.pdf and report.docx produces
    // "report.md" and "report-docx.md", not "report.md" and "report (2).md":
    // both are collision-safe, but only one tells the person who unzipped it
    // which file they are looking at without opening it. The counter remains
    // for the genuine case of two identically named sources.
    private static string MakeCollisionSafeName(string originalFileName, HashSet<string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(originalFileName);
        var candidate = string.IsNullOrWhiteSpace(stem) ? "unnamed" : stem;

        if (used.Add(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(extension))
        {
            var byExtension = $"{candidate}-{extension}";
            if (used.Add(byExtension))
            {
                return byExtension;
            }
        }

        for (var suffix = 2; ; suffix++)
        {
            var suffixed = $"{candidate} ({suffix})";
            if (used.Add(suffixed))
            {
                return suffixed;
            }
        }
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive, string path, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static async Task WriteReadmeAsync(
        ZipArchive archive, ExportManifest manifest, CancellationToken cancellationToken)
    {
        var readme = new StringBuilder()
            .AppendLine("AI Document Converter export")
            .AppendLine("============================")
            .AppendLine()
            .AppendLine($"Run:       {manifest.RunId}")
            .AppendLine($"Preset:    {manifest.Preset}")
            .AppendLine($"Exported:  {manifest.GeneratedAtUtc:u}")
            .AppendLine($"Status:    {manifest.Status}")
            .AppendLine()
            .AppendLine("Contents")
            .AppendLine("  markdown/   converted output, one file per source document")
            .AppendLine("  metadata/   per-document provenance and warnings")
            .AppendLine("  manifest.json   the authoritative record of this export")
            .AppendLine()
            .AppendLine("Before using this output")
            .AppendLine();

        foreach (var note in manifest.Notes)
        {
            readme.AppendLine($"  - {note}");
        }

        var entry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(readme.ToString()), cancellationToken);
    }
}

public sealed record ManifestArtifact(string? Path, string Kind, long SizeBytes, string? Unavailable);

public sealed record ManifestWarning(
    string Code,
    string Severity,
    string Message,
    int? PageNumber,
    int? SlideNumber,
    string? SheetName,
    Dictionary<string, string>? Details);

public sealed record ManifestFile(
    string SourceFileName,
    string SourceSha256,
    long SourceSizeBytes,
    string Status,
    string? ErrorCategory,
    string? ErrorMessage,
    IReadOnlyList<ManifestArtifact> Artifacts,
    IReadOnlyList<ManifestWarning> Warnings);

public sealed record ManifestSummary(
    int Total, int Completed, int CompletedWithWarnings, int Failed, int Cancelled);

public sealed record ExportManifest(
    string ManifestVersion,
    Guid RunId,
    string Preset,
    string Status,
    DateTime RunCreatedAtUtc,
    DateTime GeneratedAtUtc,
    ManifestSummary Summary,
    IReadOnlyList<ManifestFile> Files,
    IReadOnlyList<string> Notes);
