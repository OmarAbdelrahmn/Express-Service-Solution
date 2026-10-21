using Domain.Auditing;

namespace Domain.Entities;

[AuditIgnore]
public class RiderMobileSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RiderId { get; set; }
    public string Language { get; set; } = "ar";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public RiderDetails Rider { get; set; } = default!;
}
