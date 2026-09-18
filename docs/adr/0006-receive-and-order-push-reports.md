# 0006 — Receive and order push reports

Status: accepted

Push monitoring must distinguish a completed job from a periodically reported
condition, treat silence independently from explicit failure, and accept
retries without allowing an older success to erase newer failure evidence.
Because the sender and upaffe do not share a clock, one timestamp cannot safely
serve both observation ordering and evidence freshness.

## Modes, intervals, and first evidence

A push monitor has one immutable reporting mode:

- `job_completion`: success means one job completed. The next success is due
  one configured interval plus tolerance after that success. Failure opens or
  updates an incident immediately but does not move the success deadline.
- `state_report`: success or failure describes the sender's latest evaluation.
  Every applicable report moves the next report deadline by one interval plus
  tolerance from its receipt, while a failure opens or updates an incident
  immediately.

The interval is from 30 seconds through 365 days, inclusive. Tolerance is from
zero through 30 days, inclusive, and cannot exceed the interval. Push monitors
have no failure threshold: one explicit failure or one missed deadline is
enough to open an incident. This does not add calendar schedules or time zones.

Creation starts an evaluation generation in `untested`. Its first deadline is
creation time plus interval plus tolerance. The same rule applies from the
instant a paused monitor is resumed. Silence beyond that persisted deadline
creates a `report_missing` failure observation and opens an incident. The
deadline worker may run late, but records the deadline itself as when failure
began; repeated processing of the same crossed deadline is idempotent and does
not create repeated history or incidents.

Pausing clears the pending deadline, starts no missing observation, and keeps
history and any open incident. Reports presented while paused are rejected and
do not refresh evidence. Resuming starts a new generation in `untested` with a
new initial deadline. A fresh applicable success is required to recover an
open incident; resuming alone never does so.

## Receipt time, observation order, and lifecycle

Every accepted report receives an immutable `received_at` from the server's
`TimeProvider`. It is authoritative for report freshness, deadline movement,
retention age, and display of last receipt. A sender cannot backdate or
postdate receipt.

The JSON route additionally requires `observed_at`, an RFC 3339 UTC instant
assigned by the sender when it obtained the result. Within one evaluation
generation, only a report with an `observed_at` strictly newer than the last
applied report may change health or an incident. The first accepted report at
an observation instant wins; later distinct reports at the same instant are
late. A late report remains immutable history and returns a successful receipt,
but cannot change health, last success, a deadline, or an incident. In
particular, a delayed old success cannot recover a newer failure.

An applicable explicit failure sets the active monitor to `failing` and opens
or updates its one incident immediately with reason `reported_failure`.
Tolerance never postpones it. An applicable success sets the monitor to
`healthy`, records last success, and resolves the open incident. In state
report mode, either applicable outcome updates the report deadline. Every
accepted report records last receipt in both modes, including a late report,
but only an applicable job-completion success changes the success deadline.

When a deadline is crossed, upaffe allocates a monitor-local observation
sequence and persists a synthetic failure observation in the current
generation. A later JSON report whose `observed_at` predates that deadline is
late. A success observed after it is a fresh recovery. The existing single
open-incident constraint and transactional application rules from ADR 0003
also apply to report and missing-deadline observations.

`observed_at` may be up to five minutes ahead of receipt and up to 90 days
behind it. Values outside that input boundary are rejected. Sender clocks are
therefore used only to order the sender's own observations; server time remains
authoritative for whether evidence arrived on time.

## Reporting routes and idempotency

The full reporting route is:

```http
POST /api/reports
Authorization: Bearer uar_<secret>
Content-Type: application/json

{
  "reportId": "01990f4e-4958-7ab2-9d4d-b31833b4c706",
  "observedAt": "2026-09-18T16:30:00Z",
  "outcome": "success",
  "reason": null
}
```

`reportId` is a caller-generated UUID and `outcome` is `success` or `failure`.
`reason` is optional UTF-8 text of at most 1,024 Unicode scalar values and is
accepted only for failure. It is untrusted diagnostic data, never instruction,
and is excluded from logs and notification subjects. The stable incident reason
is `reported_failure`; the diagnostic reason can be shown separately.

The pair `(monitor, reportId)` is unique across credential rotation. Repeating
the same ID and canonical payload returns the original receipt without another
observation or transition. Reusing an ID with different content is
`report_id_conflict`. Idempotency records are retained for 90 days from receipt;
this is the explicit retry boundary, and clients must not retry an older report.

A minimal caller may use either of these equivalent routes:

```http
POST /api/report/{secret}
GET /api/report/{secret}
```

Each accepted request creates a success whose observation and receipt time are
the same server instant. It has no caller idempotency key, failure payload, or
backdated observation. Retries are therefore separate successes, which are
harmless to current health but can create separate history. `POST /api/reports`
is required when callers need failures, reliable retry identity, or explicit
observation ordering. `HEAD` and all other methods do not report success.

A first JSON receipt returns `202 Accepted` with `reportId`, `receivedAt`, and
whether it was applied; an identical retry returns the same response. A simple
success returns `204 No Content`. Malformed input returns `400`; a syntactically
valid but out-of-bound report returns `422`; conflicting ID reuse returns `409`.
An absent, unknown, expired, or revoked reporting secret always returns the same
`401` problem and reveals no monitor existence. Reporting routes never accept
a management credential or browser session.

## Reporting credentials and secrecy

Each push monitor owns one reporting credential identity and one current
secret. A secret is 32 random bytes encoded without padding behind the
recognizable `uar_` prefix. PostgreSQL stores only a unique SHA-256 digest.
Creation and rotation disclose plaintext exactly once in an explicit credential
response. Rotation installs a new current secret and keeps the previous digest
valid for a five-minute overlap; revocation invalidates every digest
immediately. Removing the monitor also invalidates the credential.

The same secret authenticates the bearer and simple routes, but it grants only
report creation for its bound monitor. It cannot read that monitor, choose a
different monitor, or invoke management operations. Ordinary monitor responses,
lists, reports, history, exports, browser pages, CLI output, and database
diagnostics expose only whether a credential exists and when it was rotated or
revoked.

The simple path is a credential. Request logging, tracing, metrics, exception
messages, access logs owned by the application, and `Location` headers must
redact its segment before emission. The product documentation warns operators
to apply equivalent redaction in any reverse proxy. Reporting secrets never
appear in query strings.

## History and retention

Reports and synthetic missing observations are monitoring history under the
same 90-day policy as HTTP checks. The exclusive cutoff and incident-reference
exceptions remain: observations exactly 90 days old survive until the next
daily run, and an open incident plus every observation it references is kept.
Current last-received, last-success, deadline, health, and incident facts are
durable monitor state rather than inferred from retained detail.

Consequently, pruning old report detail never invents health or changes a
deadline. Idempotency rows may be pruned only after their 90-day guarantee has
expired. Restarts reconstruct neither deadlines nor missing observations from
memory: the persisted monitor deadline and transactional uniqueness boundary
remain authoritative.

## Consequences

The JSON contract makes senders preserve a report ID across retries and provide
an observation time. This is slightly more work than a bare heartbeat, but it
gives failures and delayed concurrent deliveries deterministic meaning. The
simple route remains sufficient for callers that can only signal success with
one request and accept its deliberately smaller guarantee.

Push monitoring performs no local inspection, starts no jobs, and interprets
no remote content as instruction. SDKs, MCP, compatibility routes, fixed
calendar schedules, and combined backup semantics remain outside this decision.
