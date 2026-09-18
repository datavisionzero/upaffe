# Storage

PostgreSQL is upaffe's sole application database. `UpaffeDbContext` in the
Infrastructure layer is the schema authority; Domain has no dependency on EF
Core or any persistence package.

## Configuration

The API reads the standard .NET connection string named `Postgres`, supplied as
`ConnectionStrings__Postgres` in an environment. Missing or malformed values
stop startup before the host is built. Connection strings are credentials:
ordinary output must use `DatabaseSettings.Redacted`, which masks every
password spelling accepted by the provider.

## Migration rule

The checked-in migration chain only moves forward. On every start, before HTTP
is served, the host:

1. opens PostgreSQL and takes an application-specific advisory lock;
2. refuses any applied migration identifier unknown to this binary;
3. applies every pending migration through EF Core; and
4. releases the lock even when migration fails or startup is canceled.

Two containers may therefore start concurrently without migrating against one
another. A connection or migration failure fails startup; the process never
serves against a partially migrated or incompatible schema. Rolling an image
back after a schema change requires restoring the pre-upgrade backup; there is
no automatic downgrade path.

The initial migration creates no product tables. The second migration adds the
access and project model decided in ADR 0002. The third adds the HTTP monitor,
check, and incident model decided in ADRs 0003 and 0004. The fourth adds the
durable HTTP execution lease decided in ADR 0005. The fifth adds the persistent
start of the current failure streak used for threshold evaluation. The sixth
adds push monitors, reports, incidents, and reporting credentials decided in
ADR 0006. Later changes add new forward migrations; an existing migration is
never rewritten after release.

Add a migration from the repository root after changing the context model:

```sh
dotnet tool restore
dotnet ef migrations add Name \
  --project src/Upaffe.Infrastructure \
  --output-dir Persistence/Migrations
```

Never edit an already published migration. Add another one on top.

## Access and project schema

`operator_identity` has a checked singleton value with a unique index, so the
database can contain at most one operator even when two callers race or bypass
application code. Its password is a self-describing hash; plaintext is never a
column. `bootstrap_grant` likewise has one row at most and stores a 32-byte
digest, expiry, and consumption time.

Browser sessions and management-credential secrets store unique 32-byte
digests. Session rows carry idle-use, absolute-expiry, and revocation facts.
Management credentials own one current secret at a time; an expiring previous
row provides the bounded rotation overlap. Revoking the credential makes all of
its secret rows unusable without revealing them.

Projects have an internal UUID and an immutable unique key. The mutable name,
optimistic version, and deletion timestamp change without changing either
identity. Deleted keys remain reserved so restoration cannot collide with a
replacement project. API writes compare the version last read before mutation,
and EF's concurrency token rejects a second writer that races between that
comparison and commit. Concurrent creation of the same accepted key and name is
idempotent; a different name is a conflict.

## HTTP monitoring schema

The row model below is used by the complete fictional lifecycle in
[the HTTP monitoring guide](./http-monitoring.md).

`http_monitor` belongs to one project through a restrictive foreign key and
reserves its key within that project for its lifetime. It stores non-secret
configuration, scheduling facts, the current evaluation generation and ordered
sequence, consecutive failures and their first check, pause/removal timestamps,
and separate foreign keys for the latest result and latest success. Check
constraints mirror the interval, timeout, threshold, status, text-rule, state,
sequence, and timestamp boundaries in ADRs 0003 and 0004. New and resumed
monitors have a due time; paused and removed monitors do not. Its version is an
optimistic concurrency token for management changes.

The target stored on `http_monitor` never contains a query. The write-only
query bytes live one-to-one in `http_monitor_secret`; the ordinary row records
only whether a query exists. `http_monitor_header` contains lower-case header
names and timestamps, while `http_monitor_header_secret` contains the UTF-8
value behind an explicit secret read. An ordinary monitor or header query
therefore cannot reconstruct either secret. These values must be available to
the HTTP executor, so unlike authentication credentials they cannot be hashed.
The database and host administrator remain trusted as established by ADR 0002;
application output, logs, histories, and exports never expose the byte values.

`http_check` represents one attempt whose ID and monitor-local sequence exist
before execution. The row is incomplete until one immutable success or failure
result supplies completion and diagnostic facts. A unique monitor/sequence
index makes repeated allocation visible. Response content and target queries
are not stored. The monitor's latest-result and latest-success references are
independent so a current failure retains the earlier successful observation.

Scheduled checks additionally retain the current execution token, lease end,
last-attempt time, and attempt count. Claiming a due monitor and advancing its
next due time are one transaction; recording the result is another, after the
network request. Expired incomplete work is reclaimed with the same check ID
and sequence. Row locks with `SKIP LOCKED` let several instances drain distinct
work, while token comparison prevents an earlier lease holder from recording a
second result. See ADR 0005 for restart and failure behavior.

`incident` retains the first, opening, latest-failure, and optional resolution
check references and their ordered sequences. Original and latest reasons and
all lifecycle times remain after resolution. A partial unique index on the
monitor ID where `resolved_at is null` is the final guard against two active
incidents for one monitor, including when application transitions race.
Accepted failures update the open row's latest observation and reason, while an
accepted success adds its immutable resolution check and time. The first and
opening failure references remain unchanged after both updates and resolution.

Completed check and incident detail is retained for 90 days. A daily cleanup
uses an exclusive cutoff: a fact exactly at the cutoff remains. It first removes
resolved incidents older than the cutoff, then removes older completed checks
and abandoned incomplete attempts. Checks referenced by any remaining incident
or by a monitor's latest result, latest success, or current failure-streak start
are excluded. Open incidents and every check they reference therefore survive
regardless of age; cleanup never rewrites monitor state or creates synthetic
successes. The retained window is evidence, not a claim of health before it.

## Push monitoring schema

`push_monitor` belongs restrictively to one project and reserves its key for
its lifetime. It stores the immutable reporting mode, interval, tolerance,
state, optimistic version, lifecycle timestamps, evaluation generation,
monitor-local sequence allocation, last applied observation order, last
receipt, and the next persisted deadline. Independent latest-report and
latest-success foreign keys preserve the distinction between recent evidence
and recent success. New and resumed monitors have a deadline; paused and
removed monitors cannot have one. Database checks enforce the 30-second to
365-day interval, bounded tolerance, state, sequence, deadline, and lifecycle
invariants from ADR 0006.

`push_report` is one immutable external or deadline-generated observation. Its
internal ID is separate from the sender's report ID. Unique monitor/report-ID
and monitor/sequence indexes make retries and competing allocation visible.
The row records both sender observation time and authoritative server receipt
time, outcome, bounded diagnostic reason, generation, whether it is applicable
to current state, and whether upaffe created it for a crossed deadline. A
report is applicable only when its sender observation is newer than the last
applied observation in the current evaluation generation. Checks enforce the
accepted clock window and prevent a success or ordinary external report from
masquerading as a missing-report observation.

`reporting_credential` is one-to-one with a push monitor and carries only
issue, rotation, and revocation metadata. Each `reporting_credential_secret`
stores a unique 32-byte digest, never plaintext. A filtered unique index permits
one unexpired current-secret row; rotation gives the prior digest a five-minute
expiry before inserting its replacement. Revocation remains a credential fact
so every associated digest becomes unusable immediately.

`push_incident` has the same single-open-episode guarantee as an HTTP incident,
but its opening, latest failure, and optional resolution references target push
reports. A partial unique index prevents concurrent writers from opening two
incidents for one push monitor. Stable reasons distinguish explicit
`reported_failure` from `report_missing`; sender diagnostics remain separate.
External report insertion, current monitor evaluation, and incident transition
share one transaction under a monitor row lock. An applicable job-completion
success advances its deadline; an explicit job failure does not. Every
applicable state report advances its freshness deadline, including a failure
that proves the sender is alive but unhealthy. Either mode opens an incident on
the first explicit failure without waiting for tolerance, reuses it for later
failures while preserving its opening facts, and resolves it only with a newer
applicable success. Older or duplicate reports remain immutable history and do
not alter deadlines, state, success pointers, or incidents.

Push history uses the same 90-day exclusive cutoff and reference protection as
HTTP history. Current receipt, success, deadline, state, and incident facts are
stored on durable rows and are never reconstructed from whatever detail remains
after retention. Report-ID uniqueness is retained for the full 90-day retry
boundary.

## Tests

Integration tests use the same PostgreSQL 18 major intended for deployment.
One container is shared for a test run, while every test receives a fresh
database. The suite proves application to an empty database, idempotent and
concurrent startup, rejection of unknown migrations, and refusal to start when
the database is unavailable. Persistence tests additionally exercise the
singleton operator, secret lifecycles, revocation, expiry, project identity,
monitor configuration and pause transitions, separated HTTP secrets, ordered
results, failure thresholds, repeated and competing evaluation, cursor
pagination, retention boundaries and open-incident protection, concurrent
scheduler claims, expired-lease recovery, and incident uniqueness and
resolution against PostgreSQL constraints rather than an in-memory substitute.
Push persistence tests additionally prove restart-safe deadlines and reports,
digest-only credential rotation and revocation, duplicate and concurrent
report rejection, value constraints, and one-open-push-incident uniqueness.
