# Web UI review

The reference is the planaffe source revision recorded in [ui.md](ui.md).
Upaffe uses its typography, warm neutral surfaces, teal accent, compact
headers, persistent sidebar, and semantic component patterns. Monitoring keeps
health and incident evidence ahead of administration, and treats maintenance
and email delivery as separate facts. No tracker navigation or backend behavior
is shared.

## Reproducible visual coverage

From `src/web`, run `npm ci`, `npx playwright install chromium`, and
`npm run test:visual`. The suite intercepts the API with invented `.test`
addresses and monitor data. It compares focused screenshots of the sidebar,
health counts, push status and configuration, and SMTP settings against the
checked-in `visual/alignment.spec.ts-snapshots` images. Browser/platform-specific
baselines cover local macOS and CI Linux. Review any changed image before
updating a baseline with `npm run test:visual -- --update-snapshots`.

The same suite loads these authenticated routes at 360, 720, 768, and 1440 CSS
pixels in light and dark appearance, checking the primary heading and page
overflow at each size:

| Route | State or operation checked |
| --- | --- |
| Dashboard | Health counts, failure-first attention, delivery summary |
| Projects | Create, live/deleted lists, rename and destructive controls |
| Project overview | Counts, attention, healthy monitors, project context |
| Monitor inventory | Search/filter form, table and narrow labeled cards |
| HTTP monitors | Create form, monitor list, and deep-linked detail |
| Push monitors | Both reporting modes, list, and deep-linked detail |
| Instance email | SMTP, write-only password, defaults, test recipient |
| Project email | Recipients, maintenance, delivery history |

The 720px case checks reflow at the CSS width of a 1440px window viewed at
200% zoom. A separate access case exercises bootstrap, sign-in, and connection
failure at 360px. Vitest covers pending/error/empty states, version conflicts,
deep links, keyboard navigation and drawer focus return, credential handoff,
secret clearing, and API request behavior. The visual suite checks rendered
layout; the behavior suite remains the source for workflow assertions.

The reviewed browser surfaces had no horizontal overflow at 360px and no
WCAG 2A/AA violations in axe checks of the dashboard, project overview,
inventory, push detail, projects, and instance/project email screens. Status
badges pair icons with text; focusable controls have visible focus; failures
and pending states retain readable text in both themes. Rendered evidence is
limited to representative states, so new component variants need a reviewed
fixture when introduced.
