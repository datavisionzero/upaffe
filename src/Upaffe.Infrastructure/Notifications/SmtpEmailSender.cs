using System.IO;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Upaffe.Application.Ports;

namespace Upaffe.Infrastructure.Notifications;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly TimeSpan timeout;

    public SmtpEmailSender() : this(TimeSpan.FromSeconds(30)) { }

    public SmtpEmailSender(TimeSpan timeout) => this.timeout = timeout;

    public async Task<EmailSendResult> SendAsync(SmtpConnectionSnapshot settings,
        RenderedEmail message, CancellationToken cancellationToken)
    {
        if (settings.Host is null || settings.Port is null || settings.SenderAddress is null)
            return EmailSendResult.Failed("smtp_not_configured", false);
        if (settings.Username is not null && settings.Password is null)
            return EmailSendResult.Failed("smtp_credentials_missing", false);

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(settings.SenderName ?? string.Empty, settings.SenderAddress));
        email.To.Add(MailboxAddress.Parse(message.Recipient));
        email.Subject = message.Subject;
        email.MessageId = message.MessageId;
        email.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var smtp = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds };
        try
        {
            var security = settings.Security switch
            {
                "none" => SecureSocketOptions.None,
                "tls" => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.StartTls,
            };
            await smtp.ConnectAsync(settings.Host, settings.Port.Value, security, deadline.Token);
            if (settings.Username is not null)
                await smtp.AuthenticateAsync(settings.Username, settings.Password!, deadline.Token);
            await smtp.SendAsync(email, deadline.Token);
            try { await smtp.DisconnectAsync(true, deadline.Token); }
            catch (Exception) { /* SMTP already accepted the message. */ }
            return EmailSendResult.Accepted();
        }
        catch (SmtpCommandException ex)
        {
            return EmailSendResult.Failed("smtp_rejected", (int)ex.StatusCode is >= 400 and < 500);
        }
        catch (MailKit.Security.AuthenticationException) { return EmailSendResult.Failed("smtp_authentication_failed", false); }
        catch (System.Security.Authentication.AuthenticationException) { return EmailSendResult.Failed("smtp_tls_failed", false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return EmailSendResult.Failed("smtp_timeout", true); }
        catch (SocketException) { return EmailSendResult.Failed("smtp_connection_failed", true); }
        catch (IOException) { return EmailSendResult.Failed("smtp_connection_failed", true); }
        catch (SmtpProtocolException) { return EmailSendResult.Failed("smtp_protocol_error", true); }
        catch (ServiceNotAuthenticatedException) { return EmailSendResult.Failed("smtp_authentication_failed", false); }
    }
}
