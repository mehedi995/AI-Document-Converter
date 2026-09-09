using System.Security.Claims;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Presets;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Document.Converter.Web.Controllers;

[Authorize]
[Route("upload")]
public sealed class UploadController : Controller
{
    // Server-enforced. A browser can be told to send any number of files; the
    // limit that matters is the one applied here (NFR-013, SR-SEC-1).
    private const int MaxFilesPerUpload = 25;

    private readonly ConversionIntakeService _intake;
    private readonly WorkspaceAccessService _workspaceAccess;
    private readonly MeteringService _metering;
    private readonly RetentionPolicy _retentionPolicy;

    public UploadController(
        ConversionIntakeService intake,
        WorkspaceAccessService workspaceAccess,
        MeteringService metering,
        Microsoft.Extensions.Options.IOptions<RetentionPolicy> retentionPolicy)
    {
        _intake = intake;
        _workspaceAccess = workspaceAccess;
        _metering = metering;
        _retentionPolicy = retentionPolicy.Value;
    }

    // Built from the SAME policy object the worker's sweep enforces, so the
    // page cannot promise one thing while the sweep does another (SR-SEC-6).
    private async Task<UploadPageModel> BuildPageModelAsync(CancellationToken cancellationToken)
    {
        // Shown BEFORE the upload, so a customer near their limit finds out
        // here rather than after waiting for a large file to transfer. This is
        // the honest limit of a preflight: the true cost of a document cannot
        // be known until its bytes arrive, so what can be offered in advance is
        // the balance and the rule, not a per-file quote.
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);

        var period = workspace is null
            ? null
            : await _metering.FindCurrentPeriodAsync(workspace.Id, DateTime.UtcNow, cancellationToken);

        return new UploadPageModel(
            UploadValidator.MaxFileSizeBytes / (1024 * 1024),
            MaxFilesPerUpload,
            _retentionPolicy.DescribeForCustomer(),
            ConversionPresets.All.ToList(),
            period?.AvailableCredits,
            period?.IncludedCredits,
            ConversionCredits.Describe());
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await BuildPageModelAsync(cancellationToken));

    [HttpPost("")]
    [RequestSizeLimit(UploadValidator.MaxFileSizeBytes * MaxFilesPerUpload)]
    public async Task<IActionResult> Index(
        List<IFormFile> files, string? preset, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        // SR-SEC-2: the workspace comes from the signed-in user's membership,
        // never from the form. A hidden field naming a workspace would be
        // exactly the "client-supplied TenantId is authorization" mistake.
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(userId, cancellationToken);
        if (workspace is null)
        {
            return View("~/Views/Dashboard/NoWorkspace.cshtml");
        }

        if (files.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose at least one file to convert.");
            return View(await BuildPageModelAsync(cancellationToken));
        }

        if (files.Count > MaxFilesPerUpload)
        {
            ModelState.AddModelError(
                string.Empty, $"Upload at most {MaxFilesPerUpload} files at a time.");
            return View(await BuildPageModelAsync(cancellationToken));
        }

        // Buffered to a seekable stream because validation reads the header,
        // inspects the container, then hashes, then stores - four passes over
        // the same bytes. IFormFile's stream is seekable when buffered by the
        // framework, which it is at these sizes.
        var intakeFiles = new List<IntakeFile>(files.Count);
        var openedStreams = new List<Stream>(files.Count);

        try
        {
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                openedStreams.Add(stream);

                if (!stream.CanSeek)
                {
                    var buffered = new MemoryStream();
                    await stream.CopyToAsync(buffered, cancellationToken);
                    buffered.Position = 0;
                    openedStreams.Add(buffered);
                    intakeFiles.Add(new IntakeFile(
                        file.FileName, file.ContentType ?? "application/octet-stream",
                        buffered.Length, buffered));
                }
                else
                {
                    intakeFiles.Add(new IntakeFile(
                        file.FileName, file.ContentType ?? "application/octet-stream",
                        file.Length, stream));
                }
            }

            // Resolved server-side. The form submits a NAME; chunk sizes are
            // never accepted from the client, or anyone could ask for a
            // one-token chunk size and turn one document into a hundred
            // thousand chunks.
            var resolvedPreset = ConversionPresets.Resolve(preset);

            var result = await _intake.AcceptAsync(
                workspace.Id, userId, resolvedPreset.Name, intakeFiles, cancellationToken);

            TempData["IntakeSummary"] = System.Text.Json.JsonSerializer.Serialize(result.Files);

            return result.JobId is null
                ? View("NothingAccepted", result)
                : RedirectToAction("Index", "Dashboard");
        }
        finally
        {
            foreach (var stream in openedStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record UploadPageModel(
    long MaxFileSizeMb,
    int MaxFiles,
    string RetentionNotice,
    IReadOnlyList<ConversionPreset> Presets,
    // Null when nothing has been metered yet. Rendered as the plan's allowance
    // rather than as zero, which would read as "you have run out".
    long? AvailableCredits,
    long? IncludedCredits,
    string CreditRule);
