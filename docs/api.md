# HTTP API

The API is the single application boundary used by the web application and CLI.
Every operation is below `/api`; other paths are reserved for the SPA. There is
no API-version segment. Each response carries `Upaffe-Version`, whose value is
the release tag or `0.0.0-dev` for an untagged build.

Bootstrap and browser sessions are the first product-facing API. Bootstrap is
public only while establishing the sole operator; the proof itself is a
high-entropy secret supplied in the request body.

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
