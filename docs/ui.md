# Web interface conventions

The visual reference is planaffe commit `50e387ca2396c4b697d0e3c1a2f6245c65a600a3`.
Its `index.css`, theme provider, UI wrappers, shell, sidebar, page header, and
project switcher provide the type, color, spacing, and navigation patterns.
Upaffe owns its implementation and does not load a shared runtime package.

| Reference pattern | Upaffe surface |
| --- | --- |
| Warm neutral surfaces, IBM Plex type, teal brand accent | Semantic tokens in `src/web/src/index.css` |
| Light, dark, and system appearance | `ThemeProvider` with the local `upaffe-theme` preference |
| 16rem sidebar and compact 3rem header | Authenticated workspace shell |
| Bordered controls, menus, dialogs, tables, and status badges | Repository-owned components built on Base UI where focus management matters |
| Project context in the header and navigation | Project switcher, breadcrumbs, and project routes |
| Account and appearance menu | Top-right operator menu with Light, Dark, System, Settings, and Sign out |

Monitoring state is separate from the brand accent. Healthy, failing, untested,
paused, overdue, maintenance, and delivery problems remain distinct in text and
color. A failed observation below its alert threshold remains visible. The
dashboard keeps failures first, and the CLI and API retain their existing rules.

The theme foundation preserves a few legacy class names as token aliases while
all authenticated routes use the shared visual grammar. Fonts are bundled by
the web build. The System preference follows operating-system changes until the
operator selects Light or Dark; the preference is local to the browser and is
synchronized across tabs.

Shared controls live in `src/web/src/components/`: `Button` has primary,
secondary, subtle, and destructive variants; `TextField`, `SelectField`, and
`CheckboxField` associate labels, guidance, and errors with controls. The
presentation components provide text-bearing status badges, alerts, empty and
loading states, compact page headers, and sections. Tables use `ui-table` for
consistent density. Base UI supplies the repository-owned dropdown menu,
account menu, project switcher, mobile drawer, and confirmation dialogs so
keyboard focus, Escape handling, dismissal, and focus return remain consistent.

The authenticated shell follows the reference's persistent sidebar and compact
header. Its destinations are Dashboard, Projects, Monitors, and Settings; a
project route adds overview, HTTP, push, and project email links. The current
project comes from the URL. On narrow screens the same navigation opens as a
focus-managed drawer. The project selector changes the URL to the selected
project overview. Personal and session actions are not permanent sidebar
controls: the top-right account menu identifies the operator and contains
appearance, Settings, and Sign out. Monitoring keeps its own route hierarchy
and omits tracker-specific navigation.

The Projects route is an inventory rather than a combined creation screen. Its
search, Live/Deleted state, sort, and order are URL parameters so reload, Back,
and shared links preserve the view. Project names are the primary navigation
target; rename and confirmed deletion are secondary actions, while restoration
is immediate. A project is deleted only when its response carries a deletion
time: the API omits absent values rather than sending `null`, so the web client
treats a missing and a null value alike, here and for history cursors and
other optional facts, and never renders an unparseable time. `/projects/new` is a bounded single-column workflow with
field-level errors, a pending state, and typed-input discard protection.

Dashboard and project overviews use compact page and section headers, dense
count tiles, text-bearing status badges, and bordered lists. Failure and overdue
work come before healthy monitors. Maintenance and pause remain independent of
health, and email acceptance is described separately from inbox delivery.
The brand accent has a darker light-theme text companion so small links and
labels remain readable on warm surfaces; health and warning colors have their
own tokens. Long purpose and operator guidance text wraps within cards.

A project's HTTP and push monitor routes are inventories first: the page
header names the inventory and carries the primary `New HTTP monitor` or `New
push monitor` action, followed by the monitor list or its loading,
error-with-retry, or empty state. Monitor names link to their detail routes.
Creation is the focused route `/projects/:key/new-http-monitor` or
`/projects/:key/new-push-monitor`, a bounded single-column form with
field-level errors, a pending state, Cancel, and typed-input discard
protection; success opens the new monitor. The HTTP form keeps its secret
request headers write-only and clears the target URL and header values as soon
as they are sent, so a refused submission asks for them again. The push form
explains both reporting modes; its one-time reporting credential is issued from
the new monitor's detail. Creation routes sit beside the inventory rather than
below it because `new` is a valid monitor key and must remain reachable as a
detail deep link.

The monitor inventory uses compact filters and a dense table on wide screens;
on narrow screens each result becomes a labeled card without losing evidence or
links. HTTP and push monitor lists and details share headers, status badges,
alerts, and form styling. Their domain-specific controls and histories remain
separate: HTTP targets and secret headers, push reporting modes and one-time
credential handoff, and each monitor's pause, maintenance, and removal actions.

Project administration and email settings use the same compact panels, labeled
controls, loading and error states, and clear primary or destructive actions.
Delivery history keeps relay acceptance separate from inbox delivery. SMTP
passwords remain write-only in ordinary settings, and test-send feedback says
when the relay accepted a message without implying inbox receipt. The local
bootstrap screen still directs the operator to the installation host.

## Intentional differences from planaffe

Alignment is a product-family relationship, not a copy. Upaffe does not add
planaffe's command palette, tracker queues, knowledge-base entry, issue detail
rail, or tracker keyboard shortcuts. It uses monitoring-specific routes and
keeps status, incident, overdue, maintenance, and delivery evidence explicit in
text as well as color. Wide overview screens may use two balanced evidence
columns instead of planaffe's issue-details rail; forms remain single-column
when a second column would weaken operational scanning. Upaffe owns every
component and consumes no shared runtime UI package.
