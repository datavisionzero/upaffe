# upaffe

upaffe is a self-hosted monitoring tool for people who operate software and
infrastructure with AI agents. It is under active development and has not been
released.

The repository currently contains the technical walking skeleton. It does not
yet implement monitoring; [`VISION.md`](./VISION.md) defines the committed MVP.

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

## Local Compose environment

Build and start PostgreSQL and the complete application from a fresh checkout:

```sh
docker compose -f deploy/docker-compose.dev.yml up --build --wait
curl --fail http://localhost:8080/api/health/ready
go -C src/cli generate ./...
UPAFFE_URL=http://localhost:8080 go -C src/cli run ./cmd/ua status --json
```

The web application is then available at `http://localhost:8080`.

For a disposable end-to-end check, including the web shell, both health paths,
the version endpoint, and the generated CLI client, run:

```sh
scripts/smoke.sh
```

The smoke test uses ports 18080 and 15432 by default, creates a unique Compose
project, and removes its containers and database volume when it finishes.

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
