using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Notifications;

public record RegisterPushDeviceRequest(
    [Required, MaxLength(256)] string FirebaseInstallationId,
    [Required, MaxLength(16)] string Platform,
    [Required, MaxLength(16)] string Permission,
    [MaxLength(64)] string? AppVersion);

public record NotificationContent(string Title, string Body);
public record SendNotificationRequest(
    Dictionary<string, NotificationContent> Content,
    Dictionary<string, string> Data,
    int ExpiresInSeconds = 86400);
public record NotificationQueuedResponse(Guid RequestId, string Status = "queued");
public record NotificationStatusResponse(Guid RequestId, string Status, int AudienceRiders,
    int SkippedNoDeviceRiders, int TotalDeliveries, int PendingDeliveries, int AcceptedDeliveries,
    int FailedDeliveries, int SkippedDeliveries, int ExpiredDeliveries, DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);
