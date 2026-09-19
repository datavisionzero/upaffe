# 0014 — Send only empty healthy heartbeats

Status: accepted

An operator may opt in to an outbound heartbeat by mounting a receiver URL as
`UPAFFE_HEARTBEAT_URL_FILE`. With no file setting, no sender request is made.
The URL is a secret because its path or query can carry the receiver token.
Startup accepts only an absolute HTTPS URL with a multi-label DNS name, no user
information or fragment, and at most 2048 characters. IP literals, loopback,
and `.local` names are rejected. The configured destination is never included
in ordinary logs or status output.

The sender uses the same two-minute, two-worker freshness rule as
`/api/health/progress`. It makes one empty GET attempt immediately on startup
if progress is already fresh, then waits 60 seconds between attempts. Each
attempt has a five-second timeout. There is no immediate retry. A failed attempt can
be retried at the next interval only if monitoring remains fresh. Redirects,
cookies, environment proxies, and default credentials are disabled. The
receiver's response body is neither read nor logged. Non-success responses
produce only a status-code warning; transport failures log only their type.

The receiver must operate outside the instance's host and failure domain. It
should alert after more than three minutes without a successful request,
accounting for the two-minute progress freshness window, the one-minute send
interval, and network latency. A missing heartbeat reports either local
monitoring failure or a sender, network, or receiver fault; operators use the
separate HTTPS progress endpoint to narrow the cause. The sender does not
create monitor observations or incidents and does not add an inbound service.
