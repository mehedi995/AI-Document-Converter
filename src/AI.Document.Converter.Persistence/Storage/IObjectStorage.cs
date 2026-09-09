namespace AI.Document.Converter.Persistence.Storage;

// Where document bytes live. Lives alongside the DbContext because both Web and
// Worker need it - the web host writes uploads and serves downloads, the worker
// reads sources and writes artifacts - and because durable state is durable
// state whether it is a row or a blob. A separate project for one interface and
// one adapter would be ceremony without benefit.
//
// Keys are opaque, server-generated, and always tenant-prefixed. No method here
// accepts or returns a filesystem path: SR-SEC-4 requires that customer bytes
// stay outside any public webroot and that nothing derived from user input ever
// reaches a path.
public interface IObjectStorage
{
    // Returns the number of bytes actually written, which the caller should
    // compare against what it expected. A stream that lies about its length is
    // one of the ways a declared-size check gets bypassed.
    Task<long> WriteAsync(string key, Stream content, CancellationToken cancellationToken);

    // Null when the object does not exist or its bytes have been removed by
    // retention. Callers must handle null rather than assuming a row implies
    // bytes - metadata outlives content on purpose (SR-SEC-6).
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    // Idempotent: deleting something already gone is a success, so retention
    // sweeps and customer deletions can both run without racing each other.
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

// Builds the only key shapes this system uses. Centralized so a caller cannot
// invent its own layout and accidentally drop a tenant prefix.
public static class StorageKeys
{
    // Tenant first. Every object is immediately attributable to a workspace by
    // its key alone, which makes a per-tenant retention sweep or export a
    // prefix scan rather than a database join, and makes a missing tenant
    // prefix obvious on sight.
    public static string ForSource(Guid workspaceId, Guid documentId) =>
        $"workspaces/{workspaceId:N}/sources/{documentId:N}";

    public static string ForArtifact(Guid workspaceId, Guid artifactId) =>
        $"workspaces/{workspaceId:N}/artifacts/{artifactId:N}";
}
