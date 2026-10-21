using System.ComponentModel.DataAnnotations;

namespace Application.Service.RiderMobile;

public class RiderMobileOptions
{
    public const string SectionName = "RiderMobile";
    [Range(1, 90)] public int SessionDays { get; set; } = 30;
    [Range(0, 100)] public decimal OnTimeDeliveryTargetPercentage { get; set; } = 95m;
    [Range(0, 100)] public decimal VerificationTargetPercentage { get; set; } = 95m;
    [Range(0, 100)] public decimal DeliveryQualityTargetPercentage { get; set; } = 95m;
}
