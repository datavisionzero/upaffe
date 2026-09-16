# Product Vision: upaffe

> Product direction and scope. The MVP is the first complete, usable release.
> The roadmap records intended additions after it, without promising release
> dates. Conditional ideas are separate from that commitment. This document
> describes the intended product, not features already implemented.

## 1. The product

upaffe is a lean, self-hosted monitoring tool for people who operate software
and infrastructure together with AI agents. Across projects, it answers three
questions: What is working? What has failed? Which expected success report
has not arrived?

Humans get a clear dashboard. Agents configure and maintain monitoring through
a complete CLI designed for unattended use. Both use the same API and the same
application rules.

The initial focus is backups, scheduled jobs, and website availability with
simple content checks. Email is the notification channel. The product favors
a few dependable workflows over a large catalog of integrations.

## 2. Why it exists

[Uptime Kuma](https://github.com/louislam/uptime-kuma) is a functional reference
for HTTP, keyword, and push monitoring with email notifications. Matching its
full feature set is not the objective.

Monitoring should be as straightforward for an agent to configure as deploying
an application. That requires a stable, complete, documented interface without
browser automation. upaffe makes that workflow a product promise from the
start, without making claims about every integration available for other tools.

Its value comes from complete CLI coverage, project organization, focused
scope, and a familiar interface. The public projects
[planaffe](https://github.com/datavisionzero/planaffe) and
[hostingaffe](https://github.com/datavisionzero/hostingaffe) provide the technical
and visual reference; upaffe has its own monitoring-specific dashboard.

## 3. Product commitments

- **Projects organize monitoring.** Every monitor belongs to exactly one
  project. The instance provides an overview across projects and a dedicated
  view of each project. Projects are organizational boundaries, not tenants.
- **Every web operation is available through the CLI.** An appropriately
  authorized agent can perform every configuration or administrative action
  available to a human in the web interface, including project management,
  notification settings, credential management, and instance settings.
- **The CLI is designed for agents.** Unattended operation is a primary product
  interface. Convenience features for interactive terminal use are secondary.
- **One operator is enough.** There is one administrative operator identity,
  with no organizations, invitations, or multi-user role system. The dashboard,
  administration, and management API require authentication. Separate automation
  credentials do not imply separate human accounts.
- **Email is sufficient.** Additional delivery channels are not a prerequisite
  for a useful product and are not a committed roadmap item.
- **Clarity takes precedence over breadth.** A person should quickly understand
  the current problem; an agent should retrieve the same facts in a compact,
  structured response.

## 4. MVP: monitoring

### Active HTTP checks

upaffe periodically requests an HTTP or HTTPS address. A monitor checks
reachability and the expected HTTP status, and can require a text fragment to
be present or absent in the response.

Each monitor has its own interval, timeout, and threshold of consecutive
failures before an alert opens. A website might be checked every five minutes;
backup schedules do not affect that interval. Request headers support protected
health endpoints, with secret values kept out of normal output and history.
TLS validation failures count as failures. Advance certificate-expiry warnings
belong to the roadmap.

### Incoming heartbeats and results

Scripts and applications report over outbound HTTPS, without requiring an
inbound connection to the monitored host. Push monitors support two explicit
reporting modes:

- **Periodic job completion.** A backup or maintenance script reports success
  only after successful completion, and explicitly reports failure when a run
  fails. The next success is due within a configured interval plus tolerance
  after the previous success.
- **Periodic state reports.** A local script evaluates a condition, such as
  backup freshness or container health, and regularly reports healthy or
  failed. upaffe also checks that reports continue to arrive. The sender owns
  the actual local inspection.

For the MVP, schedules use elapsed intervals and tolerance, without fixed
calendar times or time zones. For example, a daily job with a 24-hour interval
and one hour of tolerance that succeeds on Monday at 04:00 is next due by
Tuesday at 05:00. An earlier or later success moves the next deadline. These
24 hours are an example for daily backups, not a limit on monitoring frequency.
For periodic state reports, report freshness is measured from the last receipt.
A failed report refreshes evidence that the sender is alive, but does not make
the monitored condition healthy.

The last received report and the last reported success are distinct facts.
An explicit failure opens an incident immediately; tolerance for missing
reports must never postpone it by another daily or weekly run. Repeated
failures update the existing incident rather than opening new ones or sending
an email for every report. A fresh successful result resolves the incident.

A running reporting script is not proof of a recent backup. A completed backup
is not proof that it can be restored. Integrity checks and restore tests can
already report through separate monitors in the MVP; a combined backup view
comes later. A future start signal must never count as successful completion.

### Initial state, pauses, and freshness

A new monitor is untested until its first result. Active checks are scheduled
promptly. A newly enabled push monitor receives one configured interval plus
tolerance for its first report or success, according to its reporting mode;
remaining silent indefinitely must produce an incident.

Pausing a monitor suspends monitoring and notifications. It appears paused,
never healthy. Resuming requires a fresh evaluation: HTTP checks run promptly,
and push monitors receive a new initial reporting window. Earlier history and
unresolved incidents remain visible; resuming alone does not resolve them.

Untested, healthy, failing, and paused states remain distinguishable. Before an
HTTP failure threshold is reached, the failed result is visible even though no
incident has opened yet. Missing reports and monitoring gaps have explicit
reasons, rather than silently preserving an old green result.

## 5. MVP: incidents, email, and maintenance

An incident records a monitor's failure, when it began, its reason, and its
resolution. The MVP uses a simple failure model without configurable warning
and critical severity levels.

A shared SMTP configuration sends alert and recovery emails. Recipients are
configured per project, with an instance default for new projects. Setup
includes a test email. Messages identify the project, monitor, reason, and
time, and link to the relevant detail view.

Delivery failures are visible and retried within a bounded policy. SMTP
acceptance is recorded separately from a failed send; it is not proof that a
message reached an inbox. Reprocessing must not intentionally create duplicate
notifications, while uncertain SMTP outcomes cannot guarantee exactly-once
delivery. Recovery notifications correspond to previously announced incidents;
obsolete queued alerts must not be delivered as if a resolved failure were
still current.

**Timed maintenance is part of the MVP.** An operator or agent can put a monitor
or project into maintenance for a specified duration, such as 20 minutes during
a deployment. Checks, incoming reports, and incident history continue, while
notifications are suppressed. Maintenance ends automatically. Any incident
still open is then eligible for notification, without replaying every suppressed
transition. Maintenance is visibly distinct from pausing and from health.
Recurring maintenance calendars and escalation chains are outside the MVP.

## 6. MVP: integration and access

A small documented HTTP API has two responsibilities: managing configuration
through the web application and CLI, and receiving results from scripts and
applications.

The primary reporting route is an HTTPS POST with a small JSON result and a
monitor-scoped token. A short curl example must be sufficient to integrate a
script. A secret heartbeat URL supports callers that can only make a simple
request. Its exact contract is an implementation decision; such URLs are
credentials and must be redacted from normal logs and output.

A reporting token can submit results only for its monitor. It cannot change
configuration or read projects. Tokens can be revoked and rotated. Management
credentials remain separate, and automation credentials can be revoked
independently without changing the operator's login.

Initial installation establishes trust through a secure bootstrap procedure.
After that, configuration must not require a manual detour through the browser.
An authorized management credential can administer the instance through the CLI.
Secrets are supplied and revealed only through explicit credential operations,
not through ordinary status reports or configuration exports.

upaffe defines its own reporting contract. Existing scripts may need small
changes; drop-in compatibility with another product is not an MVP requirement.
Neither an SDK nor an MCP server is needed to use the API.

## 7. MVP: the agent interface

The CLI is a standalone binary and a client of the documented API. It provides:

- Noninteractive commands without unexpected prompts, editors, or pagers.
- Machine-readable JSON, stable error codes, meaningful exit codes, data on
  stdout, and diagnostics on stderr.
- Explicit project selection and stable identifiers instead of ambiguous
  display-name matching.
- Structured input from files or stdin, discoverable commands, and documented
  schemas that do not require knowledge of the web interface.
- Safely repeatable writes, so retrying after a connection failure does not
  silently create duplicate monitors.
- Complete administration, including pause and resume, timed maintenance,
  removal, test checks, test emails, and credential rotation.

**One project report gives an agent enough context to investigate.** In one
call, it returns current failures, their age and causes, missing reports, last
successes, relevant non-secret configuration, and maintenance state. Healthy
monitors have compact summaries. Detailed history is fetched separately so that
the report remains useful as a project grows.

Each monitor can carry a short operator-written instruction and a runbook link.
These appear in the project report and the web detail view. They guide an agent's
investigation; upaffe itself does not execute remediation commands. Remote
response content remains diagnostic data, separate from operator instructions.

## 8. MVP: the human interface

The dashboard prioritizes failing monitors, overdue reports, and affected
projects. Healthy projects remain easy to scan. A project view lists its
monitors; a monitor detail view shows its latest check or report, last success,
next check or reporting deadline, failure reason, runbook, and a simple history
of failures and recoveries. HTTP monitors also show response time.

Maintenance, pauses, stale results, and notification delivery problems are
visible. Color alone never communicates state. The interface uses the product
family's navigation, typography, forms, and components, with an original layout
suited to project health and incident history.

## 9. MVP: technical direction and dependable operation

The technical foundation follows the public reference products: .NET 10 and
ASP.NET Core, PostgreSQL, React with TypeScript, Tailwind and Base UI, and Go for
the CLI. A documented OpenAPI contract connects the clients to the backend.

**Docker Compose is the primary, supported deployment path for self-hosters.**
The MVP ships a maintained Compose configuration and prebuilt container images
so that an operator can configure and start an instance without building source
code or installing application runtimes on the host. The default stack consists
of the application and PostgreSQL, with persistent storage explicitly declared.
It requires no managed cloud service or Kubernetes cluster; the operator supplies
an SMTP service for email and can use an existing reverse proxy for HTTPS.

Installation documentation covers configuration, secret provisioning, initial
access, persistent volumes, health checks, and reverse-proxy setup. Routine
operation includes documented image updates and database migrations, backup and
restore of all required persistent state, and recovery from a failed upgrade.
Container recreation must preserve that state. A fresh installation and a
restore onto another host must both be supported through the documented Compose
workflow. Simple self-hosting is an MVP requirement, not a later packaging task.

Scheduling, reporting deadlines, maintenance expiry, incidents, and pending
notifications survive restarts. Repeated processing must not produce duplicate
incidents or notification storms. Historical data has bounded retention.
Monitoring gaps remain visible; the product does not invent successful checks
for periods when it was offline.

**The monitoring system's own health is an MVP responsibility.** The dashboard
shows overdue check execution and email delivery problems. A minimal health
endpoint supports an independent external check and reflects monitoring
progress, not merely whether the login page can be served. An optional outbound
heartbeat to an independent receiver is tied to recent monitoring-loop progress.
The endpoint and heartbeat expose no project details or credentials.

An unavailable instance cannot reliably report its own failure. Independent
monitoring must run outside its failure domain. Email delivery also needs to be
exercised through the test-send operation; a live HTTP endpoint cannot prove
that email works.

The first release does not require a separate message broker, distributed
probing infrastructure, or a general observability platform. Exact persistence,
scheduling, and retry mechanisms are implementation decisions.

## 10. Roadmap after the MVP

These are intended additions, ordered by practical value and dependency. Each
addition must preserve full CLI coverage and remain useful without an SDK.

### 1. Monitoring configuration as a file

A project can keep its desired monitor configuration in a version-controlled
file. An agent previews the proposed changes and applies them through the CLI.
Reapplying the same file is safe and does not create duplicates. Removing a
monitor from a file does not silently delete it: deletion requires an explicit
operation or deletion option. Secret references are separate from committed
configuration. The contract must also make conflicts with manual edits visible.

This builds on repeatable MVP operations and makes monitoring part of a normal
deployment workflow without turning upaffe into a deployment engine.

### 2. Dependencies that reduce notification noise

Simple monitor dependencies express that a website or container depends on a
host or another monitored service. When the prerequisite fails, notifications
for affected dependents can be suppressed while their checks, failures, and
history remain visible. A dependency indicates a possible shared cause, not
proof that every downstream failure has that cause. Circular dependencies are
rejected. Dependents still failing after the prerequisite recovers become
eligible for their own alerts.

### 3. A combined backup view

Related backup, integrity-check, and restore-test monitors can be shown together.
The view makes each last success and freshness deadline explicit, so that a
recent backup cannot hide an overdue restore test. Each monitor retains its
own schedule and incident state. This is a view over reported evidence, not a
backup engine or a guarantee of recoverability.

### 4. Reminders and incident acknowledgement

Optional reminders repeat after a configurable interval while an incident
remains unresolved. An operator or authorized agent can acknowledge an incident
to stop reminders without marking the monitor healthy or suppressing new,
unrelated incidents. Recovery closes the incident. Maintenance and dependency
suppression also apply to reminders. This remains a small operator workflow,
without on-call rotations or escalation trees.

### 5. Certificate expiry and JSON checks

HTTPS monitors can warn before a certificate expires, using a configurable
lead time. HTTP monitors can evaluate a selected JSON field against a simple
expected value, such as `status == "healthy"`. Certificate notices must be
distinguishable from current availability failures. JSON evaluation stays
bounded and declarative rather than becoming a general scripting environment.

## 11. Conditional ideas

These are possibilities, not committed roadmap features:

- Fixed calendar schedules with time zones, if interval-based job deadlines
  prove insufficient.
- Job-start reports and duration tracking, if completion and freshness alone
  do not provide enough diagnostic value.
- A language SDK, once repeated integrations demonstrate shared client logic
  worth maintaining. The HTTP API remains independently usable.
- An MCP interface over the same application operations, if agent workflows
  require it beyond the CLI.
- Compatibility adapters or migration helpers when concrete migration effort
  justifies them.

## 12. Boundaries

upaffe does not aim for full feature parity with another monitoring product.
It does not provide public status pages, multi-user or tenant management, a
host agent, a metrics database, log collection, tracing, browser journeys, or
automatic remediation within this vision.

It monitors backups but does not create or restore them. Local scripts retain
responsibility for inspecting host-specific conditions. Application telemetry
and checks from multiple geographic locations remain the responsibility of
specialized tools. A broad integration marketplace is not a product goal.

## 13. What makes the MVP successful

- A self-hoster can start the published images with the supplied Docker Compose
  configuration, complete initial setup, and update, back up, and restore the
  instance using the documentation, without a source build or a managed platform.
- Given an instance address and an authorized credential, an agent can create
  a project, a website monitor, a backup monitor, and working email notifications
  without using the web interface. It can test the setup and retrieve a useful
  project report in one call.
- A failed HTTP check reaching its threshold, an explicit reported failure,
  and an absent heartbeat each produce an understandable incident and alert at
  the expected time. Fresh success resolves the incident and produces the
  appropriate recovery notification.
- Timed maintenance suppresses notifications while preserving observations,
  expires automatically, and surfaces any unresolved failures afterwards.
- Restarts preserve configuration, incidents, deadlines, and notification work.
  Missing execution and delivery failures are observable, and an independent
  receiver can detect a stopped monitoring process.
- A person can quickly identify the affected project and reason from the
  dashboard. A backup script and a website can be integrated using documented
  HTTP and CLI operations without an SDK.

## 14. Decisions to settle before implementation

The product boundaries above are settled. Implementation needs concrete rules
for minimum intervals, timeout limits, retention, HTTP response size limits,
and bounded notification retries, including uncertain SMTP delivery outcomes.

The reporting contract must define duplicate handling, out-of-order results,
event time versus receipt time, and protection against delayed old successes
incorrectly clearing a current failure. The exact heartbeat URL format and
secret handling must be specified alongside the JSON endpoint.

HTTP checks also need explicit rules for redirects, allowed targets, and
credential forwarding. Credential bootstrap and recovery must preserve the
single-operator model and complete CLI administration. These details should be
recorded in the API contract and focused architecture decisions, rather than
expanding the product scope.
