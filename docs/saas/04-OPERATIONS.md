# Operator access

**Status: implemented.** Verified against a running instance on 2026-09-09; see
`IMPLEMENTATION_STATUS.md` for the evidence.

This document is part of the control, not a description of it: SR-SEC-7 requires customer document
inspection by support to be a *documented*, narrowly scoped, audited access flow.

---

## 1. What the operator console is for

Running the service: seeing what is queued, what is stuck, what is failing, whether retention is
keeping its promises, and what each workspace is consuming.

It is **read-only**. It cannot cancel, retry, or delete another tenant's work. Acting on a
customer's data on their behalf is a different capability with different consent questions, and it
should be designed deliberately rather than appearing as a convenient button beside a diagnostic
table.

## 2. What it deliberately does not show

The console shows **no filenames, no document content, and no warning or error messages**.

This is SR-SEC-7's "operator status alone must not reveal documents", and it is a real constraint
rather than a stylistic one:

- A filename can be the sensitive part. `quarterly-redundancies.xlsx` discloses something the
  customer never agreed to share with support.
- An error message can quote the text that caused it.
- A warning message can quote the row it was raised on.

What the console shows instead is error **categories** (a fixed vocabulary the system produces),
counts, timings, attempt counts and lease state. Diagnosing an outage does not require reading
anybody's documents, and the console is shaped so that it cannot.

## 3. Inspecting one job

When a category and a count are genuinely not enough - a customer reports every file failing and
support needs the filenames and error text - there is one path: `/operator/jobs/{id}/inspect`.

It is narrow by construction:

| Property | Why |
|---|---|
| One job, addressed by id | There is no listing, no filename search, and no way to sweep a workspace |
| A written reason is **mandatory** (10 characters minimum) | Having to say why, before the data appears, is most of what prevents casual browsing - and it is the field an auditor reads |
| The audit entry is committed **before** the data is returned | If the write fails the caller sees nothing; recording access afterwards would let a crash disclose data with no trace |
| POST, not GET | A job's details cannot be opened by following a link somebody sent you |
| Still **no document content** | "Support can see why your conversion failed" is a far smaller promise than "support can read your documents", and only the first is offered |

Warning **codes and locations** are shown; warning **messages** are not, because they can quote the
document.

## 4. The audit trail

`/operator/audit`. Append-only: there is no update or delete path in the service that writes it. An
audit trail an operator can edit is not an audit trail.

Entries record who (id **and** the email as it was at the time), what, which job, which workspace,
the stated reason, the source IP, and when.

They are visible to the operators they record, on purpose. A log only its subjects cannot see is a
surveillance tool; one everybody can see is a deterrent.

The table has **no foreign key** to jobs or workspaces. The record that someone looked at a
customer's files must outlive those files - a cascade would erase the evidence at exactly the moment
it becomes relevant. This is covered by a test that hard-deletes the job and asserts the entry
survives.

## 5. Getting operator access

Two independent things are required, and either alone is refused:

1. **The `Operator` role.**
2. **A session established with a second factor.** Not merely an account *capable* of MFA - the
   session itself. Identity stamps `amr=mfa` on the principal when a sign-in completes through the
   two-factor step, and the policy requires that claim.

The second point has a consequence worth knowing before it confuses somebody: **enabling two-factor
does not unlock the console in your current session.** That cookie was minted without it. Sign out
and sign in again. The two-factor page says so, and so does the access-denied page.

### Granting the role

From the command line on the host, never through the web UI - a page that hands out administrative
access is a privilege-escalation surface, for something done a handful of times in a system's life.

```bash
dotnet run --project src/AI.Document.Converter.Web -- grant-operator someone@example.com
```

```bash
dotnet run --project src/AI.Document.Converter.Web -- list-operators
```

```bash
dotnet run --project src/AI.Document.Converter.Web -- revoke-operator someone@example.com
```

`list-operators` prints each operator's two-factor state, because an operator without one cannot
actually reach the console and that is otherwise invisible until they try.

### Two-factor enrolment

`/security/two-factor`. TOTP (RFC 6238) via an authenticator app.

No SMS. SIM swapping is a routine attack, and offering a channel we would then have to warn people
not to rely on is worse than not offering it.

An operator **cannot switch their own two-factor off** while holding the role. The request is
refused rather than silently removing the role - losing production administration as a side effect
of a checkbox would be a surprising way to find out.

## 6. Limitations

- **Role membership changes are not themselves audited** in the operator trail. `grant-operator`
  logs to the application log and the change is visible in `list-operators`, but there is no
  tamper-evident record of who granted what. This should be added before a team larger than one.
- **Recovery codes are not implemented.** An operator who loses their authenticator needs an
  administrator with database access to clear `TwoFactorEnabled`. Acceptable while operators are
  few; not acceptable at scale.
- The console has **no pagination**. It shows the most recent 50 jobs, 25 workspaces and 100 audit
  entries. Fine now, insufficient once the audit trail is long enough to matter.
- `viewConsole` and `viewAuditTrail` exist in the audit vocabulary but **are not written**: only
  inspection is recorded. Recording every page view would bury the entries that matter in noise, and
  the console reveals no customer documents anyway. If that trade is ever revisited, the constants
  are already there.
