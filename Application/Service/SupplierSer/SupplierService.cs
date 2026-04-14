using Application.Abstraction;
using Application.Contracts.SupplierCon;
using Domain;
using Domain.Entities.Spare;
using Microsoft.EntityFrameworkCore;

namespace Application.Service.SupplierSer;

public class SupplierService(ApplicationDbcontext dbcontext) : ISupplierService
{
    private readonly ApplicationDbcontext _dbcontext = dbcontext;

    public async Task<Result<IEnumerable<SupplierListResponse>>> GetAllAsync()
    {
        var suppliers = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var response = suppliers.Select(MapToListResponse);
        return Result.Success<IEnumerable<SupplierListResponse>>(response);
    }

    public async Task<Result<IEnumerable<SupplierListResponse>>> GetActiveAsync()
    {
        var suppliers = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .Where(s => s.IsActive)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        var response = suppliers.Select(MapToListResponse);
        return Result.Success<IEnumerable<SupplierListResponse>>(response);
    }

    public async Task<Result<SupplierResponse>> GetByIdAsync(int id)
    {
        var supplier = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null)
            return Result.Failure<SupplierResponse>(
                new Error("NotFound", "Supplier not found", 404));

        return Result.Success(MapToResponse(supplier));
    }

    public async Task<Result<SupplierResponse>> CreateAsync(SupplierRequest request)
    {
        // Check if supplier with same name exists
        var exists = await _dbcontext.Suppliers
            .AnyAsync(s => s.Name.ToLower() == request.Name.ToLower());

        if (exists)
            return Result.Failure<SupplierResponse>(
                new Error("DuplicateName", "Supplier with this name already exists", 400));

        var supplier = new Supplier
        {
            Name = request.Name,
            ContactPerson = request.ContactPerson,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
            TaxNumber = request.TaxNumber,
            CommercialRegister = request.CommercialRegister,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddHours(3)
        };

        await _dbcontext.Suppliers.AddAsync(supplier);
        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(supplier));
    }

    public async Task<Result<SupplierResponse>> UpdateAsync(int id, SupplierRequest request)
    {
        var supplier = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null)
            return Result.Failure<SupplierResponse>(
                new Error("NotFound", "Supplier not found", 404));

        // Check if another supplier has the same name
        var duplicateName = await _dbcontext.Suppliers
            .AnyAsync(s => s.Id != id && s.Name.ToLower() == request.Name.ToLower());

        if (duplicateName)
            return Result.Failure<SupplierResponse>(
                new Error("DuplicateName", "Another supplier with this name already exists", 400));

        supplier.Name = request.Name;
        supplier.ContactPerson = request.ContactPerson;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.Address = request.Address;
        supplier.TaxNumber = request.TaxNumber;
        supplier.CommercialRegister = request.CommercialRegister;

        await _dbcontext.SaveChangesAsync();

        return Result.Success(MapToResponse(supplier));
    }

    public async Task<Result> ToggleActiveAsync(int id)
    {
        var supplier = await _dbcontext.Suppliers.FindAsync(id);

        if (supplier == null)
            return Result.Failure(
                new Error("NotFound", "Supplier not found", 404));

        supplier.IsActive = !supplier.IsActive;
        await _dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id)
    {
        var supplier = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null)
            return Result.Failure(
                new Error("NotFound", "Supplier not found", 404));

        // Check if supplier has bills
        if (supplier.Bills.Any())
            return Result.Failure(
                new Error("HasBills",
                    "Cannot delete supplier with existing bills. Consider deactivating instead.", 400));

        _dbcontext.Suppliers.Remove(supplier);
        await _dbcontext.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<SupplierListResponse>>> SearchAsync(string keyword)
    {
        keyword = keyword.ToLower();

        var suppliers = await _dbcontext.Suppliers
            .Include(s => s.Bills)
            .Where(s => s.Name.ToLower().Contains(keyword) ||
                       (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(keyword)) ||
                       (s.Phone != null && s.Phone.Contains(keyword)) ||
                       (s.Email != null && s.Email.ToLower().Contains(keyword)))
            .AsNoTracking()
            .ToListAsync();

        var response = suppliers.Select(MapToListResponse);
        return Result.Success<IEnumerable<SupplierListResponse>>(response);
    }

    private static SupplierResponse MapToResponse(Supplier supplier)
    {
        return new SupplierResponse(
            supplier.Id,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Phone,
            supplier.Email,
            supplier.Address,
            supplier.TaxNumber,
            supplier.IsActive,
            supplier.CreatedAt,
            supplier.Bills?.Count ?? 0,
            supplier.Bills?.Sum(b => b.TotalAmount) ?? 0,
            supplier.CommercialRegister

        );
    }

    private static SupplierListResponse MapToListResponse(Supplier supplier)
    {
        return new SupplierListResponse(
            supplier.Id,
            supplier.Name,
            supplier.Phone,
            supplier.Email,
            supplier.IsActive,
            supplier.Bills?.Count ?? 0,
            supplier.Bills?.Sum(b => b.TotalAmount) ?? 0,
            supplier.CommercialRegister
        );
    }
}