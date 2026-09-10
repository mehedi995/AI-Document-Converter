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

Role changes are **recorded in the audit trail** (`grantOperatorRole` / `revokeOperatorRole`).
Operator status is what makes every other entry possible, so becoming one leaves its own record -
otherwise the log shows what operators did but never how somebody became one, which is the first
question an investigation asks.

The actor on those entries is **not an application user**: the command runs from a shell, so what
is honestly recordable is the OS identity and machine, stored as `command line: user@machine` with
`ActorUserId` left null. Filling it with a placeholder id would put a lie in the audit trail.

An optional reason can be appended and is stored:

```bash
dotnet run --project src/AI.Document.Converter.Web -- grant-operator someone@example.com incident 2291 - on-call cover
```

### Two-factor enrolment

`/security/two-factor`. TOTP (RFC 6238) via an authenticator app.

No SMS. SIM swapping is a routine attack, and offering a channel we would then have to warn people
not to rely on is worse than not offering it.

An operator **cannot switch their own two-factor off** while holding the role. The request is
refused rather than silently removing the role - losing production administration as a side effect
of a checkbox would be a surprising way to find out.

### Recovery codes

Ten are issued **at the moment two-factor is switched on**, and shown once. A second factor with no
way back is not a security control; it is a way to lose an account. Issuing them later would mean
most people never come back for them.

They are stored hashed, so there is no page that can display them again - `/security/recovery-codes`
shows only how many remain. Generating a new set invalidates the old one, which is the point when
you think the old codes were seen. Turning two-factor off destroys them along with the shared
secret.

Signing in with one: the two-factor challenge page links to `/account/recovery-code`. Each code
works once, and using one is logged at warning level with the number remaining - burning a recovery
code means somebody lost their authenticator, or somebody else has their codes.

## 6. Limitations

- The console has **no pagination**. It shows the most recent 50 jobs, 25 workspaces and 100 audit
  entries. Fine now, insufficient once the audit trail is long enough to matter.
- `viewConsole` and `viewAuditTrail` exist in the audit vocabulary but **are not written**: only
  inspection is recorded. Recording every page view would bury the entries that matter in noise, and
  the console reveals no customer documents anyway. If that trade is ever revisited, the constants
  are already there.
- The audit trail is append-only **by construction** - no service writes an update or delete - but
  nothing stops someone with direct database access from editing it. Tamper-evidence beyond that
  (hash chaining, or shipping entries off-host) has not been built.
- An operator who loses **both** their authenticator and their recovery codes still needs someone
  with database access. That is the expected floor for account recovery without a separate identity
  provider, but it is worth knowing before it happens.
