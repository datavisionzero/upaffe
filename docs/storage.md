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

The initial migration creates no product tables. The next migration adds only
the access and project model decided in ADR 0002; monitoring tables arrive with
the features that define their rules.

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

## Tests

Integration tests use the same PostgreSQL 18 major intended for deployment.
One container is shared for a test run, while every test receives a fresh
database. The suite proves application to an empty database, idempotent and
concurrent startup, rejection of unknown migrations, and refusal to start when
the database is unavailable. Persistence tests additionally exercise the
singleton operator, secret lifecycles, revocation, expiry, and project identity
against PostgreSQL constraints rather than an in-memory substitute.
