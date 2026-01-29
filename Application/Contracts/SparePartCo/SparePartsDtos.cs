using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Contracts.SparePartCo;

internal class SparePartsDtos
{
}

public record BatchSparePartUsageRequest(
    List<SparePartUsageItemRequest> Usages
);

public record SparePartUsageItemRequest(
    int SparePartId,
    string VehicleNumber,
    int QuantityUsed
);

public record BatchRiderAccessoryUsageRequest(
    List<RiderAccessoryUsageItemRequest> Usages
);

public record RiderAccessoryUsageItemRequest(
    int AccessoryId,
    int RiderId
);

public record BatchUsageResponse(
    int TotalProcessed,
    int SuccessCount,
    int FailureCount,
    List<UsageResultDetail> Details
);

public record UsageResultDetail(
    bool Success,
    string ItemName,
    string? TargetIdentifier, // VehicleNumber or RiderName
    string? Message
);

public record VehicleCostResponse(
    string VehicleNumber,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal TotalCost,
    List<CostItemDetail> SparePartDetails,
    List<CostItemDetail> AccessoryDetails
);

public record RiderCostResponse(
    int RiderId,
    string RiderNameEN,
    string RiderNameAR,
    decimal TotalAccessoriesCost,
    List<CostItemDetail> AccessoryDetails
);

public record CostItemDetail(
    string ItemName,
    int QuantityUsed,
    decimal UnitPrice,
    decimal TotalCost,
    DateTime UsedAt
);

public record CostSummaryResponse(
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal GrandTotal,
    DateTime FromDate,
    DateTime ToDate
);
public record SparePartRequest(
    string Name,
    int Quantity,
    decimal Price,
    string Location
);

public record SparePartResponse(
    int Id,
    string Name,
    int Quantity,
    decimal Price,
    string Location,
    DateTime CreatedAt
);

public record SparePartUsageRequest(
    string VehicleNumber,
    int QuantityUsed

);

public record SparePartUsageResponse(
    int Id,
    int SparePartId,
    string SparePartName,
    string VehicleNumber,
    int QuantityUsed,
    DateTime UsedAt
);


public record ReturnRequest(
    int SupplierId,
    string? ReturnNumber,
    string Reason,
    string ProcessedBy,
    string? Notes,
    List<ReturnItemRequest> Items
);

public record ReturnItemRequest(
    int ItemId,
    string ItemName,
    int ItemType, // 1=SparePart, 2=Accessory
    int Quantity,
    decimal UnitPrice
);

public record ReturnResponse(
    int Id,
    int SupplierId,
    string SupplierName,
    string? ReturnNumber,
    DateTime ReturnDate,
    decimal TotalAmount,
    string Reason,
    string ProcessedBy,
    string? Notes,
    List<ReturnItemResponse> Items
);

public record ReturnItemResponse(
    int Id,
    string ItemName,
    string ItemType,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal
);