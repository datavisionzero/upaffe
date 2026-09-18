# upaffe

upaffe is a self-hosted monitoring tool for people who operate software and
infrastructure with AI agents. It is under active development and has not been
released.

The repository currently contains the technical foundation, secure access and
project administration, and complete HTTP- and push-monitoring paths. HTTP
monitors perform bounded public-internet checks with threshold incidents. Push
monitors accept ordered job-completion or state reports, detect persisted
deadlines, and use monitor-scoped rotatable reporting credentials. Both paths
survive restarts, retain 90-day history, and have API, CLI, web, and composed
system-test coverage. Notifications are still under active implementation;
[`VISION.md`](./VISION.md) defines the committed MVP and
[`docs/http-monitoring.md`](./docs/http-monitoring.md) and
[`docs/push-monitoring.md`](./docs/push-monitoring.md) document the implemented
monitoring workflows.

## Backend development

The backend requires the .NET SDK selected by `global.json`.

```sh
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/Upaffe.Api
```

The API requires `ConnectionStrings__Postgres`, for example against the local
development database added by the Compose setup:

```sh
ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=upaffe;Username=upaffe;Password=local-development-only' \
  dotnet run --project src/Upaffe.Api
```

While the host is running, `GET /api/health/live` checks only that the process
can answer. It is a technical liveness signal, not evidence that monitoring is
working. `GET /api/health/ready` additionally verifies that PostgreSQL answers
with exactly the migration set known to this build.

A fresh database needs a one-time operator bootstrap. Set a locally generated
`UPAFFE_BOOTSTRAP_SECRET` for one startup and follow the request documented in
[`docs/operations.md`](./docs/operations.md#establish-the-operator). Do not put
that value in a committed file or URL.

## API contract and generated clients

`docs/api/openapi.json` is the checked-in source for the TypeScript and Go
clients. Generated client files are deliberately ignored and recreated before
their consumers build:

```sh
npm ci --prefix src/web
npm run generate --prefix src/web
(cd src/cli && go generate ./... && go test ./...)
```

After changing an endpoint, recapture the contract from the running in-process
host and run the combined consistency check:

```sh
UPAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Upaffe.IntegrationTests \
  --filter FullyQualifiedName~ContractTests
scripts/check-contract.sh
```

## Web development

The React application uses Node.js 24 in CI and regenerates its API types before
every build, typecheck, and test:

```sh
npm ci --prefix src/web
npm run typecheck --prefix src/web
npm test --prefix src/web
npm run lint --prefix src/web
npm run dev --prefix src/web
```

The development server forwards `/api` to the .NET host on port 5000. A
production build writes into `src/Upaffe.Api/wwwroot`, where the same .NET
process serves it:

```sh
npm run build --prefix src/web
```

## CLI development

The standalone Go CLI is `ua`. It regenerates its API package before checks:

```sh
cd src/cli
go generate ./...
go vet ./...
go test ./...
go build -o ua ./cmd/ua
./ua version --json
UPAFFE_URL=http://localhost:5000 ./ua status --json
```

Version and help are offline. `status` is the technical end-to-end diagnostic;
it does not claim that any monitor exists or is healthy.

After an initial management credential has been issued through a signed-in
browser request, use it for noninteractive administration:

```sh
UPAFFE_URL=http://localhost:5000 \
UPAFFE_CREDENTIAL='<token from the explicit create response>' \
  ./ua credential list --json

UPAFFE_URL=http://localhost:5000 \
UPAFFE_CREDENTIAL='<management credential>' \
  ./ua project create --key backup-jobs --name 'Backup jobs' --json
```

Create and rotate print new secret material once. Lists and diagnostics never
repeat it. Project commands address immutable keys and use explicit versions for
concurrent changes; see [`docs/cli.md`](./docs/cli.md) for commands and exit
codes. The same CLI completely administers HTTP and push monitors, runs
immediate HTTP checks, reads both history models, and explicitly issues,
rotates, and revokes monitor-scoped reporting credentials. Secret-bearing
monitor configuration is accepted only from an explicit JSON file or stdin and
is never returned by ordinary text or JSON output.

## Local Compose environment

Build and start PostgreSQL and the complete application from a fresh checkout:

```sh
docker compose -f deploy/docker-compose.dev.yml up --build --wait
curl --fail http://localhost:8080/api/health/ready
go -C src/cli generate ./...
UPAFFE_URL=http://localhost:8080 go -C src/cli run ./cmd/ua status --json
```

The web application is then available at `http://localhost:8080`.

For a disposable end-to-end check of the compiled web host, one-operator
bootstrap, browser session, credential rotation/revocation, and the same
project, HTTP monitor, and both push-monitor modes through browser, CLI, and
direct API paths, run:

```sh
scripts/smoke.sh
```

The system test uses ports 18080 and 15432 by default, creates a unique Compose
project from an empty database, verifies deadlines, ordering, incidents,
restart durability, and that ordinary output and logs contain none of its
generated secrets, then removes its containers and volume. HTTP execution
defaults to `https://example.com/`; restricted test
environments can provide another public status-200 URL through
`UPAFFE_SMOKE_HTTP_TARGET`.

The default password is explicitly development-only. Set
`UPAFFE_DEV_DB_PASSWORD`, `UPAFFE_DEV_DB_PORT`, or `UPAFFE_DEV_PORT` in the
shell or an ignored `deploy/.env` when local ports or credentials must differ.
See [`docs/operations.md`](./docs/operations.md) for restart and reset commands.

## Complete local check

CI runs on every push to `main` and every pull request, including contributions
from forks. It needs no private secret and validates .NET, the React application,
the Go CLI, the checked-in OpenAPI contract, generated clients, and the Compose
definition. Run the same essential checks locally with Docker available:

```sh
scripts/check.sh
```

The .NET and Go versions come from `global.json` and `src/cli/go.mod`; the
workflow pins Node.js 24 for the web build. Generated clients and build outputs
are never uploaded; the workflow uses only the package-manager caches provided
by the Node.js and Go setup actions.
