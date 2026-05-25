using Application.Abstraction;
using Application.Contracts.SparePartCo;

namespace Application.Service.SparePart;

public interface ISparePartService
{
    Task<Result<ComprehensiveHousingCostReport>> GetAllHousingsCostReportAsync(
    DateTime fromDate,
    DateTime toDate);
    Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync();
    Task<Result<IEnumerable<SparePartResponse>>> GetAllAsync2();
    Task<Result<SparePartResponse>> GetByIdAsync(int id);
    Task<Result<SparePartResponse>> CreateAsync(SparePartRequest request);
    Task<Result<SparePartResponse>> UpdateAsync(int id, SparePartRequest request);
    Task<Result> DeleteAsync(int id);
    Task<Result<IEnumerable<SparePartResponse>>> SearchAsync(string keyword);
    Task<Result<SparePartResponse>> RecordUsageAsync(int sparePartId, SparePartUsageRequest request);
    Task<Result<IEnumerable<SparePartUsageResponse>>> GetUsageHistoryAsync(int sparePartId);
    Task<Result<IEnumerable<SparePartUsageResponse>>> GetVehicleUsageHistoryAsync(string vehicleNumber);

    Task<Result<BatchUsageResponse>> RecordBatchSparePartUsageAsync(DateTime Date, BatchSparePartUsageRequest request);


    // <summary>
    /// Get cost summary for company main stock "الشركة"
    /// </summary>
    Task<Result<HousingDetailedCostResponse>> GetCompanyStockCostAsync(
        DateTime fromDate,
        DateTime toDate);

    /// <summary>
    /// Compare costs across all housings
    /// </summary>
    Task<Result<HousingCostComparisonResponse>> CompareHousingCostsAsync(
        DateTime fromDate,
        DateTime toDate);

    Task<Result<HousingDetailedCostResponse>> GetHousingDetailedCostAsync(
       string housingName,
       DateTime fromDate,
       DateTime toDate);

    Task<Result<AllHousingsCostSummaryResponse>> GetAllHousingsCostSummaryAsync(
DateTime fromDate,
DateTime toDate);

    public record HousingDetailedCostResponse(
    int? HousingId,
    string HousingName,
    DateTime FromDate,
    DateTime ToDate,
    decimal TotalSparePartsCost,
    decimal TotalAccessoriesCost,
    decimal GrandTotal,
    List<VehicleSparePartCostDetail> VehicleCosts,
    List<RiderAccessoryCostDetail> RiderCosts,
    HousingCostStatistics Statistics
);

    public record VehicleSparePartCostDetail(
        string VehicleNumber,
        string VehiclePlate,
        string Location,
        decimal TotalCost,
        int UsageCount,
        List<SparePartUsageDetail> UsageDetails
    );

    public record RiderAccessoryCostDetail(
        int RiderId,
        long RiderIqamaNo,
        string RiderNameEN,
        string RiderNameAR,
        string WorkingId,
        string HousingName,
        decimal TotalCost,
        int AccessoryCount,
        List<AccessoryUsageDetail> UsageDetails
    );

    public record HousingCostComparisonResponse(
    DateTime FromDate,
    DateTime ToDate,
    List<HousingComparisonItem> Comparisons,
    ComparisonInsights Insights
);

    public record HousingComparisonItem(
        string HousingName,
        decimal SparePartsCost,
        decimal AccessoriesCost,
        decimal TotalCost,
        decimal CostPerVehicle,
        decimal CostPerRider,
        int Rank
    );

    public record ComparisonInsights(
        string HighestCostHousing,
        decimal HighestCost,
        string LowestCostHousing,
        decimal LowestCost,
        decimal AverageCostPerHousing,
        decimal TotalCostAllHousings,
        string MostEfficientHousing // Lowest cost per vehicle+rider
    );



    public record SparePartUsageDetail(
        string SparePartName,
        int QuantityUsed,
        decimal UnitPrice,
        decimal TotalCost,
        DateTime UsedAt
    );

    public record AccessoryUsageDetail(
        string AccessoryName,
        decimal Price,
        DateTime IssuedAt
    );

    public record HousingCostStatistics(
        int TotalVehicles,
        int TotalRiders,
        int TotalSparePartUsages,
        int TotalAccessoryUsages,
        decimal AverageCostPerVehicle,
        decimal AverageCostPerRider,
        string TopCostVehicle,
        string TopCostRider
    );

    public record AllHousingsCostSummaryResponse(
    DateTime FromDate,
    DateTime ToDate,
    decimal GrandTotalSparePartsCost,
    decimal GrandTotalAccessoriesCost,
    decimal GrandTotalCost,
    List<HousingCostSummaryItem> HousingCosts,
    CompanyStockCostSummary CompanyStock
);
    public record HousingCostSummaryItem(
    int? HousingId,
    string HousingName,
    decimal SparePartsCost,
    decimal AccessoriesCost,
    decimal TotalCost,
    int VehicleCount,
    int RiderCount
);

    public record CompanyStockCostSummary(
        decimal SparePartsCost,
        decimal AccessoriesCost,
        decimal TotalCost,
        int TotalVehiclesServiced,
        int TotalRidersServiced
    );
}