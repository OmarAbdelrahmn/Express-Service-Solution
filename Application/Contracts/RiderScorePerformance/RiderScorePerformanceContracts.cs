namespace Application.Contracts.RiderScorePerformance;

public record CreateRiderScorePerformanceRequest(
    string RiderWorkingId,
    DateOnly PerformanceDate,
    int TotalVerificationRequests,
    int SuccessfulVerificationRequests,
    decimal VerificationSuccessRate,
    int GrossOrders,
    int CompletedOrders,
    int CompletedOrdersInTime,
    int FailedOrdersByRider,
    decimal OnTimeDeliveryScore,
    decimal FinalDeliveryQualityScore,
    string Segment);

public record UpdateRiderScorePerformanceRequest(
    string? RiderWorkingId,
    DateOnly? PerformanceDate,
    int? TotalVerificationRequests,
    int? SuccessfulVerificationRequests,
    decimal? VerificationSuccessRate,
    int? GrossOrders,
    int? CompletedOrders,
    int? CompletedOrdersInTime,
    int? FailedOrdersByRider,
    decimal? OnTimeDeliveryScore,
    decimal? FinalDeliveryQualityScore,
    string? Segment);

public record RiderScorePerformanceFilter(
    int? RiderId,
    string? WorkingId,
    DateOnly? Date,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Segment);

public record RiderScorePerformanceResponse(
    int Id,
    int RiderId,
    string WorkingId,
    string SourceRiderId,
    string RiderName,
    string? SubstituteRiderName,
    DateOnly PerformanceDate,
    int TotalVerificationRequests,
    int SuccessfulVerificationRequests,
    decimal VerificationSuccessRate,
    int GrossOrders,
    int CompletedOrders,
    int CompletedOrdersInTime,
    int FailedOrdersByRider,
    decimal OnTimeDeliveryScore,
    decimal FinalDeliveryQualityScore,
    string Segment,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record RiderScorePerformanceTotals(
    int RecordCount,
    int RiderCount,
    int TotalVerificationRequests,
    int SuccessfulVerificationRequests,
    decimal VerificationSuccessRate,
    int GrossOrders,
    int CompletedOrders,
    int CompletedOrdersInTime,
    int FailedOrdersByRider,
    decimal AverageOnTimeDeliveryScore,
    decimal AverageFinalDeliveryQualityScore);

public record RiderScorePerformanceDay(
    DateOnly PerformanceDate,
    IReadOnlyList<RiderScorePerformanceResponse> Records,
    RiderScorePerformanceTotals Totals);

public record RiderScorePerformanceListResponse(
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<RiderScorePerformanceDay> Days,
    RiderScorePerformanceTotals Totals);

public record RiderScorePerformanceImportError(
    int RowNumber,
    string RiderWorkingId,
    string Message);

public record RiderScorePerformanceImportResult(
    int TotalRecords,
    int SuccessCount,
    int ErrorCount,
    IReadOnlyList<RiderScorePerformanceImportError> Errors);
