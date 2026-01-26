using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities.Spare;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public ICollection<Bill> Bills { get; set; } = [];
}
