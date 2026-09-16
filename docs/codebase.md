# The codebase

`VISION.md` defines the product. This document defines where its implementation
lives, which way dependencies point, and which artifacts are authoritative. It
is updated as each epic changes the repository.

Current state: ADR 0001 and this documentation structure exist. The source,
tests, deployment files, and interface documents described below are the target
of UP-E1 and are explicitly marked as planned until their owning ticket lands.

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
├─ .github/workflows/        CI (planned: UP-9)
├─ deploy/                   local Compose now; production packaging later
├─ docs/
│  ├─ adr/                   local and adopted architecture decisions
│  ├─ api/openapi.json       checked-in HTTP contract (planned: UP-5)
│  ├─ codebase.md            this map
│  ├─ api.md                 HTTP conventions and surface, when implemented
│  ├─ cli.md                 CLI contract, when implemented
│  ├─ storage.md             schema and retention rules, when implemented
│  ├─ install.md             supported installation, when implemented
│  └─ operations.md          operating procedures, when implemented
├─ src/
│  ├─ Upaffe.Domain/         domain rules (planned: UP-3)
│  ├─ Upaffe.Application/    use cases and required ports (planned: UP-3)
│  ├─ Upaffe.Infrastructure/ adapters and PostgreSQL (planned: UP-3/UP-4)
│  ├─ Upaffe.Api/            HTTP and composition root (planned: UP-3)
│  ├─ cli/                   standalone Go CLI `ua` (planned: UP-7)
│  └─ web/                   React application (planned: UP-6)
└─ tests/
   ├─ Upaffe.UnitTests/
   └─ Upaffe.IntegrationTests/  (planned: UP-3)
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

- **Domain** holds monitoring terms and rules. It has no project or package
  references.
- **Application** holds operations and the ports they require. It can reference
  Domain and nothing outward.
- **Infrastructure** implements Application ports using PostgreSQL and external
  services. Domain never references EF Core.
- **API** is the composition root and HTTP boundary. It is the only productive
  project that knows all implementation layers.

Architecture tests protect these directions once the projects exist. A term
introduced in code is documented with the domain model when that model lands.

## Interfaces and generated artifacts

The HTTP contract is an artifact, not a second description of the API. UP-5
will capture it deterministically at `docs/api/openapi.json` and generate the
TypeScript and Go clients from it. The web application and CLI do not share
backend assemblies or reimplement endpoint shapes by hand.

Every API endpoint lives below `/api`; every other route is available to the
SPA. The technical health path established by the foundation is not a promise
of functioning monitoring. The operational health contract required by the
MVP arrives with the monitoring loop.

## Web and CLI

The web application is a Vite/React/TypeScript project built independently from
.NET during development. Tailwind supplies the token layer and repository-owned
components use Base UI. The production build is later served by the API process,
so no second application server is required in an installation.

The CLI is an independent Go module whose executable is `ua`. It is designed
for unattended use: machine-readable output, data on stdout, diagnostics on
stderr, stable exit categories, and no implicit prompt, editor, or pager. Its
only application boundary is the same generated API client used by the web
application.

## Persistence and delivery

PostgreSQL is the sole application database. Infrastructure owns EF Core and a
forward-only migration chain; the API host owns startup migration. Integration
tests use real PostgreSQL and isolate their data. These are planned in UP-4 and
become statements of existing behavior only after that ticket lands.

Docker Compose is the supported deployment direction. UP-8 provides the local
development environment; production images, upgrades, backup, restore, and
failed-upgrade recovery belong to the later operations epic. GitHub Actions in
UP-9 reproduces the documented local build, test, and generation checks without
requiring private secrets from contributors.

## Documentation ownership

- `docs/api.md` changes with the public HTTP contract.
- `docs/cli.md` changes with commands, configuration, output, and exit codes.
- `docs/storage.md` changes with schema, migrations, retention, and backup
  boundaries.
- `docs/install.md` exists when there is a supported installation procedure.
- `docs/operations.md` exists when there is a supported operational procedure.

Those files are created with the behavior they describe. Until then, this map
names their future responsibility without pretending that the interface or
procedure is already available.
