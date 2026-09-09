# Development Setup — Cloud SaaS Edition

Everything needed to build, migrate, and run the SaaS host and worker from a clean checkout.
The desktop application's own setup is unchanged and documented in `README.md`.

---

## 1. Prerequisites

| Tool | Version used | Notes |
|---|---|---|
| .NET SDK | **10.0.400** | Pinned by `global.json` (`rollForward: latestFeature`). |
| PostgreSQL | **18.6** | Verified against a local install on port 5432. |
| Python | 3.13 | Build-time only — needed to rebuild the engine bundle, not to run it. |
| Node / npm | 24 / 11 | For the Tailwind + TypeScript pipeline (not wired up yet). |

The repository targets two frameworks on purpose:

- `net8.0` / `net8.0-windows` — the existing desktop application and its shared libraries.
- `net10.0` — `Web`, `Worker`, `Persistence`, `Contracts`.

SDK 10 builds both. `global.json` exists so the repo does not silently build on whatever SDK
happens to be first on `PATH`; verified that the desktop solution still builds clean under it.

---

## 2. Database

### 2.1 Create the database and application role

Run these once, as a PostgreSQL superuser. **Choose your own password** — do not reuse the
`postgres` superuser password for the application role, and do not commit it anywhere.

```sql
CREATE ROLE adc_app WITH LOGIN PASSWORD 'choose-a-strong-password';
CREATE DATABASE adc_dev OWNER adc_app;
```

From a shell (adjust the path if your PostgreSQL version differs):

```bash
"/c/Program Files/PostgreSQL/18/bin/psql" -U postgres -c "CREATE ROLE adc_app WITH LOGIN PASSWORD 'choose-a-strong-password';"
```

```bash
"/c/Program Files/PostgreSQL/18/bin/psql" -U postgres -c "CREATE DATABASE adc_dev OWNER adc_app;"
```

The application connects as `adc_app`, never as a superuser. A compromised application
credential should not be able to drop other databases or read the server's files.

### 2.2 Store the connection string in user secrets

**The connection string contains a password and must never be committed** (SEC-003).
`appsettings.Development.json` deliberately contains no connection string at all — only a comment
describing the expected shape. The real value goes in .NET user secrets, which live in your own
profile outside the repository:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=adc_dev;Username=adc_app;Password=choose-a-strong-password" --project src/AI.Document.Converter.Web
```

`Program.cs` throws on startup if this is missing rather than falling back to a default. A silent
fallback is how a developer ends up unknowingly writing to the wrong database.

### 2.3 Apply migrations

```bash
dotnet ef database update --project src/AI.Document.Converter.Persistence --startup-project src/AI.Document.Converter.Web
```

Install the CLI first if needed:

```bash
dotnet tool install --global dotnet-ef
```

---

## 3. Run

```bash
dotnet run --project src/AI.Document.Converter.Web
```

Liveness check:

```bash
curl -k https://localhost:5001/healthz
```

`/healthz` is deliberately liveness-only — it does not touch the database or the engine. A health
endpoint that fails whenever a dependency blips causes the orchestrator to kill an otherwise
healthy process.

---

## 4. Python engine

The SaaS worker uses the same engine as the desktop app. Rebuild it after **any** Python change:

```bash
pwsh -File scripts/build-python-engine.ps1
```

Two things that have already caused real problems here:

- **The integration tests run the built bundle in `dist/`, not your source.** Skip the rebuild and
  they will silently test the previous engine.
- The script carries load-bearing `--exclude-module` flags: `pymupdf`/`fitz` for licensing
  (AGPL must never ship) and `numpy`/`pandas` for size (131 MB → 78 MB). Do not drop them.
  `scripts/check-licences.py` catches the licensing half; nothing catches the size half
  automatically.

---

## 5. Tests

```bash
dotnet test --filter "Category!=Performance"
```

The `Performance` category contains a 100 MB benchmark that is known to fail intermittently under
memory pressure (risk R-20, documented in that test's own header). It predates the SaaS work and
is excluded from routine runs by `docs/16-TEST-STRATEGY.md`.

Licence gate, which should run in CI and before any release:

```bash
python scripts/check-licences.py
```

---

## 6. Project layout

```
src/AI.Document.Converter.Domain          net8.0          shared entities, enums, value objects
src/AI.Document.Converter.Application     net8.0          conversion pipeline services
src/AI.Document.Converter.Infrastructure  net8.0          engine client, file system, config
src/AI.Document.Converter.Wpf             net8.0-windows  desktop app (unchanged by SaaS work)
src/AI.Document.Converter.Python          --              extraction engine
src/AI.Document.Converter.Contracts       net10.0         versioned worker request/result contract
src/AI.Document.Converter.Persistence     net10.0         EF Core entities, DbContext, migrations
src/AI.Document.Converter.Web             net10.0         ASP.NET Core host + Identity
src/AI.Document.Converter.Worker          net10.0         background job processor
```

`Persistence` is its own project because **both** `Web` and `Worker` need the DbContext — the
worker persists job state. It is deliberately not folded into `Infrastructure`, which targets
`net8.0` and ships inside the desktop application; adding EF Core and Npgsql there would drag
server dependencies into a desktop install.

---

## 7. Data model notes

- **Tenancy is explicit, not implicit.** There are no EF global query filters. A global filter is
  bypassable (`IgnoreQueryFilters`, raw SQL, `Find()` by primary key) and hides the security
  decision from the reader. Every tenant-owned row carries `WorkspaceId`, and ownership is
  enforced at each call site so it is visible in review and testable (SR-SEC-2).
- **`WorkspaceId` is denormalized onto child rows** (job items, artifacts, warnings) so a worker
  can authorize a message without a join, and a message carrying the wrong workspace cannot reach
  another tenant's data.
- **Every timestamp is UTC**, enforced by a model-wide value converter rather than by every call
  site remembering `DateTime.UtcNow`.
- **Concurrency uses PostgreSQL's `xmin`** on jobs and job items, so two workers completing two
  items of the same job cannot clobber each other's status update.
- **Leases, not locks.** A job item records `LeaseOwner` and `LeaseExpiresAtUtc`. An expired lease
  means the worker died and the item becomes claimable again — that is what stops a crash from
  stranding work forever. Queue delivery is at-least-once; exactly-once execution is not claimed.
- **`SourceDocument` deletes are `Restrict`, not `Cascade`.** Removing a source document must not
  erase the record that work was done on it. Source *bytes* are removed by retention; the row
  stays.

---

## 8. Current state

Scaffold only. `Web` serves a placeholder page and `/healthz`; `Worker` is the default template
with a database reference. **There is no upload, no job processing, and no results view yet** —
nothing here should be described as a working SaaS.
