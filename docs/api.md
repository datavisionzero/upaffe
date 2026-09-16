# HTTP API

The API is the single application boundary used by the web application and CLI.
Every operation is below `/api`; other paths are reserved for the SPA. There is
no API-version segment. Each response carries `Upaffe-Version`, whose value is
the release tag or `0.0.0-dev` for an untagged build.

This foundation exposes only technical operations:

| Method and path | Purpose |
| --- | --- |
| `GET /api/version` | The instance version. This is the first operation exercised by both generated clients. |
| `GET /api/health/live` | Whether the process can answer; it touches no dependency. |
| `GET /api/health/ready` | Whether PostgreSQL answers with exactly the schema this binary knows. |
| `GET /api/openapi/v1.json` | The generated OpenAPI document. It does not list itself. |

All are currently anonymous and expose no product data. Authentication and
authorization arrive with the access epic. Liveness and readiness are technical
deployment checks; neither asserts that the monitoring loop is progressing.

## Contract rule

`docs/api/openapi.json` is checked in and is the source for both clients. It is
captured from the running API, not maintained by hand. The contract test compares
JSON structures so formatting does not decide whether two contracts agree.

Regenerate after an endpoint change:

```sh
UPAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Upaffe.IntegrationTests \
  --filter FullyQualifiedName~ContractTests
```

The TypeScript and Go outputs are generated, not committed:

```sh
npm run generate --prefix src/web
(cd src/cli && go generate ./...)
```

The outputs are ignored in `.gitignore`. Web scripts regenerate the TypeScript
schema before compiling, and Go checks run `go generate` before `go test` or
build. This removes the ambiguous state in which a committed generated client
could agree with an old contract. `scripts/check-contract.sh` is the combined
local check and is used by CI once the CI ticket lands.
