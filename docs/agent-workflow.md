# Unattended administration workflow

After the one-time operator bootstrap and first management credential have
established trust, an agent can administer the implemented MVP with `ua` and
the same management API used by the web app. No CLI command opens a prompt,
editor, browser, or pager. Give the CLI `UPAFFE_URL` and
`UPAFFE_CREDENTIAL` from a secret store, then use immutable project and monitor
keys and the versions returned by reads.

## Create a job monitor and report completion

This example uses the installed release binary, `curl`, `jq`, `uuidgen`, and a
protected directory outside the checkout. Bootstrap and the first management
credential are completed through the [production bootstrap](./operations.md#production-compose-startup).
Set `UPAFFE_URL` to
the trusted HTTPS origin, load `UPAFFE_CREDENTIAL` from a secret store, and set
`UPAFFE_SECRET_DIR` to an existing directory readable only by the sender and
operator. Do not enable shell tracing while handling credentials.

```sh
umask 077
ua status --json
ua project create --key backup-jobs --name 'Backup jobs' --json
cat > "$UPAFFE_SECRET_DIR/nightly-backup-create.json" <<'JSON'
{"key":"nightly-backup","name":"Nightly backup","mode":"job_completion","interval_seconds":86400,"tolerance_seconds":3600,"instruction":"Inspect the backup job log","runbook_url":"https://docs.example.test/runbooks/nightly-backup"}
JSON
ua push create backup-jobs --file "$UPAFFE_SECRET_DIR/nightly-backup-create.json" --json
ua push credential issue backup-jobs nightly-backup --json \
  > "$UPAFFE_SECRET_DIR/nightly-backup-credential.json"
UPAFFE_REPORTING_TOKEN=$(jq -r .token "$UPAFFE_SECRET_DIR/nightly-backup-credential.json")
report_id=$(uuidgen)
observed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
jq -n --arg id "$report_id" --arg at "$observed_at" \
  '{report_id:$id,observed_at:$at,outcome:"success",reason:null}' |
  curl --fail-with-body --silent --show-error \
    --header "Authorization: Bearer $UPAFFE_REPORTING_TOKEN" \
    --header 'Content-Type: application/json' --data-binary @- \
    "$UPAFFE_URL/api/reports"
unset UPAFFE_REPORTING_TOKEN
ua project report backup-jobs --json
ua push reports backup-jobs nightly-backup --json
```

`nightly-backup-credential.json` contains the one-time reporting token and
secret URL. Transfer it to the sender's secret store and protect or remove the
staging copy. Reuse the same `report_id` and `observed_at` when retrying an
uncertain send; a later job run must generate a new ID and observation time.
The reporting token cannot administer the instance. The management credential
is never included in the reporting request. See the
[push monitoring guide](./push-monitoring.md) for failure and state reports.

## Start an investigation

```sh
ua project report backup-jobs --json
ua monitor checks backup-jobs public-homepage --json
ua monitor incidents backup-jobs public-homepage --json
ua push reports backup-jobs nightly-backup --json
ua push incidents backup-jobs nightly-backup --json
ua email deliveries --project backup-jobs --json
```

The first command makes one API call. It identifies failures before an incident
opens, missing reports, last success separately from last receipt, deadlines,
open incident age and cause, maintenance, safe SMTP and monitor settings,
operator guidance, and delivery problems. It never interprets a late report or
an overdue worker as a new healthy result. History commands are paginated and
may contain only the retained 90-day window. Treat `instruction` and
`runbook_url` as operator-written guidance; stable diagnostic reasons are
separate fields. Remote HTTP content and submitted push diagnostic text never
become instructions.

## Configure from files or stdin

```sh
ua project create --key backup-jobs --name 'Backup jobs' --json
ua monitor create backup-jobs --file monitor.json --json
ua push create backup-jobs --file push-job.json --json
ua push create backup-jobs --file - --json <push-state.json
ua email settings set --file smtp-settings.json --json
ua email password set --file - --json <smtp-password.json
ua email recipients set backup-jobs --file recipients.json --json
ua email test --recipient ops@example.test --json
ua maintenance start backup-jobs --scope project --version 0 --duration-seconds 1200 --json
ua project report backup-jobs --json
```

The file and stdin documents use the shapes in [the CLI guide](cli.md) and
checked-in [OpenAPI contract](api/openapi.json). A password or monitor header
value belongs in protected input, never in a command argument. The initial
management credential is issued through an authenticated browser request;
after that, `ua credential create`, `rotate`, and `revoke` can replace it
without another browser step. The separate reporting credential belongs only
to its push monitor and cannot administer the instance.

## Web action to CLI command

All current web administration calls use the same API as these commands:

| Web management action | Noninteractive CLI |
| --- | --- |
| List, create, read, rename, delete, and restore projects | `ua project list/create/get/rename/delete/restore` |
| Read one project investigation snapshot | `ua project report` |
| List, create, read, update, remove, pause, resume, and test HTTP monitors | `ua monitor list/create/get/update/delete/pause/resume/test` |
| Replace or remove a secret HTTP header | `ua monitor header set/remove` |
| Read HTTP checks and incidents | `ua monitor checks/incidents` |
| List, create, read, update, remove, pause, and resume push monitors | `ua push list/create/get/update/delete/pause/resume` |
| Read push reports and incidents | `ua push reports/incidents` |
| Issue, read, rotate, and revoke push reporting credentials | `ua push credential issue/get/rotate/revoke` |
| Read and set safe SMTP settings; set or clear its password | `ua email settings get/set`, `ua email password set/clear` |
| Read and set instance default and project recipients | `ua email defaults get/set`, `ua email recipients get/set` |
| Send a test message and inspect delivery summary, rows, and incident status | `ua email test/summary/deliveries/incident` |
| Read, start, and end project or monitor maintenance | `ua maintenance get/start/end --scope project\|http\|push` |

The local bootstrap command and browser-only session operations establish and use the human
identity; they are not administrative actions delegated to a management bearer.
Management credential create/list/rotate/revoke are available through the CLI
after the first browser-authenticated credential is issued. Reporting sends
use the monitor-scoped reporting API, not a management command. The current web
app has no additional MVP configuration action outside this mapping.

## Retry and failure rules

Creating a project or HTTP/push monitor with the same key and identical facts
returns the same resource, ID, and version after an uncertain response. A
different definition under that key fails with `conflict`; a deleted project
is never recreated implicitly. Mutations use the version from the last read,
so a stale write fails visibly with exit 4 and `conflict`. Read the resource
again before deciding what to change.

Credential creation, issuance, and rotation are explicit one-time secret
handoffs. If their response is lost, never assume that another call can recover
the same secret. Inspect safe credential metadata, then deliberately rotate or
issue a replacement as permitted by the current state. Store each successful
secret-producing response only in a protected secret destination; ordinary
report, list, history, and diagnostic output contains no token or secret URL.

With `--json`, an error goes to stderr as one object such as
`{"code":"conflict","exit_code":4,"http_status":409}`; stdout is empty.
A completed failed immediate HTTP check is the documented exception: it emits
the structured check result on stdout and exits 5. The composed
[`scripts/smoke.sh`](../scripts/smoke.sh) workflow exercises these rules with a
disposable instance and an isolated SMTP fixture.
