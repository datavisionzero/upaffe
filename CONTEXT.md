# upaffe

The language of one monitoring instance operated by one person and administered
by that person or by automation acting for them.

## Access

**Operator**:
The sole human responsible for an instance and all of its monitoring
configuration.
_Avoid_: User, administrator, owner, member

**Identity**:
The authenticated origin of a management action: either the operator using a
browser session or automation using a management credential.
_Avoid_: Account, principal, role

**Bootstrap**:
The one-time establishment of the operator in a fresh instance.
_Avoid_: Registration, sign-up, invitation

**Browser session**:
A revocable, time-bounded admission created when the operator signs in.
_Avoid_: Login token, browser credential

**Management credential**:
A named, independently revocable authorization for noninteractive automation
acting with the operator's management authority.
_Avoid_: API user, agent account, reporting token

**Credential rotation**:
The controlled replacement of a management credential's secret while its
identity and name remain the same.
_Avoid_: New account, password change

## Monitoring organization

**Project**:
A persistent organizational boundary for monitoring configuration. Every
monitor belongs to exactly one project; a project is not a security tenant.
_Avoid_: Organization, workspace, team, tenant

**Project key**:
The stable, operator-chosen identifier of a project. It survives renaming,
deletion, and restoration.
_Avoid_: Project name, database ID

**Project name**:
The mutable human-readable label of a project.
_Avoid_: Project key

**Deleted project**:
A project removed from ordinary use while retaining its identity for explicit
restoration.
_Avoid_: Archived project, purged project

## Monitoring

**Monitor**:
A persistent definition of something upaffe observes, belonging to exactly one
project and identified there by an immutable monitor key.
_Avoid_: Check, probe, job

**Monitor key**:
The stable, operator-chosen identifier of a monitor within its project. A
project key and monitor key together identify one monitor across its lifetime.
_Avoid_: Monitor name, database ID

**Monitor name**:
The mutable human-readable label of a monitor.
_Avoid_: Monitor key

**HTTP monitor**:
A monitor that evaluates an HTTP or HTTPS target by actively making requests.
_Avoid_: Website, endpoint, uptime check

**Push monitor**:
A monitor whose observations are submitted by an external script or
application rather than produced by an upaffe check. It has either job
completion or state report mode.
_Avoid_: Heartbeat, HTTP monitor, remote check

**Job completion**:
The push-monitor mode in which each success means one job completed and moves
the next success deadline. A failure is immediate evidence of a failed job but
does not move that deadline.
_Avoid_: State report, job start, process liveness

**State report**:
The push-monitor mode in which every report describes the sender's latest
evaluation of a condition. Success and failure both prove the sender reported,
while only success makes the monitored condition healthy.
_Avoid_: Job completion, check result

**Report**:
One immutable success or failure observation submitted to a push monitor. A
JSON report has a sender-chosen report ID and observation time; a simple secret
route creates a success observed when upaffe receives the request.
_Avoid_: Check, monitor state, incident

**Received at**:
The server-assigned instant when upaffe accepts a report. It is authoritative
for evidence freshness and cannot be supplied by the sender.
_Avoid_: Observed at, last received

**Last received**:
The most recent authoritative receipt time that counts as evidence the sender
is still reporting. It is retained separately from health and last success.
_Avoid_: Latest result, latest success

**Last success**:
For a push monitor, the newest applicable successful report in observation
order. In job-completion mode it anchors the next success deadline; in either
mode it is retained when a later report fails.
_Avoid_: Last received, recovery, healthy state

**Reporting deadline**:
The persisted instant by which the next required success or report must arrive:
the interval plus tolerance after the applicable anchor. Crossing it produces
one missing-report failure observation, not a synthetic report.
_Avoid_: Calendar schedule, check due time, timeout

**Tolerance**:
The configured grace duration added once to a push monitor's interval. It
applies to missing evidence and never delays an explicit failure.
_Avoid_: Failure threshold, retry delay, maintenance

**Reporting credential**:
A monitor-scoped, independently rotatable and revocable secret that may submit
reports only to its push monitor. It grants no management or read access.
_Avoid_: Management credential, browser session, operator account

**Check**:
One scheduled or explicitly requested attempt to evaluate an active monitor.
It has a stable check ID and a monitor-local order assigned before execution.
_Avoid_: Monitor, test, run

**Check result**:
The immutable success or failure observation produced by one completed check.
At most one result exists for a check ID.
_Avoid_: Check, status, incident

**Failure threshold**:
The number of consecutive applicable failed results required to open an
incident. It does not hide a failed result or make a failing monitor healthy.
_Avoid_: Retry count, severity

**Incident**:
One persistent record of a monitor's uninterrupted failure episode. A monitor
has at most one open incident; later failures update rather than duplicate it.
_Avoid_: Alert, check failure, outage

**Recovery**:
The transition caused by a fresh successful result that resolves an open
incident and makes an active monitor healthy.
_Avoid_: Acknowledgement, reset, incident deletion

**Incident announcement**:
The first SMTP-accepted alert for an incident and recipient. It is evidence of
SMTP acceptance, not inbox delivery.
_Avoid_: Incident opening, successful delivery, acknowledgement

**Notification intent**:
The durable decision to send one alert or recovery for one incident and
recipient. Its identity survives retries and worker restarts.
_Avoid_: SMTP attempt, email message, incident

**Delivery attempt**:
One bounded attempt to submit a notification intent to the configured SMTP
server. An attempt can fail or have an uncertain outcome without changing the
intent's identity.
_Avoid_: Notification intent, inbox delivery

**Terminal delivery failure**:
An intent whose retry policy is exhausted or whose failure cannot be retried.
It stays visible until the notification record ages out.
_Avoid_: Incident resolution, SMTP acceptance

**Project recipients**:
The addresses eligible for notifications about a project's incidents. A new
project copies the instance default recipient set once, at creation.
_Avoid_: Operator accounts, escalation chain

**Timed maintenance**:
A finite, server-timed suppression window for one project or monitor. It leaves
checks, reports, health, and incident history running.
_Avoid_: Pause, recurring schedule, incident resolution

**Pause**:
An operator-controlled suspension of checks and notifications that leaves
history and any open incident intact.
_Avoid_: Maintenance, healthy, disabled

**Monitor state**:
The current evaluation state of a monitor: untested, healthy, failing, or
paused. It is distinct from whether an incident is open.
_Avoid_: Incident state, last result

**Latest result**:
The newest applicable check result in monitor order. It remains visible as
history when the monitor is paused or awaiting evaluation after resumption.
_Avoid_: Monitor state, latest completion

**Latest success**:
The newest applicable successful result in monitor order, retained when later
checks fail or the monitor is paused.
_Avoid_: Recovery, healthy state

## Deliberately absent

An instance has no organizations, memberships, invitations, human roles, or
additional human accounts. A reporting credential and the shared SMTP
credential are not management credentials.
Maintenance is not a synonym for pause: it suppresses notifications while
monitoring continues.
