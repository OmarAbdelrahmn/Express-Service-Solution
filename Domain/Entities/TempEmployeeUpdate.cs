using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class TempEmployeeUpdate
{
    public int Id { get; set; }
    public long IqamaNo { get; set; }

    // Old values (from database)
    public DateOnly? OldIqamaEndM { get; set; }
    public DateOnly? OldIqamaEndH { get; set; }
    public string? OldPassportNo { get; set; }
    public DateOnly? OldPassportEnd { get; set; }
    public string? OldSponsor { get; set; }
    public long? OldSponsorNo { get; set; }
    public string? OldJobTitle { get; set; }
    public string? OldNameAR { get; set; }
    public string? OldNameEN { get; set; }
    public string? OldCountry { get; set; }
    public string? OldPhone { get; set; }
    public DateOnly? OldDateOfBirth { get; set; }
    public string? OldStatus { get; set; }
    public string? OldIBAN { get; set; }
    public bool? OldINKSA { get; set; }

    // New values (from Excel)
    public DateOnly? NewIqamaEndM { get; set; }
    public DateOnly? NewIqamaEndH { get; set; }
    public string? NewPassportNo { get; set; }
    public DateOnly? NewPassportEnd { get; set; }
    public string? NewSponsor { get; set; }
    public long? NewSponsorNo { get; set; }
    public string? NewJobTitle { get; set; }
    public string? NewNameAR { get; set; }
    public string? NewNameEN { get; set; }
    public string? NewCountry { get; set; }
    public string? NewPhone { get; set; }
    public DateOnly? NewDateOfBirth { get; set; }
    public string? NewStatus { get; set; }
    public string? NewIBAN { get; set; }
    public bool? NewINKSA { get; set; }

    public bool IsNewEmployee { get; set; } // If employee doesn't exist in DB
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow.AddHours(3);
    public string? UploadedBy { get; set; }
    public bool IsResolved { get; set; } = false;
    public string? Resolution { get; set; } // "Approved" or "Rejected"
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    public Employees? Employee { get; set; }
}
public class TempEmployeeStatusChange
{
    public int Id { get; set; }
    public long EmployeeIqamaNo { get; set; }
    public string Action { get; set; } = string.Empty; // "Enable" or "Disable"
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow.AddHours(3);

    public bool IsResolved { get; set; } = false;
    public string? Resolution { get; set; } // "Approved" or "Rejected"
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? AdminNotes { get; set; }

    public Employees Employee { get; set; } = default!;
}


public class TempVehicleOperation
{
    public int Id { get; set; }

    public long? RiderIqamaNo { get; set; }
    public RiderDetails? Rider { get; set; } = default!;

    public string VehiclePlateNumber { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public Vehicle Vehicle { get; set; } = default!;

    public VehicleStatusType VehicleStatusType { get; set; }
    public string? Reason { get; set; }

    public string? Permission { get; set; }
    public DateTime? PermissionEndDate { get; set; }

    public DateTime RequestedAt { get; set; }
    public string RequestedBy { get; set; } = string.Empty;

    public bool IsResolved { get; set; }
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? AdminNotes { get; set; }
}