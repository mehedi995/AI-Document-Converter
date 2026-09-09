using System.Security.Claims;
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

    public UploadController(ConversionIntakeService intake, WorkspaceAccessService workspaceAccess)
    {
        _intake = intake;
        _workspaceAccess = workspaceAccess;
    }

    [HttpGet("")]
    public IActionResult Index() => View(new UploadPageModel(
        UploadValidator.MaxFileSizeBytes / (1024 * 1024), MaxFilesPerUpload));

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
            return View(new UploadPageModel(
                UploadValidator.MaxFileSizeBytes / (1024 * 1024), MaxFilesPerUpload));
        }

        if (files.Count > MaxFilesPerUpload)
        {
            ModelState.AddModelError(
                string.Empty, $"Upload at most {MaxFilesPerUpload} files at a time.");
            return View(new UploadPageModel(
                UploadValidator.MaxFileSizeBytes / (1024 * 1024), MaxFilesPerUpload));
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

            var result = await _intake.AcceptAsync(
                workspace.Id, userId, string.IsNullOrWhiteSpace(preset) ? "default" : preset,
                intakeFiles, cancellationToken);

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

public sealed record UploadPageModel(long MaxFileSizeMb, int MaxFiles);
