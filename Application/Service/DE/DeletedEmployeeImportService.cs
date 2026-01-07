// Application/Service/Empolyee/DeletedEmployeeImportService.cs
using Application.Abstraction;
using Domain;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Service.DE;

public class DeletedEmployeeImportService(ApplicationDbcontext context) : IDeletedEmployeeImportService
{
    private readonly ApplicationDbcontext _context = context;
    private readonly Random _random = new();

    public async Task<Result<ImportResult>> RestoreSingleEmployeeAsync(
        long iqamaNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existingEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.IqamaNo == iqamaNo, cancellationToken);

            if (existingEmployee != null && !existingEmployee.IsDeleted)
            {
                return Result.Failure<ImportResult>(
                    new Error("AlreadyExists",
                        $"Employee with Iqama {iqamaNo} already exists", 400));
            }

            var deletedEmployee = await _context.DeletedEmployees
                .FirstOrDefaultAsync(d => d.IqamaNo == iqamaNo, cancellationToken);

            if (deletedEmployee == null)
            {
                return Result.Failure<ImportResult>(
                    new Error("NotFound",
                        $"No deleted employee record found for Iqama {iqamaNo}", 404));
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                Employees employee;
                if (existingEmployee != null)
                {
                    employee = existingEmployee;
                    UpdateEmployeeFromDeleted(employee, deletedEmployee);
                    employee.IsDeleted = true;
                    employee.DeletedAt = DateTime.UtcNow.AddHours(3);
                }
                else
                {
                    // Create new employee
                    employee = CreateEmployeeFromDeleted(deletedEmployee);
                    await _context.Employees.AddAsync(employee, cancellationToken);
                }

                // Restore rider details if exists
                RiderImportData? riderData = null;
                if (!string.IsNullOrEmpty(deletedEmployee.WorkingId))
                {
                    riderData = await RestoreRiderDetailsAsync(
                        deletedEmployee,
                        employee,
                        cancellationToken);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var result = new ImportResult(
                    Success: true,
                    IqamaNo: iqamaNo,
                    Message: "Employee restored successfully",
                    EmployeeData: new EmployeeImportData(
                        employee.IqamaNo,
                        employee.NameAR,
                        employee.NameEN,
                        employee.IsEmployee
                    ),
                    RiderData: riderData
                );

                return Result.Success(result);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<ImportResult>(
                new Error("RestoreError",
                    $"Failed to restore employee: {ex.Message}", 500));
        }
    }

    public async Task<Result<BulkImportResult>> RestoreAllDeletedEmployeesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedEmployees = await _context.DeletedEmployees
                .OrderBy(d => d.IqamaNo)
                .ToListAsync(cancellationToken);

            var results = new List<ImportResult>();
            int successCount = 0;
            int failedCount = 0;
            int skippedCount = 0;

            foreach (var deletedEmployee in deletedEmployees)
            {
                var restoreResult = await RestoreSingleEmployeeAsync(
                    deletedEmployee.IqamaNo,
                    cancellationToken);

                if (restoreResult.IsSuccess)
                {
                    successCount++;
                    results.Add(restoreResult.Value);
                }
                else
                {
                    if (restoreResult.Error.Code == "AlreadyExists")
                    {
                        skippedCount++;
                        results.Add(new ImportResult(
                            Success: false,
                            IqamaNo: deletedEmployee.IqamaNo,
                            Message: $"Skipped: {restoreResult.Error.Description}",
                            EmployeeData: null,
                            RiderData: null
                        ));
                    }
                    else
                    {
                        failedCount++;
                        results.Add(new ImportResult(
                            Success: false,
                            IqamaNo: deletedEmployee.IqamaNo,
                            Message: $"Failed: {restoreResult.Error.Description}",
                            EmployeeData: null,
                            RiderData: null
                        ));
                    }
                }
            }

            var bulkResult = new BulkImportResult(
                TotalRecords: deletedEmployees.Count,
                SuccessfulImports: successCount,
                FailedImports: failedCount,
                SkippedRecords: skippedCount,
                Results: results
            );

            return Result.Success(bulkResult);
        }
        catch (Exception ex)
        {
            return Result.Failure<BulkImportResult>(
                new Error("BulkRestoreError",
                    $"Failed to restore employees: {ex.Message}", 500));
        }
    }

    public async Task<Result<List<DeletedEmployeeSummary>>> GetDeletedEmployeesPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedEmployees = await _context.DeletedEmployees
                .OrderBy(d => d.IqamaNo)
                .ToListAsync(cancellationToken);

            var summaries = deletedEmployees.Select(d => new DeletedEmployeeSummary(
                IqamaNo: d.IqamaNo,
                NameAR: d.NameAR ?? "N/A",
                NameEN: d.NameEN ?? "N/A",
                JobTitle: d.JobTitle ?? "N/A",
                Status: d.AcountStatus ?? "N/A",
                DeletedAt: d.CreatedAt,
                HasRiderData: !string.IsNullOrEmpty(d.WorkingId) && d.CompanyId.HasValue
            )).ToList();

            return Result.Success(summaries);
        }
        catch (Exception ex)
        {
            return Result.Failure<List<DeletedEmployeeSummary>>(
                new Error("PreviewError",
                    $"Failed to get preview: {ex.Message}", 500));
        }
    }

    #region Private Helper Methods

    private Employees CreateEmployeeFromDeleted(DeletedEmployees deleted)
    {
        return new Employees
        {
            IqamaNo = deleted.IqamaNo,
            IqamaEndM = deleted.IqamaEndM != default
                ? deleted.IqamaEndM
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1).AddHours(3)),
            IqamaEndH = deleted.IqamaEndH != default
                ? deleted.IqamaEndH
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1).AddHours(3)),
            PassportNo = !string.IsNullOrWhiteSpace(deleted.PassportNo)
                ? deleted.PassportNo
                : $"PASS{deleted.IqamaNo}",
            PassportEnd = deleted.PassportEnd ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5).AddHours(3)),
            Sponsor = !string.IsNullOrWhiteSpace(deleted.Sponsor)
                ? deleted.Sponsor
                : "الخدمة السريعة",
            sponsorNo = deleted.IqamaNo,
            JobTitle = !string.IsNullOrWhiteSpace(deleted.JobTitle)
                ? deleted.JobTitle
                : "Employee",
            NameAR = !string.IsNullOrWhiteSpace(deleted.NameAR)
                ? deleted.NameAR
                : $"موظف {deleted.IqamaNo}",
            NameEN = !string.IsNullOrWhiteSpace(deleted.NameEN)
                ? deleted.NameEN
                : $"Employee {deleted.IqamaNo}",
            Country = !string.IsNullOrWhiteSpace(deleted.Country)
                ? deleted.Country
                : "Saudi Arabia",
            Phone = !string.IsNullOrWhiteSpace(deleted.Phone)
                ? deleted.Phone
                : $"05{_random.Next(10000000, 99999999)}",
            DateOfBirth = deleted.DateOfBirth != default
                ? DateOnly.FromDateTime(deleted.DateOfBirth)
                : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30).AddHours(3)),
            Status = !string.IsNullOrWhiteSpace(deleted.AcountStatus)
                ? deleted.AcountStatus.ToLower()
                : "enable",
            IBAN = deleted.IBAN,
            CreatedAt = deleted.CreatedAt != default
                ? deleted.CreatedAt
                : DateTime.UtcNow.AddHours(3),
            INKSA = deleted.INKSA,
            IsEmployee = false,
            IsDeleted = true,
            HousingId = null,
            DeletedAt = DateTime.UtcNow.AddHours(3)
        };
    }

    private void UpdateEmployeeFromDeleted(Employees employee, DeletedEmployees deleted)
    {
        employee.IqamaEndM = deleted.IqamaEndM != default
            ? deleted.IqamaEndM
            : employee.IqamaEndM;
        employee.IqamaEndH = deleted.IqamaEndH != default
            ? deleted.IqamaEndH
            : employee.IqamaEndH;
        employee.PassportNo = !string.IsNullOrWhiteSpace(deleted.PassportNo)
            ? deleted.PassportNo
            : employee.PassportNo;
        employee.PassportEnd = deleted.PassportEnd ?? employee.PassportEnd;
        employee.Sponsor = !string.IsNullOrWhiteSpace(deleted.Sponsor)
            ? deleted.Sponsor
            : employee.Sponsor;
        employee.JobTitle = !string.IsNullOrWhiteSpace(deleted.JobTitle)
            ? deleted.JobTitle
            : employee.JobTitle;
        employee.NameAR = !string.IsNullOrWhiteSpace(deleted.NameAR)
            ? deleted.NameAR
            : employee.NameAR;
        employee.NameEN = !string.IsNullOrWhiteSpace(deleted.NameEN)
            ? deleted.NameEN
            : employee.NameEN;
        employee.Country = !string.IsNullOrWhiteSpace(deleted.Country)
            ? deleted.Country
            : employee.Country;
        employee.Phone = !string.IsNullOrWhiteSpace(deleted.Phone)
            ? deleted.Phone
            : employee.Phone;
        employee.Status = !string.IsNullOrWhiteSpace(deleted.AcountStatus)
            ? deleted.AcountStatus.ToLower()
            : "enable";
        employee.IBAN = deleted.IBAN ?? employee.IBAN;
        employee.INKSA = deleted.INKSA;
        employee.HousingId = deleted.HousingId ?? employee.HousingId;
    }

    private async Task<RiderImportData?> RestoreRiderDetailsAsync(
        DeletedEmployees deleted,
        Employees employee,
        CancellationToken cancellationToken)
    {
        var existingRider = await _context.RiderDetails
            .FirstOrDefaultAsync(r => r.EmployeeIqamaNo == deleted.IqamaNo, cancellationToken);

        if (existingRider != null)
        {
            // Update existing rider
            existingRider.WorkingId = GenerateWorkingId(deleted.WorkingId);
            existingRider.CompanyId = 1;
            existingRider.TshirtSize = deleted.TshirtSize ?? "M";
            existingRider.LicenseNumber = deleted.LicenseNumber ?? $"LIC{deleted.IqamaNo}";

            return new RiderImportData(
                existingRider.Id,
                existingRider.WorkingId ?? string.Empty,
                existingRider.CompanyId
            );
        }

        // Verify company exists
        //var companyExists = await _context.Set<Company>()
        //    .AnyAsync(c => c.Id == deleted.CompanyId, cancellationToken);

        //if (!companyExists)
        //{
        //    return null; // Skip rider creation if company doesn't exist
        //}

        // Create new rider
        var rider = new RiderDetails
        {
            EmployeeIqamaNo = deleted.IqamaNo,
            WorkingId = GenerateWorkingId(deleted.WorkingId),
            CompanyId = 1,
            TshirtSize = !string.IsNullOrWhiteSpace(deleted.TshirtSize)
                ? deleted.TshirtSize
                : "M",
            LicenseNumber = !string.IsNullOrWhiteSpace(deleted.LicenseNumber)
                ? deleted.LicenseNumber
                : $"LIC{deleted.IqamaNo}",
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _context.RiderDetails.AddAsync(rider, cancellationToken);

        // Update employee to indicate it's also a rider
        employee.IsEmployee = false;

        return new RiderImportData(
            rider.Id,
            rider.WorkingId ?? string.Empty,
            rider.CompanyId
        );
    }

    private string GenerateWorkingId(string? existingWorkingId)
    {
        if (!string.IsNullOrWhiteSpace(existingWorkingId) &&
            existingWorkingId.ToUpper() != "N/A")
        {
            return existingWorkingId;
        }

        var timestamp = DateTime.UtcNow.AddHours(3).ToString("yyyyMMdd");
        var random = _random.Next(1000, 99999999);
        return $"WID-{timestamp}-{random}";
    }

    #endregion
}