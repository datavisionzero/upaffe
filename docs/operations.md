# Operations

Only the local development lifecycle is supported by the current foundation.
Production images, reverse-proxy configuration, upgrades, backup, restore, and
failed-upgrade recovery arrive in the operations epic and are not implied by
the commands below.

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

HTTP check/incident detail and push report/incident detail are pruned at startup
and every 24 hours under the 90-day product limit.
`HistoryRetention__Enabled=false` disables both workers for controlled
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
identities, and absence of generated access and monitor secrets from ordinary
HTTP/CLI artifacts and application logs. Explicit secret input and credential
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
