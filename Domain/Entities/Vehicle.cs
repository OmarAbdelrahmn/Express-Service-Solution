using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Domain.Entities;

public class Vehicle
{
    [Length(1,20)]
    public string VehicleType { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;  
    public int SerialNumber { get; set; }
    public string PlateNumberA { get; set; } = string.Empty;
    public string PlateNumberE { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int ManufactureYear { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public DateOnly LicenseExpiryDate { get; set; }
    public string? VehicleImagePath { get; set; } 
    public string? LicenseImagePath { get; set; } 
    public string? ExstraImage { get; set; } 
    public string? ExstraImage1 { get; set; } 
    public string Location { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public RiderDetails? RiderDetails { get; set; }

    public ICollection<RiderVehicleStatus> RiderVehicleStatuses { get; set; } = new List<RiderVehicleStatus>();

}
