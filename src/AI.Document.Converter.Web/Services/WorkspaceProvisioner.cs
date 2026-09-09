using System.Text;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Services;

// SaaS §5.3: every customer is a tenant from day one, before team invitations
// exist. Creating the personal workspace at registration - rather than
// retrofitting tenancy when teams ship - is what lets every query be
// workspace-scoped from the first line of code.
public sealed class WorkspaceProvisioner
{
    private readonly ConverterDbContext _db;

    public WorkspaceProvisioner(ConverterDbContext db) => _db = db;

    // Creates the workspace and the Owner membership together. The caller is
    // responsible for saving inside the same transaction as the user creation,
    // so a user can never exist without a workspace - an account with no tenant
    // would be unable to do anything and would have to be repaired by hand.
    public Workspace AddPersonalWorkspace(ApplicationUser user, string displayName, DateTime nowUtc)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = displayName,
            Slug = GenerateSlug(),
            CreatedAtUtc = nowUtc
        };

        _db.Workspaces.Add(workspace);
        _db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = WorkspaceRole.Owner,
            JoinedAtUtc = nowUtc
        });

        return workspace;
    }

    // Random rather than derived from the user's name or email. A slug derived
    // from an email address would leak who owns the workspace to anyone who can
    // see a URL, and would need renaming when the display name changes.
    private static string GenerateSlug()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyz23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var builder = new StringBuilder("ws-", 15);

        foreach (var b in bytes)
        {
            builder.Append(alphabet[b % alphabet.Length]);
        }

        return builder.ToString();
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        _db.Workspaces.AnyAsync(w => w.Slug == slug, cancellationToken);
}
