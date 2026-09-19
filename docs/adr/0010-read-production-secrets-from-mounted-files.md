# 0010 — Read production secrets from mounted files

Status: accepted

The production Compose stack supplies the PostgreSQL password and the one-time
bootstrap proof as mounted UTF-8 files. It passes file locations, not values,
to the application. PostgreSQL uses its `POSTGRES_PASSWORD_FILE` convention for
the same password. The API assembles its connection string in memory after
reading `UPAFFE_POSTGRES_PASSWORD_FILE`; nonsecret host, port, database, and
username settings are `UPAFFE_DB_HOST`, `UPAFFE_DB_PORT`, `UPAFFE_DB_NAME`, and
`UPAFFE_DB_USER`. Their defaults are `db`, `5432`, `upaffe`, and `upaffe`.

Development retains `ConnectionStrings__Postgres`. It cannot be combined with
`UPAFFE_POSTGRES_PASSWORD_FILE`; the startup error names the conflicting
settings without repeating either value. The production stack never embeds a
password-bearing connection string in Compose interpolation or container
environment. Diagnostic rendering of database settings is a fixed redacted
message because connection-string values can contain quoted semicolons.

`UPAFFE_BOOTSTRAP_SECRET_FILE` replaces `UPAFFE_BOOTSTRAP_SECRET` for a fresh
production installation. Supplying both is an error, even if an operator
already exists. The API reads the proof file only while bootstrap is needed;
after establishment, removing the file does not prevent the application from
starting. The operator identity and consumed grant remain in PostgreSQL.
Compose configuration must also remove the obsolete secret mount before its
source file is deleted. The API still permits the original environment
variable for local development and existing integrations.

File locations must be absolute. Each file contains one nonempty UTF-8 line,
optionally ending in LF or CRLF. The password is at most 1024 characters, as
is the bootstrap proof, whose existing minimum is 32 characters. Read size is
bounded. Missing, unreadable, malformed, and conflicting inputs fail with
short setting-name errors that omit the path and secret. File contents never
enter ordinary logs, health responses, or status output.

The optional outbound heartbeat uses `UPAFFE_HEARTBEAT_URL_FILE` with an HTTPS
destination URL stored as a secret, since provider URLs commonly embed a token.
Its sender, URL validation, timeout, and retry policy are defined in
[ADR 0014](./0014-send-only-empty-healthy-heartbeats.md). There is no direct
environment-variable form for that URL in the production contract.
