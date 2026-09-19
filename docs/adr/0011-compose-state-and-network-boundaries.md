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

The database container becomes healthy when `pg_isready` can connect. The
application starts after that condition and becomes healthy when
`/api/health/ready` confirms the expected schema. Compose health checks
describe technical startup and database readiness. The independent
monitoring-progress endpoint has a separate meaning and must not be replaced
by these checks.

Initial bootstrap uses a small optional Compose overlay that mounts the proof
file only while the operator is established. Removing the overlay recreates
the application without the proof mount. The password secret remains on both
services. Local secret source files live next to the deployment definitions
in a host directory restricted to the operator; they are never included in
the application image or the named database volume.
