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
check, and incident model decided in ADRs 0003 and 0004. Later changes add new
forward migrations; an existing migration is never rewritten after release.

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

`http_monitor` belongs to one project through a restrictive foreign key and
reserves its key within that project for its lifetime. It stores non-secret
configuration, scheduling facts, the current evaluation generation and ordered
sequence, consecutive failures, pause/removal timestamps, and separate foreign
keys for the latest result and latest success. Check constraints mirror the
interval, timeout, threshold, status, text-rule, state, sequence, and timestamp
boundaries in ADRs 0003 and 0004. New and resumed monitors have a due time;
paused and removed monitors do not. Its version is an optimistic concurrency
token for management changes.

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

`incident` retains the first, opening, latest-failure, and optional resolution
check references and their ordered sequences. Original and latest reasons and
all lifecycle times remain after resolution. A partial unique index on the
monitor ID where `resolved_at is null` is the final guard against two active
incidents for one monitor, including when application transitions race.
Historical retention is deliberately not set by this schema migration; its
bounded policy is decided with the history operation.

## Tests

Integration tests use the same PostgreSQL 18 major intended for deployment.
One container is shared for a test run, while every test receives a fresh
database. The suite proves application to an empty database, idempotent and
concurrent startup, rejection of unknown migrations, and refusal to start when
the database is unavailable. Persistence tests additionally exercise the
singleton operator, secret lifecycles, revocation, expiry, project identity,
monitor configuration and pause transitions, separated HTTP secrets, ordered
results, and incident uniqueness and resolution against PostgreSQL constraints
rather than an in-memory substitute.
