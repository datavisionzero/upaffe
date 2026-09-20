# 0008 — One-call project report

Status: accepted

An authenticated agent needs a compact entry point for investigating one
project. The report is a read-only projection of the same persisted facts used
by the web app and the existing management API. It does not evaluate monitors,
advance deadlines, create incidents, or infer a recovery from elapsed time.

## Contract

`GET /api/projects/{key}/report` accepts a browser session or management bearer
and returns `200` with one JSON object. An unknown or deleted project returns
`404 not_found`; a malformed key returns `400 validation`. The report captures
one `generated_at` UTC instant. Ages and `overdue` flags use that instant, so
the fields remain internally comparable even if the query spans several reads.

The object contains:

- `project`: `id`, `key`, `name`, `version`, `created_at`, and `updated_at`.
- `counts`: total, healthy, failing, untested, and paused counts across both
  monitor types, plus HTTP and push totals. A monitor appears in exactly one
  state count regardless of maintenance or overdue work.
- `attention`: full current snapshots of failing, untested, paused, or overdue
  monitors, ordered by urgency (open incident, failing, overdue, untested,
  paused), then type (`http`, `push`) and key. It may be empty.
- `healthy`: compact summaries of healthy monitors that are not overdue,
  ordered by type and key. Each keeps identity, mode where relevant, last
  success time, next check or reporting deadline, and direct/effective
  maintenance. Detail is available through the existing monitor and paginated
  history endpoints.
- `project_maintenance`: direct active window with start and end times, or
  `null`. Attention and healthy items also have direct and effective maintenance
  facts.
- `email`: configured relay flag, safe relay host, port, security, sender,
  public base URL, password-presence flag, project recipients, and the existing
  project delivery summary (`pending`, `retrying`, `terminal_failure`,
  `accepted`, and oldest pending time). Accepted means SMTP relay acceptance,
  not inbox delivery. Retained counts cover only available history.

Every `attention` item has `type`, `key`, `name`, `purpose`, `state`, `id`, `version`,
`instruction`, `runbook_url`, `latest_result`, `last_success`, `incident`,
`next_due_at`, `overdue`, `direct_maintenance`, and
`effective_maintenance_until`. `type` is `http` or `push`; `mode` is null for
HTTP and otherwise `job_completion` or `state_report`. An HTTP item also has
the query-redacted target, expected status, text condition, interval, timeout,
failure threshold, and current failure count. A push item has interval,
tolerance, and last receipt time. Type-specific values not applicable to the
other type are null. `latest_result` gives the last applicable outcome, stable
reason, observation time and result ID. `last_success` gives its separate ID
and observation time, including when the monitor now fails, is paused, or was
resumed. `incident` gives ID, beginning/opening time, age in seconds, original
and latest stable reasons; it can remain open on a paused or resumed monitor.
Healthy summaries carry the same optional purpose. It describes what the
monitor covers, separately from incident investigation `instruction`.

`next_due_at` is null while paused. `overdue` means an active monitor's stored
next check or reporting deadline is before `generated_at`. It is a scheduling
or reporting gap, not a new observation or incident. A healthy monitor whose
check execution is overdue therefore moves to `attention` without changing
its state. A push deadline awaiting processing is likewise shown as overdue;
the report does not claim a missing-report incident until the evaluator records
it. Late HTTP completions and push submissions that did not apply to current
state remain on paginated history endpoints and never override the current
snapshot. `latest_result` may be null for a new or resumed `untested` monitor;
earlier `last_success` remains distinct. Paused monitors remain paused even
when they retain an open incident. Maintenance suppresses email, not state.

The report reads monitor pointers and incident rows rather than scanning
history. The retention workers preserve checks and reports referenced by a
monitor's latest-result and latest-success pointers even beyond 90 days, so
the report can keep the observation time. The query count must be bounded
independently of monitor count.

## Trust boundary

`instruction` and `runbook_url` are operator-written guidance. They are
separate from system-generated `latest_result.reason` and incident reasons.
The report never includes an HTTP response body, executor message, effective
target query, request-header value, submitted push diagnostic reason,
management or reporting credential, reporting URL, SMTP password, or raw SMTP
response. A runbook URL is operator-authored data; clients should display it
as a labeled link and never execute its contents automatically.

## State examples

- An HTTP monitor with one failed check and threshold three is `failing` in
  `attention`, with `failure_count: 1`, a failed `latest_result`, and
  `incident: null`.
- A silent push monitor is initially `untested` with a deadline. Once the
  deadline is exceeded but before evaluation, it is overdue; after the
  evaluator records `report_missing`, it is `failing` with that stable reason.
- Pausing retains last success and an open incident; resuming produces
  `untested` with a fresh due time until a new applicable observation arrives.
- Active project maintenance appears in `project_maintenance` and each
  monitor's effective maintenance. A terminal email delivery raises the
  `terminal_failure` count even when a monitor is currently healthy.

This projection follows the state and access rules in ADRs 0002, 0003, 0006,
and 0007. It adds no automatic remediation.
