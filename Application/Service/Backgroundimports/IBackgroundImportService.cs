using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Backgroundimports;

public interface IBackgroundImportService
{
    Task<string> StartRiderVerificationAsync(IFormFile file, string uploadedBy);
    ImportJobStatus? GetJobStatus(string jobId);
    RiderVerificationResponse? GetJobResult(string jobId);
    bool CancelJob(string jobId);

    Task<string> StartWorkingIdSyncAsync(IFormFile file, string uploadedBy);
    WorkingIdSyncResponse? GetWorkingIdSyncResult(string jobId);
}
