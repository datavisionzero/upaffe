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
name or password, query, fragment, or control character. HTTPS is the
deployment expectation; HTTP remains usable for the local loopback development
environment.

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
JSON value followed by a newline. A failed `--json` command writes exactly one
error object to stderr and leaves stdout empty, except that a completed
immediate HTTP check writes its result to stdout before exiting 5. The object
has a stable `code`, numeric `exit_code`, and `http_status` only for an HTTP
response. For example:

```json
{"code":"authentication_rejected","exit_code":7,"http_status":401}
```

Local failures use `usage_error`, `instance_unreachable`, or
`unexpected_response`; an immediate failed check uses `check_failed`.
Recognized API problem codes retain their names. An unknown or malformed
problem uses the category's generic code (`unauthorized`, `not_found`,
`request_refused`, or `unexpected_response`). Text diagnostics contain the
same stable code and, where applicable, HTTP status, such as
`ua: authentication_rejected (HTTP 401)`. Neither mode copies a remote title,
response body, submitted document, credential, or transport error text.
Callers should branch on `exit_code` and use `code` for finer known cases;
future releases may add new stable codes without changing the exit categories.

| Code | Category |
| ---: | --- |
| 0 | success |
| 1 | unexpected response or internal failure |
| 2 | usage or missing configuration |
| 3 | endpoint or object not found |
| 4 | request refused by validation, conflict, or SMTP test rejection |
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
ua project report KEY [--url ADDRESS] [--credential TOKEN] [--json]
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
ua push create PROJECT_KEY --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua push list PROJECT_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua push get PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua push update PROJECT_KEY MONITOR_KEY --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua push delete PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua push pause PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua push resume PROJECT_KEY MONITOR_KEY --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua push reports PROJECT_KEY MONITOR_KEY [--before-sequence N] [--limit N] [--url ADDRESS] [--credential TOKEN] [--json]
ua push incidents PROJECT_KEY MONITOR_KEY [--before-opening-sequence N] [--limit N] [--url ADDRESS] [--credential TOKEN] [--json]
ua push credential issue PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua push credential get PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua push credential rotate PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua push credential revoke PROJECT_KEY MONITOR_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua email settings get [--url ADDRESS] [--credential TOKEN] [--json]
ua email settings set --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua email password set --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua email password clear --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
ua email defaults get [--url ADDRESS] [--credential TOKEN] [--json]
ua email defaults set --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua email recipients get PROJECT_KEY [--url ADDRESS] [--credential TOKEN] [--json]
ua email recipients set PROJECT_KEY --file PATH|- [--url ADDRESS] [--credential TOKEN] [--json]
ua email test --recipient ADDRESS [--url ADDRESS] [--credential TOKEN] [--json]
ua email deliveries [--project KEY] [--monitor-type http|push] [--monitor KEY] [--incident UUID] [--state STATE] [--limit N] [--offset N] [--url ADDRESS] [--credential TOKEN] [--json]
ua email summary [--project KEY] [--url ADDRESS] [--credential TOKEN] [--json]
ua email incident UUID --monitor-type http|push [--url ADDRESS] [--credential TOKEN] [--json]
ua maintenance get PROJECT_KEY [MONITOR_KEY] --scope project|http|push [--url ADDRESS] [--credential TOKEN] [--json]
ua maintenance start PROJECT_KEY [MONITOR_KEY] --scope project|http|push --version VERSION --duration-seconds SECONDS [--url ADDRESS] [--credential TOKEN] [--json]
ua maintenance end PROJECT_KEY [MONITOR_KEY] --scope project|http|push --version VERSION [--url ADDRESS] [--credential TOKEN] [--json]
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

### One-call project report

`ua project report KEY --json` makes one authenticated GET to
`/api/projects/{key}/report` and writes the typed project report as one JSON
value. It is the starting point for an unattended investigation. The report
timestamp, state counts, attention list, compact healthy summaries, maintenance,
and email delivery summary are defined in [ADR 0008](adr/0008-one-call-project-report.md)
and [the API guide](api.md). Use the existing history commands for individual
checks, reports, incidents, or deliveries. The report reads current facts; it
does not trigger checks or acknowledge an incident.

Text mode begins with the project and report time, state counts, project
maintenance, and safe SMTP settings and delivery summary. It then prints
attention monitors in urgency order, followed by healthy summaries. Each
attention monitor has labeled
`settings`, `diagnostic`, optional `incident`, `maintenance`, and
`operator_guidance` lines. The latest result, last success, last receipt, and
next deadline are distinct. `operator_guidance` contains only operator-written
instruction and runbook data; `diagnostic` contains stable system reason codes.
Display strings are quoted so control characters cannot create forged lines.
Target queries, header values, reporting URLs, submitted push reasons, and
credentials do not appear in either mode. Unknown or deleted projects exit 3;
authentication exits 7, unreachable instances exit 10, and malformed success
responses exit 1 with empty stdout.

### HTTP monitors

For a complete fictional monitor lifecycle across CLI, web, scheduling,
incidents, storage, and security boundaries, see
[the HTTP monitoring guide](./http-monitoring.md).

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

### Push monitors

For the complete reporting lifecycle across CLI, web, deadlines, incidents,
storage, and security boundaries, see
[the push monitoring guide](./push-monitoring.md).

`push` manages both `job_completion` and `state_report` monitors through
explicit project and monitor keys. Create and update read exactly one bounded
JSON object from `--file PATH` or `--file -`; no prompt, editor, or pager is
available. A job-completion create document is:

```json
{
  "key": "nightly-backup",
  "name": "Nightly backup",
  "mode": "job_completion",
  "interval_seconds": 86400,
  "tolerance_seconds": 3600,
  "instruction": "Inspect the backup log",
  "runbook_url": "https://docs.example.test/runbooks/nightly-backup"
}
```

Update omits the immutable key and mode and includes the positive `version`
last read. Delete, pause, and resume likewise require that version. Ordinary
text and JSON output contains mode, state, last receipt, latest report and
success IDs, next deadline, open incident ID, and whether a reporting
credential exists; it never contains the credential secret or secret URL.

`push credential issue` and `push credential rotate` are explicit
secret-producing commands and reveal the new token and secret reporting URL in
their successful output. `get` returns only safe credential identity and
lifecycle timestamps, while `revoke` returns only the monitor identity and
revoked state. Store an issued value immediately; it is not recoverable later.

`push reports` and `push incidents` use the same exclusive cursor and 1-100
page-size rules as HTTP history. Report output includes stable reason codes and
applicability but omits sender diagnostic text. Remote problem bodies are never
copied into diagnostics, and conflicts and validation errors retain exit code
4.

JSON reporting uses the monitor-scoped token, a retry-stable report ID, and the
sender observation time:

```sh
curl --fail-with-body https://monitor.example.test/api/reports \
  --header "Authorization: Bearer $UPAFFE_REPORTING_TOKEN" \
  --header 'Content-Type: application/json' \
  --data '{"report_id":"018f47f0-9f5d-7c63-9dc2-0bdf2fc2566f","observed_at":"2026-09-18T12:00:00Z","outcome":"success","reason":null}'
```

A caller that only signals success may use the one-time secret URL returned by
the issue or rotate command:

```sh
curl --fail-with-body --request POST "$UPAFFE_ORIGIN$UPAFFE_REPORT_PATH"
```

The issued `report_url` is an instance-relative path. Combine it only with the
same trusted origin used for management requests.

### Email and timed maintenance

For the incident lifecycle and maintenance rules behind these commands, see
[the email and maintenance guide](./email-maintenance.md).

`email settings get` returns the safe shared relay configuration, default
recipients, version, and `has_password`. `settings set` reads one JSON object
with `version`, relay host and port, security mode, sender address and name,
public base URL, and optional username. `email password set` reads a separate
`{"version":2,"password":"..."}` document. Only password presence appears in
the response. `password clear` requires the last read version. Put credential
documents in a protected file or pipe them with `--file -`; do not put a
password on the command line.

`email defaults set` and `email recipients set` read
`{"version":2,"recipients":["ops@example.test"]}` from `--file PATH` or
`--file -`. An empty list opts the project out of incident email. New projects
copy defaults once; existing project lists change only through `recipients
set`. Every write uses the version last read. Conflicts exit with code 4.
`email test` submits one explicit message and reports relay acceptance or a
sanitized refusal; it creates no incident and does not retry.

`email deliveries` pages newest first. Text output shows incident and scope,
recipient, kind, state, attempts, sanitized error and suppression reason, and
relevant times. JSON retains the API page shape, including `total` and
`has_more`. `--state` filters the stored state; a pending row can display
`suppressed` while maintenance or pause applies. `email summary` shows pending,
retrying, terminal failures, and SMTP acceptances for the instance or one
project. `email incident` shows the announcement decision and all per-recipient
deliveries for an explicit incident and monitor type. SMTP acceptance does not
prove inbox arrival. History older than 90 days may be pruned.

`maintenance get`, `start`, and `end` address a project, HTTP monitor, or push
monitor with `--scope`. Project scope takes only `PROJECT_KEY`; monitor scopes
also take `MONITOR_KEY`. `get` returns the direct window version and active
state, plus effective active scopes and expiry. `start` uses a nonnegative
version (`0` for a new scope) and a duration of 60–2,592,000 seconds. `end`
uses a positive version. Project and monitor windows compose independently;
ending one does not end the other. Maintenance suppresses email while checks,
reports, and incident history continue.
