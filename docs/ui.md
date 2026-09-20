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
