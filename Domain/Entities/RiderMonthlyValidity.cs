using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class RiderMonthlyValidity
{
    public int Id { get; set; }

    /// <summary>Year of the target month (e.g. 2025)</summary>
    public int Year { get; set; }

    /// <summary>Month of the target month (1-12)</summary>
    public int Month { get; set; }

    /// <summary>FK – links to Employees.IqamaNo</summary>
    public long EmployeeIqamaNo { get; set; }


    /// <summary>Whether the rider passed all validation rules for this month</summary>
    public ValidityStatus Status { get; set; }

    /// <summary>Snapshot: total accepted orders in that month</summary>
    public int TotalOrders { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    // ── Navigation ──────────────────────────────────────────────────────────
    public Employees Employee { get; set; } = default!;
}

public enum ValidityStatus
{
    Valid = 1,
    Invalid = 2,
    Freelancer = 3,
}