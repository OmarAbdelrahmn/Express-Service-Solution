using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.Employees;

public record EmpolyeeResponse(
    int IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    int SponsorNo,
    string Sponsor,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA,
    DateTime CreatedAt
    );
public record EmpolyeeRequest(
    int IqamaNo,
    DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string PassportNo,
    DateOnly? PassportEnd,
    string Sponsor,
    int SponsorNo,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly? DateOfBirth,
    string Status,
    string IBAN,
    bool INKSA
    );
public record UEmpolyeeRequest(
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
    bool? INKSA
    );
public record RiderResponse(
    int IqamaNo,
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
    DateTime CreatedAt,
    string HousingAddress,
    int? WorkingId, 
    int EmployeeIqamaNo,
    string? TshirtSize, 
    string? LicenseNumber, 
    string? CompanyName 
    );

