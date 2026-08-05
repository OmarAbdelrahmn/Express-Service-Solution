namespace Application.Contracts.rider;

public record RiderRequest
(
    long IqamaNo,
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
    bool IsEmployee,
    string? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    bool? IsFreelancer            // ← add
    );
public record URiderRequest
(
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
    bool? INKSA,
    string? WorkingId,
    long? EmployeeIqamaNo,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    int? HousingId,
    bool? IsFreelancer            // ← add
    );

public record EMTOR
(
    string? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    string? VehicleNumber,
    bool? IsFreelancer            // ← add
    );