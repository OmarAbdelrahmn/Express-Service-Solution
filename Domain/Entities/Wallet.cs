namespace Domain.Entities;

public class Wallet
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// Set only when a substitution exists.
    /// This is the RiderDetails.Id whose WorkingId appeared in the Excel file
    /// but who did NOT actually work (the "original" slot holder).
    /// </summary>
    public int? MainRiderId { get; set; }
    public RiderDetails? MainRider { get; set; }

    /// <summary>
    /// The rider who actually worked.
    /// - No substitution → same rider as the Excel WorkingId.
    /// - Substitution     → the substitute rider.
    /// </summary>
    public int WorkedRiderId { get; set; }
    public RiderDetails WorkedRider { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public DateTime? UpdatedAt { get; set; }
}