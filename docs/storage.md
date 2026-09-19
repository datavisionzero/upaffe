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
ADR 0006. The seventh adds durable push-deadline leases and the unique
synthetic-observation boundary. Later changes add new forward migrations; an
existing migration is never rewritten after release.

The next migration adds the singleton email configuration and project
recipient snapshot decided in ADR 0007.
The following migration adds durable notification deliveries and their due-work
index and logical uniqueness boundary.
The next migration adds timed maintenance windows for projects and monitors.

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

`email_configuration` is a single keyed row with a concurrency version,
shared relay and sender settings, default recipients, and an optional SMTP
password. The password must be readable by the sender and therefore cannot be
hashed. It is excluded from ordinary API reads and logs, like HTTP monitor
request secrets. The database and host administrator remain trusted. Project
rows contain an independent `text[]` recipient list copied from the defaults
at creation; replacement uses the project concurrency version. Existing
projects never track later default changes.

## Email delivery schema

The operator-facing behavior of these rows is described in
[the email and maintenance guide](./email-maintenance.md).

`notification_delivery` represents one alert or recovery intent for one
incident and normalized recipient. A unique `(incident_id, kind, recipient_key)`
index prevents intentional duplicates even when two evaluators race. The row
stores only the facts needed to render a message when attempted: project and
monitor identity and display names, stable reason, event time, and recipient.
It stores no rendered body or SMTP password. Its state, attempt count, next
attempt, lease token and expiry, acceptance time, terminal time, and sanitized
last failure code are durable. Message-ID derives from the logical identity.

A worker claims due rows with `FOR UPDATE SKIP LOCKED` and a two-minute lease.
An expired lease can be reclaimed after a crash; completion requires the latest
lease token. After a final eligibility check, beginning SMTP submission counts
as an attempt before network work starts; deferred claims do not consume an
attempt. Repeated crashes therefore cannot bypass the five-attempt cap. If the
fifth begun attempt expires without a recorded result, its outcome is marked
unknown and terminal. The first transient failure retries after one minute,
then after two, four, and eight minutes. Five attempts is the maximum. Permanent failure
or the fifth transient failure is terminal. SMTP acceptance is recorded only
after the relay returns success. A crash between that success and committing
acceptance can cause a second SMTP submission; the row cannot prove inbox
delivery or guarantee exactly-once email.

The worker logs a fixed failure message without SMTP exception text. Status
surfaces only a short failure code. Daily retention removes final deliveries
older than 90 days, except records tied to open incidents and accepted alerts
still awaiting a recovery decision. Pending work has no retention cutoff.

HTTP threshold openings and push explicit or missing-report openings add alert
intents in the incident transaction. Later failures and late observations do
not add more. A fresh resolution obsoletes unsent alerts and adds recovery
intents only for recipients whose alert was SMTP-accepted and who remain
configured. Before submission the worker reloads incident, project, monitor,
recipient, pause, and maintenance facts. A resolved alert or deleted/removed
scope becomes obsolete; pause or maintenance defers without using a send
attempt. A transition after that check can still race an SMTP submission, as
ADR 0007 explains.

## Timed maintenance schema

`maintenance_window` records each finite project, HTTP monitor, or push monitor
window with its scope identity, start, planned end, optional early end, and
optimistic version. A unique scope/version index detects simultaneous new
windows. Starting an active scope extends the same row; starting after expiry
or early end appends another row and preserves history. Expiry is derived from
`ends_at <= server now`, so restart or delayed processing cannot leave a
window active forever. Project and monitor windows compose by union, with the
later active end as the effective expiry. Neither monitoring observations nor
incident lifecycle rows are changed by maintenance mutations.
Daily retention removes windows whose effective end is older than 90 days only
when a newer window exists for the scope; the latest version remains for
optimistic concurrency.

HTTP and push incidents each persist `notification_decision_at`. An opening
outside maintenance records that decision even when the project has no
recipients, preventing later recipient additions from replaying the incident.
An opening inside maintenance leaves it unset. The delivery worker scans open,
unannounced incidents after all effective windows end, locks their monitor row,
and atomically records the decision with the current recipients' alert intents.
A resolution inside maintenance records the decision without an alert. Accepted
alerts also have a recovery-decision marker: when SMTP acceptance races the
incident's resolution, the worker can add one matching recovery later.

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

The complete reporting lifecycle using these rows is in
[the push monitoring guide](./push-monitoring.md).

`push_monitor` belongs restrictively to one project and reserves its key for
its lifetime. It stores the immutable reporting mode, interval, tolerance,
state, optimistic version, lifecycle timestamps, evaluation generation,
monitor-local sequence allocation, last applied observation order, last
receipt, the next persisted deadline, and an optional bounded deadline-worker
lease. Independent latest-report and
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

The deadline worker claims only a strictly crossed deadline whose prior lease
is absent or expired. Claim tokens and expiry survive process loss; completion
rereads the locked monitor and abandons work when a fresh report, pause,
resume, or removal superseded it. A synthetic `report_missing` stores the
persisted deadline as its observation time and the worker time as its receipt
time, without pretending that a sender report was received. A partial unique
index on monitor, generation, and deadline makes that observation singular.
Once present it excludes the unchanged deadline from future claims, preserving
the real monitoring gap without manufacturing missed intervals.

Push history uses the same 90-day exclusive cutoff and reference protection as
HTTP history. Current receipt, success, deadline, state, and incident facts are
stored on durable rows and are never reconstructed from whatever detail remains
after retention. Report-ID uniqueness is retained for the full 90-day retry
boundary. Cleanup first removes resolved push incidents older than the cutoff,
then removes older reports unless a monitor points to them as latest report or
success or a remaining incident uses them for opening, latest failure, or
resolution. Open incidents and all of their referenced reports survive
regardless of age; an observation exactly at the cutoff survives.

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
report rejection, value constraints, one-open-push-incident uniqueness, and
pause/resume generation isolation with retained incident and success facts.
