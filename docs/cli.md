# The CLI

`ua` is the noninteractive client of upaffe's public HTTP API. It is a separate
Go binary and imports no .NET assembly. Commands never open a prompt, editor, or
pager. A command reads stdin only when `--file -` explicitly says so.

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
| 5 | an immediate monitor check completed with a failure |
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
ua monitor create PROJECT_KEY --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor list PROJECT_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor get PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor update PROJECT_KEY MONITOR_KEY --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor delete PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor pause PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor resume PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor test PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor checks PROJECT_KEY MONITOR_KEY [--before-sequence N] [--limit N] [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor incidents PROJECT_KEY MONITOR_KEY [--before-opening-sequence N] [--limit N] [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor header set PROJECT_KEY MONITOR_KEY NAME --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua monitor header remove PROJECT_KEY MONITOR_KEY NAME --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
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

### HTTP monitors

Every monitor command addresses an immutable project key and project-scoped
monitor key. Monitor UUIDs are returned as durable facts but are never resolved
from display names. Mutations use the positive version returned by the last
read; stale versions exit with code 4 and `conflict`.

Create, update, and header-set documents are read only from the explicit
`--file` operand. `--file -` reads one document from stdin, which is convenient
for a secret-store pipe; a path should refer to a file protected by appropriate
filesystem permissions. Input is limited to 1 MiB, must contain exactly one
JSON object, and rejects unknown properties. The CLI never includes submitted
content in input-error diagnostics.

A create document uses the API's complete monitor shape. Header values and the
target query are accepted here but never appear in the returned text or JSON:

```json
{
  "key": "homepage",
  "name": "Public homepage",
  "target_url": "https://status.example.test/health?access=secret-from-store",
  "expected_status_code": 200,
  "text_condition": "contains",
  "text_fragment": "ready",
  "interval_seconds": 60,
  "timeout_seconds": 10,
  "failure_threshold": 3,
  "instruction": "Inspect the public endpoint",
  "runbook_url": "https://docs.example.test/runbooks/homepage",
  "headers": [
    {"name": "Authorization", "value": "Bearer secret-from-store"}
  ]
}
```

An update document carries `version` and every non-secret setting. Omitting or
setting `target_url` to `null` preserves the complete existing URL, including
its hidden query. A value replaces it. Header replacement is separate and uses
`{"value":"secret-from-store","version":4}`; there is deliberately no
header-value command-line flag that could be retained in shell history or
process inspection.

`list` and `get` expose `untested`, `healthy`, `failing`, and `paused` directly,
together with the failure count and threshold, open incident ID, next due time,
and latest applied failure details when a failure streak is active. Their JSON
objects add `latest_check` when those details exist. The embedded monitor and
history objects are otherwise the checked-in API shapes and contain no target
query, request-header value, response body, or executor message.

`test` waits up to 75 seconds so the API can honor the monitor's bounded
60-second execution timeout. Both output modes report success, status, response
time, stable reason, check ID, and whether the result affected current state. A
completed failure still writes that structured result, then exits with code 5;
transport, authentication, and API failures retain their ordinary categories.

`checks` and `incidents` return newest-first pages. Their cursor flags are
exclusive and `--limit` accepts 1 through 100; omitting the limit lets the API
use its default of 50. JSON mode writes the page object including its next
cursor. Text mode writes one tab-separated result per line.
