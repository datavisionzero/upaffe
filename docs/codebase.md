# The codebase

`VISION.md` defines the product. This document defines where its implementation
lives, which way dependencies point, and which artifacts are authoritative. It
is updated as each epic changes the repository.

[`CONTEXT.md`](../CONTEXT.md) is the canonical glossary for product terms. Code,
HTTP, CLI, and web copy use those terms rather than inventing parallel names.

Current state: the four-layer .NET 10 solution, API host, technical health and
version endpoints, PostgreSQL context, forward-only startup migration,
checked-in OpenAPI contract, two generated client packages, React application,
and Go CLI exist. Access, projects, and HTTP-monitor administration work through
the shared API, web application, and CLI.
The HTTP monitoring domain and PostgreSQL schema persist monitor configuration,
scheduling and current-result facts, explicitly separated secrets, ordered
checks, and incident lifecycles. A shared bounded executor performs one
public-internet observation. Authenticated operations manage and immediately
test HTTP monitors; recurring scheduling, threshold evaluation, incident
lifecycle, and retained history complete the first monitoring path. Local
Compose builds and runs the delivered slices; production delivery remains
planned.

## Provenance and maintenance boundary

[ADR 0001](./adr/0001-adopt-the-affe-foundation-without-its-domain.md)
records the comparison and selection. planaffe supplies the canonical stack
decisions; hostingaffe supplies the closest implementation examples. Files are
selectively adapted into this repository. There is no shared library, submodule,
template dependency, or runtime dependency on another affe product.

No foreign product domain belongs here. planaffe's tracker objects and
hostingaffe's infrastructure inventory are implementation references only where
they demonstrate a general mechanism.

## Repository shape

```text
upaffe/
├─ .github/workflows/        build, test, contract, and Compose validation
├─ deploy/                   local Compose and development image
├─ docs/
│  ├─ adr/                   local and adopted architecture decisions
│  ├─ api/openapi.json       checked-in HTTP contract
│  ├─ codebase.md            this map
│  ├─ api.md                 HTTP conventions and surface, when implemented
│  ├─ cli.md                 CLI contract, when implemented
│  ├─ storage.md             schema and retention rules, when implemented
│  ├─ install.md             supported installation, when implemented
│  └─ operations.md          supported local operating procedures
├─ src/
│  ├─ Upaffe.Domain/         domain rules
│  ├─ Upaffe.Application/    use cases and required ports
│  ├─ Upaffe.Infrastructure/ PostgreSQL and external adapters
│  ├─ Upaffe.Api/            HTTP and composition root
│  ├─ cli/                   standalone Go CLI `ua`
│  └─ web/                   React application
└─ tests/
   ├─ Upaffe.UnitTests/
   └─ Upaffe.IntegrationTests/
```

Planned entries document a settled responsibility, not behavior that already
exists. A directory is created only when it has real contents; the repository
does not carry empty placeholder documents or folders.

## Backend boundaries

The productive .NET dependencies point inward:

```text
Api ──────> Application ──────> Domain
 └────────> Infrastructure ──>
```

- **Domain** holds access, project, and monitoring terms and rules. It has no
  project or package references.
- **Application** holds operations and the ports they require. It can reference
  Domain and nothing outward.
- **Infrastructure** implements Application ports using PostgreSQL and external
  services. Domain never references EF Core.
- **API** is the composition root and HTTP boundary. It is the only productive
  project that knows all implementation layers.

## HTTP execution boundary

`IHttpCheckExecutor` is the shared application port for one observation. Its
Infrastructure implementation owns DNS resolution, public-address
classification, IP-pinned connections, manual redirects, system TLS
validation, one whole-operation timeout, decompression and response limits,
strict text decoding, and status/text evaluation. It returns stable reason
codes and sanitized metadata; it neither schedules another run nor changes a
monitor or incident.

Each initial target and redirect hop is resolved independently. Every returned
address must be globally reachable according to the checked IANA
[IPv4](https://www.iana.org/assignments/iana-ipv4-special-registry/iana-ipv4-special-registry.xhtml)
and [IPv6](https://www.iana.org/assignments/iana-ipv6-special-registry/iana-ipv6-special-registry.xhtml)
special-purpose registries, and IPv4 embedded in mapped or NAT64 IPv6 addresses
is classified as IPv4. The connection callback receives only those validated
addresses, so a second resolver lookup cannot redirect the socket. Automatic
redirects, cookies, credentials, and proxies are disabled. Configured headers
are applied only while the exact origin is unchanged.

The test seam separates name resolution and socket connection without weakening
production policy. Tests supply controlled public-looking answers and route the
already-approved connection to a loopback raw HTTP server. They exercise DNS
changes, forbidden and mixed answers, redirects and header stripping, status
and text failures, timeout and cancellation, TLS failure, strict encoding, and
compressed body and header limits without contacting an external service.

HTTP monitor management follows the same transport-independent application-act
shape as projects. `IHttpMonitorStore` owns project association, idempotent
creation, optimistic changes, secret-specific header operations, and requested
check persistence. The nested management API exposes create/read/list/update,
pause/resume/remove, header set/remove, and immediate-test operations. Ordinary
snapshots contain only target-query and header metadata; only the store builds
the executor request from explicit secret rows. An immediate test persists its
ordered check before network I/O and completes it afterwards without shifting
the regular due time.

`IScheduledHttpCheckStore` gives the background monitoring act two short,
durable transaction boundaries around that same executor. PostgreSQL claims a
due monitor or an expired incomplete check with row locks and `SKIP LOCKED`.
The application host executes outside the transaction and then completes only
the check whose current lease token it owns. This is at-least-once HTTP
execution with one immutable stored result, as detailed in ADR 0005.

Both requested and scheduled completion lock the check and then its monitor
before applying the result. The shared evaluator updates latest-result,
latest-success, visible state, failure count, and persistent failure-streak
start in the same transaction as completion. Repeated or older sequences are
ignored. Reaching the configured threshold creates the first incident from the
persisted beginning of that streak; PostgreSQL's unique open-incident index and
the monitor lock keep competing completions from opening two. A later accepted
failure updates that incident's latest observation and reason without changing
its origin. A later accepted success resolves it with the recovery check and
time. Because all three transitions follow `ApplyResult` in the same locked
transaction, late and repeated results cannot change incident history.
Compact monitor snapshots query the open incident separately and expose only
its ID. This makes sub-threshold failure, open incident, pause, and resumed
untested state unambiguous without turning every list response into incident
history; paginated details use their own operation.

`IHttpMonitorHistoryStore` supplies separate newest-first check and incident
pages with exclusive sequence cursors. It also owns the daily 90-day cleanup:
resolved incidents are removed before unreferenced old checks in one
transaction, while monitor pointers and every remaining incident reference are
protected. A small application act derives the exclusive cutoff from the
injected clock; the API host runs it at startup and once per day.

Unit tests protect these directions by reading the project references. A term
introduced in code is documented with the domain model when that model lands.

## Interfaces and generated artifacts

The HTTP contract is an artifact, not a second description of the API. A
contract integration test captures the running host deterministically at
`docs/api/openapi.json` and fails when the checked-in document differs. The
TypeScript schema in `src/web/src/api/schema.d.ts` and Go client in
`src/cli/internal/api/client.gen.go` are generated from that one file.

Only the contract is committed. Generated outputs are ignored because every
web and Go build regenerates them first; a generator failure or a client compile
failure therefore fails the build rather than leaving a stale checked-in copy.
`scripts/check-contract.sh` performs the implementation/contract comparison and
both generation paths.

Every API endpoint lives below `/api`; every other route is available to the
SPA. `GET /api/health/live` is the technical health path established by the
foundation. It says only that the process can answer and is not a promise of
functioning monitoring. The operational health contract required by the MVP
arrives with the monitoring loop.

## Web and CLI

The web application is a Vite/React/TypeScript project built independently from
.NET during development. Tailwind supplies the light/dark token layer and
repository-owned components wrap Base UI primitives. It routes the generated
bootstrap and session responses into distinct first-start, sign-in, and project
surfaces. Proofs and passwords use password inputs and leave component state as
soon as they are submitted. The project surface lists live or deleted projects
and creates, renames, soft-deletes, and restores them using only generated API
operations and their optimistic versions. A live project opens a functional
HTTP-monitor workspace for complete public configuration, write-only header
creation/replacement/removal, explicit lifecycle state, immediate tests, pause,
resume, retained removal, and paginated check and incident history. The edit
form preserves a hidden target query unless the operator explicitly replaces
the complete target. Secret header values and replacement targets leave
component state as soon as they are submitted. Native forms, visible loading,
empty, validation, success, and concurrency states, semantic status regions,
and focus-visible controls keep the slice keyboard accessible. The production build lands in
`src/Upaffe.Api/wwwroot`, which the API process serves, so no second application
server is required in an installation.

The CLI is an independent Go module whose executable is `ua`. It is designed for
unattended use: machine-readable output, data on stdout, diagnostics on stderr,
stable exit categories, and no implicit prompt, editor, or pager. `ua version`
and help are offline; `ua status` resolves `--url` before `UPAFFE_URL` and calls
the generated version operation with a bounded timeout. Credential commands
resolve `--credential` before `UPAFFE_CREDENTIAL` and call only the generated
create, list, rotate, and revoke operations. Its only application boundary is
the generated Go client of the same contract used to generate the web
application's TypeScript types. [`cli.md`](./cli.md) is the process contract.

The end-to-end technical path is deliberately small:

```text
docs/api/openapi.json ──> TypeScript types ──> web app ───┐
                       └─> Go client ────────> ua CLI ────┼─> API ─> PostgreSQL
                                                         ┘
```

## Persistence and delivery

PostgreSQL is the sole application database. Infrastructure owns EF Core and a
forward-only migration chain; the API host applies it before serving. Two starts
serialize migration through a PostgreSQL advisory lock, and an older binary
refuses a database containing unknown migrations. Integration tests use a real
PostgreSQL 18 container and isolate every test in its own database. The full
rule is in [`storage.md`](./storage.md). Access rows retain only password hashes
or fixed-length secret digests; database constraints enforce the singleton
operator, credential lifecycles, and stable project identity. Startup can arm a
30-minute bootstrap grant only while no operator exists; the proof is digested
before persistence and consumed transactionally with operator creation.
Browser authentication compares Argon2id work for every email outcome, stores
only the session-secret digest, and applies both idle and absolute expiry during
each admission. Cookie-authenticated writes pass the same-origin CSRF guard.
Named management credentials use parseable bearer tokens with a public UUID and
hashed secret; transactional rotation overlaps the prior digest for ten minutes
and revocation is checked on every admission. Every routed endpoint records one
public, browser, or management boundary in its metadata, while a global
authenticated fallback closes an endpoint whose classification was omitted.
Authentication audit logs identify the HTTP operation, outcome, access path,
and public access ID without recording presented secrets.
Project application acts validate immutable keys, mutable names, and positive
expected versions once for every transport. The PostgreSQL project store maps
unique-key races to idempotent create or conflict, uses the persisted version as
an optimistic concurrency token, and retains soft-deleted rows for explicit
restoration. The generated Go client backs noninteractive `ua project`
create/get/list/rename/delete/restore commands; they address immutable keys,
carry explicit versions for writes, and preserve the API's stable problem
categories in process exit codes.

The same generated client backs the complete noninteractive `ua monitor`
surface: create, list, detail, update, removal, pause, resume, immediate test,
header replacement/removal, and paginated check and incident history. Structured
and secret-bearing writes come from an explicit bounded JSON file or stdin, not
command-line values. Ordinary text and JSON output use only the API's redacted
monitor and history responses. List and detail locate the current
`latest_result_id` in bounded history pages while a failure streak is active, so
sub-threshold errors remain visible even though compact monitor state contains
only its reference. An immediate check failure has its own process category
after its structured result is written; HTTP/API failures retain the shared
categories.

HTTP monitor entities validate the durable limits from ADR 0004 and retain the
state and observation ordering from ADR 0003 without taking a dependency on EF
Core. Infrastructure maps them to PostgreSQL with a project-scoped immutable
key, distinct latest-result and latest-success references, separate target-query
and header-value secret tables, immutable completed check rows, and a partial
unique index for one open incident per monitor.

The local Compose environment builds the React application and .NET API into
one development image and starts it beside PostgreSQL 18. Database readiness
gates application startup, application readiness verifies the migrated
database, and a named volume preserves local state across ordinary restarts.
Production images, reverse proxying, upgrades, backup, restore, and
failed-upgrade recovery belong to the later operations epic. GitHub Actions
reproduces the documented local build, test, generation, Compose validation,
and disposable access/project system test without requiring private secrets
from contributors. Only the setup actions' package-manager caches persist
between jobs; generated clients and distributable build outputs are rebuilt and
are not uploaded.

## Development and verification commands

Run these from the repository root:

- `scripts/check.sh` reproduces the essential .NET, web, CLI, contract-client,
  and Compose checks performed by CI.
- `scripts/check-contract.sh` compares the running API description with the
  checked-in OpenAPI document, regenerates both clients, and compiles their
  consumers.
- `scripts/smoke.sh` builds an isolated Compose project from an empty database;
  establishes the operator and browser session; manages one project through
  browser, CLI, and direct API paths; proves credential rotation/revocation and
  singular persisted identity; checks ordinary artifacts and app logs for its
  secrets; then removes the disposable database volume.
- `docker compose -f deploy/docker-compose.dev.yml up --build --wait` starts the
  persistent local development environment described in
  [`operations.md`](./operations.md).

Generated TypeScript and Go clients are prerequisites produced by their normal
build commands, not files a contributor edits. The component suite exercises
the monitor interface; the disposable system test still proves only access and
project behavior and does not assert that monitoring works.

## Documentation ownership

- `docs/api.md` changes with the public HTTP contract.
- `docs/cli.md` changes with commands, configuration, output, and exit codes.
- `docs/storage.md` changes with schema, migrations, retention, and backup
  boundaries.
- `docs/install.md` exists when there is a supported installation procedure.
- `docs/operations.md` currently owns the local Compose lifecycle and grows
  with supported production procedures later.

Those files are created with the behavior they describe. Until then, this map
names their future responsibility without pretending that the interface or
procedure is already available.
