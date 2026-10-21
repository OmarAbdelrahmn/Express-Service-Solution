using Application.Abstraction;
using Application.Contracts.Employees;

namespace Application.Service.Empolyee;

public interface IVehicleDocumentService
{
    Task<Result<VehicleDocumentReport>> GetExpiringAsync(VehicleDocumentQuery query, CancellationToken ct = default);
    Task<Result<VehicleDocumentReport>> GetMissingAsync(VehicleDocumentQuery query, CancellationToken ct = default);
    Task<Result<VehicleDocumentReport>> GetRemindersAsync(VehicleDocumentQuery query, CancellationToken ct = default);
    Task<Result<VehicleResponse>> UpdateAsync(string vehicleNumber, UpdateVehicleDocumentsRequest request, CancellationToken ct = default);
    Task<Result<VehicleDocumentSheetImportReport>> ImportExpiryDatesAsync(
        Stream input, bool dryRun = false, string? worksheetName = null, CancellationToken ct = default);
}
