# 0003 — Monitor state and incident lifecycle

Status: accepted

HTTP monitoring needs to show a failed observation before it deserves an
incident, preserve unresolved incidents through pauses, and remain correct
when checks overlap or finish out of order. The monitor's evaluation state and
its incident lifecycle are therefore related but separate. The same model can
later accept push results without assigning push-specific deadlines yet.

## Identity and ordered observations

A monitor belongs to exactly one project. Its operator-chosen key is immutable
and unique within that project; the pair `(project key, monitor key)` is its
stable public identity. Its display name and configuration may change without
changing that identity. A check receives an immutable check ID and a strictly
increasing, monitor-local sequence before work begins. Its result is immutable,
and recording the same result again is idempotent.

Only a result whose sequence is newer than the monitor's last applied sequence
may change current state or an incident. A late older result remains useful
history but cannot overwrite the latest result, add to the current failure
streak, open an incident, or recover one. Applying a result and changing the
monitor and incident records is one transaction. A database constraint allows
at most one open incident per monitor, so concurrent or repeated processing
cannot split one uninterrupted failure into duplicate incidents.

## Evaluation state

A monitor has exactly one visible evaluation state:

| State | Meaning |
| --- | --- |
| `untested` | The active monitor has no applicable result in its current evaluation generation. |
| `healthy` | Its latest applicable result in the current generation succeeded. |
| `failing` | Its latest applicable result in the current generation failed, even if the threshold has not opened an incident. |
| `paused` | The operator has suspended evaluation; retained results do not make it healthy or failing. |

Creation starts the first evaluation generation in `untested` and makes an HTTP
check due promptly. Scheduled and explicitly requested checks follow the same
result rules. The latest result and latest success are retained facts, not
state aliases: a failing monitor can still have a latest success, and a paused
or newly resumed monitor can show earlier facts while its state is neither
healthy nor failing.

Pausing prevents new checks from being claimed and invalidates outstanding
checks for state transitions. Their eventual results may be retained as late
history, but cannot alter current state or incidents. Resuming starts a new
evaluation generation in `untested`, clears any sub-threshold failure streak,
and makes an HTTP check due promptly. It preserves history and any open
incident; resumption alone is neither recovery nor proof of health.

## Thresholds, incidents, and recovery

Each applicable successful result sets the active monitor to `healthy`, resets
its consecutive-failure count, updates latest result and latest success, and
resolves its open incident if one exists. This fresh success is a recovery;
editing configuration, pausing, resuming, or merely reaching time does not
resolve an incident.

Each applicable failed result sets the active monitor to `failing`, updates its
latest result, and increments the consecutive-failure count. The first failure
is visible immediately. When the configured threshold is reached and there is
no open incident, one is opened with the first failure in that streak as its
start and the threshold-crossing result as its opening observation. Further
failures update that incident's latest reason and observation time. When an
incident already remains open across a pause, a fresh failure after resumption
updates the same incident immediately; it never opens a second incident or
waits for the threshold again.

Examples for a threshold of three:

- A new monitor is `untested`. Failure 1 makes it `failing` with no incident;
  failure 2 remains visible without an incident; failure 3 opens one incident
  whose failure began at failure 1.
- Further failures update that incident. Replaying failure 3 changes nothing.
  If failure 5 finishes before failure 4, failure 5 applies and the late
  failure 4 is history only.
- A fresh success after the failures makes the monitor `healthy`, resets the
  streak, and resolves the incident. A delayed success ordered before a newer
  failure cannot recover it.
- Pausing that failing monitor changes its state to `paused` but preserves the
  incident. Resuming changes it to `untested`; the first fresh success recovers
  it, while the first fresh failure keeps the same incident open.
- Pausing after only two failures preserves both results as history but not the
  partial streak. After resumption, three fresh consecutive failures are needed
  to open the first incident.

Push monitors will use the same monitor states, immutable observations, single
open-incident rule, and fresh-success recovery. Their first-report windows,
missing-report results, and explicit-failure timing are separate decisions.
