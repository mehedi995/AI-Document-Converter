using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Services;

// The single place workspace access is decided (SR-SEC-2).
//
// The rule this type exists to enforce: **a client-supplied workspace id is not
// authorization.** Every entry point that accepts a workspace id must resolve
// it through here, which checks for a membership row belonging to the signed-in
// user. Nothing may read a tenant-owned row using an id that came straight off
// the request.
//
// Centralized rather than repeated so there is exactly one implementation to
// review and one to test, and so a new controller cannot invent a subtly
// weaker check of its own.
public sealed class WorkspaceAccessService
{
    private readonly ConverterDbContext _db;

    public WorkspaceAccessService(ConverterDbContext db) => _db = db;

    // Returns null when the user is not a member - deliberately the same result
    // as "no such workspace", so a caller cannot distinguish a workspace that
    // exists but belongs to someone else from one that does not exist at all.
    // Leaking that difference would turn any id into an existence oracle.
    public async Task<Workspace?> GetAuthorizedWorkspaceAsync(
        Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        return await _db.WorkspaceMemberships
            .Where(m => m.UserId == userId && m.WorkspaceId == workspaceId)
            .Select(m => m.Workspace!)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WorkspaceRole?> GetRoleAsync(
        Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        var memberships = await _db.WorkspaceMemberships
            .Where(m => m.UserId == userId && m.WorkspaceId == workspaceId)
            .Select(m => (WorkspaceRole?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return memberships;
    }

    // Every workspace the user actually belongs to. This is the only list a
    // workspace picker may ever be built from.
    public async Task<IReadOnlyList<Workspace>> GetWorkspacesForUserAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        return await _db.WorkspaceMemberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => m.Workspace!)
            .ToListAsync(cancellationToken);
    }

    // The workspace to act in when the request does not name one. Today every
    // user has exactly one; when teams ship this becomes a stored preference,
    // but it must still only ever return a workspace the user is a member of.
    public async Task<Workspace?> GetDefaultWorkspaceAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        return await _db.WorkspaceMemberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => m.Workspace!)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
