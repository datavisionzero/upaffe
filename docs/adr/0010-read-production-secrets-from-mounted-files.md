# 0010 — Read production secrets from mounted files

Status: accepted

The production Compose stack supplies the PostgreSQL password as a mounted
UTF-8 file. It passes the file location, not the value,
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

The initial operator password is instead read by the local `upaffe bootstrap`
command from standard input or a protected file. Its one-time token output is
redirected to another protected host file. Neither value is passed as a
command argument or placed in Compose environment values. The prior bootstrap
proof setting and mount are removed from fresh installations.

File locations must be absolute. Each file contains one nonempty UTF-8 line,
optionally ending in LF or CRLF. The database password is at most 1024
characters; the operator password is 12–200 characters. Read size is
bounded. Missing, unreadable, malformed, and conflicting inputs fail with
short setting-name errors that omit the path and secret. File contents never
enter ordinary logs, health responses, or status output.

The optional outbound heartbeat uses `UPAFFE_HEARTBEAT_URL_FILE` with an HTTPS
destination URL stored as a secret, since provider URLs commonly embed a token.
Its sender, URL validation, timeout, and retry policy are defined in
[ADR 0014](./0014-send-only-empty-healthy-heartbeats.md). There is no direct
environment-variable form for that URL in the production contract.
