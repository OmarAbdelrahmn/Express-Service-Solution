using Domain.Entities.Spare;

namespace Application.Contracts.SupplierCon;

internal class Transferss
{
}


public record TransferRequest(
    int HousingId,
    List<TransferItemRequest> Items,
    DateTime? TransferredAt
);

public record TransferItemRequest(
    int ItemId,
    TransferItemType ItemType,
    int Quantity
);

public record TransferResponse(
    int Id,
    string FromLocation,
    string ToLocation,
    int HousingId,
    int TotalItems,
    string TransferredBy,
    DateTime TransferredAt,
    List<TransferItemResponse> Items
);

public record TransferItemResponse(
    int ItemId,
    string ItemName,
    TransferItemType ItemType,
    int Quantity
);