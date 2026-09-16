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

Scheduled HTTP monitoring starts by default after migration and bootstrap. It
uses PostgreSQL for due work and recovery, so a normal application restart does
not require a separate queue. `Monitoring__Enabled=false` disables the worker
for controlled maintenance or test hosts; leaving it disabled stops new checks
and must not be treated as a healthy monitoring deployment.

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
overwritten. The current web slice does not claim monitoring health and does
not add operations outside the API and `ua` contracts.

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

## Disposable access and project system test

Run the complete implemented vertical slice from the repository root:

```sh
scripts/smoke.sh
```

The script builds an isolated Compose application on ports 18080 and 15432 by
default, starts with an empty database, and then performs the supported setup
and administration flow. It establishes the sole operator, signs in through the
browser-session path, creates and rotates a management credential, and manages
one stable project through browser-authenticated requests, the real generated
`ua` CLI, and a direct bearer API request. It proves the rotation overlap,
immediate revocation, one persisted operator, one surviving project identity,
and absence of its proof, password, cookie secret, and credential tokens from
ordinary HTTP/CLI artifacts and application logs. Explicit credential create
and rotate responses are excluded because revealing the newly issued token once
is their documented purpose.

The React component tests separately exercise the same generated browser
operations and verify secret-state clearing, keyboard operation, and bounded
rendering of API problems. Neither a green system test nor the existence of a
project claims that any monitor is healthy.

Override `UPAFFE_SMOKE_APP_PORT` or `UPAFFE_SMOKE_DB_PORT` when those ports are
occupied. The script always removes its containers and disposable volume.

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
