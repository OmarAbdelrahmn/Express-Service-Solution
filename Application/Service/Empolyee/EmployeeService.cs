using Application.Abstraction;
using Application.Contracts.Employees;
using Domain;
using Domain.Entities;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.Empolyee;

public class EmployeeService(ApplicationDbcontext dbcontext) : IEmployeeService
{
    private readonly ApplicationDbcontext dbcontext = dbcontext;

    public async Task<Result<IEnumerable<EmpolyeeResponse>>> Get(long IqamaNo)
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(e => e.IqamaNo.ToString().StartsWith(IqamaNo.ToString()) && e.IsEmployee)
            .Include(e => e.Housing)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<EmpolyeeResponse>>(
                new Error("No Employees Found", "no employees", 400)
            );

        var res = isexist.Select(emp => new EmpolyeeResponse(
       emp.IqamaNo,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.sponsorNo,
       emp.Sponsor,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt
            )).ToList();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(res);
    }
    public async Task<Result<EmpolyeeResponse>> Get1(long IqamaNo)
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(e => e.IqamaNo == IqamaNo)
            .Include(e => e.Housing)
            .SingleOrDefaultAsync();

        if (isexist == null)
            return Result.Failure<EmpolyeeResponse>(
                new Error("No Employees Found", "no employees", 400)
            );

        var emp = isexist; // since it's already ONE

        var res = new EmpolyeeResponse(
            emp.IqamaNo,
            emp.IqamaEndM,
            emp.IqamaEndH,
            emp.PassportNo!,
            emp.PassportEnd ?? default,
            emp.sponsorNo,
            emp.Sponsor,
            emp.JobTitle,
            emp.NameAR,
            emp.NameEN,
            emp.Country,
            emp.Phone,
            emp.DateOfBirth,
            emp.Status,
            emp.IBAN!,
            emp.INKSA,
            emp.CreatedAt
        );


        return Result.Success(res);
    }

    public async Task<Result<IEnumerable<EmpolyeeResponse>>> GetAllEmployee()
    {
        var isexist = await dbcontext
            .Employees
            .AsNoTracking()
            .Where(c => c.IsEmployee == true)
            .Include(e => e.Housing)
            .ToListAsync();

        if (isexist.Count == 0)
            return Result.Failure<IEnumerable<EmpolyeeResponse>>(
                new Error("No Employees Found", "no employees", 400)
            );

        var res = isexist.Select(emp => new EmpolyeeResponse(
       emp.IqamaNo,
       emp.IqamaEndM,
       emp.IqamaEndH,
       emp.PassportNo!,
       emp.PassportEnd ?? default,
       emp.sponsorNo,
       emp.Sponsor,
       emp.JobTitle,
       emp.NameAR,
       emp.NameEN,
       emp.Country,
       emp.Phone,
       emp.DateOfBirth,
       emp.Status,
       emp.IBAN!,
       emp.INKSA,
       emp.CreatedAt
            )).ToList();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(res);
    }

    public async Task<Result<EmpolyeeResponse>> CreateAsync(EmpolyeeRequest Request)
    {
        var isexist = dbcontext.Employees.Any(x => x.IqamaNo == Request.IqamaNo);
        if (isexist)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));


        var Em = Request.Adapt<Employees>();

        Em.IsEmployee = true;

        await dbcontext.Employees.AddAsync(Em);
        await dbcontext.SaveChangesAsync();

        var response = Em.Adapt<EmpolyeeResponse>();
        return Result.Success(response);
    }

    public async Task<Result> DeleteAsync(long IqamaNo, CancellationToken cancellationToken = default)
    {
        var isexist = await dbcontext.Employees.Where(c => c.IqamaNo == IqamaNo && c.IsEmployee).FirstOrDefaultAsync();

        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        var Done = new DeletedEmployees
        {
            IqamaNo = isexist.IqamaNo,
            IqamaEndM = isexist.IqamaEndM,
            IqamaEndH = isexist.IqamaEndH,
            PassportNo = isexist.PassportNo,
            PassportEnd = isexist.PassportEnd,
            Sponsor = isexist.Sponsor,
            JobTitle = isexist.JobTitle,
            NameAR = isexist.NameAR,
            NameEN = isexist.NameEN,
            Country = isexist.Country,
            Phone = isexist.Phone,
            DateOfBirth = isexist.DateOfBirth.ToDateTime(TimeOnly.MaxValue),
            Status = isexist.Status,
            AcountStatus = isexist.Status,
            IBAN = isexist.IBAN,
            CreatedAt = isexist.CreatedAt,
            INKSA = isexist.INKSA,
            HousingId = isexist.HousingId
        };


        await dbcontext.DeletedEmployees.AddAsync(Done, cancellationToken);

        dbcontext.Employees.Remove(isexist);

        dbcontext.SaveChanges();

        return Result.Success();
    }

    public async Task<Result<EmpolyeeResponse>> UpdateAsync(long IqamaNo, UEmpolyeeRequest request)
    {
        var employee = await dbcontext.Employees.Where(c => c.IqamaNo == IqamaNo && c.IsEmployee).Include(c => c.Housing).FirstOrDefaultAsync();

        if (employee is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));


        if (request.IqamaEndM.HasValue)
            employee.IqamaEndM = request.IqamaEndM.Value;
        if (request.IqamaEndH.HasValue)
            employee.IqamaEndH = request.IqamaEndH.Value;

        if (request.PassportNo is not null)
            employee.PassportNo = request.PassportNo;

        if (request.PassportEnd.HasValue)
            employee.PassportEnd = request.PassportEnd;

        if (request.Sponsor is not null)
            employee.Sponsor = request.Sponsor;

        if (request.sponsorNo.HasValue)
            employee.sponsorNo = request.sponsorNo.Value;

        if (request.JobTitle is not null)
            employee.JobTitle = request.JobTitle;

        if (request.NameAR is not null)
            employee.NameAR = request.NameAR;

        if (request.NameEN is not null)
            employee.NameEN = request.NameEN;

        if (request.Country is not null)
            employee.Country = request.Country;

        if (request.Phone is not null)
            employee.Phone = request.Phone;

        if (request.DateOfBirth.HasValue)
            employee.DateOfBirth = request.DateOfBirth.Value;

        if (request.Status is not null)
            employee.Status = request.Status;

        if (request.IBAN is not null)
            employee.IBAN = request.IBAN;

        if (request.INKSA is not null)
            employee.INKSA = request.INKSA.Value;


        await dbcontext.SaveChangesAsync();

        var response = MapToResponse(employee);

        return Result.Success(response);
    }

    public async Task<Result> AddEmployeeToHousing(long IqamaNo, string HousingName)
    {
        var isexist = await dbcontext
            .Employees
            .Where(e => e.IqamaNo == IqamaNo)
            .AsNoTracking()
            .SingleOrDefaultAsync();


        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        if (isexist.HousingId is not null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("AlreadyIn", "This Employee is already In housing if you want to change the housing go to change housing page", 400));


        var housingId = await dbcontext
            .Housings
            .Where(c => c.Name == HousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        if (housingId == 0)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No housing Found", "no housing found with this name", 400));


        isexist.HousingId = housingId;

        dbcontext.Update(isexist);
        await dbcontext.SaveChangesAsync();

        var response = MapToResponse(isexist);

        return Result.Success();

    }

    public async Task<Result> ChangeEmployeeToHousing(long IqamaNo, string oldHousingName, string NewHousingName)
    {
        var isexist = await dbcontext
           .Employees
           .Where(e => e.IqamaNo == IqamaNo)
           .AsNoTracking()
           .SingleOrDefaultAsync();


        if (isexist is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No Employee Found", "no employee found with this Iqama", 400));

        if (isexist.HousingId is null)
            return Result.Failure<EmpolyeeResponse>(error: new Error("he is not in housing", "This Employee is not In housing if you want to add him go to add housing page", 400));


        var housingId = await dbcontext
            .Housings
            .Where(c => c.Name == oldHousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        var newhousingId = await dbcontext
            .Housings
            .Where(c => c.Name == NewHousingName)
            .Select(c => c.Id)
            .SingleOrDefaultAsync();

        if (housingId == 0 || newhousingId == 0)
            return Result.Failure<EmpolyeeResponse>(error: new Error("No housing Found", "no housing found with this name", 400));

        isexist.HousingId = newhousingId;

        dbcontext.Update(isexist);

        await dbcontext.SaveChangesAsync();

        return Result.Success();

    }
    public async Task<Result<IEnumerable<EmpolyeeResponse>>> Filter(EmployeeFilter filter)
    {
        var query = dbcontext.Employees
            .Where(e => e.IsEmployee)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.IqamaEndH is not null)
            query = query.Where(e => e.IqamaEndH == filter.IqamaEndH);

        if (filter.IqamaEndM is not null)
            query = query.Where(e => e.IqamaEndM == filter.IqamaEndM);

        if (!string.IsNullOrWhiteSpace(filter.Sponsor))
            query = query.Where(e => e.Sponsor.Contains(filter.Sponsor));

        if (filter.sponsorNo.HasValue)
            query = query.Where(e => e.sponsorNo == filter.sponsorNo.Value);

        if (filter.PassportEnd is not null)
            query = query.Where(e => e.PassportEnd == filter.PassportEnd);

        if (!string.IsNullOrWhiteSpace(filter.JobTitle))
            query = query.Where(e => e.JobTitle.Contains(filter.JobTitle));

        if (!string.IsNullOrWhiteSpace(filter.NameAR))
            query = query.Where(e => e.NameAR.Contains(filter.NameAR));

        if (!string.IsNullOrWhiteSpace(filter.NameEN))
            query = query.Where(e => e.NameEN.Contains(filter.NameEN));

        if (!string.IsNullOrWhiteSpace(filter.Country))
            query = query.Where(e => e.Country.Contains(filter.Country));

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(e => e.Status.Contains(filter.Status));

        if (filter.INKSA is not null)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (!string.IsNullOrWhiteSpace(filter.HousingName))
            query = query.Where(e => e.Housing != null &&
                                     e.Housing.Name.Contains(filter.HousingName));

        var list = await query
            .Select(emp => new EmpolyeeResponse(
           emp.IqamaNo,
           emp.IqamaEndM,
           emp.IqamaEndH,
           emp.PassportNo!,
           emp.PassportEnd ?? default,
           emp.sponsorNo,
           emp.Sponsor,
           emp.JobTitle,
           emp.NameAR,
           emp.NameEN,
           emp.Country,
           emp.Phone,
           emp.DateOfBirth,
           emp.Status,
           emp.IBAN!,
           emp.INKSA,
           emp.CreatedAt
                )).ToListAsync();


        return Result.Success<IEnumerable<EmpolyeeResponse>>(list);
    }

    private static EmpolyeeResponse MapToResponse(Employees employee)
    {
        return new EmpolyeeResponse(
            IqamaNo: employee.IqamaNo,
            IqamaEndM: employee.IqamaEndM,
            IqamaEndH: employee.IqamaEndH,
            PassportNo: employee.PassportNo,
            PassportEnd: employee.PassportEnd ?? default,
            Sponsor: employee.Sponsor,
            JobTitle: employee.JobTitle,
            NameAR: employee.NameAR,
            NameEN: employee.NameEN,
            Country: employee.Country,
            Phone: employee.Phone,
            DateOfBirth: employee.DateOfBirth,
            Status: employee.Status,
            IBAN: employee.IBAN,
            CreatedAt: employee.CreatedAt,
            INKSA: employee.INKSA,
            sponsorNo: employee.sponsorNo
        );
    }

    public async Task<Result<PagedList<EmpolyeeResponse>>> Filter2(EmployeeFilter2 filter)
    {

        var query = dbcontext.Employees
            .Where(e => e.IsEmployee)
            .Include(e => e.Housing)
            .AsQueryable();

        if (filter.Sponsor?.Any() == true)
            query = query.Where(e => filter.Sponsor!.Contains(e.Sponsor));

        if (filter.sponsorNo?.ToString().Any() == true)
            query = query.Where(e => filter.sponsorNo!.ToString().Contains(e.sponsorNo.ToString()));

        if (filter.JobTitle?.Any() == true)
            query = query.Where(e => filter.JobTitle!.Contains(e.JobTitle));

        if (filter.NameAR?.Any() == true)
            query = query.Where(e => filter.NameAR!.Any(v => e.NameAR.Contains(v)));

        if (filter.NameEN?.Any() == true)
            query = query.Where(e => filter.NameEN!.Any(v => e.NameEN.Contains(v)));

        if (filter.Country?.Any() == true)
            query = query.Where(e => filter.Country!.Contains(e.Country));

        if (filter.Status?.Any() == true)
            query = query.Where(e => filter.Status!.Contains(e.Status));

        if (filter.INKSA is not null)
            query = query.Where(e => e.INKSA == filter.INKSA);

        if (filter.HousingName?.Any() == true)
            query = query.Where(e => e.Housing != null &&
                                     filter.HousingName!.Contains(e.Housing.Name));


        if (!string.IsNullOrWhiteSpace(filter.SortBy))
        {
            bool descending = filter.SortDirection?.ToUpper() == "DESC";

            query = filter.SortBy switch
            {
                "IqamaEndH" => descending ? query.OrderByDescending(e => e.IqamaEndH)
                                             : query.OrderBy(e => e.IqamaEndH),

                "IqamaEndM" => descending ? query.OrderByDescending(e => e.IqamaEndM)
                                             : query.OrderBy(e => e.IqamaEndM),

                "Sponsor" => descending ? query.OrderByDescending(e => e.Sponsor)
                                             : query.OrderBy(e => e.Sponsor),

                "sponsorNo" => descending ? query.OrderByDescending(e => e.sponsorNo)
                                             : query.OrderBy(e => e.sponsorNo),

                "JobTitle" => descending ? query.OrderByDescending(e => e.JobTitle)
                                             : query.OrderBy(e => e.JobTitle),

                _ => query
            };
        }


        int skip = (filter.Page - 1) * filter.PageSize;

        var totalCount = await query.CountAsync();

        var data = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .Select(emp => new EmpolyeeResponse(
                   emp.IqamaNo,
                   emp.IqamaEndM,
                   emp.IqamaEndH,
                   emp.PassportNo!,
                   emp.PassportEnd ?? default,
                     emp.sponsorNo,
                   emp.Sponsor,
                   emp.JobTitle,
                   emp.NameAR,
                   emp.NameEN,
                   emp.Country,
                   emp.Phone,
                   emp.DateOfBirth,
                   emp.Status,
                   emp.IBAN!,
                   emp.INKSA,
                   emp.CreatedAt
                        )).ToListAsync();

        return Result.Success(
            new PagedList<EmpolyeeResponse>(data, totalCount, filter.Page, filter.PageSize)
        );
    }

    public async Task<List<EmpolyeeResponse>> SmartSearch(string keyword)
    {

        keyword = keyword.ToLower();

        var query = dbcontext.Employees.Where(e => e.IsEmployee)
            .Include(e => e.Housing)
            .Where(e =>
                e.NameAR.ToLower().Contains(keyword) ||
                e.NameEN.ToLower().Contains(keyword) ||
                e.Country.ToLower().Contains(keyword) ||
                e.Sponsor.ToLower().Contains(keyword) ||
                e.sponsorNo.ToString().Contains(keyword) ||
                e.IqamaNo.ToString().StartsWith(keyword) ||
                e.JobTitle.ToLower().Contains(keyword)
            );

        return await query
            .Select(emp => new EmpolyeeResponse(
           emp.IqamaNo,
           emp.IqamaEndM,
           emp.IqamaEndH,
           emp.PassportNo!,
           emp.PassportEnd ?? default,
                  emp.sponsorNo,
           emp.Sponsor,
           emp.JobTitle,
           emp.NameAR,
           emp.NameEN,
           emp.Country,
           emp.Phone,
           emp.DateOfBirth,
           emp.Status,
           emp.IBAN!,
           emp.INKSA,
           emp.CreatedAt
                )).ToListAsync();
    }

    public async Task<Result> RequestStatusChangeAsync(long IqamaNo, string newStatus, string reason, string requestedBy)
    {
        try
        {
            // Validate status
            if (!EmployeeStatus.IsValid(newStatus))
            {
                return Result.Failure(
                    new Error("InvalidStatus",
                        $"Invalid status. Valid statuses are: {string.Join(", ", EmployeeStatus.ValidStatuses)}",
                        400));
            }

            var employee = await dbcontext.Employees
                .Include(e => e.RiderDetails)
                .FirstOrDefaultAsync(e => e.IqamaNo == IqamaNo);

            if (employee == null)
                return Result.Failure(
                    new Error("NotFound", "Employee not found", 404));

            // Check if there's already a pending request for this employee
            var existingRequest = await dbcontext.TempEmployeeStatusChanges
                .AnyAsync(t => t.EmployeeIqamaNo == IqamaNo && !t.IsResolved);

            if (existingRequest)
                return Result.Failure(
                    new Error("PendingRequest",
                        "There is already a pending status change request for this employee", 400));

            // Check if the status is already set to the requested status
            if (employee.Status.ToLower() == newStatus.ToLower())
                return Result.Failure(
                    new Error("SameStatus",
                        $"Employee status is already set to '{newStatus}'", 400));

            var statusChange = new TempEmployeeStatusChange
            {
                EmployeeIqamaNo = IqamaNo,
                Action = newStatus.ToLower(),
                Reason = reason,
                RequestedBy = requestedBy,
                RequestedAt = DateTime.UtcNow.AddHours(3),
                IsResolved = false
            };

            await dbcontext.TempEmployeeStatusChanges.AddAsync(statusChange);
            await dbcontext.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                new Error("RequestError", $"Failed to request status change: {ex.Message}", 500));
        }
    }

    public async Task<Result<IEnumerable<TempEmployeeStatusChangeResponse>>> GetPendingStatusChangesAsync()

    {
        try
        {
            var pendingChanges = await dbcontext.TempEmployeeStatusChanges
                .Where(t => !t.IsResolved)
                .Include(t => t.Employee)
                    .ThenInclude(e => e.RiderDetails)
                .OrderBy(t => t.RequestedAt)
                .ToListAsync();

            var responses = pendingChanges.Select(MapToResponse1).ToList();

            return Result.Success<IEnumerable<TempEmployeeStatusChangeResponse>>(responses);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<TempEmployeeStatusChangeResponse>>(
                new Error("GetPendingError", $"Failed to get pending status changes: {ex.Message}", 500));
        }
    }

    public async Task<Result> ResolveStatusChangeAsync(long IqamaNo, string resolution, string resolvedBy, string? adminNotes = null)
    {
        using var transaction = await dbcontext.Database.BeginTransactionAsync();
        try
        {
            if (resolution != "Approved" && resolution != "Rejected")
                return Result.Failure(
                    new Error("InvalidResolution", "Resolution must be 'Approved' or 'Rejected'", 400));

            var statusChange = await dbcontext.TempEmployeeStatusChanges
                .Where(t => t.EmployeeIqamaNo == IqamaNo && !t.IsResolved)
                .Include(t => t.Employee)
                .FirstOrDefaultAsync();

            if (statusChange is null)
                return Result.Failure(
                    new Error("NoChanges", "No pending status change found for this employee", 404));

            // Validate the new status
            if (!EmployeeStatus.IsValid(statusChange.Action))
                return Result.Failure(
                    new Error("InvalidStatus", $"Invalid status in request: {statusChange.Action}", 400));

            if (resolution == "Approved")
            {
                var employee = await dbcontext.Employees
                    .FirstOrDefaultAsync(e => e.IqamaNo == statusChange.EmployeeIqamaNo);

                if (employee == null)
                {
                    return Result.Failure(new Error(
                        "NotFound",
                        $"Employee not found: {statusChange.EmployeeIqamaNo}",
                        404
                    ));
                }

                // Update employee status
                employee.Status = statusChange.Action;
                dbcontext.Employees.Update(employee);
            }

            var empname = await dbcontext.Employees
                .Where(e => e.IqamaNo.ToString() == resolvedBy)
                .Select(e => e.NameAR)
                .FirstOrDefaultAsync();


            // Mark status change as resolved
            statusChange.IsResolved = true;
            statusChange.Resolution = resolution;
            statusChange.ResolvedBy = empname ?? resolvedBy;
            statusChange.ResolvedAt = DateTime.UtcNow.AddHours(3);
            statusChange.AdminNotes = adminNotes ?? (resolution == "Rejected" ? "Request was rejected" : null);

            await dbcontext.SaveChangesAsync();
            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Result.Failure(
                new Error("ResolveError", $"Failed to resolve status change: {ex.Message}", 500));
        }
    }

    private TempEmployeeStatusChangeResponse MapToResponse1(TempEmployeeStatusChange statusChange)
    {
        var isRider = statusChange.Employee?.IsEmployee == false;
        var employeeType = isRider ? "Rider" : "Employee";

        return new TempEmployeeStatusChangeResponse(
            Id: statusChange.Id,
            EmployeeIqamaNo: statusChange.EmployeeIqamaNo,
            EmployeeNameAR: statusChange.Employee?.NameAR ?? "N/A",
            EmployeeNameEN: statusChange.Employee?.NameEN ?? "N/A",
            EmployeeType: employeeType,
            CurrentStatus: statusChange.Employee?.Status ?? "N/A",
            RequestedStatus: statusChange.Action,
            Reason: statusChange.Reason,
            RequestedBy: statusChange.RequestedBy,
            RequestedAt: statusChange.RequestedAt,
            IsResolved: statusChange.IsResolved,
            Resolution: statusChange.Resolution,
            ResolvedBy: statusChange.ResolvedBy,
            ResolvedAt: statusChange.ResolvedAt,
            AdminNotes: statusChange.AdminNotes
        );
    }

    // Updated response record
    public record TempEmployeeStatusChangeResponse(
        int Id,
        long EmployeeIqamaNo,
        string EmployeeNameAR,
        string EmployeeNameEN,
        string EmployeeType,
        string CurrentStatus,
        string RequestedStatus,
        string? Reason,
        string RequestedBy,
        DateTime RequestedAt,
        bool IsResolved,
        string? Resolution,
        string? ResolvedBy,
        DateTime? ResolvedAt,
        string? AdminNotes
    );

    private TempEmployeeStatusChangeResponse1 MapToResponse(TempEmployeeStatusChange statusChange)
    {
        return new TempEmployeeStatusChangeResponse1(
            Id: statusChange.Id,
            EmployeeIqamaNo: statusChange.EmployeeIqamaNo,
            EmployeeNameAR: statusChange.Employee?.NameAR ?? "N/A",
            EmployeeNameEN: statusChange.Employee?.NameEN ?? "N/A",
            Action: statusChange.Action,
            Reason: statusChange.Reason,
            RequestedBy: statusChange.RequestedBy,
            RequestedAt: statusChange.RequestedAt,
            IsResolved: statusChange.IsResolved,
            Resolution: statusChange.Resolution,
            ResolvedBy: statusChange.ResolvedBy,
            ResolvedAt: statusChange.ResolvedAt
        );
    }


    public async Task<Result<IEnumerable<DeletedEmployeeResponse>>> GetAlldeletedEmployee()
    {
        var deletedEmployees = await dbcontext.Employees
            .Where(e => e.IsDeleted)
            .Include(e => e.RiderDetails)
                .ThenInclude(rd => rd.Company)
            .Include(e => e.Housing)
            .AsNoTracking()
            .ToListAsync();

        if (deletedEmployees.Count == 0)
            return Result.Failure<IEnumerable<DeletedEmployeeResponse>>(
                new Error("No Deleted Employees Found", "No deleted employees", 400)
            );

        var response = deletedEmployees.Select(emp => new DeletedEmployeeResponse(
            IqamaNo: emp.IqamaNo,
            IqamaEndM: emp.IqamaEndM,
            IqamaEndH: emp.IqamaEndH,
            PassportNo: emp.PassportNo,
            PassportEnd: emp.PassportEnd,
            sponsorNo: emp.sponsorNo,
            Sponsor: emp.Sponsor,
            JobTitle: emp.JobTitle,
            NameAR: emp.NameAR,
            NameEN: emp.NameEN,
            Country: emp.Country,
            Phone: emp.Phone,
            DateOfBirth: emp.DateOfBirth,
            Status: emp.Status,
            IBAN: emp.IBAN,
            INKSA: emp.INKSA,
            CreatedAt: emp.CreatedAt,
            DeletedAt: emp.DeletedAt,
            HousingName: emp.Housing?.Name,
            // Rider details if exists
            WorkingId: emp.RiderDetails?.WorkingId,
            TshirtSize: emp.RiderDetails?.TshirtSize,
            LicenseNumber: emp.RiderDetails?.LicenseNumber,
            CompanyName: emp.RiderDetails?.Company?.Name,
            VehicleNumber: emp.RiderDetails?.VehicleNumber
        )).ToList();

        return Result.Success<IEnumerable<DeletedEmployeeResponse>>(response);
    }
    public record DeletedEmployeeResponse(
    long IqamaNo,
    DateOnly IqamaEndM,
    DateOnly IqamaEndH,
    string? PassportNo,
    DateOnly? PassportEnd,
    long sponsorNo,
    string Sponsor,
    string JobTitle,
    string NameAR,
    string NameEN,
    string Country,
    string Phone,
    DateOnly DateOfBirth,
    string Status,
    string? IBAN,
    bool INKSA,
    DateTime CreatedAt,
    DateTime? DeletedAt,
    string? HousingName,
    // Rider details
    string? WorkingId,
    string? TshirtSize,
    string? LicenseNumber,
    string? CompanyName,
    string? VehicleNumber
);
    public async Task<Result<EmployeeStatusHistoryResponse>> GetEmployeeStatusHistoryAsync(long IqamaNo)
    {
        try
        {
            // Get employee/rider details
            var employee = await dbcontext.Employees
                .Include(e => e.RiderDetails)
                    .ThenInclude(rd => rd.Company)
                .Include(e => e.Housing)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IqamaNo == IqamaNo);

            if (employee == null)
                return Result.Failure<EmployeeStatusHistoryResponse>(
                    new Error("NotFound", "Employee or Rider not found", 404));

            // Get all status change history for this employee
            var statusChanges = await dbcontext.TempEmployeeStatusChanges
                .Where(t => t.EmployeeIqamaNo == IqamaNo)
                .OrderByDescending(t => t.RequestedAt)
                .AsNoTracking()
                .ToListAsync();

            var historyItems = statusChanges.Select(sc => new StatusChangeHistoryDto(
                Id: sc.Id,
                RequestedStatus: sc.Action,
                Reason: sc.Reason,
                RequestedBy: sc.RequestedBy,
                RequestedAt: sc.RequestedAt,
                IsResolved: sc.IsResolved,
                Resolution: sc.Resolution,
                ResolvedBy: sc.ResolvedBy,
                ResolvedAt: sc.ResolvedAt,
                AdminNotes: sc.AdminNotes
            )).ToList();

            var response = new EmployeeStatusHistoryResponse(
                IqamaNo: employee.IqamaNo,
                NameAR: employee.NameAR,
                NameEN: employee.NameEN,
                CurrentStatus: employee.Status,
                EmployeeType: !employee.IsEmployee ? "Rider" : "Employee",
                CompanyName: employee.RiderDetails?.Company?.Name,
                HousingName: employee.Housing?.Name,
                TotalStatusChanges: historyItems.Count,
                PendingRequests: historyItems.Count(h => !h.IsResolved),
                ApprovedChanges: historyItems.Count(h => h.IsResolved && h.Resolution == "Approved"),
                RejectedChanges: historyItems.Count(h => h.IsResolved && h.Resolution == "Rejected"),
                StatusChangeHistory: historyItems
            );

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            return Result.Failure<EmployeeStatusHistoryResponse>(
                new Error("ServerError", $"Failed to get status history: {ex.Message}", 500));
        }
    }


    public async Task<Result<IEnumerable<StatusChangeHistoryDto>>> GetStatusChangesByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var statusChanges = await dbcontext.TempEmployeeStatusChanges
                .Where(t => t.RequestedAt >= startDate && t.RequestedAt <= endDate)
                .Include(t => t.Employee)
                .OrderByDescending(t => t.RequestedAt)
                .AsNoTracking()
                .Select(sc => new StatusChangeHistoryDto(
                    Id: sc.Id,
                    IqamaNo: sc.EmployeeIqamaNo,
                    EmployeeNameAR: sc.Employee.NameAR,
                    EmployeeNameEN: sc.Employee.NameEN,
                    RequestedStatus: sc.Action,
                    Reason: sc.Reason,
                    RequestedBy: sc.RequestedBy,
                    RequestedAt: sc.RequestedAt,
                    IsResolved: sc.IsResolved,
                    Resolution: sc.Resolution,
                    ResolvedBy: sc.ResolvedBy,
                    ResolvedAt: sc.ResolvedAt,
                    AdminNotes: sc.AdminNotes
                ))
                .ToListAsync();

            return Result.Success<IEnumerable<StatusChangeHistoryDto>>(statusChanges);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<StatusChangeHistoryDto>>(
                new Error("ServerError", $"Failed to get status changes by date range: {ex.Message}", 500));
        }
    }

    public async Task<Result<StatusChangeStatisticsDto>> GetStatusChangeStatisticsAsync()
    {
        try
        {
            var allChanges = await dbcontext.TempEmployeeStatusChanges
                .AsNoTracking()
                .ToListAsync();

            var statistics = new StatusChangeStatisticsDto(
                TotalRequests: allChanges.Count,
                PendingRequests: allChanges.Count(sc => !sc.IsResolved),
                ApprovedRequests: allChanges.Count(sc => sc.IsResolved && sc.Resolution == "Approved"),
                RejectedRequests: allChanges.Count(sc => sc.IsResolved && sc.Resolution == "Rejected"),
                StatusBreakdown: allChanges
                    .GroupBy(sc => sc.Action)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RequestsByMonth: allChanges
                    .GroupBy(sc => new { sc.RequestedAt.Year, sc.RequestedAt.Month })
                    .OrderByDescending(g => g.Key.Year)
                    .ThenByDescending(g => g.Key.Month)
                    .Take(12)
                    .ToDictionary(
                        g => $"{g.Key.Year}-{g.Key.Month:D2}",
                        g => g.Count()
                    )
            );

            return Result.Success(statistics);
        }
        catch (Exception ex)
        {
            return Result.Failure<StatusChangeStatisticsDto>(
                new Error("ServerError", $"Failed to get statistics: {ex.Message}", 500));
        }
    }

    public async Task<bool> Togle(long iqama)
    {
        var emp = await dbcontext.Employees.FirstOrDefaultAsync(x => x.IqamaNo == iqama);

        if (emp == null)
            return false;

        emp.IsEmployee = !emp.IsEmployee;

        await dbcontext.SaveChangesAsync();

        return true;
    }
}

// DTOs
public record EmployeeStatusHistoryResponse(
    long IqamaNo,
    string NameAR,
    string NameEN,
    string CurrentStatus,
    string EmployeeType,
    string? CompanyName,
    string? HousingName,
    int TotalStatusChanges,
    int PendingRequests,
    int ApprovedChanges,
    int RejectedChanges,
    List<StatusChangeHistoryDto> StatusChangeHistory
);

public record StatusChangeHistoryDto(
    int Id,
    long? IqamaNo = null,
    string? EmployeeNameAR = null,
    string? EmployeeNameEN = null,
    string RequestedStatus = "",
    string? Reason = null,
    string RequestedBy = "",
    DateTime RequestedAt = default,
    bool IsResolved = false,
    string? Resolution = null,
    string? ResolvedBy = null,
    DateTime? ResolvedAt = null,
    string? AdminNotes = null
);

public record StatusChangeStatisticsDto(
    int TotalRequests,
    int PendingRequests,
    int ApprovedRequests,
    int RejectedRequests,
    Dictionary<string, int> StatusBreakdown,
    Dictionary<string, int> RequestsByMonth
);




public record TempEmployeeStatusChangeResponse1(
    int Id,
    long EmployeeIqamaNo,
    string EmployeeNameAR,
    string EmployeeNameEN,
    string Action,
    string? Reason,
    string RequestedBy,
    DateTime RequestedAt,
    bool IsResolved,
    string? Resolution,
    string? ResolvedBy,
    DateTime? ResolvedAt
);