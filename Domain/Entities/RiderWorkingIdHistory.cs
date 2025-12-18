using System;

namespace Domain.Entities;

public class RiderWorkingIdHistory
{
    public int Id { get; set; }
    public long RiderIqamaNo { get; set; }
    public string WorkingId { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }

    public Employees Employee { get; set; } = default!;
    public Company Company { get; set; } = default!;
}