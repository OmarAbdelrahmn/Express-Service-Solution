namespace Domain.Entities;

public class EmployeeOrder
{
    public int Id { get; set; }

    public long EmployeeIqamaNo { get; set; }
    public Employees Employee { get; set; } = default!;

    public bool Order { get; set; } // true = active/on order, false = off

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public DateOnly OrderDate { get; set; } // The date this order belongs to

    public int CompanyId { get; set; } = 4;
    public Company Company { get; set; } = default!;

    public string RequestedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? Notes { get; set; }
}