# Incident email and timed maintenance

upaffe sends alerts and recoveries through one instance SMTP relay. A project
has its own recipient list. HTTP threshold incidents, explicit push failures,
and missing push reports use the same delivery rules. The web application and
noninteractive `ua` CLI use the same management API and version checks.

## Configure and test

An operator first configures relay host, port, security (`none`, `starttls`, or
`tls`), sender address and name, and a public base URL through **Instance
email** in the web app or `ua email settings set --file settings.json`. The
CLI document includes the settings version last read. Configure a username
when the relay requires authentication. Replace the password separately in
the web app or through `ua email password set --file -` from a secret-store
pipe. Ordinary reads show only `has_password`; they never return the value.
`none` is suitable only for a trusted local relay.

Default recipients are copied once into a new project. Existing projects keep
their own lists, editable in **Email and maintenance** or with `ua email
recipients set PROJECT --file recipients.json`. An empty list opts the project
out. Each mutation uses the version from the last read; on a conflict, refresh
before applying a new decision. A recipient removed before an unsent alert is
submitted receives no message. After SMTP acceptance, a still-configured
recipient can receive the matching recovery.

Send a test from the web form or `ua email test --recipient ops@example.test`.
The result `accepted_by_smtp` means the relay accepted submission. It does not
prove inbox delivery. The test creates no incident and has no retry. Check the
recipient mailbox or relay independently if delivery beyond SMTP matters.

## Follow an incident

An incident creates at most one alert intent per recipient. The delivery worker
stores those intents in PostgreSQL and rechecks incident, recipient, pause,
deletion, and maintenance facts before beginning each SMTP attempt. Transient
relay failures retry after 1, 2, 4, and 8 minutes, up to five attempts total.
Permanent failure or the fifth unsuccessful attempt becomes
`terminal_failure`. A restart resumes due work from its durable claim. A crash
after relay acceptance but before recording it can cause a duplicate SMTP
submission. The deterministic Message-ID helps downstream deduplication but
cannot guarantee it.

Use `ua email summary`, `ua email summary --project PROJECT`, and `ua email
deliveries --project PROJECT` for pending work and failures. Project and
monitor web views show the same safe delivery facts. `ua email incident UUID
--monitor-type http` (or `push`) and each monitor's **Email status for
incident** control show whether the episode is pending, announced,
suppressed, failed, or silent. Attempts, times, and stable failure codes are
visible per recipient. Raw SMTP response text, message bodies, and credentials
are absent. A terminal failure calls for checking relay settings, connectivity,
and an explicit test send; upaffe does not restart a terminal delivery
automatically. Status `accepted` is relay acceptance, never a claim of inbox
arrival.

Alert and recovery messages include project and monitor names, a stable
failure or recovery reason, UTC event time, and an incident detail link. The
link opens the monitor and incident after browser sign-in. Untrusted HTTP
response content and push diagnostic text are not inserted as instructions.

## Schedule maintenance

Start a finite project window or an HTTP/push monitor window in the relevant
web view or with `ua maintenance start PROJECT [MONITOR] --scope
project|http|push --version VERSION --duration-seconds SECONDS`. Durations run
from one minute to 30 days. Starting again while active extends the direct
window to the later end. `get` shows direct version, active scopes, and
effective expiry. `end` closes only the direct scope at its read version.
Project and monitor windows compose by union: a monitor stays suppressed until
both have ended. Expiry uses server UTC time and survives a restart.

Maintenance holds alert and recovery email while checks, push reports, and
incident transitions continue. An episode that opens and resolves entirely
during maintenance remains silent. An episode still open when all relevant
windows end becomes alert-eligible once, using current recipients. A recovery
for an already SMTP-accepted alert waits until maintenance ends. Pause is a
separate monitor state that suspends monitoring; maintenance does not imply
health or pause. The web app shows effective maintenance and its expiry apart
from health status.

Final delivery records and superseded maintenance windows older than 90 days
are pruned daily. Work still pending, delivery facts for open incidents, and
accepted alerts awaiting a recovery decision remain. Older absence is not
evidence that email was never attempted.

The composed system test uses a disposable SMTP fixture. It verifies test
submission, HTTP and push alert/recovery pairs, relay refusal, retry after
restart, obsolete work after a quick resolution, overlapping maintenance,
continued observations, and no replay of an episode closed inside maintenance.
It cannot prove delivery to an external mailbox. Run `scripts/smoke.sh` with
Docker available; the fixture listens only in that disposable Compose project.
