# 0004 — Bound and isolate HTTP checks

Status: accepted

HTTP monitors fetch operator-controlled URLs from a long-running process. A
monitor must therefore have predictable resource use and must not turn a
management credential into access to services inside the instance's network.
upaffe supports publicly routable HTTP and HTTPS targets only. Private and
reserved destinations have no allowlist escape hatch because monitoring them
is not required for this product.

## Configuration limits

An HTTP monitor uses `GET` without a request body and has these inclusive
configuration limits:

| Setting | Rule |
| --- | --- |
| Interval | 30 seconds to 30 days |
| Timeout | 1 to 60 seconds, and no greater than the interval |
| Failure threshold | 1 to 100 consecutive failures |
| Target URL | At most 2,048 characters; absolute `http` or `https`; no fragment or user information |
| Redirects | At most 5 followed responses |
| Decoded response body | At most 1 MiB |
| Response headers | At most 64 KiB in total |
| Custom request headers | At most 32; each value at most 4 KiB and all names and values at most 16 KiB in total |
| Text fragment | One optional required or forbidden fragment of 1 to 4,096 Unicode scalar values |

The timeout covers DNS resolution, connection establishment, redirects,
headers, body reading, decoding, and evaluation as one operation. A response
whose declared or streamed body exceeds the decoded-body limit fails as
`response_too_large`; compression cannot be used to bypass the limit. The body
is inspected in memory within that bound and is not retained in check history.
Header names must be valid HTTP field names, values cannot contain control
characters, and callers cannot set `Host`, `Content-Length`, connection-specific
headers, proxy credentials, or headers managed by the HTTP client.

## Targets, DNS, and redirects

Every initial target and redirect target is normalized and authorized before a
connection is made. Only `http` and `https` are accepted. HTTPS certificates
use the platform trust store and normal hostname validation; expired,
untrusted, or mismatched certificates fail the check. There is no option to
disable certificate validation. An HTTPS request may not redirect to HTTP. An
HTTP request may redirect to HTTPS.

A hostname is resolved for each check and again for each redirect hop. Every
address in the answer must be globally routable; a mixed public and forbidden
answer rejects the target rather than selecting the public member. Literal IP
addresses undergo the same test. The connection is pinned to one of the
validated addresses while preserving the original hostname for the HTTP
`Host` field and TLS Server Name Indication. A later check may use a changed
DNS answer after validating it again.

The forbidden set comprises every address that is not globally routable,
including unspecified, loopback, link-local, private-use, shared-address-space,
benchmarking, documentation, multicast, reserved, and limited-broadcast ranges
for IPv4 and IPv6. IPv4-mapped IPv6 addresses are evaluated as IPv4. This rule
also rejects names such as `localhost` when they resolve locally and prevents
DNS rebinding between validation and connection. New special-purpose ranges
are denied until they are deliberately classified as globally routable.

There is no private-network mode or CIDR allowlist. A self-hoster that needs to
observe a private service must expose an appropriately protected, publicly
routable health endpoint or use a later reporting monitor; HTTP monitoring does
not cross the private network boundary.

Redirect status codes 301, 302, 303, 307, and 308 may be followed within the
five-hop limit. Each hop repeats scheme, address, TLS, size, and timeout checks.
Redirect loops and a sixth hop fail the check. The effective URL may be kept as
non-secret diagnostic metadata after applying the same redaction as the
configured target.

## Headers and credentials

All configured header values are write-only secrets, irrespective of their
names. Ordinary API and CLI output, the web interface, exports, check history,
failure reasons, logs, traces, metrics, and exception text may expose header
names but never values. The target URL's query is also write-only and is
replaced in ordinary output with an indication that a query is configured.
User information in the URL is rejected rather than redacted.

Configured headers are sent only to the target's exact origin: the same scheme,
case-insensitive DNS host (or identical normalized IP address), and effective
port. Any scheme, host, or port change strips every configured header before
the redirected request. Cookies and server authentication challenges are not
persisted or replayed across checks, and URI-derived credentials are never
supported. Internal diagnostics retain stable reason codes and sanitized
origins, not raw request or response headers.

## Status and text evaluation

The expected status is one exact final HTTP status code from 100 through 599;
it defaults to 200. Redirect responses that are followed are not compared.
After transport, redirect, size, and decoding rules have succeeded, a different
final status fails as `unexpected_status`.

The optional text rule is either `required` or `forbidden`, never both. The
response is decoded strictly using a valid declared `charset`; without one, a
Unicode byte-order mark selects UTF-8, UTF-16, or UTF-32, and otherwise UTF-8 is
required. An unknown charset, a conflicting or malformed byte-order mark, or an
invalid byte sequence fails as `response_encoding_invalid`.

Text matching is a literal, ordinal, case-sensitive substring comparison over
decoded Unicode scalar values. It performs no Unicode normalization, case
folding, whitespace folding, HTML parsing, or line-ending conversion. A missing
required fragment fails as `required_text_missing`; a present forbidden
fragment fails as `forbidden_text_present`. Status is evaluated before text so
one result has one stable primary reason. Remote response content never enters
the failure reason, logs, or ordinary output.

JSON checks, certificate-expiry warnings, client certificates, request bodies,
custom methods, private-network exceptions, and insecure TLS modes are outside
this decision.
