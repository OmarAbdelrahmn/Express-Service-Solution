namespace Application.Contracts.SupplierCon;

internal class SupplierDtos
{
}

public record SupplierRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    string? CommercialRegister
);

public record SupplierResponse(
    int Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    bool IsActive,
    DateTime CreatedAt,
    int TotalBills,
    decimal TotalPurchases,
    string? CommercialRegister
);

public record SupplierListResponse(
    int Id,
    string Name,
    string? Phone,
    string? Email,
    bool IsActive,
    int TotalBills,
    decimal TotalPurchases,
    string? CommercialRegister
);