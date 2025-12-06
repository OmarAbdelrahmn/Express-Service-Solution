using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.rider;

public record RiderRequest
(
    long IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    string Sponsor,
    int SponsorNo,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA,
    string WorkingId,
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
    int? SponsorNo,
    string? JobTitle,
    string? NameAR,
    string? NameEN,
    string? Country,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Status,
    string? IBAN,
    bool? INKSA,
    string? WorkingId,
    long? EmployeeIqamaNo,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName
    );

public record EMTOR
(
    string? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    string? VehicleNumber
    );