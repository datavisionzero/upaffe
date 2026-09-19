using System.Net;
using System.Text;
using System.Security.Cryptography;
using Upaffe.Application.Ports;

namespace Upaffe.Application.Notifications;

public sealed record IncidentEmailFacts(
    Guid IncidentId, string Kind, string ProjectKey, string ProjectName,
    string MonitorKey, string MonitorName, string MonitorType,
    string StableReason, DateTimeOffset OccurredAt, string Recipient);

public static class IncidentEmailRenderer
{
    public static RenderedEmail Render(IncidentEmailFacts facts, string? publicBaseUrl)
    {
        if (facts.Kind is not ("alert" or "recovery"))
            throw new ArgumentException("Unknown notification kind.", nameof(facts));
        var kind = facts.Kind == "alert" ? "Alert" : "Recovery";
        var reason = SafeReason(facts.StableReason);
        var subject = $"upaffe {kind.ToLowerInvariant()}: {facts.ProjectKey}/{facts.MonitorKey} ({reason})";
        var relative = $"/projects/{Uri.EscapeDataString(facts.ProjectKey)}/{Uri.EscapeDataString(facts.MonitorType)}-monitors/{Uri.EscapeDataString(facts.MonitorKey)}?incident={facts.IncidentId:D}";
        var link = publicBaseUrl is null ? relative : publicBaseUrl.TrimEnd('/') + relative;
        var when = facts.OccurredAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        var text = $"{kind}: {facts.ProjectName} / {facts.MonitorName}\nReason: {reason}\nTime: {when}\nDetails: {link}\n";
        var html = new StringBuilder()
            .Append("<p><strong>").Append(kind).Append(":</strong> ")
            .Append(WebUtility.HtmlEncode(facts.ProjectName)).Append(" / ")
            .Append(WebUtility.HtmlEncode(facts.MonitorName)).Append("</p><p>Reason: ")
            .Append(WebUtility.HtmlEncode(reason)).Append("<br>Time: ")
            .Append(WebUtility.HtmlEncode(when)).Append("</p><p><a href=\"")
            .Append(WebUtility.HtmlEncode(link)).Append("\">Open incident details</a></p>")
            .ToString();
        var recipientHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(facts.Recipient.ToLowerInvariant())))[..16].ToLowerInvariant();
        var messageId = $"{facts.IncidentId:N}.{facts.Kind}.{recipientHash}@upaffe.invalid";
        return new(facts.Recipient, subject, text, html, messageId);
    }

    public static RenderedEmail Test(string recipient, DateTimeOffset now)
    {
        var when = now.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        return new(recipient, "upaffe SMTP test", $"upaffe SMTP test accepted for sending at {when}.\n",
            $"<p>upaffe SMTP test accepted for sending at {WebUtility.HtmlEncode(when)}.</p>",
            $"test.{Guid.CreateVersion7():N}@upaffe.invalid");
    }

    private static string SafeReason(string value) => value is
        "target_invalid" or "target_not_allowed" or "dns_failed" or
        "response_too_large" or "response_encoding_invalid" or "timeout" or
        "response_headers_too_large" or "tls_failed" or "connection_failed" or
        "too_many_redirects" or "redirect_invalid" or "redirect_downgrade" or
        "unexpected_status" or "required_text_missing" or "forbidden_text_present" or
        "reported_failure" or "report_missing" or "recovered" ? value : "monitor_failure";
}
