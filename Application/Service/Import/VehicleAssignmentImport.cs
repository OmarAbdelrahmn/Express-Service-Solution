using Application.Abstraction;
using ClosedXML.Excel;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Application.Service.Import;

public partial class ImportService
{
    public async Task<Result<VehicleAssignmentImportResponse>> ImportVehicleAssignmentsAsync(
        IFormFile file, string uploadedBy, bool fullSnapshot = false, CancellationToken ct = default)
    {
        Result<VehicleAssignmentImportResponse> Invalid(string message) =>
            Result.Failure<VehicleAssignmentImportResponse>(new Error("InvalidAssignments", message, 400));
        if (file == null || file.Length == 0) return Invalid("The assignment file is empty.");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Invalid("Use an Excel .xlsx workbook.");

        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();
            var header = FindVehicleAssignmentHeaderRow(sheet);
            var mapping = BuildVehicleAssignmentColumnMapping(header);
            if (!mapping.IsValid) return Invalid(mapping.ErrorMessage!);
            var rows = sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber())
                .Select(r => ParseVehicleAssignmentRowData(r, mapping, r.RowNumber())).ToList();
            if (rows.Count == 0) return Invalid("No assignment rows were supplied.");
            var invalid = rows.FirstOrDefault(r => !r.IsValid);
            if (invalid != null) return Invalid($"Row {invalid.RowNumber}: {invalid.ErrorMessage}");
            if (rows.GroupBy(r => AssignmentVehicleKey(r.VehicleNumber!)).Any(g => g.Count() > 1))
                return Invalid("A vehicle occurs more than once in the assignment file.");
            if (rows.GroupBy(r => r.IqamaNo).Any(g => g.Count() > 1))
                return Invalid("A rider occurs more than once in the assignment file.");
            if (rows.Any(r => r.PermissionStartDate > r.PermissionEndDate))
                return Invalid("A permission start date is after its end date.");

            await using var transaction = _dbcontext.Database.IsRelational()
                ? await _dbcontext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var vehicles = await _dbcontext.Vehicles.ToListAsync(ct);
            var vehicleLookup = vehicles.GroupBy(v => AssignmentVehicleKey(v.VehicleNumber))
                .ToDictionary(g => g.Key, g => g.ToList());
            var iqamas = rows.Select(r => r.IqamaNo!.Value).ToList();
            var employees = await _dbcontext.Employees.Include(e => e.RiderDetails).Include(e => e.Housing)
                .Where(e => iqamas.Contains(e.IqamaNo)).ToDictionaryAsync(e => e.IqamaNo, ct);
            var assigned = await _dbcontext.RiderDetails.Where(r => r.VehicleNumber != null).ToListAsync(ct);
            var statuses = await _dbcontext.RiderVehicleStatus.Where(s => s.IsActive).ToListAsync(ct);
            var defaultCompany = await _dbcontext.Companies.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
            // Resolve and validate every row before releasing a single assignment.
            foreach (var row in rows)
            {
                if (!vehicleLookup.TryGetValue(AssignmentVehicleKey(row.VehicleNumber!), out var matches) || matches.Count != 1)
                    return Invalid($"Row {row.RowNumber}: vehicle/chassis number '{row.VehicleNumber}' was not uniquely found.");
                if (!employees.TryGetValue(row.IqamaNo!.Value, out var employee))
                    return Invalid($"Row {row.RowNumber}: employee {row.IqamaNo} was not found.");
                if (employee.IsDeleted || employee.Status != "enable")
                    return Invalid($"Row {row.RowNumber}: employee {row.IqamaNo} is disabled or deleted.");
                if (employee.RiderDetails == null && defaultCompany == null)
                    return Invalid("A company is required to create a rider record.");
            }

            var at = DateTime.UtcNow.AddHours(3); // Preserve existing operational time semantics.
            var converted = new HashSet<long>();
            foreach (var employee in employees.Values.Where(e => e.RiderDetails == null))
            {
                employee.RiderDetails = new RiderDetails { EmployeeIqamaNo = employee.IqamaNo,
                    WorkingId = $"AUTO_{employee.IqamaNo}", TshirtSize = "M", LicenseNumber = "N/A",
                    CompanyId = defaultCompany!.Id, CreatedAt = at };
                _dbcontext.RiderDetails.Add(employee.RiderDetails);
                converted.Add(employee.IqamaNo);
            }
            var targets = rows.ToDictionary(r => r.IqamaNo!.Value,
                r => vehicleLookup[AssignmentVehicleKey(r.VehicleNumber!)][0]);
            var riders = assigned.Concat(employees.Values.Select(e => e.RiderDetails!)).Distinct().ToList();
            var desired = riders.ToDictionary(r => r.EmployeeIqamaNo, r => r.VehicleNumber);
            var targetVehicles = targets.Values.Select(v => v.VehicleNumber).ToHashSet();
            foreach (var rider in riders)
            {
                if (targets.TryGetValue(rider.EmployeeIqamaNo, out var vehicle)) desired[rider.EmployeeIqamaNo] = vehicle.VehicleNumber;
                else if (fullSnapshot || (rider.VehicleNumber != null && targetVehicles.Contains(rider.VehicleNumber)))
                    desired[rider.EmployeeIqamaNo] = null;
            }
            bool IsTake(RiderVehicleStatus s) => s.StatusType is VehicleStatusType.Taken or VehicleStatusType.switched;
            var changed = riders.Where(r => r.VehicleNumber != desired[r.EmployeeIqamaNo]
                || (targets.ContainsKey(r.EmployeeIqamaNo) && !statuses.Any(s => IsTake(s)
                    && s.VehicleNumber == r.VehicleNumber && s.EmployeeIqamaNo == r.EmployeeIqamaNo))).ToList();
            var affectedVehicles = changed.Select(r => r.VehicleNumber).Where(v => v != null).Cast<string>()
                .Concat(changed.Select(r => desired[r.EmployeeIqamaNo]).Where(v => v != null).Cast<string>()).ToHashSet();
            var releases = statuses.Where(s => affectedVehicles.Contains(s.VehicleNumber) && IsTake(s) && s.EmployeeIqamaNo.HasValue)
                .Select(s => (s.VehicleNumber, Iqama: s.EmployeeIqamaNo!.Value)).ToHashSet();
            foreach (var rider in changed.Where(r => r.VehicleNumber != null)) releases.Add((rider.VehicleNumber!, rider.EmployeeIqamaNo));
            foreach (var status in statuses.Where(s => affectedVehicles.Contains(s.VehicleNumber)))
            { status.IsActive = false; status.PermissionEndDate = at; }
            foreach (var release in releases)
                _dbcontext.RiderVehicleStatus.Add(new RiderVehicleStatus { VehicleNumber = release.VehicleNumber,
                    EmployeeIqamaNo = release.Iqama, StatusType = VehicleStatusType.Returned, Timestamp = at,
                    PermissionEndDate = at, Reason = $"Assignment import release by {uploadedBy}", IsActive = false });
            var reassignments = assigned.Count(r => r.VehicleNumber != null && targetVehicles.Contains(r.VehicleNumber)
                && !targets.Any(t => t.Key == r.EmployeeIqamaNo && t.Value.VehicleNumber == r.VehicleNumber));
            // Release unique RiderDetails.VehicleNumber values before swaps. Both saves
            // belong to one transaction, so a failed take cannot leave a committed return.
            foreach (var rider in changed) rider.VehicleNumber = null;
            if (changed.Count > 0) await _dbcontext.SaveChangesAsync(ct);

            var results = new List<VehicleAssignmentRowResult>();
            foreach (var row in rows)
            {
                var employee = employees[row.IqamaNo!.Value];
                var rider = employee.RiderDetails!;
                var vehicle = targets[employee.IqamaNo];
                var permission = row.Permission;
                var end = row.PermissionEndDate;
                if (permission?.Contains("مرور") == true) { permission = "تصريح مرور"; end ??= at.AddDays(30); }
                var previousLocation = vehicle.Location;
                vehicle.Location = employee.Housing?.Name ?? "غير محدد";
                if (!string.IsNullOrWhiteSpace(row.Phone)) employee.Phone = row.Phone;
                RiderVehicleStatus status;
                if (changed.Contains(rider))
                {
                    rider.VehicleNumber = vehicle.VehicleNumber;
                    status = new RiderVehicleStatus { VehicleNumber = vehicle.VehicleNumber,
                        EmployeeIqamaNo = employee.IqamaNo, StatusType = VehicleStatusType.Taken, IsActive = true,
                        Timestamp = at, PermissionStartDate = row.PermissionStartDate ?? at,
                        PermissionEndDate = end, Permission = permission ?? "تصريح عام",
                        Reason = $"Assigned via bulk import (by {uploadedBy})" };
                    _dbcontext.RiderVehicleStatus.Add(status);
                }
                else
                {
                    status = statuses.Where(s => IsTake(s) && s.VehicleNumber == vehicle.VehicleNumber
                        && s.EmployeeIqamaNo == employee.IqamaNo).OrderByDescending(s => s.Timestamp).First();
                    // A repeated snapshot is not a new possession event.
                    if (!string.IsNullOrWhiteSpace(permission)) status.Permission = permission;
                    if (row.PermissionStartDate.HasValue) status.PermissionStartDate = row.PermissionStartDate;
                    if (end.HasValue) status.PermissionEndDate = end;
                }
                results.Add(new VehicleAssignmentRowResult(row.RowNumber, true, employee.IqamaNo.ToString(),
                    employee.NameEN, employee.NameAR, vehicle.PlateNumberA, vehicle.VehicleNumber,
                    converted.Contains(employee.IqamaNo), true, previousLocation, vehicle.Location,
                    status.Permission, status.PermissionStartDate, status.PermissionEndDate, [], null));
            }
            await _dbcontext.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Result.Success(new VehicleAssignmentImportResponse(rows.Count, rows.Count, converted.Count,
                0, 0, 0, reassignments, results, [], at));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _assignmentLogger?.LogError(ex, "Vehicle assignment import failed for {UploadedBy}", uploadedBy);
            _dbcontext.ChangeTracker.Clear();
            return Result.Failure<VehicleAssignmentImportResponse>(new Error("AssignmentImportFailed",
                "The assignment import failed; no assignments were committed.", 500));
        }
        catch (OperationCanceledException)
        {
            _dbcontext.ChangeTracker.Clear();
            throw;
        }
    }

    private static string AssignmentVehicleKey(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
