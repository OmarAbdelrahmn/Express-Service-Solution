using Application.Abstraction;
using Application.Contracts.SupplierCon;

namespace Application.Service.SupplierSer;

public interface IBillService
{
    Task<Result<BillResponse>> ReceiveBillAsync(ReceiveBillRequest request, string processedBy);
    Task<Result<IEnumerable<BillSummaryResponse>>> GetAllBillsAsync();
    Task<Result<BillResponse>> GetBillByIdAsync(int id);
    Task<Result<IEnumerable<BillSummaryResponse>>> GetBillsByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result<IEnumerable<BillSummaryResponse>>> GetBillsBySupplierAsync(int supplierId);
    Task<Result> DeleteBillAsync(int id);
}