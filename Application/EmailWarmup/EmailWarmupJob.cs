using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Application.Service.DailyReport;
using Application.EmailWarmup;

namespace Application.Service.EmailWarmup;

public class EmailWarmupJob : IEmailWarmupJob
{
    private readonly DailyReportSettings _settings;
    private static readonly Random _rng = new();

    public EmailWarmupJob(IOptions<DailyReportSettings> options)
    {
        _settings = options.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var toEmail = FakeEmailData.FakeEmails[_rng.Next(FakeEmailData.FakeEmails.Count)];
        var (subject, body) = EmailTemplates.Templates[_rng.Next(EmailTemplates.Templates.Count)];

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Headers.Add("Content-Language", "ar");
        message.Date = DateTimeOffset.Now;

        // BCC yourself — copy lands in your inbox
        // Then set a webmail filter to auto-move it to Sent folder
        message.Bcc.Add(MailboxAddress.Parse(_settings.FromEmail));

        var bodyBuilder = new BodyBuilder { TextBody = body };
        message.Body = bodyBuilder.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(
            _settings.SmtpHost,
            _settings.SmtpPort,
            _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        await smtp.AuthenticateAsync(_settings.SmtpUser, _settings.SmtpPassword, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }
}