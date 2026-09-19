# Operations

Production installation, backup, update, and recovery are the first sections
of this guide. Local development procedures follow them.

## Production image identity

The production application image is published from validated `main` commits as
`ghcr.io/datavisionzero/upaffe:sha-<full-commit-sha>`. Pin that full revision tag
or its registry digest in a deployment. The published image supports Linux
amd64 and arm64 hosts. It contains the compiled React
application, .NET API, and embedded forward migrations. It listens on port
`8080` as a non-root user and reports `0.0.0-rev.<full-commit-sha>` in the
`Upaffe-Version` response header. A local build uses `0.0.0-dev` unless an
`APP_VERSION` build argument is supplied. The publication workflow needs only
the scoped GitHub Actions package token; deployment secrets are supplied at
runtime and never enter the image build.

The production Compose installation procedure is below. Pin a published image
from the same source revision as the Compose files.

## Production secret inputs

The application accepts these mounted secret files at startup. Paths are
passed as environment values; secret contents are not. See
[ADR 0010](./adr/0010-read-production-secrets-from-mounted-files.md) for the
conflict and validation rules.

| Application setting | File contents | When needed |
| --- | --- | --- |
| `UPAFFE_POSTGRES_PASSWORD_FILE` | PostgreSQL password shared with `POSTGRES_PASSWORD_FILE` in the database container | Every start |
| `UPAFFE_BOOTSTRAP_SECRET_FILE` | One-time random proof, 32–1024 characters | Only until the operator is established |
| `UPAFFE_HEARTBEAT_URL_FILE` | Optional HTTPS URL for an independent receiver | Only when the outbound sender is enabled |

Files contain one UTF-8 line with an optional trailing newline. Keep their
source files outside the public checkout and readable only by the operator and
the containers that need them. An unset bootstrap proof leaves a fresh instance
unavailable for establishment. Once the operator exists, remove the bootstrap
mount and setting from Compose before deleting its source file; the application
will then start without it. The database password file remains required.

The file-backed database configuration defaults to host `db`, port `5432`,
database `upaffe`, and user `upaffe`. Override these nonsecret fields with
`UPAFFE_DB_HOST`, `UPAFFE_DB_PORT`, `UPAFFE_DB_NAME`, and `UPAFFE_DB_USER` when
needed. `ConnectionStrings__Postgres` and `UPAFFE_BOOTSTRAP_SECRET` remain
available for local development. Each direct setting conflicts with its file
form; startup reports setting names only. No database connection string or
proof belongs in production Compose environment values.

## Production Compose startup

Choose a validated `main` revision whose image tag has been published. Download
the deployment files from that same revision into one directory. This uses no
source checkout or application runtime on the host. Replace the revision
placeholder in the first line with its full 40-character commit SHA:

```sh
set -eu
UPAFFE_REV=REPLACE_WITH_FULL_COMMIT_SHA
mkdir -m 0700 upaffe-deploy
cd upaffe-deploy
for file in docker-compose.yml docker-compose.bootstrap.yml \
  docker-compose.heartbeat.yml docker-compose.verify-restore.yml \
  backup-production.sh restore-production.sh production.env.example \
  nginx-upaffe.conf.example; do
  curl --fail --location --silent --show-error \
    "https://raw.githubusercontent.com/datavisionzero/upaffe/$UPAFFE_REV/deploy/$file" \
    --output "$file"
done
chmod 0700 backup-production.sh restore-production.sh
cp production.env.example .env
```

Edit `.env`: replace `REPLACE_WITH_FULL_COMMIT_SHA` with the same revision,
leaving `UPAFFE_IMAGE` pinned to its full GHCR revision tag. Keep the deployment
directory, `.env`, and these exact Compose files for the installation's lifetime.
The optional overlays are used only for their named procedures.

Create a `secrets` directory in the same directory as the Compose files with
mode `0700`. Put a generated password in `secrets/postgres_password` and a
separate generated proof in `secrets/bootstrap_proof`, each on one line. The
file sources need mode `0644` inside this operator-only directory so the
non-root application and PostgreSQL containers can read their mounts. The
directory prevents other host users from traversing to them. Keep both files
out of source control and backups that are not access controlled. For example,
from the deployment directory:

```sh
mkdir -m 0700 secrets
openssl rand -base64 48 > secrets/postgres_password
openssl rand -base64 48 > secrets/bootstrap_proof
chmod 0644 secrets/postgres_password secrets/bootstrap_proof
docker compose -f docker-compose.yml -f docker-compose.bootstrap.yml config --quiet
docker compose -f docker-compose.yml -f docker-compose.bootstrap.yml up -d --wait
```

The first start waits for PostgreSQL and applies forward migrations. The app
listens at `http://127.0.0.1:8080` unless `UPAFFE_PORT` changes the local host
port. Use the browser bootstrap form over this loopback address or an SSH
tunnel, then remove the one-time proof mount and source file:

```sh
docker compose -f docker-compose.yml up -d --wait
rm secrets/bootstrap_proof
curl --fail-with-body http://127.0.0.1:8080/api/health/ready
```

The first command recreates the application without the bootstrap overlay;
the database volume remains. `docker compose ps` shows container health.
`/api/health/live` proves that the process answers, and `/api/health/ready`
proves PostgreSQL and schema readiness. These do not prove that monitoring
workers are progressing. The database has no host port; the only host binding
is the application's loopback HTTP port for a trusted reverse proxy.

`/api/health/progress` is the separate public signal for independent checking.
It returns `200 {"status":"progressing"}` only after both the scheduled HTTP
and push-deadline workers have completed a successful database claim or
completion within two minutes. An idle poll counts, so an installation with no
due work can be healthy. Startup, disabled monitoring, a stalled worker, or a
sustained database failure return `503 {"status":"stalled"}` no later than two
minutes after the last successful iteration. A fresh successful iteration is
required for recovery. Point an external checker at the HTTPS proxy URL and
poll at least once per minute from outside this host's failure domain; a
missing HTTP response is also a failure. The response contains no monitor or
project data. See [ADR 0013](./adr/0013-report-worker-progress-separately-from-readiness.md).

## Optional outbound heartbeat

The base Compose stack makes no outbound heartbeat request. To opt in, arrange
an independent HTTPS receiver outside this host and its failure domain. Write
its full URL, including any receiver token, to `secrets/heartbeat_url` in the
deployment directory. The file must be a single UTF-8 line, at most 2048
characters, and readable by the non-root application container. It must use
a multi-label DNS name; loopback, IP literals, `.local`, URL user information,
and fragments are rejected. Keep the source file in the protected `secrets`
directory and out of source control. Copy `docker-compose.heartbeat.yml` from
the same source revision and start with both Compose files:

```sh
chmod 0644 secrets/heartbeat_url
docker compose -f docker-compose.yml -f docker-compose.heartbeat.yml up -d --wait
```

The sender makes an empty GET to that URL only while both monitoring workers
have progressed within two minutes. It waits one minute between attempts, with
a five-second timeout and no immediate retry. A receiver should alert after
more than three minutes without a successful request. Check the public HTTPS
`/api/health/progress` endpoint separately: its response distinguishes worker
progress from a missing heartbeat caused by network or receiver failure. The
sender does not log the destination, read the response body, or send project
data. Removing the overlay and recreating the app disables the sender. See
[ADR 0014](./adr/0014-send-only-empty-healthy-heartbeats.md).

## Production workflow validation

Maintainers can rehearse the production path with two already published
revision images. Run this from a source checkout containing the matching
`deploy/` and `scripts/` files. Docker Compose, `curl`, `jq`, `openssl`, and
Python 3 are required; the scripts do not build an application image:

```sh
scripts/check-production-workflow.sh \
  ghcr.io/datavisionzero/upaffe:sha-REPLACE_WITH_PREVIOUS_FULL_SHA \
  ghcr.io/datavisionzero/upaffe:sha-REPLACE_WITH_CURRENT_FULL_SHA
```

The command first exercises the optional sender against a local HTTPS fixture
receiver. It checks that the base stack sends nothing, that healthy workers
send, that live and ready HTTP endpoints still answer while disabled monitoring
makes progress return `503`, that no heartbeat is sent during that stall, and
that progress and sends resume after monitoring restarts. The receiver's
fixture URL is not printed by the command.

It then installs the previous image from empty state, establishes a fixture
operator, creates a project and push monitor, records a failed report, and
checks its incident, deadline, and pending notification after container
recreation. It backs up, proves a failed image pull can be rolled back, updates
to the current image, and confirms worker progress and preserved state. A
fixture future migration makes the previous image reject that database; the
command restores the pre-upgrade backup into a separate project and volume,
then verifies operator access, report evidence, incident, deadline, and
pending notification there. It also rejects reused restore destinations and
tampered archives. The script prints each phase and exits nonzero on any
failed assertion.

The tests use only invented addresses and local loopback ports `18083`,
`18084`, and `18087` by default. Override those with
`UPAFFE_WORKFLOW_TEST_PORT`, `UPAFFE_RESTORE_TEST_PORT`, and
`UPAFFE_HEARTBEAT_TEST_PORT` if needed. Each script removes only its own
temporary directory, Compose projects, and volumes on exit. Run this separately
from the development smoke test and migration tests; the production rehearsal
uses published images and the actual Compose deployment procedure.

See [ADR 0011](./adr/0011-compose-state-and-network-boundaries.md) for the
state and network boundaries. The backup and restore procedure is below;
routine updates and failed-upgrade recovery follow in the operator runbook.

## Back up a production installation

The `postgres-data` volume holds all application state: operator access,
projects, monitor definitions and stored headers, incidents and history,
deadlines, email settings and password, and pending deliveries. Back up the
whole `upaffe` database, not selected tables. Also retain the deployment
`.env`, the Compose files in use, the backup/restore helpers and verification
overlay, and `secrets/postgres_password`; include the optional heartbeat URL
or a still-active bootstrap proof when present. The
reverse proxy's TLS certificates and configuration, DNS, external receiver
account, and any other infrastructure outside this Compose stack need their
own backup or re-provisioning. Existing clients must retain their issued
reporting tokens; the database stores token hashes and cannot recover their
plaintext values.

Copy `backup-production.sh`, `restore-production.sh`, and
`docker-compose.verify-restore.yml` from `deploy/` at the same revision as
the Compose files into the deployment directory. Run the
backup helper there with the Compose project name used for this installation
(`upaffe` by default) and a destination path that does not already exist:

```sh
mkdir -m 0700 ../upaffe-backups
./backup-production.sh ../upaffe-backups/2026-09-19 upaffe
```

The helper creates a mode `0700` directory, copies the deployment inputs, and
streams a consistent PostgreSQL custom-format snapshot into
`database.dump`. It records a SHA-256 checksum and removes an incomplete
backup after any failure. PostgreSQL's [pg_dump documentation](https://www.postgresql.org/docs/18/app-pgdump.html)
states that a dump remains consistent while application writes continue.
Avoid changing deployment configuration or rotating mounted secrets during
the backup. Treat the entire directory as confidential: the dump includes
saved credentials and private monitoring data. Transfer it to protected,
off-host storage with access control and encryption appropriate to the
operator's environment. The helper runs only when invoked; upaffe does not
schedule or retain backups.

## Restore to a clean host or Compose project

Start with a host that has Docker Compose and the protected backup directory.
Use the PostgreSQL major version and application image pinned in the saved
`.env` first; do not treat restore as an application upgrade. The restore
helper refuses an existing destination directory, database volume, Compose
network, or project container. Choose an unused project name, especially
when testing on the same host:

```sh
./restore-production.sh ../upaffe-backups/2026-09-19 ../upaffe-restored upaffe-restore
```

The helper verifies the archive checksum, copies the deployment inputs to a
mode `0700` directory, starts only a new PostgreSQL container and volume,
checks the archive, and restores it in one transaction. It never attaches to
or replaces the source volume. A failed restore leaves only the isolated
destination for inspection; its protected `restore-error.log` may contain
database diagnostics. PostgreSQL documents
[`pg_restore --single-transaction`](https://www.postgresql.org/docs/18/app-pgrestore.html)
as all-or-nothing for the restored commands. A dump may generally load into
a newer PostgreSQL major version, but this guide's verified path uses the
saved major version and app image first.

Before returning it to service, verify the restored app on a separate
loopback port with monitoring and email delivery disabled. From the new
deployment directory, using the same project name chosen above:

```sh
UPAFFE_PORT=18082 UPAFFE_TRUSTED_PROXY_IPS= UPAFFE_PUBLIC_ORIGIN= \
  docker compose -p upaffe-restore -f docker-compose.yml \
  -f docker-compose.verify-restore.yml up -d --wait app
curl --fail-with-body http://127.0.0.1:18082/api/health/ready
curl --fail-with-body http://127.0.0.1:18082/api/bootstrap
```

Readiness must be `ready`, and bootstrap must report that an operator
already exists. Sign in through the loopback UI or an SSH tunnel using the
restored operator password. Confirm projects, monitor settings, a known
incident and history record, deadlines, and pending email deliveries. The
verification overlay prevents duplicate checks and sends while the original
instance may still run; `/api/health/progress` correctly reports stalled in
this mode. A disposable verification copy can be removed with
`docker compose -p upaffe-restore -f docker-compose.yml -f docker-compose.verify-restore.yml down --volumes`
only after confirming its project name and that it is no longer needed.

For a permanent move, stop the old application before enabling the restored
one. Reconfigure the new host's loopback port, HTTPS proxy, trusted proxy IP,
public origin, TLS certificates, DNS, and external receiver as appropriate;
re-enter or rotate external SMTP credentials if the provider changed. Remove
the verification overlay, start the base stack with the optional heartbeat
overlay if used, and check readiness, progress, operator sign-in, and an SMTP
test send. Keep the original installation and backup until these checks pass.

## HTTPS reverse proxy

The production Compose port is bound to host loopback so a reverse proxy on
that host can terminate HTTPS. Use the
[Nginx example](../deploy/nginx-upaffe.conf.example) as a starting point;
replace its fictional hostname and certificate files. Keep the application
port and PostgreSQL port off public interfaces. The proxy must replace
`X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` with the actual
client address, `https`, and the public host. It must not pass client-supplied
values for those headers. The example limits the request path in access logs
to a redacted marker for `/api/report/*`, omits query strings, and disables
request-bearing proxy error logs. Apply equivalent redaction to any other
proxy, load balancer, or ingress logs; simple reporting paths contain secrets.

After the first Compose start has created `upaffe_edge`, find the gateway IP
that the application sees for connections through the host port:

```sh
docker network inspect upaffe_edge --format '{{(index .IPAM.Config 0).Gateway}}'
```

Put that exact address in `UPAFFE_TRUSTED_PROXY_IPS` in the deployment `.env`
and set `UPAFFE_PUBLIC_ORIGIN` to the external HTTPS origin, for example
`https://status.example.test`. The setting accepts up to eight comma-separated
IP addresses, not hostnames or broad network ranges. Restart the application
with `docker compose -f docker-compose.yml up -d --wait`. If the Docker network
is recreated, inspect its gateway again and update the setting. The app
accepts forwarded identity only from those addresses, considers at most one
proxy hop, and accepts a forwarded host only for the configured public origin.
Both settings must be supplied together. With neither set, direct local HTTP
continues to work without forwarded headers.

Under HTTPS, sign-in issues a `Secure`, host-scoped `__Host-upaffe_session`
cookie, and browser writes must carry an Origin matching the public HTTPS
scheme and host.
Check this through the public URL after configuring the proxy. Set the SMTP
public base URL to the same HTTPS origin when configuring incident links;
that saved email setting is separate from `UPAFFE_PUBLIC_ORIGIN`.

## Routine production operation

Run these commands from the deployment directory with the project name used
at installation; add `-p NAME` if it differs from the default `upaffe`. If
the optional heartbeat overlay is active, include
`-f docker-compose.heartbeat.yml` on `config` and `up` commands so a recreation
retains its secret mount.

```sh
docker compose -f docker-compose.yml ps
docker compose -f docker-compose.yml logs --tail=100 app db
docker compose -f docker-compose.yml exec -T db df -h /var/lib/postgresql
docker system df
```

The logs are operational diagnostics; keep copies private. Check disk space
before image updates and backups. A normal restart or app container recreation
keeps the PostgreSQL volume:

```sh
docker compose -f docker-compose.yml restart app
docker compose -f docker-compose.yml up -d --wait --force-recreate app
```

The app applies pending forward migrations before it becomes ready. A restart
may briefly return `503` from `/api/health/progress` until both workers have
polled. `docker compose down` without `--volumes` also preserves state, but
stops PostgreSQL and is unnecessary for routine app restarts. Never use
`down --volumes` for a production restart.

## Update the production image

Choose a new validated `main` revision with a published image. Review its
changes and download the matching deployment files into a separate
staging directory. Replace the placeholder with its full commit SHA:

```sh
set -eu
UPAFFE_NEXT_REV=REPLACE_WITH_FULL_COMMIT_SHA
mkdir -m 0700 ../upaffe-next
for file in docker-compose.yml docker-compose.bootstrap.yml \
  docker-compose.heartbeat.yml docker-compose.verify-restore.yml \
  backup-production.sh restore-production.sh; do
  curl --fail --location --silent --show-error \
    "https://raw.githubusercontent.com/datavisionzero/upaffe/$UPAFFE_NEXT_REV/deploy/$file" \
    --output "../upaffe-next/$file"
done
```

Review differences in every deployed file. Keep the current files in place
until the pre-upgrade backup completes; that backup must contain the old
revision's deployment contract. Use the same Compose file set throughout the
update. The commands below show the base stack.

Stop the app so the backup is the exact pre-upgrade point, then create a new
protected backup. The database container stays running for `pg_dump`. If the
backup fails, restart the unchanged old app and resolve that failure first.

```sh
docker compose -f docker-compose.yml stop app
./backup-production.sh ../upaffe-backups/pre-upgrade-2026-09-19 upaffe
```

Now install the reviewed Compose files and helpers from staging, retaining
the current `.env` and `secrets/` directory:

```sh
cp ../upaffe-next/docker-compose.yml ../upaffe-next/docker-compose.bootstrap.yml \
  ../upaffe-next/docker-compose.heartbeat.yml \
  ../upaffe-next/docker-compose.verify-restore.yml \
  ../upaffe-next/backup-production.sh ../upaffe-next/restore-production.sh .
chmod 0700 backup-production.sh restore-production.sh
```

Edit only `UPAFFE_IMAGE` in `.env` to the new full `sha-<commit>` tag or a
registry digest. Keep `UPAFFE_POSTGRES_IMAGE` on its saved major version;
changing PostgreSQL major versions is a separate migration. Then validate,
pull, and start the new app. `up --wait` fails if readiness does not become
healthy:

```sh
docker compose -f docker-compose.yml config --quiet
docker compose -f docker-compose.yml pull app
docker compose -f docker-compose.yml up -d --wait app
curl --fail-with-body http://127.0.0.1:8080/api/health/ready
curl --fail-with-body --retry 6 --retry-delay 2 \
  http://127.0.0.1:8080/api/health/progress
```

If `UPAFFE_PORT` differs from `8080`, use that loopback port in the checks.
After startup, verify the public HTTPS login, a representative monitor, the
external progress checker, the optional heartbeat receiver, and an
[SMTP test send](#smtp-setup-and-test-send). A live or ready process alone does
not prove that monitoring or email delivery works. Retain the pre-upgrade
backup until the new revision has operated successfully.

### Failed upgrade and recovery

If the image could not be pulled or the new container never started, restore
the old `.env` and deployment files from the pre-upgrade backup, then start
the app again. The database has not been touched by the new binary:

```sh
cp ../upaffe-backups/pre-upgrade-2026-09-19/.env .env
cp ../upaffe-backups/pre-upgrade-2026-09-19/docker-compose.yml .
docker compose -f docker-compose.yml up -d --wait app
```

Restore any optional old overlay from that backup as well. If the new container
started, stop it and inspect container state and the app/database logs:

```sh
docker compose -f docker-compose.yml stop app
docker compose -f docker-compose.yml ps
docker compose -f docker-compose.yml logs --tail=100 app db
```

Startup applies forward-only migrations before HTTP is ready, and an older
binary rejects a schema it does not know. There is no automatic schema
downgrade.

When a newer migration was applied, reverting the image also requires the
pre-upgrade database backup. If it is unclear whether startup changed the
schema or accepted writes, use that backup as well. Do not run the old image
against the possibly upgraded volume or restore over it. Use
`restore-production.sh` with the pre-upgrade backup to create a fresh Compose
project and volume:

```sh
./restore-production.sh ../upaffe-backups/pre-upgrade-2026-09-19 \
  ../upaffe-recovered upaffe-recovered
```

Follow the [restore verification procedure](#restore-to-a-clean-host-or-compose-project)
with `upaffe-recovered` as the project name. Cut over after stopping the
failed stack. The saved backup `.env` pins the old image and PostgreSQL major
version. This path deliberately preserves the failed volume for diagnosis.

## Troubleshooting production health

Use the three health paths for different questions:

| Signal | What it proves | Next check when it fails |
| --- | --- | --- |
| `/api/health/live` | HTTP process answers | `docker compose ps`, app logs, port and proxy routing |
| `/api/health/ready` | PostgreSQL answers with exactly this image's migrations | Database health and disk space, app migration logs, secret-file mount |
| `/api/health/progress` | Both monitoring workers recently completed a database-backed iteration | `Monitoring__Enabled`, worker retry logs, PostgreSQL reachability |

From the host, use the loopback URL; after configuring HTTPS, run the same
checks through the public origin, replacing the fictional hostname below:

```sh
curl --fail-with-body https://status.example.test/api/health/progress
```

Point the independent progress checker at that public HTTPS URL from outside
this host's failure domain. A missing HTTP response is a failure even though
no JSON status arrives. A missing outbound heartbeat with healthy progress
calls for checking DNS, TLS, network egress, and the independent receiver; the
receiver URL is a secret and is not printed by the app. Persistent email
delivery failures call for the authenticated
delivery summary, relay configuration, and a new test send. A green health
endpoint cannot prove SMTP acceptance or inbox delivery. If browser sign-in
or writes fail only behind HTTPS, recheck the exact trusted proxy gateway IP,
public origin, overwritten forwarded headers, and cookie/Origin behavior in
the [proxy section](#https-reverse-proxy).

## Local Compose environment

From the repository root:

```sh
docker compose -f deploy/docker-compose.dev.yml up --build --wait
```

This builds the React application and .NET API into one development image and
starts it beside PostgreSQL 18. The database healthcheck must pass before the
application is started. The application then migrates the schema and becomes
healthy only when `/api/health/ready` verifies the database.

Scheduled HTTP monitoring and push deadline detection start by default after
migration and bootstrap. They use PostgreSQL for due work, bounded leases, and
recovery, so a normal application restart does not require a separate queue.
`Monitoring__Enabled=false` disables both workers for controlled maintenance or
test hosts; leaving it disabled stops new checks and missing-report detection
and must not be treated as a healthy monitoring deployment.

HTTP check/incident detail, push report/incident detail, final email deliveries,
and superseded maintenance windows are pruned at startup
and every 24 hours under the 90-day product limit.
`HistoryRetention__Enabled=false` disables the retention workers for controlled
maintenance or tests. Leaving it disabled allows unbounded database growth and
is not a supported steady-state configuration. Cleanup reports only deleted
row counts and never logs retained result data or secrets.

The default addresses are:

- application: `http://localhost:8080`
- PostgreSQL: `localhost:5432`

Plain HTTP is supported for local setup and uses a non-`Secure` development
session cookie. Do not expose that transport beyond a trusted development
machine; installed instances require HTTPS at their deployment boundary.

The default database password, `local-development-only`, is intentionally
fictitious and unsuitable outside a developer machine. Override values in the
shell or in the ignored `deploy/.env`:

| Variable | Default | Purpose |
| --- | --- | --- |
| `UPAFFE_DEV_DB_PASSWORD` | `local-development-only` | PostgreSQL password shared only by the two local containers |
| `UPAFFE_DEV_DB_PORT` | `5432` | PostgreSQL host port |
| `UPAFFE_DEV_PORT` | `8080` | application host port |
| `UPAFFE_BOOTSTRAP_SECRET` | unset | One-use installation proof; 32-1024 characters and valid for 30 minutes after startup |

## Establish the operator

A database without an operator reports `{"required":true,"available":false}`
from `GET /api/bootstrap`. Generate a random proof locally, supply it only as
`UPAFFE_BOOTSTRAP_SECRET`, and restart the application. For example:

```sh
export UPAFFE_BOOTSTRAP_SECRET="$(openssl rand -base64 32)"
docker compose -f deploy/docker-compose.dev.yml up --build --wait
```

The state then reports `available:true` for 30 minutes. Submit the proof in the
JSON request body together with the operator email and a password of 12-200
characters:

```sh
curl --fail-with-body http://localhost:8080/api/bootstrap \
  --header 'Content-Type: application/json' \
  --data-binary @- <<EOF
{"proof":"$UPAFFE_BOOTSTRAP_SECRET","email":"operator@example.test","password":"a long local password"}
EOF
unset UPAFFE_BOOTSTRAP_SECRET
```

Remove the variable from the environment or ignored `deploy/.env` after the
request succeeds. The database stores only the proof digest, success consumes
the grant, and future starts cannot arm bootstrap again while the operator
exists. A restart before success may arm a new proof; an expired, missing, or
incorrect proof receives the same `bootstrap_rejected` response.

## Browser workflow

Opening the application root checks bootstrap and session state through the
same documented API. A database without an operator shows the establishment
form; when no live proof is armed, it explains that setup cannot proceed and
offers a state refresh. After establishment, or on an initialized instance
without a live session, the application shows sign-in. Proof and password
values are password fields and leave browser component state when submitted.

After sign-in, the project workspace lists live projects by default. It creates
a project from an immutable key and mutable display name, renames at the version
shown, soft-deletes, switches to the deleted list, and restores. A concurrent
change is shown as a conflict and the list is refreshed rather than silently
overwritten. A live project opens its HTTP monitors for configuration, current
state, immediate tests, pause/resume, retained removal, secret-header writes,
and check/incident history. These views report persisted monitoring facts but
do not widen the API and `ua` contracts or replace the technical health paths.
The same workspace switches to push monitors for both reporting modes,
deadlines, reports, incidents, pause/resume, and one-time credential handoff.

## SMTP setup and test send

The complete email and maintenance workflow is in
[the email and maintenance guide](./email-maintenance.md).

Configure one relay and sender through the authenticated email settings API.
The security mode is `starttls` for a relay that upgrades a plain connection,
`tls` for TLS from connection start, or `none` for an explicitly trusted local
relay. Set its host, port, sender address, and a public base URL such as
`https://status.example.test` for incident links. Configure an authentication
username and replace the write-only password only if the relay requires it.
Set instance default recipients before creating projects, then inspect or
replace each project's own list. Existing projects do not follow later default
changes.

Use `POST /api/email/test` with an explicit `recipient` after configuration.
The `accepted_by_smtp` result means the relay accepted the message for
processing; it cannot establish inbox delivery. A configuration or relay
failure returns a sanitized problem code. This test creates no incident and
does not retry on its own. Keep SMTP credentials in the authenticated write
request and out of shell history, logs, and ordinary status output.

Incident email uses durable work. Transient failures retry up to five total
attempts with one, two, four, and eight minute delays; permanent failures stop.
The worker recovers an expired two-minute claim after a restart. SMTP may have
accepted a message just before a crash without upaffe recording that fact, so
the retry can produce a duplicate even though no second logical notification
is created. `EmailDelivery__Enabled=false` disables draining for controlled
tests; leaving it disabled on a running instance accumulates pending work.

Timed maintenance can cover a project or one HTTP or push monitor. During an
active window, checks and reports still run, incidents still open and resolve,
and the worker holds alert and recovery submissions. Project and monitor
windows overlap; email resumes only after both have ended. Expiry uses the
server clock even across restart. An incident still open after expiry gets one
current-recipient alert, while an incident that opened and resolved entirely
inside maintenance remains in history without delayed mail. A recovery for an
alert accepted before maintenance waits until the window ends. Pause remains a
separate control that suspends monitoring rather than just email.

Use `GET /api/email/deliveries/summary` for instance counts,
`GET /api/projects/{key}/email-summary` for project counts, and
`GET /api/email/deliveries` to inspect bounded per-recipient history. Filter by
project, monitor, incident, or stored delivery state. The incident status
operation adds the announcement decision and any active suppression reason.
`accepted` is relay acceptance, while `terminal_failure` needs operator review
of SMTP settings and a new test send. Raw relay diagnostics are deliberately
absent; only stable failure codes appear. History older than 90 days may have
been pruned, but delivery records for open incidents remain available for a
future recovery decision.

## Simple push reporting

Credential issuance for a push monitor explicitly returns an instance-relative
secret reporting path once. Store that path as a deployment secret, combine it
only with the trusted upaffe origin, and call it after the job has completed
successfully. A caller that can make a simple request needs no JSON client:

```sh
curl --fail-with-body --request POST "$UPAFFE_ORIGIN$UPAFFE_REPORT_PATH"
```

`GET` has the same semantics for constrained callers. Every accepted request is
a separate success; use the JSON reporting route when a sender needs explicit
failure, retry identity, or sender observation time. Never put the secret URL
in source control, command history, ordinary status output, or monitoring
exports.

upaffe redacts the secret segment before its own routing and application logs
and suppresses the framework request-start log that would run before that
redaction. A reverse proxy sits outside this boundary: configure it not to log
`/api/report/*` paths verbatim, replacing the final segment with a fixed marker.
Rotation keeps the prior URL valid for five minutes; after that window or an
explicit revocation, it returns the same `401` as an unknown secret.

## Change cycle

For quick API work, leave only PostgreSQL running and start .NET on the host:

```sh
docker compose -f deploy/docker-compose.dev.yml up -d db --wait
ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=upaffe;Username=upaffe;Password=local-development-only' \
  dotnet watch --project src/Upaffe.Api
```

For web work, keep the API on port 5000 and run Vite separately; it forwards
all `/api` requests:

```sh
npm run dev --prefix src/web
```

Rebuild the composed application after source changes:

```sh
docker compose -f deploy/docker-compose.dev.yml up --build --wait
```

## Disposable monitoring system test

For the noninteractive administration path and a mapping from every current
web management action to a CLI command, see the
[unattended administration workflow](./agent-workflow.md).

Run the complete implemented vertical slice from the repository root:

```sh
scripts/smoke.sh
```

The script builds an isolated Compose application on ports 18080 and 15432 by
default, starts with an empty database, and then performs the supported setup
and administration flow. It establishes the sole operator, signs in through the
browser-session path, creates and rotates a management credential, and manages
one stable project and HTTP monitor through browser-authenticated requests, the
real generated `ua` CLI, and a direct bearer API request. The HTTP monitor observes a
scheduled and requested success, crosses a two-failure threshold into exactly
one incident, survives restarts while planned and while failing, exercises
pause/resume through both access paths, and records a fresh recovery. It proves
credential rotation overlap, immediate revocation, singular persisted
identities, repeated keyed project and monitor creates, stale version errors,
stdin and file input, one-call project reports, and absence of generated access
and monitor secrets from ordinary HTTP/CLI artifacts and application logs.
Explicit secret input and credential
issuance files are excluded because those are their documented secret-bearing
purposes.

The same run creates job-completion and state-report push monitors, issues
monitor-scoped credentials, and reports through JSON and simple secret paths.
It proves immediate and repeated failure, duplicate retry, old-success
ordering, fresh recovery, silence after interval plus tolerance, restart
durability, pause/resume generations, browser/CLI agreement, reporting-token
isolation, rotation overlap, revocation, and absence of every generated
reporting secret from ordinary artifacts and logs.

The React component tests separately exercise the same generated browser
operations for access, projects, HTTP monitors, and push monitors. They verify secret-state
clearing, keyboard operation, explicit lifecycle labels, optimistic-concurrency
refresh, and bounded rendering of API problems.

The system path requires outbound HTTPS and uses `https://example.com/` by
default. `UPAFFE_SMOKE_HTTP_TARGET` may select another stable, publicly routable
HTTPS target that returns status 200; private/local fixtures are deliberately
rejected by the monitor boundary. Passing the test proves the created monitor's
observations, not the health of any unrelated target.

Override `UPAFFE_SMOKE_APP_PORT` or `UPAFFE_SMOKE_DB_PORT` when those ports are
occupied. The script always removes its containers and disposable volume. See
[the HTTP monitoring guide](./http-monitoring.md) and
[push monitoring guide](./push-monitoring.md) for complete fictional operator
workflows and the deterministic security-test boundary.

## Stop, restart, and reset

A normal stop preserves the named PostgreSQL volume:

```sh
docker compose -f deploy/docker-compose.dev.yml down
docker compose -f deploy/docker-compose.dev.yml up --wait
```

Restart one service without removing state:

```sh
docker compose -f deploy/docker-compose.dev.yml restart app
```

Remove the development database only when that data is deliberately disposable:

```sh
docker compose -f deploy/docker-compose.dev.yml down --volumes
```

The final command is destructive for the local development database. It does
not describe a production reset or recovery procedure.
