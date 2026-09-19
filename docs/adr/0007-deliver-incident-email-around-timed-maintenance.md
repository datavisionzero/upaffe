# 0007 — Deliver incident email around timed maintenance

Status: accepted

The HTTP and push evaluators in ADRs 0003 and 0006 already make incident
transitions transactional and ordered. Email follows those transitions; it
does not decide whether a monitor is healthy. This decision fixes one shared
SMTP channel and finite maintenance for the MVP in `VISION.md`.

## Configuration and recipient identity

The instance has one SMTP configuration: host, port, `none`, `starttls`, or
`tls` transport security, sender address and optional display name, optional
authentication username and write-only password, and a public HTTPS base URL
for detail links. Insecure SMTP is an explicit operator choice for a trusted
local relay. Configuration reads show only `has_password`; replacement and
clearing are explicit operations. Passwords and full rendered messages never
appear in status, exports, logs, or API errors. A missing required setting is
an actionable configuration failure, not a silently discarded notification.

The instance default recipient list is copied when a project is created.
Changing it never mutates an existing project. Each project has an ordered,
replaceable list of at most 50 addresses. Inputs are trimmed; domains are
lowercased; duplicate detection is case-insensitive over the whole address.
Empty lists deliberately disable email for new incidents in that project.
Malformed and duplicate addresses are rejected rather than ignored.

An alert snapshots the project's current recipients when it first becomes
eligible, either at incident opening or after suppression ends. A subsequent
addition does not retroactively announce an existing incident. Removing an
address makes its unsent intents obsolete; accepted announcements remain
history. A recovery goes to an address only if it is still configured and its
alert for that incident was SMTP-accepted. Later recipient edits never alter
the identity or address of an existing intent.

## Intents, attempts, and SMTP outcomes

The logical key is `(incident ID, alert|recovery, normalized recipient)`,
enforced by the database. The incident transition and creation of eligible
intents commit together. Repeated failures, duplicate reports, old check
results, retries, and worker restarts cannot create another logical alert.
No reminder is sent for an incident. A fresh recovery creates a recovery
intent only for an accepted alert to the same current recipient. A queued or
claimed alert becomes `obsolete` when its incident resolves; a terminally
failed or never-created alert produces no recovery. Before SMTP submission,
the worker rechecks incident state, recipient membership, deletion, pause,
and effective maintenance. A transition racing after this final check cannot
undo a message already in flight, and the resulting status remains honest.

An intent is `queued`, `claimed`, `retrying`, `accepted`, `terminal_failure`,
or `obsolete`. `suppressed` is a visible, derived condition on queued work
while maintenance or pause applies; it is not SMTP acceptance. Every attempt
records a count and time; status shows the next attempt and a short sanitized
failure code, not raw server responses. An SMTP `2xx` completion means the
server accepted the message for processing; it does not prove mailbox
delivery. A rejected attempt records `failed` for that attempt and leaves the
intent `retrying` or `terminal_failure`. Permanent SMTP `5xx`, invalid
configuration, and invalid address errors fail terminally. Connection loss,
timeout, and SMTP `4xx` may be retried. The policy is five total attempts,
with delays of 1, 2, 4, and 8 minutes, no jitter, and at most one concurrent
claim per intent. A two-minute lease lets another worker recover a crashed
claim. SMTP connection and submission together have a 30-second deadline.
Test send uses the same transport and timeout but creates no intent and does
not retry automatically.

The worker records acceptance after SMTP returns. If the process dies after
SMTP acceptance but before that record commits, the next attempt may send a
duplicate. SMTP supplies no reliable end-to-end idempotency or inbox receipt;
the API and CLI must say `accepted by SMTP`, never `delivered`. A stable
Message-ID derived from intent identity helps downstream deduplication but
does not promise it. We intentionally avoid replaying an accepted intent.

## Maintenance and incident reconciliation

Maintenance is a finite window starting at the server's current UTC time.
Duration is 1 minute through 30 days. One active record exists per project or
monitor scope. Starting again while active extends its end to the later of
the current end and `now + duration`, using the scope's version for conflict
detection. A project window covers both HTTP and push monitors in it; a
monitor window covers that monitor alone. They compose by union: email stays
suppressed while either applies, and the effective expiry is the later end.
Ending one scope early closes only that record. The monitor's state, checks,
incoming reports, deadlines, and incident transitions continue throughout.

Expiry is `ends_at <= server now`, even when no worker ran at that instant.
Workers reconcile after expiry, early end, restart, and recipient changes.
For every still-open, unannounced incident on an active, undeleted monitor,
reconciliation creates one alert per currently eligible recipient. It creates
no alert or recovery for an incident opened and resolved wholly under
maintenance. A previously announced incident that recovers under maintenance
keeps one matching recovery queued until suppression ends; that closes the
announcement without replaying other suppressed transitions. A queued alert
is never submitted while maintenance is active, including when maintenance
starts after intent creation. Pausing also suspends submission and preserves
the incident; after resumption, a still-open unannounced incident is eligible
when the worker reconciles. Deleting a monitor or project obsoletes unsent
intents and prevents reconciliation; restoration does not replay old incidents.

## Surface and retention

The management API and CLI expose safe SMTP settings, project and default
recipients, test-send acceptance or sanitized failure, scoped maintenance
with `starts_at`, `ends_at`, and effective suppression, and delivery state per
incident and recipient. Mutations use the existing version conflict convention.
Errors distinguish invalid input, version conflict, absent or deleted scope,
missing SMTP configuration, and SMTP rejection. Status can be filtered and
paged; it never contains credentials, full message bodies, or raw untrusted
SMTP or remote diagnostic text. Detail links use the configured public base
URL and escaped project, monitor, and incident identity; untrusted reason
detail is HTML-encoded in the body and excluded from the subject.

Intent and attempt metadata are retained for 90 days after final state. Open
incidents and their intents remain regardless of age; retention cannot erase
evidence used to decide recovery eligibility. Maintenance records remain for
90 days after ending, while active windows remain until ended or expired.
Current incident and maintenance facts are never reconstructed from pruned
history. The one-operator model grants management through the browser session
or management credential only. Email recipients are destinations, not users.
No second channel, escalation, acknowledgement, recurring calendar, or
inbox-delivery guarantee is introduced.

## Examples

- An HTTP monitor crosses its threshold and opens an incident. Two configured
  recipients yield two alert intents in the same transaction. Further failed
  checks yield none. If both alerts are accepted and a fresh success resolves
  the incident, two matching recovery intents are created.
- A push monitor receives an explicit failure or crosses its missing-report
  deadline. Either opens one incident and its alert intents. A late success
  predating that failure remains history and creates no recovery. A fresh
  success resolves the incident and recovers only accepted announcements.
- A project window expires while the instance is down. On restart, an HTTP
  incident still open becomes eligible once. A push incident that both opened
  and resolved in the window creates no email. An accepted pre-window alert
  whose incident resolved during the window gets a matching recovery after
  expiry.
- The relay times out twice and accepts the third attempt. The intent shows
  two failed attempts, then SMTP acceptance. A permanent rejection instead
  becomes terminal failure; the incident stays open and visible, and no
  recovery email is created for that recipient.
