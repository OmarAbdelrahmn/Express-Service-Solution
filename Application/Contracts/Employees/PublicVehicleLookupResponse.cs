namespace Application.Contracts.Employees;

public record PublicVehicleLookupResponse(
    int SerialNumber,
    PublicVehicleRiderResponse? CurrentRider);

public record PublicVehicleRiderResponse(
    string NameArabic,
    string NameEnglish,
    PublicVehicleHousingResponse? Housing);

public record PublicVehicleHousingResponse(
    string Name,
    string Address);
