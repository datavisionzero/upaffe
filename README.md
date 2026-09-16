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

The React application uses the Node version required by `src/web/package.json`'s
toolchain and regenerates its API types before every build, typecheck, and test:

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
