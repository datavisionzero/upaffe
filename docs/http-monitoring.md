# HTTP monitoring

This is the operator and automation guide for upaffe's first complete product
path. It connects the public API contract, `ua` commands, web workflow,
persistence, scheduling, and bounded network behavior. HTTP monitors observe
publicly routable HTTP or HTTPS endpoints; private targets are rejected.
Incident and recovery email follow the rules in the
[email and maintenance guide](./email-maintenance.md).

## Complete fictional example

Assume the project `public-site` already exists and the shell supplies
`UPAFFE_URL` and `UPAFFE_CREDENTIAL`. Keep secret-bearing input in an ignored
working area or pipe it from a secret store. This fictional document configures
an exact status check, a required case-sensitive fragment, two failures before
an incident, operator guidance, a runbook, a secret target query, and a
write-only request header:

```json
{
  "key": "homepage",
  "name": "Public homepage",
  "purpose": "Checks the public homepage after each deployment.",
  "target_url": "https://status.example.test/health?probe=secret-from-store",
  "expected_status_code": 200,
  "text_condition": "required",
  "text_fragment": "ready",
  "interval_seconds": 60,
  "timeout_seconds": 10,
  "failure_threshold": 2,
  "instruction": "Confirm the public deployment before escalating.",
  "runbook_url": "https://docs.example.test/runbooks/homepage",
  "headers": [
    {"name": "Authorization", "value": "Bearer secret-from-store"}
  ]
}
```

Create it without placing the values on the command line:

```sh
ua monitor create public-site --file scratchpad/homepage.json --json
```

The response contains `target_url` without its query,
`"has_target_query":true`, and header metadata without values. `untested`
means no check in the current evaluation generation has applied yet. A new
monitor is due promptly; an explicit check uses the same bounded executor:

```sh
ua monitor test public-site homepage --json
ua monitor get public-site homepage --json
ua monitor checks public-site homepage --limit 20 --json
ua monitor incidents public-site homepage --limit 20 --json
```

The optional `purpose` describes what this monitor covers; the separate
`instruction` explains what to do during investigation. Purpose is trimmed to
240 characters and can be cleared with an empty or null update value.

The test command writes its structured result in both success and failure
cases. A completed failure exits with code 5, which is distinct from usage,
authentication, conflict, and network failures. The result reports the stable
reason, final status when available, duration, check ID, and whether it applied
to current state.

Configuration writes use the positive `version` last read. An update supplies
all public mutable fields. `target_url:null` preserves the complete existing
target, including its hidden query; a string explicitly replaces it:

```json
{
  "name": "Public homepage",
  "target_url": null,
  "expected_status_code": 200,
  "text_condition": "required",
  "text_fragment": "ready",
  "interval_seconds": 120,
  "timeout_seconds": 10,
  "failure_threshold": 3,
  "instruction": "Confirm the public deployment before escalating.",
  "runbook_url": "https://docs.example.test/runbooks/homepage",
  "version": 4
}
```

```sh
ua monitor update public-site homepage --file scratchpad/homepage-update.json
secret-store read upaffe/homepage-header.json |
  ua monitor header set public-site homepage X-Probe-Key --file -
ua monitor header remove public-site homepage X-Probe-Key --version 6
ua monitor pause public-site homepage --version 7
ua monitor resume public-site homepage --version 8
```

Removal is a versioned retained removal, not history erasure:

```sh
ua monitor delete public-site homepage --version 9
```

The complete command and exit-code contract is in [the CLI guide](./cli.md).
The corresponding routes and response fields are in [the API guide](./api.md)
and the checked-in OpenAPI document.

## State and incident meaning

An opening incident creates one alert intent per currently configured project
recipient unless maintenance suppresses it. Later failed checks update the
same incident without another alert. A fresh applicable success resolves it,
obsoletes unsent alerts, and creates recovery intents only for recipients with
an SMTP-accepted alert. The transition and intents commit together.

| Visible state | Meaning |
| --- | --- |
| `untested` | No result has applied in the current generation. A resumed monitor may still retain an incident opened earlier. |
| `healthy` | The latest applicable result succeeded. |
| `failing` below threshold | The latest applicable result failed, but no incident is open yet. The failure count and latest reason remain visible. |
| `failing` with `open_incident_id` | The configured consecutive-failure threshold opened one incident. Later failures update that same incident. |
| `paused` | No new scheduled work is due. Existing results and any open incident remain visible. |

Only a fresh applicable success resolves an incident. Pause, resume, config
changes, stale completions, and process restarts do not manufacture recovery.
The database partial unique index and transactional evaluator permit at most one
open incident per monitor.

The web application exposes the same operations inside a live project. Its list
and detail views state lifecycle text explicitly instead of relying on color.
Detail shows the latest result, last success, next run, duration, failure
reason, instruction, runbook, check history, and incident history. A target
replacement is opt-in so an ordinary edit cannot accidentally discard a hidden
query. Header values are never prefilled, and submitted target and header
secrets leave browser state immediately.

## Scheduling, restart, and retention

New and resumed monitors are due promptly. The scheduler commits an ordered
check, advances the next due time, and records a two-minute execution lease
before network I/O. Completion is a second short transaction. A stopped process
therefore retains planned work; an expired incomplete lease is reclaimed with
the same check identity. Missed intervals are not replayed as a burst.

Check and incident pages are newest first with exclusive sequence cursors,
default pages of 50, and a maximum of 100. Completed detail is retained for 90
days. Daily cleanup preserves monitor current-fact references, every open
incident, and all checks referenced by surviving incidents. The precise row and
transaction model is in [the storage guide](./storage.md); runtime controls are
in [the operations guide](./operations.md).

## Network and secret boundary

One whole-operation timeout covers DNS, connect, redirects, TLS, headers, body,
decoding, and evaluation. Every initial and redirected destination is resolved,
classified as globally routable, and socket-pinned before use. Private,
loopback, link-local, documentation, reserved, mixed public/private, and newly
unknown special-purpose answers are rejected without an allowlist escape hatch.
HTTPS validation cannot be disabled, HTTPS cannot downgrade to HTTP, redirects
are capped at five, cross-origin redirects lose configured headers, response
headers are capped at 64 KiB, and the decoded body is capped at 1 MiB.

Target queries and all configured header values are write-only. They are kept
in separate secret rows because execution needs their plaintext, but ordinary
API/CLI/web responses, histories, logs, and executor diagnostics cannot
reconstruct them. Response bodies are inspected only within the bound and are
never persisted. [ADR 0004](./adr/0004-bound-and-isolate-http-checks.md) is the
authoritative network decision.

## Verification

The security cases use deterministic controlled DNS and raw socket fixtures in
`HttpCheckExecutorTests`: public success, private/mixed/IPv4/IPv6/mapped-literal
rejection, DNS change and redirect revalidation, pinned-socket behavior,
cross-origin header stripping, TLS failure, whole-operation timeout,
cancellation, oversized streamed/decoded/compressed bodies, and oversized
response headers. The address table is checked against the
[IANA IPv4](https://www.iana.org/assignments/iana-ipv4-special-registry) and
[IANA IPv6](https://www.iana.org/assignments/iana-ipv6-special-registry)
special-purpose registries. API and PostgreSQL integration tests then prove
secret reconstruction only at execution, ordered scheduled completion,
threshold evaluation, exactly-one incident, recovery, concurrency, pagination,
retention, and redacted authenticated responses. CLI and React tests cover the
same generated contract, secret-state clearing, output redaction, lifecycle
controls, and optimistic conflicts.

Run the isolated composed system path with:

```sh
scripts/smoke.sh
```

It builds the real web/API image and CLI against a fresh PostgreSQL volume,
creates a project and monitor, observes scheduled and requested success,
creates two controlled status failures and exactly one incident, restarts while
work is planned and while the incident is open, exercises pause/resume and
immediate checks through CLI and browser-session paths, resolves the incident,
and searches ordinary artifacts and application logs for generated secrets.
The default public target is `https://example.com/`; an environment with a
restricted egress policy may set `UPAFFE_SMOKE_HTTP_TARGET` to another stable,
public HTTPS endpoint that returns status 200. The target is external by design:
a local fixture must be rejected by the product's private-network boundary.

Incident and recovery mail is delivered through the durable email worker.
SMTP acceptance, retry status, and maintenance suppression are documented in
the [email and maintenance guide](./email-maintenance.md). Delivery failure
must not be confused with scheduler or monitor failure.
