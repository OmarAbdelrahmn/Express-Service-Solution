namespace Domain.Entities;

public class RiderScorePerformance
{
    public int Id { get; set; }
    public int RiderId { get; set; }
    public string WorkingId { get; set; } = string.Empty;
    public string SourceRiderId { get; set; } = string.Empty;
    public string? SubstituteRiderName { get; set; }
    public DateOnly PerformanceDate { get; set; }
    public int TotalVerificationRequests { get; set; }
    public int SuccessfulVerificationRequests { get; set; }
    public decimal VerificationSuccessRate { get; set; }
    public int GrossOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CompletedOrdersInTime { get; set; }
    public int FailedOrdersByRider { get; set; }
    public decimal OnTimeDeliveryScore { get; set; }
    public decimal FinalDeliveryQualityScore { get; set; }
    public string Segment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }

    public RiderDetails Rider { get; set; } = default!;
}
