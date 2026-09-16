# 0001 — Adopt the affe foundation without its domain

Status: accepted

## Context

`VISION.md` commits upaffe to .NET 10 and ASP.NET Core, PostgreSQL, React with
TypeScript, Tailwind and Base UI, a Go CLI, and a checked-in OpenAPI contract.
Those choices already have working public implementations in
[`planaffe`](https://github.com/datavisionzero/planaffe) and
[`hostingaffe`](https://github.com/datavisionzero/hostingaffe). Re-selecting the
stack would add risk without changing the product.

The two references differ in how close they are to this repository's starting
point:

| Area | planaffe | hostingaffe | upaffe decision |
| --- | --- | --- | --- |
| Backend | Four directed .NET layers with architecture tests | The same layers after another product domain was removed | Adopt the four project shapes and keep the domain empty until monitoring terms arrive |
| Persistence | EF Core/PostgreSQL with forward-only migrations | A restarted migration chain for the product that actually exists | Start one upaffe chain with an empty initial schema; never import either product's schema |
| HTTP | Minimal APIs and a generated OpenAPI document | The API is grouped below `/api`, leaving every other path to the SPA | Adopt the `/api` boundary and the contract-generation mechanism |
| Web | React/Vite, Tailwind, Base UI, generated TypeScript client | The same shell and delivery model without planaffe's project navigation | Scaffold a small upaffe shell; do not copy either product's screens |
| CLI | Go client generated from the public contract | The same process contract under a product-specific executable | Build `ua` as an independent client; do not copy tracker or infrastructure verbs |
| Delivery | Multi-stage image, Compose, and GitHub Actions | The same pipeline adapted to one application and PostgreSQL | Adapt the build and local Compose shapes; production packaging remains a later epic |
| Documentation | Codebase, API, CLI, storage, operations, and ADR documents | The same structure with adopted ADRs referenced instead of copied | Use that structure and describe only behavior that has landed |

planaffe is the authority for the architectural choices. hostingaffe is the
closer implementation reference because it has already separated those choices
from planaffe's ticket domain. The foundation commits of personalaffe were also
inspected as evidence that the same selective-scaffold sequence works from an
empty repository; personalaffe is not an architectural dependency or product
authority for upaffe.

## Decision

Build a selective scaffold from the reference implementations, rather than
copying either repository wholesale. Adapt proven foundation files area by area
and review every retained type against upaffe's vocabulary and vision.

The target repository shape is:

```text
src/
  Upaffe.Domain/          monitoring rules, with no outward dependencies
  Upaffe.Application/     use cases and the ports they require
  Upaffe.Infrastructure/  PostgreSQL and external adapters
  Upaffe.Api/             HTTP and the composition root
  cli/                    the standalone Go CLI, `ua`
  web/                    the React application
tests/
  Upaffe.UnitTests/
  Upaffe.IntegrationTests/
docs/
  adr/                    upaffe decisions and adopted-decision index
  api/openapi.json        the checked-in HTTP contract
  codebase.md             actual repository structure and build paths
  api.md, cli.md          durable public interface documentation when present
  storage.md              durable schema and retention documentation when present
  install.md, operations.md  supported deployment and operation when present
deploy/                   Compose and image definitions
```

Dependencies point inward: API composes Application and Infrastructure;
Infrastructure implements Application ports and may use Domain; Application may
use Domain; Domain references no other productive project. The web application
and CLI communicate only through the checked-in HTTP contract.

The following planaffe decisions are adopted by reference:

- [0002 — The backend is four layers, not one project](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0002-the-backend-is-four-layers-not-one-project.md)
- [0003 — The CLI is Go, not a second .NET binary](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0003-the-cli-is-go-not-a-second-dotnet-binary.md)
- [0004 — The frontend is React, not Blazor](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0004-the-frontend-is-react-not-blazor.md)
- [0005 — The contract is checked in, and both clients are generated from it](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0005-the-contract-is-checked-in-and-both-clients-are-generated-from-it.md)
- [0006 — The web application is a shell before it is a screen](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0006-the-web-application-is-a-shell-before-it-is-a-screen.md)
- [0011 — The API carries no version, and migrations only run forward](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0011-the-api-carries-no-version-and-migrations-only-run-forward.md)
- [0017 — The web application is drawn by Tailwind and Base UI](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0017-the-web-application-is-drawn-by-tailwind-and-base-ui-components-the-repository-owns.md)

hostingaffe's `/api` namespace is adopted because upaffe likewise serves the SPA
and API from one process. If an adopted decision stops fitting, an upaffe ADR
must name and supersede it; the reference text is never copied and silently
edited.

## Deliberately excluded

No product domain is imported. In particular, planaffe's issues, epics,
releases, labels, claims, project scoping, questions, comments, and wake-up
mechanism do not enter the application. hostingaffe's machines, installations,
software, deployments, files, pages, and reports do not enter it either.

Identity, e-mail, idempotent management writes, and other mechanisms that
upaffe's vision does require arrive in their owning epics. They are not copied
early merely because a reference product already has them. There is no shared
runtime package, submodule, template dependency, or service dependency on any
other affe product.

## Consequences

The repositories may drift. A shared bug may need more than one fix, but upaffe
can evolve without coordinating releases with another product. Proven files are
adapted faster than they can be re-invented, while the small initial surface
makes accidental domain carry-over reviewable.

The vision and this decision currently agree. If implementation exposes a
conflict, the conflict is recorded in a new ADR and, when it changes a product
commitment, in `VISION.md`; implementation does not settle it silently.
