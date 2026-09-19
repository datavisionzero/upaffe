namespace Upaffe.Application.Ports;

public sealed record RenderedEmail(
    string Recipient, string Subject, string TextBody, string HtmlBody, string MessageId);

public sealed record EmailSendResult(bool AcceptedBySmtp, bool Transient, string? FailureCode)
{
    public static EmailSendResult Accepted() => new(true, false, null);
    public static EmailSendResult Failed(string code, bool transient) => new(false, transient, code);
}

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(SmtpConnectionSnapshot settings,
        RenderedEmail message, CancellationToken cancellationToken);
}
