using Application.Abstraction;
using Application.Contracts.SparePartCo;

namespace Application.Service.SparePart;

public interface IReturnService
{
    Task<Result<ReturnResponse>> CreateReturnAsync(ReturnRequest request);
    Task<Result<IEnumerable<ReturnResponse>>> GetAllReturnsAsync();
    Task<Result<ReturnResponse>> GetReturnByIdAsync(int id);
    Task<Result<IEnumerable<ReturnResponse>>> GetReturnsBySupplierAsync(int supplierId);
    Task<Result<IEnumerable<ReturnResponse>>> GetReturnsByDateRangeAsync(DateTime fromDate, DateTime toDate);
}