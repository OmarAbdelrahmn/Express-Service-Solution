using System.ComponentModel.DataAnnotations;

namespace Application.Service.Notifications;

public class NotificationOptions
{
    public bool Enabled { get; set; }
    [Required] public string ProjectId { get; set; } = "express-service-fcbe5";
    public string SenderId { get; set; } = "647652633668";
    public string ChannelId { get; set; } = "rider_updates";
    public string? DataProtectionKeyPath { get; set; }
    public string? DataProtectionCertificateThumbprint { get; set; }
    public bool DailyReminderEnabled { get; set; }
    public bool RejectionAlertsEnabled { get; set; } = true;
    public bool RankChangeAlertsEnabled { get; set; } = true;
    public bool OnTimeDeliveryAlertsEnabled { get; set; } = true;
    public string DailyReminderCron { get; set; } = "0 12 * * *";
    [Range(30, 3650)] public int HistoryRetentionDays { get; set; } = 90;
    public List<NotificationStaffGrant> StaffGrants { get; set; } = [];
}

public class NotificationStaffGrant
{
    public string UserId { get; set; } = "";
    // Null explicitly grants global scope. Never populated from an HTTP body.
    public int? CompanyId { get; set; }
    public bool Send { get; set; }
    public bool Broadcast { get; set; }
}
