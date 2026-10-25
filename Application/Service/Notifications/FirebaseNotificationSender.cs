using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using System.Net;

namespace Application.Service.Notifications;

public record PushMessage(string Fid, string Title, string Body, Dictionary<string, string> Data,
    TimeSpan RemainingLifetime, DateTime EventTimestampUtc);
public record PushResult(string? MessageId = null, string? ErrorCode = null, bool Retryable = false,
    bool Unregistered = false, bool ConfigurationError = false, TimeSpan? RetryAfter = null);

public interface INotificationSender
{
    Task<PushResult> SendAsync(PushMessage message, CancellationToken ct);
    Task<bool> IsReadyAsync(CancellationToken ct);
}

public sealed class FirebaseNotificationSender(IOptions<NotificationOptions> options) : INotificationSender, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private FirebaseApp? app;

    private async Task<FirebaseMessaging> ClientAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (app is null)
            {
                var credential = await GoogleCredential.GetApplicationDefaultAsync(ct);
                if (credential.UnderlyingCredential is ServiceAccountCredential account &&
                    account.ProjectId != options.Value.ProjectId)
                    throw new InvalidOperationException("Firebase credential project mismatch.");
                app = FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = options.Value.ProjectId },
                    "express-notifications-" + Guid.NewGuid().ToString("N"));
            }
            return FirebaseMessaging.GetMessaging(app);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return false;
        try { await ClientAsync(ct); return true; }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    public async Task<PushResult> SendAsync(PushMessage message, CancellationToken ct)
    {
        if (!options.Value.Enabled) return new(ErrorCode: "provider_disabled", ConfigurationError: true, Retryable: true);
        if (message.RemainingLifetime <= TimeSpan.Zero) return new(ErrorCode: "expired");
        if (message.EventTimestampUtc == DateTime.MinValue || message.EventTimestampUtc.Kind != DateTimeKind.Utc)
            return new(ErrorCode: "invalid_event_time");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var client = await ClientAsync(timeout.Token);
            var id = await client.SendAsync(BuildMessage(message), timeout.Token);
            return new(MessageId: id);
        }
        catch (FirebaseMessagingException ex)
        {
            var code = ex.MessagingErrorCode;
            var status = ex.HttpResponse?.StatusCode;
            var configuration = code is MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.ThirdPartyAuthError ||
                status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            var retry = configuration || code is MessagingErrorCode.Unavailable or MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded ||
                status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable || (int?)status >= 500;
            var header = ex.HttpResponse?.Headers.RetryAfter;
            var after = header?.Delta ?? (header?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            return new(ErrorCode: configuration ? "provider_configuration" : code?.ToString() ?? "provider_error",
                Retryable: retry, Unregistered: code == MessagingErrorCode.Unregistered,
                ConfigurationError: configuration, RetryAfter: after);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var transient = ex is HttpRequestException or OperationCanceledException;
            return new(ErrorCode: transient ? "provider_unavailable" : "provider_configuration", Retryable: true,
                ConfigurationError: !transient);
        }
    }

    internal Message BuildMessage(PushMessage message) => new()
    {
        Fid = message.Fid,
        Notification = new Notification { Title = message.Title, Body = message.Body },
        Data = message.Data,
        Android = new AndroidConfig
        {
            Priority = Priority.Normal, TimeToLive = message.RemainingLifetime,
            Notification = new AndroidNotification
            {
                ChannelId = options.Value.ChannelId, Sound = "default", Tag = message.Data["notificationId"],
                // The SDK serializes an unassigned DateTime as year 0001 in event_time.
                EventTimestamp = message.EventTimestampUtc
            }
        }
    };

    public void Dispose() { app?.Delete(); gate.Dispose(); }
}
