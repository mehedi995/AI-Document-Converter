using AI.Document.Converter.Persistence.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Persistence;

// One DbContext for the modular monolith and the worker (SaaS §4). Identity
// tables come from IdentityDbContext; everything tenant-owned is configured
// below.
//
// Deliberately NOT using EF global query filters for tenancy. A global filter
// is easy to bypass (IgnoreQueryFilters, raw SQL, a Find() by primary key) and
// it hides the security decision from the reader. Ownership is enforced
// explicitly at each call site instead, which is verifiable in review and in
// the cross-tenant tests (SR-SEC-2).
public sealed class ConverterDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ConverterDbContext(DbContextOptions<ConverterDbContext> options)
        : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();

    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();

    public DbSet<ConversionJob> ConversionJobs => Set<ConversionJob>();

    public DbSet<ConversionJobItem> ConversionJobItems => Set<ConversionJobItem>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<JobItemWarning> JobItemWarnings => Set<JobItemWarning>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<UsagePeriod> UsagePeriods => Set<UsagePeriod>();

    public DbSet<UsageReservation> UsageReservations => Set<UsageReservation>();

    public DbSet<UsageLedgerEntry> UsageLedgerEntries => Set<UsageLedgerEntry>();

    public DbSet<ProviderEventRecord> ProviderEvents => Set<ProviderEventRecord>();

    public DbSet<OperatorAuditEntry> OperatorAuditEntries => Set<OperatorAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Workspace>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).HasMaxLength(200).IsRequired();
            entity.Property(w => w.Slug).HasMaxLength(80).IsRequired();
            entity.HasIndex(w => w.Slug).IsUnique();
        });

        builder.Entity<WorkspaceMembership>(entity =>
        {
            // Composite key: a user belongs to a workspace at most once, and
            // the database enforces it rather than trusting application code.
            entity.HasKey(m => new { m.WorkspaceId, m.UserId });

            entity.HasOne(m => m.Workspace)
                .WithMany(w => w.Memberships)
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Every authorization check is "which workspaces does this user
            // belong to", so that is the direction that needs the index.
            entity.HasIndex(m => m.UserId);
        });

        builder.Entity<SourceDocument>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.OriginalFileName).HasMaxLength(400).IsRequired();
            entity.Property(d => d.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(d => d.StorageKey).HasMaxLength(400).IsRequired();

            entity.HasOne(d => d.Workspace)
                .WithMany()
                .HasForeignKey(d => d.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant-first composite: every legitimate lookup is scoped to a
            // workspace, so leading with WorkspaceId keeps the index usable and
            // makes an unscoped query stand out as a full scan.
            entity.HasIndex(d => new { d.WorkspaceId, d.UploadedAtUtc });

            // Content-addressed lookup stays inside the tenant boundary - never
            // deduplicate across workspaces (SaaS §6).
            entity.HasIndex(d => new { d.WorkspaceId, d.Sha256 });
        });

        builder.Entity<ConversionJob>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.Property(j => j.PresetName).HasMaxLength(80).IsRequired();
            entity.Property(j => j.Status).HasConversion<int>();

            // xmin is PostgreSQL's built-in row version; using it avoids adding
            // a column and keeps concurrency correct without application-side
            // bookkeeping.
            entity.Property(j => j.Version).IsRowVersion();

            entity.HasOne(j => j.Workspace)
                .WithMany()
                .HasForeignKey(j => j.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Drives the dashboard's "recent jobs" list.
            entity.HasIndex(j => new { j.WorkspaceId, j.CreatedAtUtc });

            // The retention sweep's query: find jobs whose source or output
            // clock has run out and that have not been purged yet. Without
            // this the sweep degrades into a full scan as history grows.
            entity.HasIndex(j => new { j.CreatedAtUtc, j.SourceBytesPurgedAtUtc });
            entity.HasIndex(j => new { j.CompletedAtUtc, j.OutputBytesPurgedAtUtc });
        });

        builder.Entity<ConversionJobItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Status).HasConversion<int>();
            entity.Property(i => i.ErrorCategory).HasMaxLength(60);
            entity.Property(i => i.ErrorMessage).HasMaxLength(2000);
            entity.Property(i => i.LeaseOwner).HasMaxLength(120);
            entity.Property(i => i.Version).IsRowVersion();

            entity.HasOne(i => i.Job)
                .WithMany(j => j.Items)
                .HasForeignKey(i => i.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.SourceDocument)
                .WithMany()
                .HasForeignKey(i => i.SourceDocumentId)
                // Restrict, not Cascade: deleting a source document must not
                // silently erase the record that work was done on it. Source
                // BYTES are removed by retention; the row stays.
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(i => i.JobId);

            // The worker's claim query: find items that are queued, or whose
            // lease has expired because the previous worker died.
            entity.HasIndex(i => new { i.Status, i.LeaseExpiresAtUtc });
        });

        builder.Entity<Artifact>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Kind).HasConversion<int>();
            entity.Property(a => a.StorageKey).HasMaxLength(400).IsRequired();
            entity.Property(a => a.FileName).HasMaxLength(400).IsRequired();

            entity.HasOne(a => a.JobItem)
                .WithMany(i => i.Artifacts)
                .HasForeignKey(a => a.JobItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => a.JobItemId);

            // The download path resolves an artifact by id AND workspace, so
            // changing the id in a URL cannot reach another tenant's bytes.
            entity.HasIndex(a => new { a.WorkspaceId, a.Id });
        });

        builder.Entity<JobItemWarning>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Code).HasMaxLength(80).IsRequired();
            entity.Property(w => w.Severity).HasMaxLength(20).IsRequired();
            entity.Property(w => w.Message).HasMaxLength(2000).IsRequired();
            entity.Property(w => w.BlockId).HasMaxLength(80);
            entity.Property(w => w.SheetName).HasMaxLength(400);

            entity.HasOne(w => w.JobItem)
                .WithMany(i => i.Warnings)
                .HasForeignKey(w => w.JobItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(w => w.JobItemId);
        });

        builder.Entity<Subscription>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.PlanCode).HasMaxLength(80).IsRequired();
            entity.Property(s => s.Status).HasConversion<int>();
            entity.Property(s => s.ProviderSubscriptionId).HasMaxLength(200);
            entity.Property(s => s.ProviderName).HasMaxLength(80);
            entity.Property(s => s.Version).IsRowVersion();

            entity.HasOne(s => s.Workspace)
                .WithMany()
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // One active commercial state per workspace.
            entity.HasIndex(s => s.WorkspaceId).IsUnique();
        });

        builder.Entity<UsagePeriod>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.PlanCode).HasMaxLength(80).IsRequired();
            entity.Property(p => p.Version).IsRowVersion();

            // The current period is looked up by workspace and time on every
            // job acceptance, so this index is on the hot path.
            entity.HasIndex(p => new { p.WorkspaceId, p.StartsAtUtc, p.EndsAtUtc });
        });

        builder.Entity<UsageReservation>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Status).HasConversion<int>();
            entity.Property(r => r.PolicyVersion).HasMaxLength(40).IsRequired();

            entity.HasOne(r => r.UsagePeriod)
                .WithMany()
                .HasForeignKey(r => r.UsagePeriodId)
                .OnDelete(DeleteBehavior.Cascade);

            // A job holds at most one OPEN reservation, enforced by the database
            // so a duplicate accept cannot double-hold the customer's allowance.
            //
            // Filtered on Held rather than unique on JobId alone, because a job
            // legitimately accumulates resolved reservations over its life: a
            // retry closes the first hold and takes a new one. A plain unique
            // index made the second one impossible, which is not the invariant -
            // "one hold at a time" is. It is also what makes the
            // SingleOrDefault(Held) lookups in MeteringService safe by schema.
            entity.HasIndex(r => r.JobId)
                .IsUnique()
                .HasFilter($"\"Status\" = {(int)ReservationStatus.Held}");
        });

        builder.Entity<OperatorAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActorEmail).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.TargetEmail).HasMaxLength(256);

            // Read newest-first, and filtered by who or by which job.
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.HasIndex(e => e.ActorUserId);
            entity.HasIndex(e => e.TargetJobId);
            entity.HasIndex(e => e.TargetUserId);

            // NO foreign key to ConversionJobs or Workspaces, deliberately. The
            // record that an operator looked at a customer's job must survive
            // that job being deleted - a cascade would erase the audit trail at
            // exactly the moment it becomes evidence.
        });

        builder.Entity<ProviderEventRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Kind).HasMaxLength(50).IsRequired();

            // The replay guard, in the database rather than in a check the
            // application might skip under concurrency. Two simultaneous
            // deliveries of one event both pass an "already applied?" query;
            // only one survives this index.
            entity.HasIndex(e => new { e.ProviderName, e.EventId }).IsUnique();
        });

        builder.Entity<UsageLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.BasisDescription).HasMaxLength(400).IsRequired();
            entity.Property(e => e.PolicyVersion).HasMaxLength(40).IsRequired();
            entity.Property(e => e.PlanCode).HasMaxLength(80).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();

            // THE constraint that makes double-charging impossible rather than
            // merely unlikely. Queue delivery is at-least-once, so a duplicate
            // settlement WILL happen; this turns it into a unique-violation the
            // service catches instead of a second charge (SR-BIL-5).
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();

            // Statement queries: everything a workspace was charged in a period.
            entity.HasIndex(e => new { e.WorkspaceId, e.UsagePeriodId, e.CreatedAtUtc });
        });

        ConfigureUtcDateTimes(builder);
    }

    // Every timestamp in this schema is UTC (SaaS §11). Npgsql maps DateTime to
    // `timestamp with time zone` and requires Utc-kinded values; a value read
    // back is Utc-kinded too. This converter makes that explicit rather than
    // depending on every call site remembering DateTime.UtcNow, so a local-kind
    // value can never be silently stored as though it were UTC.
    private static void ConfigureUtcDateTimes(ModelBuilder builder)
    {
        var utcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion
            .ValueConverter<DateTime, DateTime>(
                value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        var nullableUtcConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion
            .ValueConverter<DateTime?, DateTime?>(
                value => value.HasValue
                    ? (value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime())
                    : value,
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                    : value);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }
        }
    }
}
