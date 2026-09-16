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

## Deliberately absent

An instance has no organizations, memberships, invitations, human roles, or
additional human accounts. A monitor's reporting token and the shared SMTP
credential are separate later concepts; neither is a management credential.
