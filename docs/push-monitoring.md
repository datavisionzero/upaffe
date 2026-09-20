# Push monitoring

Push monitoring records work or health that happens outside upaffe. upaffe does
not run the job, backup, or local check. A sender reports its result, and
upaffe persists the report, evaluates the monitor, and detects a missing report
when the persisted deadline passes.

This guide connects the operator surfaces, reporting routes, state rules,
storage behavior, and security boundary. The exact HTTP shapes are in
[`api.md`](./api.md), CLI syntax and exit codes are in [`cli.md`](./cli.md), and
the durable model is in [`storage.md`](./storage.md).

## Choose the reporting mode

The mode is immutable because it decides what a report proves.

| Mode | Use it when | Success | Explicit failure |
| --- | --- | --- | --- |
| `job_completion` | Each success means one scheduled job completed, such as a backup. | Resolves an incident and starts the next completion deadline. | Opens or updates an incident immediately but does not imply completion or postpone the existing success deadline. |
| `state_report` | Each report is the sender's latest health assessment. | Resolves an incident and starts a fresh reporting deadline. | Opens or updates an incident immediately and starts a fresh reporting deadline because the sender proved it is alive. |

For both modes, silence is different from an explicit failure. The initial
deadline starts at creation. A strictly crossed interval-plus-tolerance
deadline creates one synthetic `report_missing` failure and opens or updates an
incident. Tolerance delays only missing-report detection; it never delays a
reported failure.

## Create and inspect a monitor

The web project workspace switches between HTTP and push monitors. Its create
form explains the selected mode and provides complete configuration. The
detail view shows current state, last receipt and success, next deadline, open
incident, instruction, runbook, credential metadata, and paginated history.

The noninteractive CLI accepts bounded JSON from an explicit file or stdin:

```sh
ua push create backup-jobs --file - --json <<'JSON'
{
  "key": "nightly-backup",
  "name": "Nightly backup",
  "purpose": "Confirms the nightly backup completes.",
  "mode": "job_completion",
  "interval_seconds": 86400,
  "tolerance_seconds": 3600,
  "instruction": "Inspect the backup log",
  "runbook_url": "https://docs.example.test/runbooks/nightly-backup"
}
JSON

ua push get backup-jobs nightly-backup --json
ua push reports backup-jobs nightly-backup --json
ua push incidents backup-jobs nightly-backup --json
```

Web and CLI call the same checked-in API contract and show the same persisted
state. `purpose` describes the job or condition the monitor covers, while
`instruction` tells an operator how to investigate a failure. Purpose is
optional, trimmed to 240 characters, and cleared with an empty or null update
value. Updates, deletion, pause, and resume use the positive `version` last
read. A conflict means another operator changed the monitor; reread it before
deciding whether to retry.

## Issue the reporting credential

Each monitor owns one reporting credential identity. Issue it explicitly:

```sh
ua push credential issue backup-jobs nightly-backup --json
```

That response is a one-time secret handoff containing a `uar_` bearer token and
an instance-relative `/api/report/{secret}` path. Store the required value in
the reporting system's secret store, prepend the same trusted upaffe origin to
the path, then discard the response. `credential get`, monitor reads, lists,
history, and the database expose metadata or digests only.

A reporting credential is bound to exactly one monitor. The JSON reporting
request contains no project or monitor selector, so the sender cannot redirect
it to another monitor. It also cannot authenticate a management or read
operation.

## Report JSON outcomes

Use JSON when a sender needs explicit failure, a stable retry identity, or its
own observation time:

```sh
export UPAFFE_ORIGIN='https://monitor.example.test'
export UPAFFE_REPORTING_TOKEN='uar_example-value-from-the-secret-store'

curl --fail-with-body "$UPAFFE_ORIGIN/api/reports" \
  --header "Authorization: Bearer $UPAFFE_REPORTING_TOKEN" \
  --header 'Content-Type: application/json' \
  --data '{
    "report_id":"018f47f0-9f5d-7c63-9dc2-0bdf2fc2566f",
    "observed_at":"2026-09-18T12:00:00Z",
    "outcome":"success",
    "reason":null
  }'
```

Generate one non-empty UUID per observation and reuse the complete payload for
retries. An identical retry returns the original receipt with
`duplicate: true`; changing facts under the same ID is a conflict. The sender's
`reason` is untrusted diagnostic data. It is stored separately but excluded
from ordinary history, logs, and stable incident reasons.

The monitor-local sequence records receipt order. State changes follow
`observed_at`: an older success remains in history with `applied: false` and
cannot resolve an incident opened by a newer failure. A newly accepted success
with a later observation time resolves that incident.

## Report simple success

Call the issued secret path when the sender can signal only successful
completion:

```sh
export UPAFFE_ORIGIN='https://monitor.example.test'
export UPAFFE_REPORT_PATH='/api/report/example-secret-path'

curl --fail-with-body --request POST "$UPAFFE_ORIGIN$UPAFFE_REPORT_PATH"
```

`GET` has the same semantics for constrained callers. Each request creates a
new success at the server time; it has no retry ID or failure payload. Use the
JSON route when retries must deduplicate or a failure must be explicit.

## Deadlines, incidents, and restarts

An applicable explicit failure or missing-report deadline opens one incident
and its eligible alert intents in the same transaction. Later failures and
late reports create no duplicate alert. A fresh applicable success resolves
the incident, obsoletes unsent alerts, and creates recovery intents only for
SMTP-accepted announcements to still-configured recipients.

The current deadline, sequence, state, latest report and success references,
and open incident are PostgreSQL facts. The deadline worker claims overdue work
with a bounded lease and writes one synthetic missing report in the same
transaction as the state and incident change. A restart before a deadline or
while an incident is open therefore loses neither the plan nor the incident.

Repeated failures update the same open incident. History retains its original
reason and opening report, its latest failure, and an optional recovery report.
Report and incident pages are newest first and use exclusive sequence cursors.
The daily retention worker removes resolved incidents and then unreferenced
reports older than 90 days; open incidents and monitor pointers remain
protected.

Pause rejects both new reports and exact retries and clears the active
deadline. Resume starts a new evaluation generation in `untested` with a fresh
interval-plus-tolerance deadline. It retains history, the last receipt and
success, and any open incident. An earlier-generation retry stays historical;
only a newly accepted success in the resumed generation recovers the monitor.

## Rotate, revoke, and remove

Rotation reveals a new token and secret path once:

```sh
ua push credential rotate backup-jobs nightly-backup --json
```

The previous secret remains valid for five minutes so a sender can switch
without a reporting gap. Complete the handoff promptly, then discard the
response. Explicit revocation rejects the current and overlapping secrets
immediately:

```sh
ua push credential revoke backup-jobs nightly-backup --json
```

Removing the monitor also revokes its credential. Missing, unknown, expired,
and revoked reporting secrets all return the same `reporting_rejected` result
so the endpoint does not disclose credential identity.

## Keep secret paths out of telemetry

Treat both the bearer token and final secret-path segment as credentials. Do
not put them in source control, command history, ordinary status output,
exports, screenshots, or diagnostic attachments. upaffe replaces the path
segment before application routing and logging, and suppresses request-start
logging that would run before that replacement. A reverse proxy is outside
this boundary and must apply equivalent `/api/report/*` redaction.

The web UI keeps an issued or rotated token and path only in the explicit
one-time handoff panel. Dismissing it, refreshing, navigating, or starting
another operation removes it from React state. Ordinary web and CLI errors
render stable local problem text rather than remote titles or response bodies.

## Verify the composed path

From the repository root:

```sh
scripts/smoke.sh
```

The disposable test builds the compiled web host and API, creates both modes
with the generated CLI, reports through JSON and simple paths, proves duplicate
and old-report ordering, immediate and repeated failure, recovery, missing
deadlines, restart durability, pause/resume generations, browser/CLI agreement,
credential isolation, rotation, and revocation. It then scans ordinary HTTP and
CLI artifacts plus application logs for every generated access and reporting
secret and removes its Compose project and database volume.
