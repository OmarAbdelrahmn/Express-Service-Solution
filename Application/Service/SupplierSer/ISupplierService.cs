using Application.Abstraction;
using Application.Contracts.SupplierCon;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Service.SupplierSer;

public interface ISupplierService
{
    Task<Result<IEnumerable<SupplierListResponse>>> GetAllAsync();
    Task<Result<IEnumerable<SupplierListResponse>>> GetActiveAsync();
    Task<Result<SupplierResponse>> GetByIdAsync(int id);
    Task<Result<SupplierResponse>> CreateAsync(SupplierRequest request);
    Task<Result<SupplierResponse>> UpdateAsync(int id, SupplierRequest request);
    Task<Result> ToggleActiveAsync(int id);
    Task<Result> DeleteAsync(int id);
    Task<Result<IEnumerable<SupplierListResponse>>> SearchAsync(string keyword);
}
