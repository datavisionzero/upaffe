# Architecture decisions

This directory records decisions made specifically for upaffe. A decision is
added when an implementation choice is durable, constrains later work, or
deliberately departs from a product commitment or an adopted reference.

## upaffe decisions

- [0001 — Adopt the affe foundation without its domain](./0001-adopt-the-affe-foundation-without-its-domain.md)
- [0002 — One operator, two access paths, and recoverable projects](./0002-one-operator-two-access-paths-and-recoverable-projects.md)
- [0003 — Monitor state and incident lifecycle](./0003-monitor-state-and-incident-lifecycle.md)
- [0004 — Bound and isolate HTTP checks](./0004-bound-and-isolate-http-checks.md)
- [0005 — Claim scheduled HTTP work in PostgreSQL](./0005-claim-scheduled-http-work-in-postgresql.md)
- [0006 — Receive and order push reports](./0006-receive-and-order-push-reports.md)
- [0007 — Deliver incident email around timed maintenance](./0007-deliver-incident-email-around-timed-maintenance.md)
- [0008 — One-call project report](./0008-one-call-project-report.md)
- [0009 — Project and instance health overview](./0009-project-instance-health-overview.md)
- [0010 — Read production secrets from mounted files](./0010-read-production-secrets-from-mounted-files.md)
- [0011 — Keep production state in PostgreSQL behind a local application port](./0011-compose-state-and-network-boundaries.md)
- [0012 — Trust forwarded identity only from named HTTPS proxies](./0012-trust-only-named-https-proxies.md)

## Decisions adopted from planaffe

The stack was already decided in planaffe. These ADRs are referenced rather
than copied so that their rationale has one authoritative home:

| ADR | Adopted decision |
| --- | --- |
| [0002](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0002-the-backend-is-four-layers-not-one-project.md) | The backend is four directed layers. |
| [0003](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0003-the-cli-is-go-not-a-second-dotnet-binary.md) | The CLI is an independent Go client. |
| [0004](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0004-the-frontend-is-react-not-blazor.md) | The web application is React and TypeScript. |
| [0005](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0005-the-contract-is-checked-in-and-both-clients-are-generated-from-it.md) | One checked-in OpenAPI contract generates both clients. |
| [0006](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0006-the-web-application-is-a-shell-before-it-is-a-screen.md) | The web application starts as a shell. |
| [0011](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0011-the-api-carries-no-version-and-migrations-only-run-forward.md) | The API path is unversioned and database migrations only move forward. |
| [0017](https://github.com/datavisionzero/planaffe/blob/main/docs/adr/0017-the-web-application-is-drawn-by-tailwind-and-base-ui-components-the-repository-owns.md) | Tailwind and repository-owned Base UI components draw the application. |

hostingaffe is the closer implementation reference for the `/api` namespace,
startup migration, Compose, and image shapes. Its product-specific decisions
are not adopted implicitly. If a referenced decision stops fitting upaffe, a
new local ADR names what is superseded and why.
