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
additional human accounts. A monitor's reporting token and the shared SMTP
credential are separate later concepts; neither is a management credential.
Maintenance is not a synonym for pause: it suppresses notifications while
monitoring continues.
