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
the local loopback development environment.

Management commands resolve their bearer credential in this order:

1. `--credential TOKEN`
2. `UPAFFE_CREDENTIAL`

The environment is preferable for unattended use because a command-line token
may be retained by shell history or process inspection. The CLI never writes the
credential it used to diagnostics or ordinary output. Create and rotate are
explicit secret-producing operations and print the newly issued token exactly
once; the caller is responsible for placing it in a secret store.

## Output and exit codes

Successful data goes to stdout. Diagnostics go to stderr. `--json` writes one
JSON value followed by a newline. Remote response bodies are never copied into
diagnostics. When the API returns a problem, diagnostics retain only its stable
code and HTTP status, such as `authentication_rejected (HTTP 401)`.

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
ua credential create --name NAME [--url ADDRESS] [--credential TOKEN] [--json]
ua credential list [--url ADDRESS] [--credential TOKEN] [--json]
ua credential rotate ID [--url ADDRESS] [--credential TOKEN] [--json]
ua credential revoke ID [--url ADDRESS] [--credential TOKEN] [--json]
ua project create --key KEY --name NAME [--url ADDRESS] [--credential TOKEN] [--json]
ua project get KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua project list [--deleted] [--url ADDRESS] [--credential TOKEN] [--json]
ua project rename KEY --name NAME --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua project delete KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua project restore KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
```

`version` prints the CLI build version and never accesses the network. `status`
calls the generated `GET /api/version` operation with a ten-second timeout. Its
text form is a tab-separated `live`, version, and base URL; its JSON form is:

```json
{"url":"https://monitor.example.test","version":"0.0.0-dev"}
```

This is a technical connectivity diagnostic. It says nothing about monitor or
monitoring-loop health.

`credential create` and `credential rotate` deliberately include the one-time
token in both text and JSON output. `credential list` contains only the stable
ID, name, timestamps, and revoked state. `credential revoke` is safely
repeatable for an existing ID and reports the stable ID it revoked. IDs are
always explicit UUIDs; names are never resolved back to credentials.

The first management credential must be created through an authenticated
browser request. Every valid management credential can then run all four CLI
commands, including issuing a replacement credential before revoking itself.
Missing credentials exit with code 7 and `authentication_required`; invalid,
expired, and revoked credentials use the same code with
`authentication_rejected`.

### Projects

Every project command addresses the immutable project key directly; display
names are never searched or resolved. `create` sends the key and name and is
idempotent under the API rules. `get` reads either a live or deleted project.
`list` returns live projects unless `--deleted` requests only deleted projects.
`rename`, `delete`, and `restore` require the positive `--version` returned by
the last read, so a concurrent change exits with code 4 and `conflict` instead
of overwriting it.

Project payloads consist only of the short scalar key, name, and version, so
they are supplied as explicit flags and arguments; no editor or prompt is
opened. Future commands that require structured documents must expose an
explicit file/stdin flag rather than becoming interactive.

JSON mode writes the API project object or list unchanged. Text mode writes one
tab-separated project per line with the same facts in this order: key, quoted
display name, UUID, version, `live` or `deleted`, creation time, update time,
and deletion time (`-` when live). For example:

```text
backup-jobs	"Backup jobs"	f0187842-6f73-4c54-8a8c-57ac7c117c39	3	live	2026-09-16T12:00:00Z	2026-09-16T13:00:00Z	-
```

API `validation` and `conflict` problems exit with code 4, `not_found` with
code 3, and authentication problems with code 7. Diagnostics include only the
stable problem code and HTTP status, never the remote title, response body, or
management credential.
