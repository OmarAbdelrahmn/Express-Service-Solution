using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.rider;

public record RiderRequest
(
    int IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    string Sponsor,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateTime DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA,
    string HousingAddress,
    int WorkingId,
    string TshirtSize,
    string LicenseNumber,
    string CompanyName
    );
public record URiderRequest
(
        DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string? PassportNo,
    DateOnly? PassportEnd,
    string? Sponsor,
    string? JobTitle,
    string? NameAR,
    string? NameEN,
    string? Country,
    string? Phone,
    DateTime? DateOfBirth,
    string? Status,
    string? IBAN,
    bool? INKSA,
    string? HousingAddress,
    int? WorkingId,
    int? EmployeeIqamaNo,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName
    );

public record EMTOR
(
    int? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    string? VehicleNumber
    );