# 0013 — Report monitoring progress separately from process readiness

Status: accepted

The process liveness endpoint proves only that HTTP can answer. Readiness proves
that PostgreSQL is reachable with the expected schema. Neither proves that
scheduled HTTP checks or push deadline detection continue to advance. A
separate unauthenticated `/api/health/progress` endpoint returns `200` only
after both workers have completed a successful iteration within the last two
minutes. A successful idle database claim counts as progress; no monitor or
incident record is invented. A failed claim or completion does not count.

The two timestamps exist only in process memory. Startup, a restart before
both workers have polled, and `Monitoring:Enabled=false` therefore return
`503`. A stalled loop or sustained database failure becomes unhealthy once
its last successful iteration is more than two minutes old; recovery requires
a new successful iteration from both loops. This bound accommodates the
maximum 60-second HTTP check while detecting a stalled worker independently
of web and database readiness. A backwards clock jump also fails closed until
fresh progress is recorded.

The response has only `{"status":"progressing"}` or
`{"status":"stalled"}`. It discloses no project, target, credential, result,
or worker timing, and uses `Cache-Control: no-store` so an intermediary cannot
reuse an earlier healthy answer. Worker retry logs identify only the exception
type, not its message or stack, so an unexpected store diagnostic cannot expose
target data.
An independent checker can poll it from outside the
instance's failure domain. The optional outbound heartbeat uses this same
freshness decision; it does not substitute for external observation of a
missing HTTP response.
