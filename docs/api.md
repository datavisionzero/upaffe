# HTTP API

The API is the single application boundary used by the web application and CLI.
Every operation is below `/api`; other paths are reserved for the SPA. There is
no API-version segment. Each response carries `Upaffe-Version`, whose value is
the release tag or `0.0.0-dev` for an untagged build.

The product-facing API covers bootstrap, browser sessions, management
credentials, projects, and HTTP monitor administration. Bootstrap is public
only while establishing the sole operator; the proof itself is a high-entropy
secret supplied in the request body.

| Method and path | Purpose |
| --- | --- |
| `GET /api/version` | The instance version. This is the first operation exercised by both generated clients. |
| `GET /api/health/live` | Whether the process can answer; it touches no dependency. |
| `GET /api/health/ready` | Whether PostgreSQL answers with exactly the schema this binary knows. |
| `GET /api/bootstrap` | Whether an operator is still required and whether a live bootstrap proof is available. |
| `POST /api/bootstrap` | Establish the sole operator with the bootstrap proof, email, and password. |
| `POST /api/session` | Sign in and receive a fresh server-side browser session cookie. |
| `GET /api/session` | Inspect the operator identity admitted by the browser session. |
| `DELETE /api/session` | Revoke the current browser session and expire its cookies. |
| `POST /api/management-credentials` | Create a named credential and reveal its token once. |
| `GET /api/management-credentials` | List credential metadata without tokens. |
| `POST /api/management-credentials/{id}/rotate` | Issue a new token with a ten-minute overlap for the previous token. |
| `DELETE /api/management-credentials/{id}` | Revoke every token for the credential immediately. |
| `POST /api/projects` | Create a project idempotently by its immutable key. |
| `GET /api/projects?deleted=false` | List live projects, or deleted projects with `deleted=true`. |
| `GET /api/projects/{key}` | Read a live or deleted project by immutable key. |
| `PUT /api/projects/{key}` | Rename a live project at the version last read. |
| `DELETE /api/projects/{key}?version={version}` | Soft-delete a project at the version last read. |
| `POST /api/projects/{key}/restore` | Restore a project at the version last read. |
| `POST /api/projects/{projectKey}/http-monitors` | Create an HTTP monitor idempotently within a project. |
| `GET /api/projects/{projectKey}/http-monitors` | List live HTTP monitors in a project. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}` | Read one HTTP monitor without secret values. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/checks` | List completed checks newest first with cursor pagination. |
| `GET /api/projects/{projectKey}/http-monitors/{monitorKey}/incidents` | List incidents newest first with cursor pagination. |
| `PUT /api/projects/{projectKey}/http-monitors/{monitorKey}` | Update monitor configuration at the version last read. |
| `DELETE /api/projects/{projectKey}/http-monitors/{monitorKey}?version={version}` | Remove a monitor while retaining its identity and history. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/pause` | Pause a monitor at the version last read. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/resume` | Resume a monitor and make a fresh check due. |
| `PUT /api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}` | Set or replace one write-only request header. |
| `DELETE /api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}?version={version}` | Remove one write-only request header. |
| `POST /api/projects/{projectKey}/http-monitors/{monitorKey}/test` | Execute and record one immediate bounded check. |
| `GET /api/openapi/v1.json` | The generated OpenAPI document. It does not list itself. |

Every routed endpoint declares exactly one access boundary. Version, health,
OpenAPI, bootstrap, sign-in, and non-API fallbacks are public; bootstrap is
nevertheless authorized by its one-use proof. Reading or deleting a session is
browser-only. Management operations accept either that browser session or
`Authorization: Bearer <token>`. The host has an authenticated fallback policy,
so an endpoint without an explicit public declaration is closed rather than
accidentally anonymous. Liveness and readiness are technical deployment checks;
neither asserts that the monitoring loop is progressing.

`POST /api/bootstrap` returns `204` and no body on success. Expected refusals use
`application/problem+json` with a stable `code`: `validation` (`400`),
`bootstrap_rejected` (`401`) for missing, wrong, or expired proofs, and
`bootstrap_closed` (`409`) after an operator exists. The proof and password are
never returned.

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
presented bearer token, cookie secret, bootstrap proof, or password.

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

## HTTP monitors

An HTTP monitor has a generated UUID and an immutable key scoped to its
project. Its create and read contract includes name, query-redacted target URL,
whether a query is configured, exact expected status, `none`, `required`, or
`forbidden` text condition and fragment, interval and timeout seconds, failure
threshold, optional operator instruction and runbook URL, current state and
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
headers, target query, response body, or executor message. Incident history is
likewise separate at `GET .../incidents` and contains its preserved first,
opening, latest-failure, and optional resolution facts.

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
