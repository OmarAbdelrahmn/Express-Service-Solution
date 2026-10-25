using Domain.Auditing;

namespace Domain.Entities.Notifications;

[AuditIgnore]
public class RiderPushDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstallationId { get; set; }
    public int RiderId { get; set; }
    public Guid SessionId { get; set; }
    public RiderMobileSession Session { get; set; } = default!;
    public string ProtectedDestination { get; set; } = "";
    public string DestinationHash { get; set; } = "";
    public string Platform { get; set; } = "android";
    public string Permission { get; set; } = "authorized";
    public string? AppVersion { get; set; }
    public bool IsActive { get; set; }
    public long Generation { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

// This row is also the transactional outbox: business code stages it on the
// same DbContext as its changes, and a worker expands it after the commit.
[AuditIgnore]
public class NotificationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActorId { get; set; }
    public int? CompanyId { get; set; }
    public int? TargetRiderId { get; set; }
    public string? IdempotencyScope { get; set; }
    public string? EventKey { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ContentJson { get; set; } = "";
    public string DataJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int UpperRiderId { get; set; }
    public int LastRiderId { get; set; }
    public bool ExpansionComplete { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

[AuditIgnore]
public class NotificationRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public NotificationRequest Request { get; set; } = default!;
    public int RiderId { get; set; }
    public bool NoDevice { get; set; }
}

[AuditIgnore]
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientId { get; set; }
    public NotificationRecipient Recipient { get; set; } = default!;
    public Guid DeviceId { get; set; }
    public RiderPushDevice Device { get; set; } = default!;
    public long DeviceGeneration { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
