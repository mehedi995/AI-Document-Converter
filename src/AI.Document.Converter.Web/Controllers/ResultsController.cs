using System.Security.Claims;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Controllers;

[Authorize]
[Route("results")]
public sealed class ResultsController : Controller
{
    // A preview is for looking at, not for downloading. Bounding it keeps a
    // 50 MB Markdown file from freezing the browser; the full content is always
    // available through the download.
    private const int PreviewCharacterLimit = 200_000;

    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly WorkspaceAccessService _workspaceAccess;
    private readonly RetentionService _retention;
    private readonly ILogger<ResultsController> _logger;

    public ResultsController(
        ConverterDbContext db,
        IObjectStorage storage,
        WorkspaceAccessService workspaceAccess,
        RetentionService retention,
        ILogger<ResultsController> logger)
    {
        _db = db;
        _storage = storage;
        _workspaceAccess = workspaceAccess;
        _retention = retention;
        _logger = logger;
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Index(Guid jobId, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);
        if (workspace is null)
        {
            return View("~/Views/Dashboard/NoWorkspace.cshtml");
        }

        // Scoped by jobId AND WorkspaceId. Looking it up by id alone would hand
        // another tenant's job to whoever changed the number in the URL - the
        // exact case the cross-tenant tests pin down (SR-SEC-2).
        var job = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspace.Id)
            .Include(j => j.Items).ThenInclude(i => i.SourceDocument)
            .Include(j => j.Items).ThenInclude(i => i.Artifacts)
            .Include(j => j.Items).ThenInclude(i => i.Warnings)
            .SingleOrDefaultAsync(cancellationToken);

        // 404, not 403. A job that exists but belongs to someone else must be
        // indistinguishable from one that does not exist, or the URL becomes an
        // existence oracle.
        if (job is null)
        {
            return NotFound();
        }

        var items = job.Items
            .OrderBy(i => i.SourceDocument!.OriginalFileName)
            .Select(i => new ResultItemView(
                i.Id,
                i.SourceDocument!.OriginalFileName,
                i.SourceDocument.SizeBytes,
                i.Status.ToString(),
                i.ErrorMessage,
                i.Artifacts
                    .Where(a => a.BytesDeletedAtUtc == null)
                    .Select(a => new ArtifactView(a.Id, a.FileName, a.SizeBytes, a.Kind.ToString()))
                    .ToList(),
                i.Warnings
                    .OrderBy(w => w.Severity)
                    .Select(w => new WarningView(w.Code, w.Severity, w.Message, w.PageNumber, w.SheetName))
                    .ToList()))
            .ToList();

        return View(new ResultsViewModel(job.Id, job.Status.ToString(), job.PresetName, job.CreatedAtUtc, items));
    }

    // The rendered preview of one artifact.
    [HttpGet("{jobId:guid}/artifact/{artifactId:guid}/preview")]
    public async Task<IActionResult> Preview(
        Guid jobId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await ResolveAuthorizedArtifactAsync(jobId, artifactId, cancellationToken);
        if (artifact is null)
        {
            return NotFound();
        }

        await using var content = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        if (content is null)
        {
            // The row outlives the bytes by design (SR-SEC-6). Say so plainly
            // rather than showing an empty document that looks like a bad
            // conversion.
            return View("ContentExpired");
        }

        using var reader = new StreamReader(content);

        // Read one character MORE than the limit. If it arrives, the file is
        // longer than the preview and is truncated for display. Checking
        // reader.EndOfStream instead would block synchronously inside an async
        // method (CA2024), and comparing the count against the limit exactly
        // would wrongly flag a file that happens to be precisely that length.
        var buffer = new char[PreviewCharacterLimit + 1];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);

        var truncated = read > PreviewCharacterLimit;
        var preview = new string(buffer, 0, Math.Min(read, PreviewCharacterLimit));

        return View(new PreviewViewModel(
            jobId, artifact.Id, artifact.FileName, preview, truncated));
    }

    [HttpGet("{jobId:guid}/artifact/{artifactId:guid}/download")]
    public async Task<IActionResult> Download(
        Guid jobId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await ResolveAuthorizedArtifactAsync(jobId, artifactId, cancellationToken);
        if (artifact is null)
        {
            return NotFound();
        }

        var content = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        if (content is null)
        {
            return View("ContentExpired");
        }

        _logger.LogInformation(
            "Artifact {ArtifactId} downloaded from workspace {WorkspaceId}",
            artifact.Id, artifact.WorkspaceId);

        // An authenticated gate rather than a signed URL (SR-SEC-4): every
        // request is re-authorized against current membership, so revoking
        // access takes effect immediately instead of when some token expires.
        //
        // Always an attachment with an explicit content type. Letting the
        // browser sniff and render user-supplied content inline is how a stored
        // file becomes stored XSS.
        return File(content, "text/markdown; charset=utf-8", artifact.FileName);
    }

    // Customer-initiated deletion (SR-SEC-6). POST, not GET: it is destructive,
    // and CSRF validation is applied to every non-GET action.
    [HttpPost("{jobId:guid}/delete")]
    public async Task<IActionResult> Delete(Guid jobId, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);
        if (workspace is null)
        {
            return NotFound();
        }

        // Scoped by workspace inside the service too. A destructive action must
        // not be reachable by changing a number in a URL (SR-SEC-2).
        var deleted = await _retention.DeleteJobContentAsync(
            workspace.Id, jobId, DateTime.UtcNow, cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        TempData["Message"] = "The files for that conversion have been deleted.";
        return RedirectToAction("Index", "Dashboard");
    }

    // The single authorization path for artifact bytes. Both the preview and
    // the download go through it, so neither can accidentally be looser than
    // the other.
    private async Task<Artifact?> ResolveAuthorizedArtifactAsync(
        Guid jobId, Guid artifactId, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);
        if (workspace is null)
        {
            return null;
        }

        // Three conditions, all required: the artifact id, the workspace, and
        // that it really belongs to the job in the URL. Matching on id alone
        // finds another tenant's row - proven by
        // ArtifactLookupByIdAloneIsNotEnough_MustAlsoMatchWorkspace.
        return await _db.Artifacts
            .Where(a => a.Id == artifactId
                        && a.WorkspaceId == workspace.Id
                        && a.JobItem!.JobId == jobId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record ArtifactView(Guid Id, string FileName, long SizeBytes, string Kind);

public sealed record WarningView(
    string Code, string Severity, string Message, int? PageNumber, string? SheetName);

public sealed record ResultItemView(
    Guid Id,
    string FileName,
    long SizeBytes,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<ArtifactView> Artifacts,
    IReadOnlyList<WarningView> Warnings);

public sealed record ResultsViewModel(
    Guid JobId,
    string Status,
    string PresetName,
    DateTime CreatedAtUtc,
    IReadOnlyList<ResultItemView> Items);

public sealed record PreviewViewModel(
    Guid JobId, Guid ArtifactId, string FileName, string Content, bool Truncated);
