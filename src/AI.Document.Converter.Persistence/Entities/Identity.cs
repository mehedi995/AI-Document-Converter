using Microsoft.AspNetCore.Identity;

namespace AI.Document.Converter.Persistence.Entities;

// Guid keys rather than the default string: job messages and storage keys carry
// these identifiers around, and a Guid is a fixed-width, non-guessable value
// that cannot be confused with a display name.
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required DateTime CreatedAtUtc { get; init; }

    public ICollection<WorkspaceMembership> Memberships { get; set; } = [];
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
}

// SaaS §5.3: every customer is a tenant from day one, even before team
// invitations exist. Creating the personal workspace at registration - rather
// than retrofitting tenancy when teams ship - is what keeps every query
// tenant-scoped from the first line of code.
public sealed class Workspace
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    // Stable, URL-safe identifier. Never derived from the display name at read
    // time, because renaming a workspace must not change its URLs.
    public required string Slug { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public ICollection<WorkspaceMembership> Memberships { get; set; } = [];
}

public enum WorkspaceRole
{
    // Can manage the workspace and its members. The creator of a personal
    // workspace is always its Owner.
    Owner = 0,

    // Can upload and convert, but not manage membership.
    Member = 1
}

// The join that authorization actually resolves against. SR-SEC-2: a
// client-supplied workspace id is never authorization - the server looks for a
// membership row for (CurrentUserId, RequestedWorkspaceId) and refuses when
// there is none.
public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; init; }

    public Guid UserId { get; init; }

    public required WorkspaceRole Role { get; set; }

    public required DateTime JoinedAtUtc { get; init; }

    public Workspace? Workspace { get; set; }

    public ApplicationUser? User { get; set; }
}
