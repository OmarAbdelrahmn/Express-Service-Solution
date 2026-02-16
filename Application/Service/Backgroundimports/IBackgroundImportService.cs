using Application.Service.Import;
using Microsoft.AspNetCore.Http;
using static Application.Service.Import.ImportService;

namespace Application.Service.Backgroundimports;

public interface IBackgroundImportService
{
    Task<string> StartRiderVerificationAsync(IFormFile file, string uploadedBy);
    ImportJobStatus? GetJobStatus(string jobId);
    RiderVerificationResponse? GetJobResult(string jobId);
    bool CancelJob(string jobId);

    Task<string> StartWorkingIdSyncAsync(IFormFile file, string uploadedBy);
    WorkingIdSyncResponse? GetWorkingIdSyncResult(string jobId);


    Task<string> StartRiderShiftImportAsync(IFormFile file, string uploadedBy);
    RiderShiftBulkImportResponse? GetRiderShiftImportResult(string jobId);

}
