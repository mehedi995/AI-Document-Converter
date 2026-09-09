using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Services;

public sealed class DevFileEmailSenderOptions
{
    // Defaults under the OS temp directory rather than anywhere in the repo, so
    // captured mail (which contains working confirmation links) cannot be
    // committed by accident.
    public string OutputDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "adc-dev-mail");
}

// LOCAL DEVELOPMENT ONLY. Writes each message to a file instead of sending it.
//
// This adapter is registered exclusively in the Development environment
// (see Program.cs), which fails to start in any other environment unless a real
// sender is configured. That is deliberate: silently "sending" mail to a folder
// in production would mean password resets and verification links quietly going
// nowhere, and the failure would only surface when a customer could not sign in.
//
// SR-SEC-8: this must never be described as working email.
public sealed class DevFileEmailSender : IEmailSender
{
    private readonly DevFileEmailSenderOptions _options;
    private readonly ILogger<DevFileEmailSender> _logger;

    public DevFileEmailSender(
        IOptions<DevFileEmailSenderOptions> options,
        ILogger<DevFileEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.OutputDirectory);

        // Timestamp-ordered and collision-safe, so a developer can find the most
        // recent message without guessing.
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.txt";
        var path = Path.Combine(_options.OutputDirectory, fileName);

        var contents =
            $"""
             *** LOCAL DEVELOPMENT CAPTURE - THIS EMAIL WAS NOT SENT ***

             To:      {toEmail}
             Subject: {subject}
             Date:    {DateTime.UtcNow:O}

             {body}
             """;

        await File.WriteAllTextAsync(path, contents, cancellationToken);

        // The recipient address is logged because a developer needs to find the
        // right file; the body is not, because it carries a working
        // confirmation token (SR-SEC / FR-035).
        _logger.LogInformation(
            "DEV EMAIL CAPTURED (not sent) to {Recipient}: {Path}", toEmail, path);
    }
}
