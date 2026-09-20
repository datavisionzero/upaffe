# 0002 — One operator, two access paths, and recoverable projects

Status: accepted

upaffe has one human operator, browser sessions for that human, and named
management credentials for automation. Both authenticated paths invoke the same
application operations; they are not separate accounts or permission models.
Projects have immutable keys, mutable names, and a recoverable deleted state.
This decision keeps the product's complete CLI promise without introducing the
organizations, invitations, users, or roles that the vision excludes.

## Trust establishment and recovery

A fresh installation has no operator and does not accept a default password.
The installer runs `upaffe bootstrap` locally in a one-off container with
database access, supplying the operator password by standard input or a
protected file. The command applies migrations without starting HTTP or
workers. It validates the email, password, and first credential name using the
application rules. One database transaction creates the singleton operator,
Argon2id password hash, and first named management credential. An advisory
lock and the singleton uniqueness constraint serialize competing attempts.
A repeat reports `already_initialized` without changing credentials or
revealing a token. The public API exposes only read-only initialization state;
the former `POST /api/bootstrap` route is removed after v0.1.0.

If the first token output is lost, a host operator can run the local
`upaffe recover-credential --name` command to rotate that named credential.
This path does not reopen bootstrap or create another operator. It invalidates
all previous tokens immediately, including any rotation overlap. It requires host and
database access and is never granted to an HTTP credential.

## Secrets and access paths

The operator signs in with a normalized email address and a password. Passwords
are stored as self-describing Argon2id hashes with a random salt and bounded
input. An unknown email runs the same password verification work as a known
email; unknown email and wrong password both produce `sign_in_rejected` in the
same relevant timing class. A failed-sign-in throttle is keyed by both remote
address and normalized email without revealing which key caused rejection. Its
bounded in-memory 15-minute window admits five failures per normalized email
and twenty per source address; a throttled attempt is still
`sign_in_rejected`.

A successful sign-in always creates a fresh, random server-side browser
session. Only a SHA-256 digest of its secret is stored. The cookie is
`HttpOnly`, `SameSite=Lax`, scoped to `/`, and `Secure` under HTTPS; its secure
name uses the `__Host-` prefix, with a plainly named development cookie only for
HTTP. Sessions expire after 12 hours idle or seven days absolute, are touched at
most every five minutes, and are immediately ineffective after revocation.
Cookie-authenticated writes require an allowed `Origin` and the custom
`X-Upaffe-CSRF: 1` header. Sign-out revokes the server row and expires the
cookie. These choices prevent session fixation, make revocation real, and keep
cross-site form submissions from becoming management actions.

A management credential has a stable ID and operator-chosen name. Its bearer
token contains a public identifier and 32 random bytes; only a SHA-256 digest is
stored. Its wire form is `upaffe_<32 hex identifier>_<base64url secret>`. The
complete token appears exactly once in the explicit create or
rotate response. Lists, logs, exports, and ordinary errors contain metadata
only. Rotation activates a new secret immediately and gives the prior secret a
ten-minute overlap before it expires. Revocation invalidates every secret for
that credential on the next request.

Management credentials carry the instance's management authority, including
project and credential administration, because every web administration
operation must remain possible through the CLI. They cannot bootstrap, recover,
change the operator's email or password, create another human identity, or
create a role. Browser sessions and management credentials are different
transports for one application identity, not interchangeable secret formats.

## Authentication boundaries and threats

Endpoints are classified explicitly:

- **Public**: liveness, readiness, version, read-only bootstrap state, and
  sign-in.
- **Browser-only**: current-session inspection, sign-out, and future changes to
  the operator's own login.
- **Management**: projects and management credentials; either a valid browser
  session or a valid management credential may call them.

Application operations receive an identity containing the singleton operator
ID and the access path. They never inspect cookies or bearer headers. A new
endpoint carries one public, browser, or management metadata marker. A new
management endpoint must opt into the management policy; an authenticated
fallback policy closes any omitted classification rather than making it
anonymously accessible.

No credential value, cookie, password, or remote response
content is logged. Logs may contain an operation name, outcome, access path,
credential ID, project key, and correlation ID. The design assumes TLS at the
deployment boundary and a trusted database/host administrator. It defends
against database disclosure of reusable secrets, credential replay after
revocation, concurrent setup, session fixation, CSRF, login enumeration,
and routine brute force. It does not defend against a compromised running
process, host root, or an operator who deliberately discloses a secret.

Authentication errors reveal no stored state:

- no presented authentication is `401 authentication_required`;
- invalid, expired, manipulated, or revoked authentication is uniformly
  `401 authentication_rejected`;
- valid authentication on a disallowed access path is `403 forbidden`;
- an unknown resource and a resource hidden from the identity share `404`.

## Projects

A project key is an explicit lower-case identifier matching
`^[a-z][a-z0-9-]{1,39}$`. It is immutable and globally unique, including while
the project is deleted, so identity is never silently reused. A trimmed project
name is 1–100 characters and can change independently.

Creation is safely repeatable by key: repeating the same key and name returns
the existing project, while reusing the key with different facts is a stable
conflict. Updates carry the version last read and reject concurrent change.
Deletion is soft, removes the project from ordinary lists, and keeps its key
reserved. Restoration revives the same project. This epic deliberately adds no
purge operation or retention deadline.

## Shared application contract

API, CLI, and web call the following application operations rather than
reimplementing their rules:

| Area | Operations |
| --- | --- |
| Bootstrap | inspect whether local setup is needed; establish the operator and first credential once |
| Browser session | sign in; inspect the current identity; sign out |
| Management credential | create, list, rotate, and revoke |
| Project | create idempotently; get; list live or deleted; rename; delete; restore |

The web uses a browser session and the CLI uses a management credential. Their
project responses and domain failures are otherwise identical. Monitor
reporting tokens and SMTP credentials remain outside this contract.
