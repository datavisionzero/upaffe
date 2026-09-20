# 0012 — Trust forwarded identity only from named HTTPS proxies

Status: accepted

The production application receives plain HTTP from a reverse proxy on the
host's loopback port. Browser security and login throttling depend on the
original scheme, host, and client address, so those values must be forwarded
before authentication and CSRF checks. The application enables forwarded
headers only when both a public HTTPS origin and an explicit list of proxy IP
addresses are configured. It accepts at most one forwarding hop, trusts exact
IP addresses rather than networks, and permits the forwarded host only for the
configured origin. Direct local development leaves forwarded headers disabled.

The proxy replaces incoming forwarding headers with values it knows: the
client socket address, HTTPS scheme, and public host. The supported Nginx and
Caddy examples run on the same host as the Compose stack and connect to the
loopback application port. An operator must update the trusted address if Docker
recreates the edge network with a different gateway. A mismatched trust
configuration fails closed for forwarded values: the application sees the
internal HTTP request and browser origin checks do not accept the public
origin. When proxy forwarding is configured and forwarded headers arrive from
an unknown peer, the application logs its address and the setting to check, at
most once per minute. It never logs the header values or request path. Browser
write protection compares the Origin scheme as well as its authority with the
effective request.

The one-time simple push reporting path contains a credential. The Nginx
example logs a redacted path, never the original request target or query
string, and suppresses request-bearing error logs. The Caddy example discards
access logs and handles proxy failures without request-bearing error logs.
Other proxies must provide the same boundary. The saved SMTP public base URL
is set separately to the same HTTPS origin for incident links; request headers
cannot rewrite that saved value.
