using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using static Application.Service.ImportService;

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
