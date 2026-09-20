# HTTP API

The API is the single application boundary used by the web application and CLI.
Every operation is below `/api`; other paths are reserved for the SPA. There is
no API-version segment. Each response carries `Upaffe-Version`: `0.1.0` for a
v0.1.0 image, `0.0.0-rev.<full-commit-sha>` for a revision image, or
`0.0.0-dev` for a local build without an explicit version.

The product-facing API covers read-only initialization state, browser sessions,
management credentials, projects, and monitor administration. Initial operator
setup uses a local container command with database access, not an HTTP write.

| Method and path | Purpose |
| --- | --- |
| `GET /api/version` | The instance version. This is the first operation exercised by both generated clients. |
| `GET /api/health/live` | Whether the process can answer; it touches no dependency. |
| `GET /api/health/progress` | Whether both monitoring workers completed a successful idle or active iteration within two minutes. |
| `GET /api/health/ready` | Whether PostgreSQL answers with exactly the schema this binary knows. |
| `GET /api/bootstrap` | Whether local operator setup is still required. |
| `POST /api/session` | Sign in and receive a fresh server-side browser session cookie. |
| `GET /api/session` | Inspect the operator identity admitted by the browser session. |
| `DELETE /api/session` | Revoke the current browser session and expire its cookies. |
| `POST /api/management-credentials` | Create a named credential and reveal its token once. |
| `GET /api/management-credentials` | List credential metadata without tokens. |
| `POST /api/management-credentials/{id}/rotate` | Issue a new token with a ten-minute overlap for the previous token. |
| `DELETE /api/management-credentials/{id}` | Revoke every token for the credential immediately. |
| `POST /api/projects` | Create a project idempotently by its immutable key. |
| `GET /api/projects?deleted=false` | List live projects, or deleted projects with `deleted=true`. |
| `GET /api/overview` | Read a current, safe health overview across live projects. |
| `GET /api/monitors` | Search and page the safe cross-project HTTP and push monitor inventory. |
| `GET /api/projects/{key}` | Read a live or deleted project by immutable key. |
| `PUT /api/projects/{key}` | Rename a live project at the version last read. |
| `DELETE /api/projects/{key}?version={version}` | Soft-delete a project at the version last read. |
| `POST /api/projects/{key}/restore` | Restore a project at the version last read. |
| `GET /api/email/settings` | Read safe SMTP settings, credential presence, and default recipients. |
| `PUT /api/email/settings` | Update SMTP sender, relay, security, authentication username, and public detail URL at the settings version last read. |
| `PUT /api/email/password` | Replace the write-only SMTP password explicitly. |
| `DELETE /api/email/password?version={version}` | Clear the SMTP password explicitly. |
| `PUT /api/email/default-recipients` | Replace recipients copied to new projects. |
| `POST /api/email/test` | Submit one explicit test message to SMTP without creating an incident. |
| `GET /api/email/deliveries` | Page safe per-recipient alert and recovery delivery states. |
| `GET /api/email/deliveries/summary` | Read instance pending, retrying, failed, and SMTP-accepted counts. |
| `GET /api/email/incidents/{incidentId}?monitor_type=http\|push` | Read an incident's announcement state and per-recipient deliveries. |
| `GET /api/projects/{key}/email-summary` | Read one project's delivery counts. |
| `GET`, `POST`, `DELETE /api/projects/{projectKey}/maintenance` | Inspect, start or extend, and end a project maintenance window. |
| `GET`, `POST`, `DELETE /api/projects/{projectKey}/http-monitors/{monitorKey}/maintenance` | Manage direct HTTP monitor maintenance and inspect inherited project maintenance. |
| `GET`, `POST`, `DELETE /api/projects/{projectKey}/push-monitors/{monitorKey}/maintenance` | Manage direct push monitor maintenance and inspect inherited project maintenance. |
| `GET /api/projects/{key}/recipients` | Read a project's recipients and version. |
| `PUT /api/projects/{key}/recipients` | Replace recipients on a live project at the version last read. |
| `POST /api/projects/{projectKey}/http-monitors` | Create an HTTP monitor idempotently within a project. |
| `GET /api/projects/{projectKey}/http-monitors` | List live HTTP monitors in a project. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}` | Read one HTTP monitor without secret values. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/checks` | List completed checks newest first with cursor pagination. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/checks/{checkId}` | Read one retained completed check as current evidence. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/incidents` | List incidents newest first with cursor pagination. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/incidents/{incidentId}` | Read one retained incident, including one outside the first page. |
| `PUT /api/projects/{projectKey}/http-monitors/{monitorKey}` | Update monitor configuration at the version last read. |
| `DELETE /api/projects/{projectKey}/http-monitors/{monitorKey}?version={version}` | Remove a monitor while retaining its identity and history. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/pause` | Pause a monitor at the version last read. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/resume` | Resume a monitor and make a fresh check due. |
| `PUT /api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}` | Set or replace one write-only request header. |
| `DELETE /api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}?version={version}` | Remove one write-only request header. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/test` | Execute and record one immediate bounded check. |
| `POST /api/projects/{projectKey}/push-monitors` | Create a job-completion or state-report monitor idempotently. |
| `GET /api/projects/{projectKey}/push-monitors` | List live push monitors in a project. |
| `GET /api/projects/{projectKey}/push-monitors/{monitorKey}` | Read push configuration and current facts without a reporting secret. |
| `GET /api/projects/{projectKey}/push-monitors/{monitorKey}/reports/{reportId}` | Read one retained push report as current evidence. |
| `GET /api/projects/{projectKey}/push-monitors/{monitorKey}/incidents/{incidentId}` | Read one retained push incident, including one outside the first page. |
| `PUT /api/projects/{projectKey}/push-monitors/{monitorKey}` | Update push configuration at the version last read. |
| `DELETE /api/projects/{projectKey}/push-monitors/{monitorKey}?version={version}` | Remove a push monitor while retaining its identity and history. |
| `POST /api/projects/{projectKey}/push-monitors/{monitorKey}/pause` | Pause a push monitor. |
| `POST /api/projects/{projectKey}/push-monitors/{monitorKey}/resume` | Resume with a fresh initial reporting window. |
| `POST /api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential` | Issue and reveal the monitor's reporting token and secret URL once. |
| `GET /api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential` | Read non-secret reporting credential metadata. |
| `POST /api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential/rotate` | Reveal a replacement with five-minute overlap. |
| `DELETE /api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential` | Revoke all reporting secrets immediately. |
| `POST /api/reports` | Submit an idempotent JSON success or failure with a reporting bearer token. |
| `GET` or `POST /api/report/{secret}` | Submit a success with the secret URL and no request body. |
| `GET /api/openapi/v1.json` | The generated OpenAPI document. It does not list itself. |

Every routed endpoint declares exactly one access boundary. Version, health,
OpenAPI, read-only bootstrap state, sign-in, and non-API fallbacks are public.
Reading or deleting a session is
browser-only. Management operations accept either that browser session or
`Authorization: Bearer <token>`. The host has an authenticated fallback policy,
so an endpoint without an explicit public declaration is closed rather than
accidentally anonymous. Liveness and readiness are technical deployment checks;
neither asserts that the monitoring loop is progressing.

`POST /api/bootstrap` was removed after v0.1.0. On upgrade, previously
initialized databases continue to start, but installers must use the local
`upaffe bootstrap` command for a new database. The command returns a stable
JSON status and exits `0` on success, `2` on invalid input, `3` when already
initialized, `4` when a recovery target is absent, and `1` on infrastructure
failure. Only successful bootstrap or recovery output contains a token.

## Browser session

`POST /api/session` accepts the operator email and password, returns `204`, and
sets a new opaque cookie. Unknown email, absent operator, wrong password, and a
throttled attempt all return `401 sign_in_rejected`; password verification still
runs in the same relevant work class for unknown and known email addresses. The
rolling throttle allows five failed attempts per normalized email and twenty per
source address in 15 minutes.

Only a SHA-256 digest of the session secret reaches PostgreSQL. A session ends
after 12 hours without use or seven days absolutely, and its use timestamp is
written at most every five minutes. Missing authentication returns
`authentication_required`; a manipulated, expired, or revoked cookie returns
the same `authentication_rejected` response.

Over HTTPS the cookie is named `__Host-upaffe_session` and is `Secure`; local
plain HTTP uses `upaffe_session`. Both are `HttpOnly`, `SameSite=Lax`, scoped to
`/`, and carry the absolute expiry. `DELETE /api/session` additionally requires
`X-Upaffe-CSRF: 1` and an `Origin` whose authority equals the request host. It
revokes the server row and expires both cookie names.

## Management credentials

A management credential has a stable UUID and a unique operator-chosen name.
Creation and rotation are the only responses that contain a full token, in the
form `upaffe_<32 hex identifier>_<base64url secret>`. The public identifier
selects the credential; PostgreSQL stores only the SHA-256 digest of the 32-byte
secret. Lists contain metadata only, including rotation and revocation times.

Rotation activates its new token immediately and keeps the previous token valid
for exactly ten minutes so an unattended client can switch without a gap.
Revocation is idempotent and rejects every token for that credential on its next
request. A bearer may administer credentials but fails the browser-only session
operation with `403 forbidden`; it cannot create an operator or alter the human
login.

No authentication returns `401 authentication_required`. A malformed, unknown,
expired, or revoked cookie or bearer token returns the same
`401 authentication_rejected`; ordinary responses and logs do not distinguish
those stored states. Unknown credential IDs return `404 not_found`, duplicate
names and rotation of a revoked credential return `409 conflict`, and invalid
names return `400 validation`.

Authentication audit messages contain the HTTP operation, outcome, access path,
and public session or credential ID when available. They never contain the
presented bearer token, cookie secret, or password.

## Monitor inventory

`GET /api/monitors` requires management authentication. It searches live HTTP
and push monitors in live projects. Optional `project`, `state` (`healthy`,
`failing`, `untested`, or `paused`), `type` (`http` or `push`), and `q` filters
compose. Search matches project name/key, monitor name/key, purpose, and the
query-redacted HTTP target, case-insensitively. Search treats `%` and `_` as
literal characters. `q` is at most 120 characters. `limit` is 1–100 (default
50); `offset` is 0–1,000,000 (default 0). Invalid filters return
`400 validation`.

The response has `as_of`, `total`, `limit`, `offset`, `has_more`, and `items`.
Each item has project and monitor identity, purpose, type, push mode or safe HTTP
target, interval and optional push tolerance, state, overdue and open-incident
flags, maintenance end, latest observation, last success, and next due time.
An absent purpose is `null`. Open incidents come first, followed by failing,
overdue, untested, paused, and healthy monitors; project key, type, and monitor
key break ties. Count and page use two database queries regardless of monitor
count. The response excludes request header values, target query values,
reporting secrets, and remote diagnostic content.

## Projects

Every project has a generated internal UUID, an immutable lower-case key, and a
mutable display name. Keys match `^[a-z][a-z0-9-]{1,39}$`; names are trimmed and
contain 1–100 characters. Every response includes the current positive
`version`, creation and update times, and a nullable deletion time.

`POST /api/projects` accepts `key` and `name`. A new key returns `201`; repeating
the same accepted key and name returns the existing project with `200`, the same
UUID, and no version change. Reusing the key with another name returns
`409 conflict`, including while the original project is deleted. Creation never
implicitly restores a deleted project.

`GET /api/projects` lists live projects by key. `deleted=true` lists only
deleted projects. `GET /api/projects/{key}` reads either state so a caller can
obtain the version needed for restoration. Rename accepts `{ "name": "...",
"version": 1 }`; restore accepts `{ "version": 3 }`; delete carries the same
positive version as its `version` query parameter. A successful change returns
the full project and increments its version. Repeating delete at the current
deleted version or restore at the current live version is idempotent and does
not increment it.

An unknown key returns `404 not_found`. Invalid keys, names, or versions return
`400 validation` with field errors. A stale version, a different name for an
existing key, or an attempted rename while deleted returns `409 conflict`.
All project operations use the management boundary and therefore accept a
browser session or management credential; anonymous requests return
`401 authentication_required`.

## Email configuration and recipients

The cross-surface operator workflow and SMTP acceptance limits are in
[the email and maintenance guide](./email-maintenance.md).

The instance has one versioned SMTP configuration. `GET /api/email/settings`
returns relay host and port, transport security (`none`, `starttls`, or `tls`),
sender address and display name, optional authentication username, public
detail URL, `has_password`, and the instance default recipient list. An
unconfigured instance has version `0`. `PUT /api/email/settings` accepts those
non-secret settings and a version. Password replacement requires a separate
`PUT /api/email/password` with `version` and `password`; clearing uses
`DELETE /api/email/password?version=…`. Reads and mutation responses never
return the password. Stale versions return `409 conflict`.

`PUT /api/email/default-recipients` accepts `version` and `recipients`. The
default is copied once to each newly created project. Changing the default
does not edit existing projects. `GET` and `PUT /api/projects/{key}/recipients`
use the project's version; changing recipients increments it, and a deleted
project cannot be changed. The ordered list has at most 50 plain addresses.
Inputs are trimmed, domains are lowercased, and case-insensitive duplicates
or malformed addresses return `400 validation`. An empty list opts a project
out of incident email. All operations require browser or management-credential
authentication; browser mutations also require CSRF protection.

`POST /api/email/test` accepts a plain `recipient` address. It uses the same
configured relay, sender, security, and optional authentication as incident
email, with a 30-second total deadline. A successful response says
`accepted_by_smtp` and records the acceptance time. That is evidence of relay
acceptance, not inbox arrival. It creates no incident or retry intent. Missing
sender settings return `409 email_not_configured`; a relay failure returns
`502 smtp_rejected` with a short failure code. Raw SMTP diagnostics and
credential values are not returned.

## Email delivery status

The four status operations require management authentication. The delivery
list accepts `project_key`, `monitor_type` (`http` or `push`), `monitor_key`,
`incident_id`, and a stored `state` (`queued`, `claimed`, `retrying`, `accepted`,
`terminal_failure`, or `obsolete`). `monitor_key` requires the project and
monitor type. Results are newest first, with `limit` 1–100 (default 20),
`offset` 0–10,000, `total`, and `has_more`. Filtering by state uses the stored
state; a pending row may instead display derived `suppressed` while maintenance
or pause is active or its scope is removed.

Each delivery contains the recipient, incident and scope identifiers, kind,
state, attempt count, last and next attempt times, SMTP acceptance or terminal
time, and a short sanitized failure code. It contains no SMTP response text,
message body, password, or monitor secret. `accepted` means the relay accepted
submission, not that an inbox received the message. The incident view
distinguishes `pending`, `suppressed`, `announced`, `failed`,
`awaiting_reconciliation`, `no_recipients`, and `silent`. Its suppression reason
is separate from a monitor's health state. Summaries expose pending, retrying,
terminal-failure, and SMTP-accepted counts with the oldest pending time.
Unknown incidents or projects return `404`; invalid filters or pagination
return `400`.

Final delivery rows older than 90 days and superseded maintenance windows older
than 90 days are removed daily. Open incidents and accepted alerts awaiting a
recovery decision retain their delivery rows. The latest maintenance version
for each scope remains available for optimistic writes. Older absence is not
evidence that no mail was attempted.

## Timed maintenance

Each project, HTTP monitor, and push monitor has an independent maintenance
scope. `GET` returns the latest direct window version and times, whether it is
active, the effective active scopes, and the latest effective expiry. A monitor
inherits project maintenance. If both direct and project windows are active,
the effective expiry is the later end; ending one scope does not end the other.
Maintenance is separate from monitor state and pause.

`POST` accepts the last read maintenance `version` and `duration_seconds` from
60 through 2,592,000. Version `0` starts a scope with no prior window. Starting
again while active extends to the later of the current expiry and server time
plus the requested duration. An expired or ended window is followed by a new
record with the next version. `DELETE ?version=…` ends only the direct active
window early. Stale versions or ending an inactive window return `409 conflict`;
invalid durations return `400 validation`, and absent scopes return `404`.
The server's current UTC time decides expiry, including after downtime: no
background transition is needed. Checks, reports, and incident state continue.
The delivery worker keeps queued mail suppressed while any effective window is
active. When all relevant windows end, an incident still open and not yet
announced becomes eligible once; an incident opened and resolved entirely in
maintenance produces no delayed email. A recovery for a previously
SMTP-accepted alert waits until suppression ends.

## HTTP monitors

The complete fictional operator workflow tying these resources to CLI, web,
storage, scheduling, and security behavior is in
[the HTTP monitoring guide](./http-monitoring.md).

An HTTP monitor has a generated UUID and an immutable key scoped to its
project. Its create and read contract includes name, query-redacted target URL,
whether a query is configured, exact expected status, `none`, `required`, or
`forbidden` text condition and fragment, interval and timeout seconds, failure
threshold, optional purpose, operator instruction and runbook URL, current state and
failure count, next due time, latest-result and latest-success IDs, header
metadata, optimistic version, and lifecycle timestamps. `open_incident_id` is
present exactly while an incident remains unresolved. In combination with the
state and failure count, it distinguishes a visible sub-threshold failure from
an open incident, including across pause and resume.

Creation accepts the complete target URL and optional `{ "name", "value" }`
headers. The first accepted key returns `201`; repeating every public and secret
fact returns the existing monitor with `200`. A different fact for the same key
returns `409 conflict`. Ordinary responses expose the target without its query
and expose header names and timestamps without values. They never return enough
information to reconstruct a query or header secret.

Update carries all non-secret configuration plus the version last read.
`target_url` is exceptional: omit or send `null` to preserve the complete
stored target, including its write-only query; send a value to replace the
complete target. A query can therefore survive unrelated edits without being
revealed. Individual header `PUT` accepts `{ "value": "...", "version": N }`
and creates or replaces the named secret. Header `DELETE` carries `version` in
the query. Both return only refreshed monitor and header metadata.

`purpose` is an optional operator-written description of what the monitor
covers. It is trimmed to at most 240 characters; an empty value becomes `null`.
It differs from `instruction`, which guides incident investigation. Both HTTP
and push monitor create and full-replacement update operations accept it.
Sending `null`, empty text, or omitting it on update clears it. Existing
monitors return `null` until documented.

Pause, resume, and removal are explicit operations. Pause and resume accept
`{ "version": N }`; removal uses the version query parameter. A pause clears
the due time and shows `paused`. Resume starts a new evaluation generation,
shows `untested`, and makes a fresh check due without discarding earlier result
or incident facts. Removal hides the monitor from ordinary reads and lists but
retains its key and history.

A check that completes before a competing pause may apply normally and is then
preserved under the paused state. A check that completes after pause is retained
as history but cannot change monitor or incident state. Resume starts a new
evaluation generation, so a check begun before that pause cannot evaluate the
resumed monitor regardless of which completion wins the race. Pause and resume
never resolve an open incident; only a fresh accepted success does.

The test operation runs the same bounded executor used by scheduled checks. It
records an ordered requested check, does not move the regular next-due time,
and applies its fresh result to the monitor under the generation and ordering
rules. Its response contains the check ID, whether it applied to current state,
structured outcome and reason, sanitized effective URL, response time, and the
updated monitor. A paused or removed monitor rejects testing with `409`.

Check history is separate from the compact monitor response. `GET .../checks`
returns completed checks newest first by monitor-local sequence. Each item has
the trigger, scheduled/start/completion times, outcome, stable failure reason,
status, response time, and sanitized effective URL. It contains no request
headers, target query, response body, or executor message. `applied_to_current_state`
records whether evaluation used the result; it is null for checks written before
that fact was stored. Incident history at `GET .../incidents` contains its preserved first,
opening, latest-failure, and optional resolution facts.
`GET .../checks/{checkId}` returns that same safe check shape when a current
result or last success is older than the first history page. The ID must belong
to the named live monitor; otherwise it returns 404.
`GET .../incidents/{incidentId}` likewise reads one retained incident in the
named live monitor, including a deep-linked incident outside the first page.

Both history operations default to `limit=50` and accept 1 through 100.
`before_sequence` and `before_opening_sequence` are exclusive positive cursors;
the page returns the matching `next_before_*` cursor only when another row is
known to exist. Removed monitors remain absent from ordinary detail operations.
History older than the documented 90-day retention boundary may no longer be
present, so absence before the retained window never asserts that a monitor was
healthy.

Application rules validate the limits in ADR 0004 independently of JSON model
binding. Invalid combinations return `400 validation`; unknown project/monitor
associations return `404 not_found`; deleted projects, removed monitors, stale
versions, and conflicting idempotent creates return `409 conflict`. All routes
use the management boundary.

## Push monitors

The complete workflow tying both modes to CLI, web, storage, deadlines,
incidents, and the reporting-secret boundary is in
[the push monitoring guide](./push-monitoring.md).

A push monitor is created with immutable `job_completion` or `state_report`
mode, a project-scoped key, name, interval and tolerance seconds, and optional
instruction and runbook. Responses include state, last receipt, latest report
and success IDs, next persisted deadline, open incident ID, lifecycle times,
and whether a reporting credential exists. They never contain a reporting
secret. Intervals are 30 seconds through 365 days; tolerance is zero through 30
days and no greater than the interval.

Create is idempotent only when every supplied fact matches. Update changes the
mutable configuration with an optimistic version; mode and key remain fixed.
Pause clears the deadline. Resume starts a fresh evaluation generation in
`untested` with a new interval-plus-tolerance window, retaining history and any
open incident, last receipt, and last success. While paused, JSON and simple
reports return `409` before retry or outcome handling and cannot change those
facts. A replay from an earlier generation after resume returns its original
receipt but does not evaluate the new generation; only a newly accepted fresh
success resolves a retained incident. Removal hides ordinary reads and lists
and revokes an existing reporting credential. Invalid values return `400`,
missing associations `404`, and stale versions, deleted resources, or
conflicting creates `409`.

Reporting credential issue and rotation are the only responses containing the
`uar_` token and its `/api/report/{secret}` URL; callers must store them before
discarding the response. Metadata and monitor responses never reveal either.
Rotation keeps the preceding digest valid for five minutes and reports that
expiry; revocation is idempotent and rejects all current and overlapping
secrets immediately. The credential is bound to one monitor and grants neither
read nor management access. Application request logging must redact the secret
segment, and reverse proxies must apply an equivalent path-redaction rule.

`POST /api/reports` is public at the management-authentication layer and accepts
only `Authorization: Bearer uar_<secret>`. Its body contains a non-empty UUID
`report_id`, RFC 3339 `observed_at`, `success` or `failure` outcome, and an
optional failure-only diagnostic reason of at most 1,024 Unicode scalar values.
The reporting credential selects exactly one monitor; neither project nor
monitor identity is accepted from the caller.

The server assigns `received_at` and a monitor-local sequence under a row lock.
The first submission returns `202` with those facts and whether the observation
was applicable. An identical `(monitor, report_id)` replay returns the original
receipt with `duplicate: true`; different content under the same ID returns
`409 report_id_conflict`. A report older than the latest applicable sender
observation is retained with `applied: false`, so a delayed success cannot erase
newer failure evidence. Receipt and observation timestamps are normalized to
PostgreSQL microsecond precision for stable retries.

Missing, malformed, unknown, expired, revoked, or wrong-monitor secrets all
return the same `401 reporting_rejected` without revealing stored identity.
Payload shape errors return `400 validation`; observation times more than five
minutes ahead or 90 days behind receipt return `422 unprocessable`. A paused
monitor refuses every report with `409`, including an otherwise identical
replay.

The simple secret route accepts `GET` and `POST` and returns `204`. Each request
is a distinct success observed and received at the server instant; it has no
failure payload or caller idempotency key. `HEAD` and other methods do not
report success. Unknown, expired, and revoked URL secrets use the same
`401 reporting_rejected` boundary. The application replaces the credential
path segment before routing and application logs; standard request-start logs
are disabled because they run before application middleware.

Push report history is available separately at `GET
.../push-monitors/{monitorKey}/reports`. It returns immutable reports newest
first with internal and sender report IDs, generation and sequence, observation
and receipt times, outcome, applicability, and only the stable
`reported_failure` or `report_missing` reason. Sender diagnostic text and
reporting credentials are deliberately absent. `GET
.../push-monitors/{monitorKey}/incidents` returns preserved opening,
latest-failure, and optional recovery report references, sequences, times, and
stable original/latest reasons. Both endpoints use the same default limit 50,
maximum 100, and exclusive `before_sequence` or `before_opening_sequence`
cursors as HTTP history.
`GET .../reports/{reportId}` retrieves the same safe report shape by internal
ID, scoped to the named live monitor. It supports current evidence retained
beyond the first page without exposing the sender's diagnostic text.
`GET .../incidents/{incidentId}` returns the scoped push incident timeline.

## Project investigation report

`GET /api/projects/{key}/report` returns one management-authenticated,
read-only snapshot for a live project. Unknown or deleted projects return
`404 not_found`; malformed keys return `400 validation`. Browser sessions and
management bearers see the same facts. See [ADR 0008](adr/0008-one-call-project-report.md)
for the full field semantics, ordering, null behavior, and trust boundary.

The typed response has `generated_at`, `project`, `counts`, `attention`,
`healthy`, `project_maintenance`, and `email`. `attention` contains full current
facts for failing, untested, paused, or overdue HTTP and push monitors. It is
ordered by urgency, type, and key. `healthy` contains shorter summaries ordered
by type and key. The snapshot exposes only stable failure reasons. It keeps
operator `purpose`, `instruction` and `runbook_url` separate from diagnostic fields.
Purpose is present in both attention and healthy entries.
Use the existing paginated check, report, incident, and delivery endpoints for
history. The report cannot prove that an overdue worker has run or that SMTP
acceptance reached an inbox.

## Instance health overview

`GET /api/overview` returns one management-authenticated, read-only
snapshot of every live project. Browser sessions and management bearers receive
the same response. `generated_at` is the single UTC instant used to compare
stored deadlines and active maintenance; an overdue deadline is not itself a
failed observation. `counts` sums all live HTTP and push monitors. `attention`
contains monitors with a non-healthy state or overdue work, ordered by open
incident, failing, overdue, untested, paused, then project key, type, and monitor
key. An incident kept during a pause remains first, with state `paused` and a
null `next_due_at`. `projects` contains every live project, including projects
with no monitors or only healthy monitors. It orders affected projects first,
then project key. All collections are empty arrays when they have no entries;
optional observations, incidents, maintenance, and deadlines are omitted when
absent by the API serializer.

Each attention entry has stable project, monitor type, and monitor keys for
navigation, current state, mode, stored due time, last success time, latest
applicable outcome and stable reason, open incident identity and time, and
effective maintenance expiry. It does not include a target URL, request
header, reporting credential, raw HTTP body, or sender diagnostic. Project and
instance delivery summaries count pending work (queued, claimed, and retrying),
overdue work, retrying, terminal failure, and SMTP acceptance. Overdue means a
queued or retrying intent whose next attempt is before `generated_at`, or a
claimed intent whose lease has expired. `oldest_pending_at` is omitted
when no work is pending. SMTP acceptance never means inbox receipt. Retained
delivery history limits these counts. Detailed configuration and histories
remain on the project report and paginated endpoints. See
[ADR 0009](adr/0009-project-instance-health-overview.md).

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
