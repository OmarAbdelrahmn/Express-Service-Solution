using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.Employees;

public record EmpolyeeResponse(
    long IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    long sponsorNo,
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
    long IqamaNo,
    DateOnly? IqamaEndM,
    DateOnly? IqamaEndH,
    string PassportNo,
    DateOnly? PassportEnd,
    string Sponsor,
    long sponsorNo,
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
    long? sponsorNo,
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
    long IqamaNo,
    bool IsEmployee,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string PassportNo,
    DateOnly PassportEnd,
    string Sponsor,
    long sponsorNo,
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
    string? WorkingId, 
    long EmployeeIqamaNo,
    string? TshirtSize, 
    string? LicenseNumber, 
    string? CompanyName 
    );

