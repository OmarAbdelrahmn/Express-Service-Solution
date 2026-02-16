using Application.Abstraction;

namespace Application.Service.temp;

public interface ITemp
{
    Task<Result<BulkUploadResult>> UploadEmployeeExcelAsync(Stream excelStream, string uploadedBy);
    Task<Result<IEnumerable<TempEmployeeUpdateResponse>>> GetPendingUpdatesAsync();
    Task<Result<BulkResolutionResponse>> ResolveUpdatesAsync(BulkResolutionRequest request);
}
