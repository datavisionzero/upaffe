# The CLI

`ua` is the noninteractive client of upaffe's public HTTP API. It is a separate
Go binary and imports no .NET assembly. Commands never open a prompt, editor, or
pager. A command reads stdin only when a future flag explicitly says so.

## Build

From `src/cli`:

```sh
go generate ./...
go vet ./...
go test ./...
go build -o ua ./cmd/ua
```

Generation creates `internal/api/client.gen.go` from
`docs/api/openapi.json`. The file is ignored and must be regenerated before
checking or building the client.

## Configuration

Commands that contact an instance resolve its base URL in this order:

1. `--url ADDRESS`
2. `UPAFFE_URL`

The address must be an absolute `http` or `https` URL and cannot embed a user
name or password. HTTPS is the deployment expectation; HTTP remains usable for
the local loopback development environment. A credential ladder will be added
by the access epic and is deliberately not invented by this foundation.

## Output and exit codes

Successful data goes to stdout. Diagnostics go to stderr. `--json` writes one
JSON object followed by a newline. Remote response bodies are never copied into
diagnostics by the foundation command.

| Code | Category |
| ---: | --- |
| 0 | success |
| 1 | unexpected response or internal failure |
| 2 | usage or missing configuration |
| 3 | endpoint or object not found |
| 4 | request refused by validation or conflict |
| 7 | unauthenticated or unauthorized |
| 10 | instance or network unreachable |

Later commands reuse these categories and may add documented categories only
when a distinct automation decision requires one.

## Commands

```text
ua version [--json]
ua status [--url ADDRESS] [--json]
```

`version` prints the CLI build version and never accesses the network. `status`
calls the generated `GET /api/version` operation with a ten-second timeout. Its
text form is a tab-separated `live`, version, and base URL; its JSON form is:

```json
{"url":"https://monitor.example.test","version":"0.0.0-dev"}
```

This is a technical connectivity diagnostic. It says nothing about monitor or
monitoring-loop health.
