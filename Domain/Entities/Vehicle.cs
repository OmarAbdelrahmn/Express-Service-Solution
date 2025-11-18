using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Domain.Entities;

public class Vehicle
{
    public int Id { get; set; }
    [Length(1,20)]
    public string VehicleType { get; set; } = string.Empty;
    [Length(1, 20)]
    public string VehicleNumber { get; set; } = string.Empty;
    [Length(1, 20)]
    public string LicenseNumber { get; set; } = string.Empty;
    public DateOnly LicenseExpiryDate { get; set; }
    public string? VehicleImagePath { get; set; } 
    public string? LicenseImagePath { get; set; } 
    public string? ExstraImage { get; set; } 
    public string? ExstraImage1 { get; set; } 
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public RiderDetails? RiderDetails { get; set; }
}
