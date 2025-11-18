using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Application.Contracts.Employees;

public record VehicleRequest
(
        string VehicleType,
        string VehicleNumber,
        string LicenseNumber,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1
    );
public record UVehicleRequest
(
        string VehicleType,
        string LicenseNumber,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1
    );
public record VehicleResponse
(
        int Id,
        string VehicleType,
        string VehicleNumber,
        string LicenseNumber,
        DateOnly LicenseExpiryDate,
        string? VehicleImagePath,
        string? LicenseImagePath,
        string? ExstraImage,
        string? ExstraImage1,
        DateTime CreatedAt
    );

