using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities;

public class Vehicle
{
    public int Id { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public DateOnly LicenseExpiryDate { get; set; }
    public string VehicleImagePath { get; set; } = string.Empty;
    public string LicenseImagePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
