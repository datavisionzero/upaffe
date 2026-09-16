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

The initial migration creates no product tables. It establishes the migration
history for a genuinely empty product model; monitoring tables arrive with the
features that define their rules.

Add a migration from the repository root after changing the context model:

```sh
dotnet tool restore
dotnet ef migrations add Name \
  --project src/Upaffe.Infrastructure \
  --output-dir Persistence/Migrations
```

Never edit an already published migration. Add another one on top.

## Tests

Integration tests use the same PostgreSQL 18 major intended for deployment.
One container is shared for a test run, while every test receives a fresh
database. The suite proves application to an empty database, idempotent and
concurrent startup, rejection of unknown migrations, and refusal to start when
the database is unavailable.
