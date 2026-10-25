using Application.Service.Notifications;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Accounting.Tests;

public class FirebaseNotificationSenderTests
{
    private static PushMessage Message(DateTime eventTimeUtc) => new("test-fid", "Title", "Body",
        new() { ["notificationId"] = "test-notification" }, TimeSpan.FromHours(1), eventTimeUtc);

    [Fact]
    public void AndroidPayload_SerializesExplicitUtcEventTimeWithInstalledSdk()
    {
        using var sender = new FirebaseNotificationSender(Options.Create(new NotificationOptions { Enabled = true }));
        var eventTimeUtc = new DateTime(2026, 10, 25, 7, 23, 0, DateTimeKind.Utc);
        var payload = sender.BuildMessage(Message(eventTimeUtc));

        Assert.Equal(eventTimeUtc, payload.Android.Notification.EventTimestamp);
        Assert.Equal(DateTimeKind.Utc, payload.Android.Notification.EventTimestamp.Kind);
        using var json = JsonDocument.Parse(JsonConvert.SerializeObject(payload));
        var eventTime = json.RootElement.GetProperty("android").GetProperty("notification")
            .GetProperty("event_time").GetString()!;
        Assert.EndsWith("Z", eventTime);
        Assert.Equal(new DateTimeOffset(eventTimeUtc), DateTimeOffset.Parse(eventTime, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Sender_RejectsInvalidEventTimeBeforeCallingFirebase(DateTimeKind kind)
    {
        using var sender = new FirebaseNotificationSender(Options.Create(new NotificationOptions { Enabled = true }));
        var eventTime = kind == DateTimeKind.Utc ? DateTime.SpecifyKind(DateTime.MinValue, kind)
            : new DateTime(2026, 10, 25, 7, 23, 0, kind);

        var result = await sender.SendAsync(Message(eventTime), default);

        Assert.Equal("invalid_event_time", result.ErrorCode);
        Assert.Null(result.MessageId);
        Assert.False(result.Retryable);
    }
}
