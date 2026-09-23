# Web UI review

The reference is planaffe revision
`50e387ca2396c4b697d0e3c1a2f6245c65a600a3`, recorded in [ui.md](ui.md).
The completed shell was reviewed side by side with that revision in light and
dark appearance at desktop, 720px (the CSS-width equivalent of 1440px at 200%
zoom), and phone widths. The shared family traits are the 16rem persistent
sidebar, compact 3rem header, IBM Plex typography, warm neutral surfaces, teal
accent, restrained borders and radii, dense controls, focus-managed menus and
drawers, and consistent page and section hierarchy.

Intentional differences preserve the product: monitoring status and incident
evidence come before administration; maintenance and delivery remain separate
facts; project, HTTP, and push routes replace tracker queues and issue details;
and upaffe has no command palette, knowledge-base navigation, tracker shortcuts,
or issue-details rail. The implementation is repository-owned and shares no
runtime UI package with planaffe.

## Reproducible visual coverage

From `src/web`, run:

```sh
npm ci
npx playwright install chromium
npm test
npm run typecheck
npm run lint
npm run test:visual
npm run build
```

The visual suite intercepts the API with invented `.test` addresses and monitor
data. Checked-in macOS and Linux baselines cover the complete shell for the
dashboard and project overview, the account menu, mobile drawer, project
inventory and creation form, HTTP and push monitor inventories and creation
forms, HTTP detail and push configuration editing, empty and filtered monitor
inventories, instance email delivery and relay tasks, destructive
confirmation, and empty, failure, and pending states. Review every changed image before updating baselines with:

```sh
npm run test:visual -- --update-snapshots
```

The suite loads these authenticated routes at 360, 720, 768, and 1440 CSS
pixels in light and dark appearance, checking the primary heading and page
overflow at each size:

| Route | State or operation checked |
| --- | --- |
| Dashboard | Health counts, failure-first attention, delivery summary |
| Projects | URL-backed search/state/sort/order, live/deleted rows, rename, confirmed deletion, and restoration |
| New project | Bounded form, discard protection, field errors, and pending submission |
| Project overview | Counts, attention, healthy monitors, project context |
| Monitor inventory | Search/filter form, table and narrow labeled cards, and actionable empty and no-match states |
| HTTP monitors | Inventory before creation, empty and error states, and deep-linked detail with evidence before administration |
| New HTTP monitor | Bounded form, write-only secret headers, field errors, Cancel, and discard protection |
| Push monitors | Inventory before creation, empty and error states, and deep-linked detail with evidence before administration |
| Monitor configuration | Focused HTTP and push edit routes with discard protection |
| New push monitor | Both reporting modes, field errors, Cancel, and discard protection |
| Instance email | Delivery landing and task navigation; relay, write-only password, defaults, and test-send task routes |
| Project email | Recipients, maintenance, delivery history |

The 720px case checks reflow at the CSS width of a 1440px window viewed at
200% zoom. A separate access case exercises bootstrap, sign-in, and connection
failure at 360px. Vitest explicitly verifies System follows OS changes until an
explicit choice is stored, plus cross-tab synchronization. It also covers
pending/error/empty states, version conflicts, deep links, account and project
menu keyboard behavior, drawer focus return, destructive dialogs, project and
monitor discard protection, credential handoff, secret clearing, and API
requests.

Playwright loads every authenticated route at all four widths in both
appearances and fails on horizontal overflow. The same suite runs axe-core WCAG
2A/AA checks across every authenticated route, alternating phone and desktop
widths and light/dark appearance, and also scans the typed-input discard dialog.
Keyboard checks cover the account menu, project switcher, sidebar drawer,
forms, and focus return. The reviewed matrix has no automated WCAG 2A/AA
violation. Status badges pair icons with text; focusable controls have visible
focus; failures and pending states retain readable text in both themes. New
routes and component variants require both a matrix entry and a reviewed
shell-level fixture.

## Repository validation

For the complete repository validation used by CI, run from the repository
root:

```sh
dotnet restore Upaffe.slnx
dotnet build Upaffe.slnx --configuration Release --no-restore
dotnet test tests/Upaffe.UnitTests --configuration Release --no-build --no-restore
dotnet test tests/Upaffe.IntegrationTests --configuration Release --no-build --no-restore
(cd src/cli && go generate ./... && go vet ./... && go test ./... && go build ./...)
scripts/check-contract.sh
```
