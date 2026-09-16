# ADR 0005: Claim scheduled HTTP work in PostgreSQL

- Status: accepted
- Date: 2026-09-16

## Context

An HTTP monitor must keep running after an application restart, and several
application instances must be able to share the same database without recording
one due run as independent results. Holding a database transaction open during
DNS, connection, TLS, and response processing would make those external delays
part of the storage lock boundary. An in-memory timer would lose work on every
restart and would give each instance its own conflicting schedule.

upaffe does not otherwise need a message broker or a general job platform. The
monitor definition, due time, ordered check row, and result already belong in
PostgreSQL.

## Decision

PostgreSQL is the durable scheduler and work queue for HTTP monitoring.

An active monitor's `next_check_at` is its pending schedule. A worker claims the
oldest due monitor under `FOR UPDATE SKIP LOCKED`, creates its ordered
`http_check`, advances the next due time from the actual start time, and commits
an execution token with a two-minute lease. The database transaction ends
before the worker crosses the HTTP execution boundary. Missed intervals are not
replayed as a burst.

The worker completes the check in a second short transaction that locks the
check row. Only the token currently stored on an incomplete check may add its
immutable result. A worker may finish after its nominal expiry while no other
worker has reclaimed the row; once a reclaim replaces the token, the older
worker can no longer complete it.

An incomplete scheduled check whose lease expired is reclaimed before new due
work. Its identity and monitor-local sequence stay unchanged, its attempt count
increases, and the HTTP request may run again. This provides at-least-once
external execution but exactly one persisted result for the scheduled check.
The two-minute lease is longer than the maximum one-minute HTTP timeout.

New monitors and resumed monitors set their due time to the change time.
Paused, removed, and project-deleted monitors have no claimable new work.
Incomplete work from an older evaluation generation is not resumed.

The transaction boundaries define recovery:

- failure before the claim commits leaves the monitor due;
- process loss after claim leaves an incomplete check that is reclaimed after
  the lease;
- an ordinary DNS, connection, TLS, timeout, status, or text failure completes
  the check with its stable failure reason;
- a transient completion-database failure leaves the check incomplete and it is
  retried after the lease;
- competing instances skip locked work and claim another due monitor, if any.

The API host drains work continuously. Its idle delay uses the injected
`TimeProvider`; scheduling operations accept explicit instants so tests can
advance time without waiting on wall-clock intervals.

## Consequences

No separate queue must be deployed or reconciled with monitor state. The
database protects allocation and result identity across restarts and replicas.

An HTTP request can be repeated when a worker loses its lease or cannot persist
completion. Checks therefore remain bounded and side-effect-free GET requests,
and the product promises one recorded result rather than exactly-once network
delivery.

The scheduler completes observation rows only. Ordered health evaluation and
incident transitions remain a separate application step so their rules do not
become part of network execution or lease ownership.
