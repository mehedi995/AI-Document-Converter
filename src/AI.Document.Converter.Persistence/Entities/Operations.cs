namespace AI.Document.Converter.Persistence.Entities;

// What an operator did that touched a customer's data.
//
// SR-SEC-7 requires support access to customer documents to be a documented,
// narrowly scoped, AUDITED flow. This is the audit half. It exists because an
// operator can cross tenant boundaries by definition, and the only thing that
// makes that acceptable is that every crossing leaves a record somebody else
// can read.
//
// Append-only. There is no update path and no delete path in the service that
// writes it: an audit trail an operator can edit is not an audit trail. It also
// deliberately outlives the job it describes - the record that someone looked
// at a customer's file must survive that file's retention period, or the
// evidence disappears exactly when it starts to matter.
public sealed class OperatorAuditEntry
{
    public Guid Id { get; init; }

    // Both the id and the email as they were AT THE TIME. The id is the durable
    // link; the email is a snapshot, because an account can be renamed or
    // deleted and the record still has to name who did this.
    //
    // NULL for an action taken from the command line, which genuinely has no
    // application user behind it - the actor is whoever holds shell access on
    // the host. Writing Guid.Empty there would put a lie in the audit trail,
    // and an audit trail that lies about who acted is worse than one that
    // admits it does not know.
    public Guid? ActorUserId { get; init; }

    // Always populated. For a command-line action this describes the OS
    // identity and machine instead of an account, prefixed so the two can
    // never be confused when reading the trail.
    public required string ActorEmail { get; init; }

    // What was done, from a fixed vocabulary rather than free text, so the log
    // can be queried rather than only read.
    public required string Action { get; init; }

    // What it was done to. Nullable because some actions (viewing queue health)
    // are not about a specific record.
    public Guid? TargetJobId { get; init; }

    public Guid? TargetWorkspaceId { get; init; }

    // The account an action was performed ON, for role changes. Distinct from
    // the actor: "who did it" and "who it was done to" are different questions
    // and a role grant needs both answered.
    public Guid? TargetUserId { get; init; }

    public string? TargetEmail { get; init; }

    // Why. Required by the service for document inspection specifically:
    // forcing a support engineer to state a reason before revealing customer
    // data is most of what makes the access "narrowly scoped" in practice,
    // and it is the field an auditor actually reads.
    public string? Reason { get; init; }

    public string? IpAddress { get; init; }

    public required DateTime OccurredAtUtc { get; init; }
}
