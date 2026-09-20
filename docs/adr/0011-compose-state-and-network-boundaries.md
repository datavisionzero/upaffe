# 0011 — Keep production state in PostgreSQL behind a local application port

Status: accepted

The supported production Compose project has one application container and one
PostgreSQL 18 container. The application pulls an operator-pinned published
image and never builds source on the host. PostgreSQL owns the named
`postgres-data` volume mounted at `/var/lib/postgresql`; no application data
is expected in the replaceable application container. This includes operator
access, projects, monitors, secrets, incidents, schedules, and queued email
work. Database backups and restores operate on this state, plus the external
deployment configuration and secret files.

PostgreSQL joins only an internal Compose `database` network and has no host
port. The application joins that network and a separate `edge` network for
outbound checks and email. Its HTTP port is bound to `127.0.0.1` on the host by
default. An existing host reverse proxy terminates HTTPS and forwards to that
local port. The proxy trust and header policy are a separate decision.
See [ADR 0012](./0012-trust-only-named-https-proxies.md).

The database container becomes healthy when `pg_isready` can connect. The
application starts after that condition and becomes healthy when
`/api/health/ready` confirms the expected schema. Compose health checks
describe technical startup and database readiness. The independent
monitoring-progress endpoint has a separate meaning and must not be replaced
by these checks.

Initial setup starts PostgreSQL first, runs the application image once with
`upaffe bootstrap` and a protected password file redirected through standard
input, then starts the application service. The command writes its one-time
credential JSON to a protected host file. No bootstrap overlay or proof mount
is needed. The database password secret remains on both services. Local secret
source files live next to the deployment definitions in a host directory
restricted to the operator; they are never included in the application image
or the named database volume.
