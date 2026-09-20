# Web interface conventions

The visual reference is planaffe commit `50e387ca2396c4b697d0e3c1a2f6245c65a600a3`.
Its `index.css`, theme provider, UI wrappers, shell, sidebar, page header, and
project switcher provide the type, color, spacing, and navigation patterns.
Upaffe owns its implementation and does not load a shared runtime package.

| Reference pattern | Upaffe surface |
| --- | --- |
| Warm neutral surfaces, IBM Plex type, teal brand accent | Semantic tokens in `src/web/src/index.css` |
| Light, dark, and system appearance | `ThemeProvider` with the local `upaffe-theme` preference |
| Compact header and persistent sidebar | Workspace shell |
| Bordered controls, panels, status badges | Repository-owned UI components and monitoring views |
| Project context in navigation | Existing project routes and their project name |

Monitoring state is separate from the brand accent. Healthy, failing, untested,
paused, overdue, maintenance, and delivery problems remain distinct in text and
color. A failed observation below its alert threshold remains visible. The
dashboard keeps failures first, and the CLI and API retain their existing rules.

The theme foundation initially preserves legacy class names as token aliases.
View migrations replace those styles incrementally; no route needs a backend
change to adopt the visual language. Fonts are bundled by the web build.

Shared controls live in `src/web/src/components/`: `Button` has primary,
secondary, subtle, and destructive variants; `TextField`, `SelectField`, and
`CheckboxField` associate labels, guidance, and errors with controls. The
presentation components provide text-bearing status badges, alerts, empty and
loading states, compact page headers, and sections. Tables use `ui-table` for
consistent density. Use native control semantics and Base UI interaction
primitives for focus-sensitive dialogs and menus when a workflow needs them.

The authenticated shell follows the reference's persistent sidebar and compact
header. Its destinations are Dashboard, Projects, Monitors, and Settings; a
project route adds overview, HTTP, push, and project email links. The current
project comes from the URL. On narrow screens the same navigation opens as a
focus-managed drawer. The project selector changes the URL to the selected
project overview. Appearance and sign-out controls stay in the sidebar.
Monitoring keeps its own route hierarchy and omits tracker-specific navigation.

Dashboard and project overviews use compact page and section headers, dense
count tiles, text-bearing status badges, and bordered lists. Failure and overdue
work come before healthy monitors. Maintenance and pause remain independent of
health, and email acceptance is described separately from inbox delivery.
The brand accent has a darker light-theme text companion so small links and
labels remain readable on warm surfaces; health and warning colors have their
own tokens. Long purpose and operator guidance text wraps within cards.

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
