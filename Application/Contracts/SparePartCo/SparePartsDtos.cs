namespace Application.Contracts.SparePartCo;

internal class SparePartsDtos
{
}


// Main Response Model
public record ComprehensiveHousingCostReport(
    DateTime FromDate,
    DateTime ToDate,
    decimal TotalCompanyCost,
    decimal TotalCompanySparePartsCost,
    decimal TotalCompanyAccessoriesCost,
    List<HousingCostDetail> Housings,
    CompanyStockDetail CompanyStock
);

// Housing Detail
public record HousingCostDetail(
    int HousingId,
    string HousingName,
    decimal TotalHousingCost,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    List<VehicleSparePartUsage> VehicleUsages,
    List<RiderAccessoryUsage> RiderUsages
);

// Vehicle Spare Part Usage
public record VehicleSparePartUsage(
    string VehicleNumber,
    string VehiclePlate,
    string VehicleLocation,
    List<SparePartUsageItem> SparePartsUsed,
    decimal TotalVehicleCost
);

// Spare Part Usage Item
public record SparePartUsageItem(
    int UsageId,
    string SparePartName,
    int QuantityUsed,
    decimal UnitPrice,
    decimal TotalCost,
    DateTime UsedAt
);

// Rider Accessory Usage
public record RiderAccessoryUsage(
    int RiderId,
    string RiderWorkingId,
    string RiderNameEN,
    string RiderNameAR,
    long RiderIqamaNo,
    List<AccessoryUsageItem> AccessoriesUsed,
    decimal TotalRiderCost
);

// Accessory Usage Item
public record AccessoryUsageItem(
    int UsageId,
    string AccessoryName,
    decimal Price,
    DateTime IssuedAt
);

// Company Stock Detail
public record CompanyStockDetail(
    decimal TotalCost,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    List<VehicleSparePartUsage> VehicleUsages,
    List<RiderAccessoryUsage> RiderUsages
);
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
    DateTime UsedAt,
    decimal? Cost
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