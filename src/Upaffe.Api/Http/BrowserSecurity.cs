using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

/// <summary>The session cookie shape selected by the request's own scheme.</summary>
public sealed record BrowserCookie(string Name, bool Secure)
{
    public const string SecureName = "__Host-upaffe_session";
    public const string PlainName = "upaffe_session";

    public static BrowserCookie For(HttpRequest request) =>
        request.IsHttps ? new(SecureName, true) : new(PlainName, false);

    public CookieOptions Options(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = Secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    public static void Forget(HttpResponse response)
    {
        foreach (var name in new[] { SecureName, PlainName })
        {
            response.Cookies.Delete(
                name,
                new BrowserCookie(name, name == SecureName).Options(DateTimeOffset.UnixEpoch));
        }
    }
}

/// <summary>A bounded rolling window over failed account and source attempts.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public const int AccountLimit = 5;
    public const int AddressLimit = 20;
    private const int MaximumKeys = 4096;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts =
        new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public bool IsBlocked(string account, string source)
    {
        lock (_gate)
        {
            return Count("account:" + account) >= AccountLimit
                || Count("source:" + source) >= AddressLimit;
        }
    }

    public void Failed(string account, string source)
    {
        lock (_gate)
        {
            Add("account:" + account);
            Add("source:" + source);
            Trim();
        }
    }

    public void Succeeded(string account)
    {
        lock (_gate)
        {
            _attempts.TryRemove("account:" + account, out _);
        }
    }

    private int Count(string key)
    {
        if (!_attempts.TryGetValue(key, out var window))
        {
            return 0;
        }

        Prune(window);
        return window.Count;
    }

    private void Add(string key)
    {
        var window = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        Prune(window);
        window.Enqueue(clock.GetUtcNow());
    }

    private void Prune(Queue<DateTimeOffset> window)
    {
        var floor = clock.GetUtcNow() - Window;
        while (window.TryPeek(out var attempt) && attempt <= floor)
        {
            window.Dequeue();
        }
    }

    private void Trim()
    {
        if (_attempts.Count <= MaximumKeys)
        {
            return;
        }

        foreach (var pair in _attempts.Where(entry =>
        {
            Prune(entry.Value);
            return entry.Value.Count == 0;
        }).ToList())
        {
            _attempts.TryRemove(pair.Key, out _);
        }

        var excess = _attempts.Count - MaximumKeys;
        if (excess <= 0)
        {
            return;
        }

        foreach (var pair in _attempts
            .OrderBy(entry => entry.Value.TryPeek(out var attempt) ? attempt : DateTimeOffset.MinValue)
            .Take(excess))
        {
            _attempts.TryRemove(pair.Key, out _);
        }
    }
}

public static class CsrfProtection
{
    public const string Header = "X-Upaffe-CSRF";

    public static bool IsSafe(HttpRequest request)
    {
        if (request.Headers[Header].ToString() != "1"
            || !Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin))
        {
            return false;
        }

        return request.Host.HasValue
            && string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && origin.AbsolutePath == "/"
            && origin.Query.Length == 0
            && origin.Fragment.Length == 0
            && string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Requires same-instance intent for every cookie-authenticated write.</summary>
public sealed class BrowserCsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isSafeMethod = context.Request.Method is "GET" or "HEAD" or "OPTIONS";
        var isAnonymous = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var isBrowser = context.Features.Get<AdmittedBrowser>() is not null;

        if (!isSafeMethod && !isAnonymous && isBrowser && !CsrfProtection.IsSafe(context.Request))
        {
            throw Refusal.Forbidden();
        }

        await next(context);
    }
}
