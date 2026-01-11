using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.Backgroundimports;

public class BackgroundImportService : IBackgroundImportService
{
    private readonly IImportService _importService;
    private static readonly ConcurrentDictionary<string, ImportJobStatus> _jobs = new();
    private static readonly ConcurrentDictionary<string, RiderVerificationResponse> _results = new();

    public BackgroundImportService(IImportService importService)
    {
        _importService = importService;
    }

    public async Task<string> StartRiderVerificationAsync(IFormFile file, string uploadedBy)
    {
        var jobId = Guid.NewGuid().ToString();

        // Save file to temp location
        var tempPath = Path.Combine(Path.GetTempPath(), $"{jobId}.xlsx");
        using (var stream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Initialize job status
        _jobs[jobId] = new ImportJobStatus
        {
            JobId = jobId,
            Status = "Processing",
            StartTime = DateTime.UtcNow.AddHours(3),
            TotalRows = 0,
            ProcessedRows = 0,
            Progress = 0
        };

        // Start background task
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessVerificationJob(jobId, tempPath, uploadedBy);
            }
            catch (Exception ex)
            {
                _jobs[jobId] = _jobs[jobId] with
                {
                    Status = "Failed",
                    ErrorMessage = ex.Message,
                    EndTime = DateTime.UtcNow.AddHours(3)
                };
            }
            finally
            {
                // Cleanup temp file
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        });

        return jobId;
    }

    private async Task ProcessVerificationJob(string jobId, string filePath, string uploadedBy)
    {
        using var stream = new FileStream(filePath, FileMode.Open);
        var formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(filePath));

        // Create custom import service with progress callback
        var result = await _importService.VerifyRidersFromExcelAsync(
            formFile,
            uploadedBy,
            (processed, total) =>
            {
                // Update progress
                _jobs[jobId] = _jobs[jobId] with
                {
                    TotalRows = total,
                    ProcessedRows = processed,
                    Progress = (int)((processed / (double)total) * 100)
                };
            });

        if (result.IsSuccess)
        {
            _results[jobId] = result.Value;
            _jobs[jobId] = _jobs[jobId] with
            {
                Status = "Completed",
                EndTime = DateTime.UtcNow.AddHours(3),
                Progress = 100
            };
        }
        else
        {
            _jobs[jobId] = _jobs[jobId] with
            {
                Status = "Failed",
                ErrorMessage = result.Error.Description,
                EndTime = DateTime.UtcNow.AddHours(3)
            };
        }

        // Clean up old jobs (keep for 1 hour)
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromHours(1));
            _jobs.TryRemove(jobId, out _);
            _results.TryRemove(jobId, out _);
        });
    }

    public ImportJobStatus? GetJobStatus(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var status) ? status : null;
    }

    public RiderVerificationResponse? GetJobResult(string jobId)
    {
        return _results.TryGetValue(jobId, out var result) ? result : null;
    }
}

public record ImportJobStatus
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty; // Processing, Completed, Failed
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
    public int Progress { get; init; } // 0-100
    public string? ErrorMessage { get; init; }
    public TimeSpan? ElapsedTime => EndTime.HasValue
        ? EndTime.Value - StartTime
        : DateTime.UtcNow.AddHours(3) - StartTime;
}