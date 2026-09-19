# 0009 — Project instance health overview

Status: accepted

The human dashboard needs one consistent cross-project entry point. Repeating
the project report for each project would make query count grow with the number
of projects, so the overview is a separate, smaller read-only projection. It
reads live projects and monitors, their current result pointers, open incidents,
active maintenance, and grouped delivery states in a fixed number of queries.
It never evaluates a monitor or changes an incident.

One `generated_at` instant decides whether stored work is overdue and whether
maintenance is active. Monitor evaluation state, overdue work, open incidents,
and maintenance are separate facts. A paused monitor can retain an incident; a
healthy monitor can have an overdue check; a silent push monitor can cross its
deadline before the evaluator records a missing-report result. The overview
does not infer an observation from elapsed time. Project counts include every
live monitor exactly once by evaluation state and count overdue work separately.

Attention is ordered by open incident, failing, overdue, untested, paused,
then stable project key, type, and monitor key. Project summaries put incidents
first, then failures or terminal delivery trouble, overdue or pending work,
untested projects, and finally healthy or empty projects, with key as a stable
tie breaker. The response omits detailed history and all secret and untrusted
diagnostic content. Delivery counts describe recorded intent state, and
`overdue` uses stored next-attempt times or an expired claim lease; `accepted`
means SMTP acceptance only. These rules follow ADRs 0003, 0007,
and 0008.
