using Domain.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Application.Contracts.Employees;

public record VehicleRequest
(
        string VehicleType,
        string VehicleNumber,
        int SerialNumber,
        string PlateNumberA,
        int OwnerId,
        string OwnerName,
        string PlateNumberE,
        int ManufactureYear,
        string Manufacturer,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1,
        DateTime CreatedAt,
        string Location
    );
public record UVehicleRequest
(
        string VehicleType,
        int SerialNumber,
        string PlateNumberA,
        int OwnerId,
        string OwnerName,
        string PlateNumberE,
        int ManufactureYear,
        string Manufacturer,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1,
        string Location
    );
public record VehicleResponse
(
        string VehicleType,
        string VehicleNumber,
        int SerialNumber,
        string PlateNumberA,
        int OwnerId,
        string OwnerName,
        string PlateNumberE,
        int ManufactureYear,
        string Manufacturer,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1,
        DateTime CreatedAt,
        string Location
    );

public class VehicleStatusGroupDto
{
    public string Status { get; set; }
    public int Count { get; set; }
    public List<VehicleWithRiderDto> Vehicles { get; set; } = [];
}
public class GroupedVehicleStatusResponse
{
    public int TotalVehicles { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<VehicleStatusGroupDto> Groups { get; set; } = [];

    public VehicleStatusSummary Summary { get; set; }
}
public class VehicleStatusSummary
{
    public int AvailableCount { get; set; }
    public int TakenCount { get; set; }
    public int ProblemCount { get; set; }
    public int StolenCount { get; set; }
    public int BreakUpCount { get; set; }
}
public class RiderInfoDto
{
    public int EmployeeIqamaNo { get; set; }
    public string RiderName { get; set; }
    public string RiderNameE { get; set; }
    public DateTime TakenDate { get; set; }
    public string TakenReason { get; set; }
}
public class UnavailableVehiclesResponse
{
    public int TotalCount { get; set; }
    public int TakenCount { get; set; }
    public int AvailableCount { get; set; }
    public int ProblemCount { get; set; }
    public int StolenCount { get; set; }     // NEW
    public int BreakUpCount { get; set; }    // NEW
    public string Filter { get; set; }
    public IEnumerable<UnavailableVehicleDto> Vehicles { get; set; }
}
public class VehicleWithRiderDto
{
    public string VehicleNumber { get; set; }
    public string VehicleType { get; set; }
    public int SerialNumber { get; set; }
    public DateOnly LicenseExpiryDate { get; set; }
    public string PlateNumberA { get; set; } = string.Empty;
    public string PlateNumberE { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int ManufactureYear { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty; // ADDED
    public RiderInfoDto? CurrentRider { get; set; }
    public bool IsAvailable { get; set; }
    public bool HasActiveProblem { get; set; }
    public bool IsStolen { get; set; }
    public bool IsBreakUp { get; set; }
    public int ActiveProblemsCount { get; set; }
    public string CurrentStatus { get; set; }
    public DateTime? StatusSince { get; set; }
}
public class UnavailableVehicleDto
{
    public string VehicleNumber { get; set; }
    public string VehicleType { get; set; }
    public int SerialNumber { get; set; }
    public string PlateNumberA { get; set; } = string.Empty;
    public string PlateNumberE { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; }
    public int ManufactureYear { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty; // ADDED
    public DateOnly LicenseExpiryDate { get; set; }
    public string StatusType { get; set; }
    public int? RiderIqamaNo { get; set; }
    public string RiderName { get; set; }
    public string RiderNameE { get; set; }
    public string Reason { get; set; }
    public DateTime Since { get; set; }
    public int ProblemsCount { get; set; }
}
public class VehicleHistoryDto
{
    public int Id { get; set; }
    public string VehicleNumber { get; set; }
    public int SerialNumber { get; set; }
    public string PlateNumberA { get; set; } = string.Empty;
    public string PlateNumberE { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int ManufactureYear { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty; // ADDED
    public int? EmployeeIqamaNo { get; set; }
    public string RiderName { get; set; }
    public string RiderNameE { get; set; }
    public VehicleStatusType StatusType { get; set; }
    public string StatusTypeDisplay { get; set; }
    public string Reason { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsActive { get; set; }
}