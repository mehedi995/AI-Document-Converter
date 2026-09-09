namespace AI.Document.Converter.Web.Services;

// SaaS §5.2: registration needs email verification, and production mail is not
// available in this environment. The boundary exists so the local capture
// adapter can be swapped for a real provider without touching the flows that
// depend on it.
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}
