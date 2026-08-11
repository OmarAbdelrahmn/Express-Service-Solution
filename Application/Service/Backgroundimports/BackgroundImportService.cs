using Application.Service.Import;
using Domain.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using static Application.Service.Import.ImportService;

namespace Application.Service.Backgroundimports;

public class BackgroundImportService : IBackgroundImportService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<BackgroundImportService> _logger;

    // Static dictionaries to persist across requests
    private static readonly ConcurrentDictionary<string, ImportJobStatus> _jobs = new();
    private static readonly ConcurrentDictionary<string, string> _resultPaths = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCancellations = new();

    public BackgroundImportService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<BackgroundImportService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<string> StartRiderVerificationAsync(IFormFile file, string uploadedBy)
    {
        var jobId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        _logger.LogInformation("Starting verification job {JobId} for file {FileName} ({FileSize} bytes)",
            jobId, file.FileName, file.Length);

        var jobFolder = Path.Combine(Path.GetTempPath(), "RiderVerification", jobId);
        Directory.CreateDirectory(jobFolder);

        var inputPath = Path.Combine(jobFolder, "input.xlsx");
        var resultPath = Path.Combine(jobFolder, "result.json");

        try
        {
            using (var stream = new FileStream(inputPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            _logger.LogInformation("File saved to: {InputPath}", inputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file for job {JobId}", jobId);
            throw;
        }

        _resultPaths[jobId] = resultPath;

        _jobs[jobId] = new ImportJobStatus
        {
            JobId = jobId,
            Status = "Initializing",
            StartTime = DateTime.UtcNow.AddHours(3),
            TotalRows = 0,
            ProcessedRows = 0,
            Progress = 0
        };

        _activeCancellations[jobId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Background processing started for job {JobId}", jobId);

                _jobs[jobId] = _jobs[jobId] with { Status = "Processing" };

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    SetAuditContext(scope.ServiceProvider, jobId, uploadedBy, "RiderVerification");
                    var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                    await ProcessVerificationJob(
                        jobId,
                        inputPath,
                        resultPath,
                        uploadedBy,
                        importService,
                        cts.Token);
                }

                _logger.LogInformation("Background processing completed for job {JobId}", jobId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} was cancelled", jobId);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Cancelled",
                    ErrorMessage = "Job was cancelled",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed with error: {Error}", jobId, ex.Message);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = $"{ex.GetType().Name}: {ex.Message}",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            finally
            {
                _activeCancellations.TryRemove(jobId, out _);

                if (File.Exists(inputPath))
                {
                    try
                    {
                        File.Delete(inputPath);
                        _logger.LogInformation("Input file deleted: {InputPath}", inputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete input file: {InputPath}", inputPath);
                    }
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromHours(2));
                    _jobs.TryRemove(jobId, out _);
                    _resultPaths.TryRemove(jobId, out _);

                    try
                    {
                        if (Directory.Exists(jobFolder))
                        {
                            Directory.Delete(jobFolder, true);
                            _logger.LogInformation("Job {JobId} folder cleaned up after 2 hours", jobId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete job folder: {JobFolder}", jobFolder);
                    }
                });
            }
        }, cts.Token);

        return jobId;
    }

    private async Task ProcessVerificationJob(
        string jobId,
        string inputPath,
        string resultPath,
        string uploadedBy,
        IImportService importService,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing verification job {JobId} from file {FilePath}", jobId, inputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
        _logger.LogInformation("File stream opened, length: {Length} bytes", stream.Length);

        var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(inputPath))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        int lastReportedProgress = 0;
        object lockObject = new object();

        void ProgressCallback(int processed, int total)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (lockObject)
            {
                if (processed == 0 ||
                    processed - lastReportedProgress >= 500 ||
                    processed == total)
                {
                    var progress = total > 0 ? (int)((processed / (double)total) * 100) : 0;

                    _jobs[jobId] = _jobs[jobId] with
                    {
                        TotalRows = total,
                        ProcessedRows = processed,
                        Progress = progress
                    };

                    lastReportedProgress = processed;

                    if (processed % 5000 == 0 || processed == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Job {JobId} progress: {Processed}/{Total} ({Progress}%)",
                            jobId, processed, total, progress);
                    }
                }
            }
        }

        try
        {
            _logger.LogInformation("Calling VerifyRidersFromExcelAsync for job {JobId}", jobId);

            var result = await importService.VerifyRidersFromExcelAsync(
                formFile,
                uploadedBy,
                ProgressCallback);

            _logger.LogInformation(
                "VerifyRidersFromExcelAsync completed for job {JobId}. Success: {IsSuccess}",
                jobId, result.IsSuccess);

            if (result.IsSuccess)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
                };

                var jsonString = JsonSerializer.Serialize(result.Value, jsonOptions);
                await File.WriteAllTextAsync(resultPath, jsonString);

                _logger.LogInformation(
                    "Result saved to file: {ResultPath} ({Size} bytes)",
                    resultPath,
                    new FileInfo(resultPath).Length);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Completed",
                    EndTime = DateTime.UtcNow.AddHours(3),
                    Progress = 100,
                    ProcessedRows = result.Value.TotalRecordsProcessed,
                    TotalRows = result.Value.TotalRecordsProcessed
                };
            }
            else
            {
                _logger.LogError(
                    "Job {JobId} failed: {ErrorCode} - {ErrorMessage}",
                    jobId, result.Error.Code, result.Error.Description);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = result.Error.Description,
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessVerificationJob for {JobId}", jobId);
            throw;
        }
    }

    public ImportJobStatus? GetJobStatus(string jobId)
    {
        _jobs.TryGetValue(jobId, out var status);

        if (status != null)
        {
            _logger.LogDebug("Status retrieved for job {JobId}: {Status}", jobId, status.Status);
        }
        else
        {
            _logger.LogWarning("Job {JobId} not found in status dictionary", jobId);
        }

        return status;
    }

    public RiderVerificationResponse? GetJobResult(string jobId)
    {
        try
        {
            if (!_resultPaths.TryGetValue(jobId, out var resultPath))
            {
                _logger.LogWarning("Result path not found for job {JobId}", jobId);
                return null;
            }

            if (!File.Exists(resultPath))
            {
                _logger.LogWarning("Result file does not exist: {ResultPath}", resultPath);
                return null;
            }

            _logger.LogInformation("Reading result from: {ResultPath}", resultPath);

            var jsonString = File.ReadAllText(resultPath);
            var result = JsonSerializer.Deserialize<RiderVerificationResponse>(jsonString);

            _logger.LogInformation("Result deserialized successfully for job {JobId}", jobId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve result for job {JobId}", jobId);
            return null;
        }
    }

    public bool CancelJob(string jobId)
    {
        if (_activeCancellations.TryGetValue(jobId, out var cts))
        {
            _logger.LogInformation("Cancelling job {JobId}", jobId);
            cts.Cancel();
            return true;
        }
        return false;
    }

    // ================================
    // NEW: WorkingId Sync Background Job
    // ================================

    public async Task<string> StartWorkingIdSyncAsync(IFormFile file, string uploadedBy)
    {
        var jobId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        _logger.LogInformation("Starting WorkingId sync job {JobId} for file {FileName} ({FileSize} bytes)",
            jobId, file.FileName, file.Length);

        var jobFolder = Path.Combine(Path.GetTempPath(), "WorkingIdSync", jobId);
        Directory.CreateDirectory(jobFolder);

        var inputPath = Path.Combine(jobFolder, "input.xlsx");
        var resultPath = Path.Combine(jobFolder, "result.json");

        try
        {
            using (var stream = new FileStream(inputPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            _logger.LogInformation("File saved to: {InputPath}", inputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file for job {JobId}", jobId);
            throw;
        }

        _resultPaths[jobId] = resultPath;

        _jobs[jobId] = new ImportJobStatus
        {
            JobId = jobId,
            Status = "Initializing",
            StartTime = DateTime.UtcNow.AddHours(3),
            TotalRows = 0,
            ProcessedRows = 0,
            Progress = 0
        };

        _activeCancellations[jobId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Background WorkingId sync started for job {JobId}", jobId);

                _jobs[jobId] = _jobs[jobId] with { Status = "Processing" };

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    SetAuditContext(scope.ServiceProvider, jobId, uploadedBy, "WorkingIdSync");
                    var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                    await ProcessWorkingIdSyncJob(
                        jobId,
                        inputPath,
                        resultPath,
                        uploadedBy,
                        importService,
                        cts.Token);
                }

                _logger.LogInformation("Background WorkingId sync completed for job {JobId}", jobId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} was cancelled", jobId);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Cancelled",
                    ErrorMessage = "Job was cancelled",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed with error: {Error}", jobId, ex.Message);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = $"{ex.GetType().Name}: {ex.Message}",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            finally
            {
                _activeCancellations.TryRemove(jobId, out _);

                if (File.Exists(inputPath))
                {
                    try
                    {
                        File.Delete(inputPath);
                        _logger.LogInformation("Input file deleted: {InputPath}", inputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete input file: {InputPath}", inputPath);
                    }
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromHours(2));
                    _jobs.TryRemove(jobId, out _);
                    _resultPaths.TryRemove(jobId, out _);

                    try
                    {
                        if (Directory.Exists(jobFolder))
                        {
                            Directory.Delete(jobFolder, true);
                            _logger.LogInformation("Job {JobId} folder cleaned up after 2 hours", jobId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete job folder: {JobFolder}", jobFolder);
                    }
                });
            }
        }, cts.Token);

        return jobId;
    }

    private async Task ProcessWorkingIdSyncJob(
        string jobId,
        string inputPath,
        string resultPath,
        string uploadedBy,
        IImportService importService,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing WorkingId sync job {JobId} from file {FilePath}", jobId, inputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
        _logger.LogInformation("File stream opened, length: {Length} bytes", stream.Length);

        var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(inputPath))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        int lastReportedProgress = 0;
        object lockObject = new object();

        void ProgressCallback(int processed, int total)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (lockObject)
            {
                if (processed == 0 ||
                    processed - lastReportedProgress >= 500 ||
                    processed == total)
                {
                    var progress = total > 0 ? (int)((processed / (double)total) * 100) : 0;

                    _jobs[jobId] = _jobs[jobId] with
                    {
                        TotalRows = total,
                        ProcessedRows = processed,
                        Progress = progress
                    };

                    lastReportedProgress = processed;

                    if (processed % 5000 == 0 || processed == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Job {JobId} progress: {Processed}/{Total} ({Progress}%)",
                            jobId, processed, total, progress);
                    }
                }
            }
        }

        try
        {
            _logger.LogInformation("Calling SyncWorkingIdsFromExcelAsync for job {JobId}", jobId);

            var result = await importService.SyncWorkingIdsFromExcelAsync(
                formFile,
                uploadedBy,
                ProgressCallback);

            _logger.LogInformation(
                "SyncWorkingIdsFromExcelAsync completed for job {JobId}. Success: {IsSuccess}",
                jobId, result.IsSuccess);

            if (result.IsSuccess)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
                };

                var jsonString = JsonSerializer.Serialize(result.Value, jsonOptions);
                await File.WriteAllTextAsync(resultPath, jsonString);

                _logger.LogInformation(
                    "Result saved to file: {ResultPath} ({Size} bytes)",
                    resultPath,
                    new FileInfo(resultPath).Length);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Completed",
                    EndTime = DateTime.UtcNow.AddHours(3),
                    Progress = 100,
                    ProcessedRows = result.Value.TotalRecordsProcessed,
                    TotalRows = result.Value.TotalRecordsProcessed
                };
            }
            else
            {
                _logger.LogError(
                    "Job {JobId} failed: {ErrorCode} - {ErrorMessage}",
                    jobId, result.Error.Code, result.Error.Description);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = result.Error.Description,
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessWorkingIdSyncJob for {JobId}", jobId);
            throw;
        }
    }

    public WorkingIdSyncResponse? GetWorkingIdSyncResult(string jobId)
    {
        try
        {
            if (!_resultPaths.TryGetValue(jobId, out var resultPath))
            {
                _logger.LogWarning("Result path not found for job {JobId}", jobId);
                return null;
            }

            if (!File.Exists(resultPath))
            {
                _logger.LogWarning("Result file does not exist: {ResultPath}", resultPath);
                return null;
            }

            _logger.LogInformation("Reading WorkingId sync result from: {ResultPath}", resultPath);

            var jsonString = File.ReadAllText(resultPath);
            var result = JsonSerializer.Deserialize<WorkingIdSyncResponse>(jsonString);

            _logger.LogInformation("WorkingId sync result deserialized successfully for job {JobId}", jobId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve WorkingId sync result for job {JobId}", jobId);
            return null;
        }
    }


    public async Task<string> StartRiderShiftImportAsync(IFormFile file, string uploadedBy)
    {
        var jobId = Guid.NewGuid().ToString();
        var cts = new CancellationTokenSource();

        _logger.LogInformation("Starting RiderShift import job {JobId} for file {FileName} ({FileSize} bytes)",
            jobId, file.FileName, file.Length);

        var jobFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            "JobResults",
            "RiderVerification",
            jobId
        ); Directory.CreateDirectory(jobFolder);



        var inputPath = Path.Combine(jobFolder, "input.xlsx");
        var resultPath = Path.Combine(jobFolder, "result.json");

        try
        {
            using (var stream = new FileStream(inputPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            _logger.LogInformation("File saved to: {InputPath}", inputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file for job {JobId}", jobId);
            throw;
        }

        _resultPaths[jobId] = resultPath;

        _jobs[jobId] = new ImportJobStatus
        {
            JobId = jobId,
            Status = "Initializing",
            StartTime = DateTime.UtcNow.AddHours(3),
            TotalRows = 0,
            ProcessedRows = 0,
            Progress = 0
        };

        _activeCancellations[jobId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {

                _logger.LogInformation("Background RiderShift import started for job {JobId}", jobId);

                _jobs[jobId] = _jobs[jobId] with { Status = "Processing" };

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    SetAuditContext(scope.ServiceProvider, jobId, uploadedBy, "RiderShiftImport");
                    var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                    await ProcessRiderShiftImportJob(
                        jobId,
                        inputPath,
                        resultPath,
                        uploadedBy,
                        importService,
                        cts.Token);
                }

                _logger.LogInformation("Background RiderShift import completed for job {JobId}", jobId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job {JobId} was cancelled", jobId);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Cancelled",
                    ErrorMessage = "Job was cancelled",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed with error: {Error}", jobId, ex.Message);
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = $"{ex.GetType().Name}: {ex.Message}",
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            finally
            {
                _activeCancellations.TryRemove(jobId, out _);

                if (File.Exists(inputPath))
                {
                    try
                    {
                        File.Delete(inputPath);
                        _logger.LogInformation("Input file deleted: {InputPath}", inputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete input file: {InputPath}", inputPath);
                    }
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromHours(24));  // ⬅️ CHANGE HERE
                    _jobs.TryRemove(jobId, out _);
                    _resultPaths.TryRemove(jobId, out _);

                    try
                    {
                        if (Directory.Exists(jobFolder))
                        {
                            Directory.Delete(jobFolder, true);
                            _logger.LogInformation("Job {JobId} folder cleaned up after 2 hours", jobId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete job folder: {JobFolder}", jobFolder);
                    }
                });
            }
        }, cts.Token);

        return jobId;
    }

    private async Task ProcessRiderShiftImportJob(
        string jobId,
        string inputPath,
        string resultPath,
        string uploadedBy,
        IImportService importService,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing RiderShift import job {JobId} from file {FilePath}", jobId, inputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
        _logger.LogInformation("File stream opened, length: {Length} bytes", stream.Length);

        var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(inputPath))
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        int lastReportedProgress = 0;
        object lockObject = new object();

        void ProgressCallback(int processed, int total)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (lockObject)
            {
                if (processed == 0 ||
                    processed - lastReportedProgress >= 1000 ||
                    processed == total)
                {
                    var progress = total > 0 ? (int)((processed / (double)total) * 100) : 0;

                    _jobs[jobId] = _jobs[jobId] with
                    {
                        TotalRows = total,
                        ProcessedRows = processed,
                        Progress = progress
                    };

                    lastReportedProgress = processed;

                    if (processed % 10000 == 0 || processed == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Job {JobId} progress: {Processed}/{Total} ({Progress}%)",
                            jobId, processed, total, progress);
                    }
                }
            }
        }

        try
        {
            _logger.LogInformation("Calling BulkImportRiderShiftsAsync for job {JobId}", jobId);

            var result = await importService.BulkImportRiderShiftsAsync(
                formFile,
                uploadedBy,
                ProgressCallback);

            _logger.LogInformation(
                "BulkImportRiderShiftsAsync completed for job {JobId}. Success: {IsSuccess}",
                jobId, result.IsSuccess);

            if (result.IsSuccess)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
                };

                var jsonString = JsonSerializer.Serialize(result.Value, jsonOptions);
                await File.WriteAllTextAsync(resultPath, jsonString);

                _logger.LogInformation(
                    "Result saved to file: {ResultPath} ({Size} bytes)",
                    resultPath,
                    new FileInfo(resultPath).Length);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Completed",
                    EndTime = DateTime.UtcNow.AddHours(3),
                    Progress = 100,
                    ProcessedRows = result.Value.TotalRecordsProcessed,
                    TotalRows = result.Value.TotalRecordsProcessed
                };
            }
            else
            {
                _logger.LogError(
                    "Job {JobId} failed: {ErrorCode} - {ErrorMessage}",
                    jobId, result.Error.Code, result.Error.Description);

                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = result.Error.Description,
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessRiderShiftImportJob for {JobId}", jobId);
            throw;
        }
    }

    public RiderShiftBulkImportResponse? GetRiderShiftImportResult(string jobId)
    {
        try
        {
            if (!_resultPaths.TryGetValue(jobId, out var resultPath))
            {
                _logger.LogWarning("Result path not found for job {JobId}", jobId);
                return null;
            }

            if (!File.Exists(resultPath))
            {
                _logger.LogWarning("Result file does not exist: {ResultPath}", resultPath);
                return null;
            }

            _logger.LogInformation("Reading RiderShift import result from: {ResultPath}", resultPath);

            var jsonString = File.ReadAllText(resultPath);
            var result = JsonSerializer.Deserialize<RiderShiftBulkImportResponse>(jsonString);

            _logger.LogInformation("RiderShift import result deserialized successfully for job {JobId}", jobId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve RiderShift import result for job {JobId}", jobId);
            return null;
        }
    }

    private static void SetAuditContext(IServiceProvider serviceProvider, string jobId, string uploadedBy, string operationName)
    {
        var operationId = Guid.TryParse(jobId, out var parsed) ? parsed : Guid.NewGuid();
        serviceProvider.GetRequiredService<IAuditContextAccessor>().Set(new AuditContext(
            operationId,
            AuditActorType.BackgroundJob,
            uploadedBy,
            uploadedBy,
            "BackgroundImport",
            operationName,
            jobId));
    }


}

public record ImportJobStatus
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
    public int Progress { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan? ElapsedTime => EndTime.HasValue
        ? EndTime.Value - StartTime
        : DateTime.UtcNow.AddHours(3) - StartTime;
}

// Add to Application/Service/IImportService.cs

